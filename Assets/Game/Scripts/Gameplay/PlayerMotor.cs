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

        public MovementStats Stats = MovementStats.Default;

        /// <summary>Hitbox bottom-left corner, px.</summary>
        public Vector2 Position;
        public Vector2 PreviousPosition;
        /// <summary>px/tick, y-up.</summary>
        public Vector2 Velocity;

        public float Width { get; }
        public float Height { get; }
        public bool Grounded { get; private set; }
        public int Direction { get; set; } = 1;

        /// <summary>Highest ledge walked onto automatically: one tile.</summary>
        const float k_MaxStepHeight = SimUnits.PixelsPerTile;
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

        int m_JumpTicksLeft;
        bool m_ReleasedJump = true;
        bool m_HitCeilingLastTick;

        public PlayerMotor(TileWorld world, float widthPx, float heightPx)
        {
            m_World = world;
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
            Collide();
        }

        void HorizontalMovement(bool left, bool right)
        {
            MovementStats s = Stats;
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
                        Velocity.y = Stats.JumpSpeed;
                        m_JumpTicksLeft--;
                    }
                }
                else if (Grounded && m_ReleasedJump)
                {
                    m_JumpTicksLeft = Stats.JumpHeight;
                    Velocity.y = Stats.JumpSpeed;
                }

                m_ReleasedJump = false;
            }
            else
            {
                m_JumpTicksLeft = 0;
                m_ReleasedJump = true;
            }
        }

        void ApplyGravity()
        {
            Velocity.y -= Stats.Gravity;
            if (Velocity.y < -Stats.MaxFallSpeed) Velocity.y = -Stats.MaxFallSpeed;
        }

        void Collide()
        {
            float x = Position.x;
            float y = Position.y;
            float vx = Velocity.x;
            float vy = Velocity.y;

            PreviousStepOffset = StepOffset;
            StepOffset = Mathf.MoveTowards(StepOffset, 0f, Mathf.Max(k_MinStepSettleSpeed, Mathf.Abs(vx)));
            TryStepUp(ref x, ref y, vx, vy);

            TileCollision.Result hit = TileCollision.Move(m_World, ref x, ref y, Width, Height, ref vx, ref vy);

            Position = new Vector2(x, y);
            Velocity = new Vector2(vx, vy);
            Grounded = hit.HitFloor;
            m_HitCeilingLastTick = hit.HitCeiling;
        }

        /// <summary>
        /// Step-up: walking into a ledge at most one tile high lifts the player onto it
        /// without jumping, provided there is room for the whole hitbox on top. Only while on the ground
        /// and not rising, so it never shortcuts a jump. The lift is instant in the simulation; the sprite
        /// eases up over a few ticks through <see cref="StepOffset"/>.
        /// </summary>
        void TryStepUp(ref float x, ref float y, float vx, float vy)
        {
            if (!Grounded || vx == 0f || vy > 0f) return;

            const float epsilon = 0.001f;
            float nextX = x + vx;

            // Nothing in the way: plain walking.
            if (!m_World.AnySolidInPixelRect(nextX, y, Width, Height)) return;

            // The obstacle must be confined to the bottom tile of the hitbox: step onto that tile's top.
            float stepTop = (SimUnits.PixelToTile(y + epsilon) + 1) * SimUnits.PixelsPerTile;
            float rise = stepTop - y;
            if (rise <= 0f || rise > k_MaxStepHeight + epsilon) return;

            // Room for the raised hitbox both where it stands and where it's heading.
            if (m_World.AnySolidInPixelRect(x, stepTop, Width, Height)) return;
            if (m_World.AnySolidInPixelRect(nextX, stepTop, Width, Height)) return;

            y = stepTop;
            StepOffset += rise;
        }
    }
}
