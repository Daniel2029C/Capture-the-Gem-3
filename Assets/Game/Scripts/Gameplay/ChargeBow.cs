using CTG.Combat;
using CTG.Items;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// Charge bow state for one player, ported from CTG2's Rancor. Per tick, while the bow is held:
    /// <list type="number">
    /// <item>With no draw in progress, a held primary (or secondary) button starts a draw if that button's
    /// use delay has passed since its last draw.</item>
    /// <item>During a draw the player faces the cursor; holding the draw's button adds charge, letting go
    /// releases it and fires the arrow from the charge reached.</item>
    /// <item>The bow stays drawn for the item's useTime after release, then a new draw can start.</item>
    /// </list>
    /// The item can't be switched while a draw is active. Arrows are unlimited.
    /// </summary>
    public sealed class ChargeBow
    {
        readonly IDamageable m_Owner;
        int m_PrimaryLastDrawTick = int.MinValue / 2;
        int m_SecondaryLastDrawTick = int.MinValue / 2;
        int m_TicksSinceRelease;
        bool m_Fired;

        /// <summary>A draw is in progress (charging, or the short hold after release).</summary>
        public bool Active { get; private set; }
        public bool Released { get; private set; }
        public bool IsCharging => Active && !Released;
        public ProjectileId Arrow { get; private set; }
        /// <summary>Raw charge. Keeps counting past the maximum; uses clamp it.</summary>
        public float Charge { get; private set; }
        /// <summary>Aim in radians (y-up), toward the cursor from the player's centre.</summary>
        public float AimAngle { get; private set; }
        /// <summary>+1 right, -1 left, from the aim.</summary>
        public int Direction { get; private set; } = 1;
        /// <summary>Ticks spent at full charge, for the full-charge glow.</summary>
        public int FullChargeTicks { get; private set; }
        public ItemDef Item { get; private set; }

        public ChargeBow(IDamageable owner) => m_Owner = owner;

        public float ChargeFraction =>
            Item?.ChargeBow == null ? 0f : Mathf.Clamp01(Charge / Item.ChargeBow.MaxCharge);

        public bool FullyCharged => Item?.ChargeBow != null && Charge >= Item.ChargeBow.MaxCharge;

        /// <summary>Drops the draw without firing (death, respawn). Use delays keep running.</summary>
        public void Cancel()
        {
            Active = false;
            Released = false;
            Charge = 0f;
        }

        public void Tick(int tick, ItemDef item, bool primaryHeld, bool secondaryHeld, Vector2 cursorPx, PlayerMotor motor)
        {
            ChargeBowStats stats = item.ChargeBow;
            if (stats == null) return;

            if (!Active)
            {
                if (primaryHeld && tick - m_PrimaryLastDrawTick >= stats.UseDelayTicks)
                {
                    m_PrimaryLastDrawTick = tick;
                    Begin(item, stats.PrimaryArrow);
                }
                else if (secondaryHeld && tick - m_SecondaryLastDrawTick >= stats.UseDelayTicks)
                {
                    m_SecondaryLastDrawTick = tick;
                    // A secondary draw counts as a use of the bow, which restarts the primary timer too.
                    if (tick - m_PrimaryLastDrawTick >= stats.UseDelayTicks) m_PrimaryLastDrawTick = tick;
                    Begin(item, stats.SecondaryArrow);
                }
            }

            if (!Active) return;

            Vector2 aim = cursorPx - motor.Center;
            if (aim.sqrMagnitude > 0f) AimAngle = Mathf.Atan2(aim.y, aim.x);
            Direction = Mathf.Cos(AimAngle) >= 0f ? 1 : -1;

            bool primary = Arrow == stats.PrimaryArrow;
            bool holding = primary ? primaryHeld : secondaryHeld;

            if (Charge >= stats.MaxCharge) FullChargeTicks++;

            if (holding && !Released)
            {
                Charge += primary ? stats.PrimaryChargePerTick : stats.SecondaryChargePerTick;
            }
            else if (!Released)
            {
                Released = true;
            }

            if (Released && !m_Fired)
            {
                Fire(item, stats, primary, motor);
                m_Fired = true;
            }

            if (Released && ++m_TicksSinceRelease >= item.UseTime)
            {
                Active = false;
            }
        }

        void Begin(ItemDef item, ProjectileId arrow)
        {
            Item = item;
            Arrow = arrow;
            Active = true;
            Released = false;
            Charge = 0f;
            FullChargeTicks = 0;
            m_TicksSinceRelease = 0;
            m_Fired = false;
        }

        void Fire(ItemDef item, ChargeBowStats stats, bool primary, PlayerMotor motor)
        {
            ProjectileSystem projectiles = ProjectileSystem.Instance;
            if (projectiles == null) return;

            float charge = Mathf.Min(Charge, stats.MaxCharge);
            bool curved = !primary && Charge < stats.SecondaryCurveBelowCharge;
            float speedPenalty = curved ? stats.SecondaryCurvedSpeedPenalty : 0f;
            int damagePenalty = primary ? stats.PrimaryDamagePenalty : 0;

            float powerRange = 1f - stats.MinPower;
            float speedPower = stats.MinPower + charge / stats.MaxCharge * powerRange;
            float damagePower = stats.MinPower + charge / (stats.MaxCharge / powerRange);

            var direction = new Vector2(Mathf.Cos(AimAngle), Mathf.Sin(AimAngle));
            Vector2 velocity = direction * item.ShootSpeed * speedPower * (stats.SpeedMultiplier - speedPenalty);
            int damage = (int)((item.Damage - damagePenalty) * damagePower);
            Vector2 spawn = motor.Center + direction * stats.SpawnOffsetPx;

            projectiles.Spawn(Arrow, m_Owner, spawn, velocity, damage, item.Knockback, stats.ArrowExtraUpdates);
        }
    }
}
