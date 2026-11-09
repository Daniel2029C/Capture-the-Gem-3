using CTG.Core;
using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// Base movement stats in simulation units (px/tick, px/tick², ticks). These are the values
    /// every tick starts from before equipment and buffs modify them;
    /// accessories, buffs and class bonuses will become modifiers on top of this.
    /// </summary>
    [System.Serializable]
    public struct MovementStats
    {
        [Tooltip("Top running speed. 3 px/tick.")]
        public float MaxRunSpeed;
        [Tooltip("Speed gained per tick while holding a direction. 0.08 px/tick².")]
        public float RunAcceleration;
        [Tooltip("Braking per tick (friction and turning). 0.2 px/tick². Halved while airborne with no input.")]
        public float RunSlowdown;
        [Tooltip("Upward speed while the jump is sustained. 5.01 px/tick.")]
        public float JumpSpeed;
        [Tooltip("Ticks of sustained rise while Jump is held. 15.")]
        public int JumpHeight;
        [Tooltip("Gravity. 0.4 px/tick².")]
        public float Gravity;
        [Tooltip("Terminal falling speed. 10 px/tick.")]
        public float MaxFallSpeed;

        public static MovementStats Default => new()
        {
            MaxRunSpeed = 3f,
            RunAcceleration = 0.08f,
            RunSlowdown = 0.2f,
            JumpSpeed = 5.01f,
            JumpHeight = 15,
            Gravity = 0.4f,
            MaxFallSpeed = 10f,
        };
    }

    /// <summary>
    /// Player movement, tick for tick, in pixels (y-up). Each tick runs, in order:
    /// horizontal movement → jump → gravity → tile collision. Because the
    /// integration is reproduced step by step (including gravity acting during the jump hold), the jump
    /// apex, airtime and run curve match the tuned values exactly rather than a continuous-time approximation.
    /// </summary>
    public class PlayerMotor
    {
        readonly TileWorld m_World;
        readonly PlayerCollision m_Collision;

        public MovementStats Stats = MovementStats.Default;

        /// <summary>
        /// The liquid the player is submerged in, set by the player each tick. Lava changes gravity, fall speed
        /// and jump; nectar only changes gravity and fall speed. In both, the displacement each tick is
        /// a fraction of the velocity (see <see cref="PlayerCollision"/>).
        /// </summary>
        public LiquidType Liquid { get; set; }

        public bool InLiquid => Liquid != LiquidType.None;

        const float k_LavaGravity = 0.2f;
        const float k_LavaMaxFallSpeed = 5f;
        const int k_LavaJumpHeight = 30;
        const float k_LavaJumpSpeed = 6.01f;

        const float k_NectarGravity = 0.1f;
        const float k_NectarMaxFallSpeed = 3f;

        /// <summary>Stats in use this tick: the base stats, with the liquid overrides while submerged.</summary>
        MovementStats Active
        {
            get
            {
                MovementStats s = Stats;
                switch (Liquid)
                {
                    case LiquidType.Nectar:
                        s.Gravity = k_NectarGravity;
                        s.MaxFallSpeed = k_NectarMaxFallSpeed;
                        break;
                    case LiquidType.Lava:
                        s.Gravity = k_LavaGravity;
                        s.MaxFallSpeed = k_LavaMaxFallSpeed;
                        s.JumpHeight = k_LavaJumpHeight;
                        s.JumpSpeed = k_LavaJumpSpeed;
                        break;
                }

                return s;
            }
        }

        /// <summary>Hitbox bottom-left corner, px.</summary>
        public Vector2 Position;
        public Vector2 PreviousPosition;
        /// <summary>px/tick, y-up.</summary>
        public Vector2 Velocity;

        public float Width { get; }
        public float Height { get; }
        public bool Grounded { get; private set; }
        public int Direction { get; set; } = 1;

        /// <summary>
        /// Slowest the sprite catches up to a step, px/tick; it settles at least as fast as the player runs.
        /// Tuned by feel.
        /// </summary>
        const float k_MinStepSettleSpeed = 1.5f;

        /// <summary>
        /// Visual-only lag after a step-up, px: draw the player this far below <see cref="Position"/>.
        /// </summary>
        public float StepOffset { get; private set; }
        public float PreviousStepOffset { get; private set; }

        /// <summary>The double jump this player has, or null for none. Set by the class.</summary>
        public DoubleJumpStats DoubleJump { get; set; }
        /// <summary>The extra jump is ready to use (refilled whenever grounded).</summary>
        public bool DoubleJumpAvailable { get; private set; }
        /// <summary>The extra jump is currently rising.</summary>
        public bool DoubleJumping { get; private set; }

        int m_JumpTicksLeft;
        bool m_ReleasedJump = true;
        bool m_HitCeilingLastTick;

        public PlayerMotor(TileWorld world, float widthPx, float heightPx)
        {
            m_World = world;
            m_Collision = new PlayerCollision(world);
            Width = widthPx;
            Height = heightPx;
        }

        public Vector2 Center => Position + new Vector2(Width * 0.5f, Height * 0.5f);

        public void Teleport(Vector2 bottomLeftPx)
        {
            Position = bottomLeftPx;
            PreviousPosition = bottomLeftPx;
            Velocity = Vector2.zero;
            m_JumpTicksLeft = 0;
            DoubleJumping = false;
            StepOffset = 0f;
            PreviousStepOffset = 0f;
        }

        /// <summary>
        /// Knockback replaces the current velocity outright and cancels a jump in progress. Afterwards
        /// normal movement takes over: input can't accelerate past top speed, so the launch decays through
        /// friction (halved while airborne).
        /// </summary>
        public void ApplyKnockback(Vector2 velocityPx)
        {
            Velocity = velocityPx;
            m_JumpTicksLeft = 0;
            DoubleJumping = false;
            if (velocityPx.y > 0f) Grounded = false;
            if (velocityPx.x != 0f) Direction = velocityPx.x > 0f ? -1 : 1; // turn to face the attacker
        }

        /// <summary>Where to draw the hitbox's bottom-left this frame: interpolated, with step easing.</summary>
        public Vector2 GetVisualPosition(float alpha)
        {
            Vector2 previous = PreviousPosition - new Vector2(0f, PreviousStepOffset);
            Vector2 current = Position - new Vector2(0f, StepOffset);
            return Vector2.Lerp(previous, current, alpha);
        }

        public void Tick(bool left, bool right, bool jump)
        {
            PreviousPosition = Position;

            HorizontalMovement(left, right);
            JumpMovement(jump);
            ApplyGravity();
            Collide(true);
        }

        /// <summary>
        /// A tick while hooked: velocity is replaced by the pull (no running, gravity or normal jumping),
        /// the extra jump refills, and a fresh jump press lets go. The remaining rise of a jump already in
        /// progress is kept, not cancelled: holding jump, hooking, releasing jump during the pull and pressing it
        /// again to let go carries that leftover rise into the launch (the "dhook"). Returns true on that press, and the jump
        /// then starts from the pull: a real jump (half the jump hold) only if the pull is exactly level,
        /// otherwise just a tiny nudge, so the player leaves with the pull's momentum.
        /// </summary>
        public bool TickGrappled(bool jump, Vector2 pullVelocity)
        {
            PreviousPosition = Position;

            Velocity = pullVelocity;
            DoubleJumping = false;
            DoubleJumpAvailable = DoubleJump != null;

            bool letGo = false;
            if (jump)
            {
                if (m_ReleasedJump)
                {
                    if (Velocity.y == 0f)
                    {
                        m_JumpTicksLeft = Active.JumpHeight / 2;
                        Velocity.y = Active.JumpSpeed;
                    }
                    else
                    {
                        Velocity.y -= 0.01f;
                    }

                    m_ReleasedJump = false;
                    letGo = true;
                }
            }
            else
            {
                m_ReleasedJump = true;
            }

            if (Velocity.x > 0f) Direction = 1;
            else if (Velocity.x < 0f) Direction = -1;

            // Letting go clears the hooks before the collision step, so on that tick the ledge step-up is
            // allowed again and runs on the pull's velocity: hooking a ledge beside the feet and releasing
            // hops onto it and carries the pull's forward speed, like walking onto a one-tile step.
            Collide(letGo);
            return letGo;
        }

        void HorizontalMovement(bool left, bool right)
        {
            MovementStats s = Active;
            if (DoubleJumping && DoubleJump != null)
            {
                s.RunAcceleration *= DoubleJump.RunAccelerationMultiplier;
                s.MaxRunSpeed *= DoubleJump.MaxRunSpeedMultiplier;
            }

            float vx = Velocity.x;

            // The first held direction that is still under top speed
            // accelerates, braking with the extra slowdown while still moving the other way.
            if (left && vx > -s.MaxRunSpeed)
            {
                if (vx > s.RunSlowdown) vx -= s.RunSlowdown;
                vx -= s.RunAcceleration;
                Direction = -1;
            }
            else if (right && vx < s.MaxRunSpeed)
            {
                if (vx < -s.RunSlowdown) vx += s.RunSlowdown;
                vx += s.RunAcceleration;
                Direction = 1;
            }
            else if (Grounded)
            {
                vx = ApproachZero(vx, s.RunSlowdown);
            }
            else
            {
                // Airborne friction is half of ground friction.
                vx = ApproachZero(vx, s.RunSlowdown * 0.5f);
            }

            // Holding a direction while already above top speed (knockback) still turns the player.
            if (left && !right) Direction = -1;
            else if (right && !left) Direction = 1;

            Velocity.x = vx;
        }

        static float ApproachZero(float v, float amount)
        {
            if (v > amount) return v - amount;
            if (v < -amount) return v + amount;
            return 0f;
        }

        void JumpMovement(bool jump)
        {
            if (Grounded) DoubleJumpAvailable = DoubleJump != null;

            if (jump)
            {
                if (m_JumpTicksLeft > 0)
                {
                    // The jump ends when vertical speed was zeroed, i.e. the head hit a ceiling.
                    if (m_HitCeilingLastTick)
                    {
                        m_JumpTicksLeft = 0;
                    }
                    else
                    {
                        Velocity.y = Active.JumpSpeed;
                        m_JumpTicksLeft--;
                    }
                }
                else if (Grounded && m_ReleasedJump)
                {
                    m_JumpTicksLeft = Active.JumpHeight;
                    Velocity.y = Active.JumpSpeed;
                }
                else if (m_ReleasedJump && DoubleJumpAvailable && DoubleJump != null)
                {
                    // Needs a fresh press, so holding jump through the first jump never spends it.
                    DoubleJumpAvailable = false;
                    DoubleJumping = true;
                    m_JumpTicksLeft = (int)(Active.JumpHeight * DoubleJump.DurationMultiplier);
                    Velocity.y = Active.JumpSpeed;
                }

                m_ReleasedJump = false;
            }
            else
            {
                m_JumpTicksLeft = 0;
                m_ReleasedJump = true;
            }

            // The speed bonus lasts only while the extra jump is still rising.
            if (m_JumpTicksLeft <= 0) DoubleJumping = false;
        }

        void ApplyGravity()
        {
            MovementStats s = Active;
            Velocity.y -= s.Gravity;
            if (Velocity.y < -s.MaxFallSpeed) Velocity.y = -s.MaxFallSpeed;
        }

        void Collide(bool allowStepUp)
        {
            PreviousStepOffset = StepOffset;
            StepOffset = Mathf.MoveTowards(StepOffset, 0f, Mathf.Max(k_MinStepSettleSpeed, Mathf.Abs(Velocity.x)));

            PlayerCollision.Result r = m_Collision.Move(Position, Velocity, Width, Height, Active.Gravity, Liquid, allowStepUp);
            Position = r.Position;
            Velocity = r.Velocity;
            Grounded = r.Grounded;
            m_HitCeilingLastTick = r.HitCeiling;
            StepOffset += r.StepVisual;
            if (r.HitCeiling) m_JumpTicksLeft = 0;
        }
    }
}
