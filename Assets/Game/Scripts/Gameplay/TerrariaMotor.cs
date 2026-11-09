using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// Base movement stats in Terraria's native units (px/tick, px/tick², ticks). These are the values
    /// Terraria's Player.ResetEffects starts every tick from before equipment and buffs modify them;
    /// accessories, buffs and class bonuses will become modifiers on top of this.
    /// </summary>
    [System.Serializable]
    public struct MovementStats
    {
        [Tooltip("Terraria Player.maxRunSpeed. 3 px/tick.")]
        public float MaxRunSpeed;
        [Tooltip("Terraria Player.runAcceleration. 0.08 px/tick².")]
        public float RunAcceleration;
        [Tooltip("Terraria Player.runSlowdown. 0.2 px/tick². Halved while airborne with no input.")]
        public float RunSlowdown;
        [Tooltip("Terraria Player.jumpSpeed. 5.01 px/tick.")]
        public float JumpSpeed;
        [Tooltip("Terraria Player.jumpHeight. 15 ticks of sustained rise while Jump is held.")]
        public int JumpHeight;
        [Tooltip("Terraria Player.gravity. 0.4 px/tick².")]
        public float Gravity;
        [Tooltip("Terraria Player.maxFallSpeed. 10 px/tick.")]
        public float MaxFallSpeed;

        public static MovementStats TerrariaDefault => new()
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
    /// Terraria's player movement, tick for tick, in pixels (y-up). Ported from the order in
    /// Player.Update: HorizontalMovement → JumpMovement → gravity → tile collision. Because the
    /// integration is reproduced step by step (including gravity acting during the jump hold), the jump
    /// apex, airtime and run curve match Terraria rather than a continuous-time approximation of it.
    /// </summary>
    public class TerrariaMotor
    {
        readonly TileWorld m_World;

        public MovementStats Stats = MovementStats.TerrariaDefault;

        /// <summary>Hitbox bottom-left corner, px.</summary>
        public Vector2 Position;
        public Vector2 PreviousPosition;
        /// <summary>px/tick, y-up.</summary>
        public Vector2 Velocity;

        public float Width { get; }
        public float Height { get; }
        public bool Grounded { get; private set; }
        public int Direction { get; set; } = 1;

        int m_JumpTicksLeft;
        bool m_ReleasedJump = true;
        bool m_HitCeilingLastTick;

        public TerrariaMotor(TileWorld world, float widthPx, float heightPx)
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

            // Same branch order as Terraria: the first held direction that is still under top speed
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
                    // Terraria ends the jump when vertical speed was zeroed, i.e. the head hit a ceiling.
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

            TileCollision.Result hit = TileCollision.Move(m_World, ref x, ref y, Width, Height, ref vx, ref vy);

            Position = new Vector2(x, y);
            Velocity = new Vector2(vx, vy);
            Grounded = hit.HitFloor;
            m_HitCeilingLastTick = hit.HitCeiling;
        }
    }
}
