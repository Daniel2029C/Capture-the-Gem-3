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
    /// Switching items drops a draw in progress (see <see cref="CancelBySwitch"/>). Arrows are unlimited.
    /// </summary>
    public sealed class ChargeBow
    {
        readonly IDamageable m_Owner;
        int m_PrimaryLastDrawTick = int.MinValue / 2;
        int m_SecondaryLastDrawTick = int.MinValue / 2;
        int m_TicksSinceRelease;
        bool m_Fired;
        int m_LockedUntilTick = int.MinValue / 2;

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
        /// <summary>The Archer's shared ability state (arrow mode, light arrows, Luminite), or null.</summary>
        public ArcherKit Kit { get; set; }
        /// <summary>The current draw is the Luminite arrow (either button).</summary>
        public bool IsLuminite { get; private set; }
        /// <summary>Drawing the Luminite arrow right now: the player runs slower.</summary>
        public bool IsChargingLuminite => IsCharging && IsLuminite;

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
            IsLuminite = false;
        }

        /// <summary>
        /// Another item was selected during a draw. An unreleased draw is dropped without firing; one already
        /// fired still blocks the next draw for the rest of its hold time, so switching can't shorten the shot cycle.
        /// </summary>
        public void CancelBySwitch(int tick)
        {
            if (!Active) return;

            if (Released && Item != null) m_LockedUntilTick = tick + Mathf.Max(0, Item.UseTime - m_TicksSinceRelease);
            Cancel();
        }

        public void Tick(int tick, ItemDef item, bool primaryHeld, bool secondaryHeld, Vector2 cursorPx, PlayerMotor motor)
        {
            ChargeBowStats stats = item.ChargeBow;
            if (stats == null) return;

            if (!Active && tick >= m_LockedUntilTick)
            {
                if (Kit != null && Kit.LuminiteArmed && (primaryHeld || secondaryHeld))
                {
                    // An armed Luminite arrow takes the next draw, whichever button starts it.
                    m_PrimaryLastDrawTick = tick;
                    m_SecondaryLastDrawTick = tick;
                    Begin(item, ProjectileId.LuminiteArrow);
                    IsLuminite = true;
                }
                else if (Kit != null && Kit.LightMode)
                {
                    // Light mode: no draw. Left click looses a light arrow when a charge, the fire interval and
                    // the switch delay allow it; right click does nothing.
                }
                else if (Kit != null && !Kit.CanDrawHeavy(tick))
                {
                    // A light arrow was just loosed: Ember and Gleam wait out the switch delay.
                }
                else if (primaryHeld && tick - m_PrimaryLastDrawTick >= stats.UseDelayTicks)
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

            if (Kit != null && Kit.LightMode && !IsCharging && primaryHeld && !Kit.LuminiteArmed && tick >= m_LockedUntilTick && Kit.CanFireLight(tick))
            {
                FireLight(tick, item, cursorPx, motor);
            }

            if (!Active) return;

            Vector2 aim = cursorPx - motor.Center;
            if (aim.sqrMagnitude > 0f) AimAngle = Mathf.Atan2(aim.y, aim.x);
            Direction = Mathf.Cos(AimAngle) >= 0f ? 1 : -1;

            bool primary = Arrow == stats.PrimaryArrow;
            bool holding = IsLuminite ? primaryHeld || secondaryHeld : primary ? primaryHeld : secondaryHeld;

            if (Charge >= stats.MaxCharge) FullChargeTicks++;

            if (holding && !Released)
            {
                Charge += IsLuminite ? Kit.Stats.LuminiteChargePerTick
                        : primary ? stats.PrimaryChargePerTick : stats.SecondaryChargePerTick;
            }
            else if (!Released)
            {
                Released = true;
            }

            if (Released && !m_Fired)
            {
                Fire(tick, item, stats, primary, motor);
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
            IsLuminite = false;
        }

        /// <summary>A light arrow: instant, straight, fast, no knockback, fixed damage.</summary>
        void FireLight(int tick, ItemDef item, Vector2 cursorPx, PlayerMotor motor)
        {
            ProjectileSystem projectiles = ProjectileSystem.Instance;
            if (projectiles == null) return;

            ArcherStats stats = Kit.Stats;
            Vector2 aim = cursorPx - motor.Center;
            Vector2 direction = aim.sqrMagnitude < 0.0001f ? new Vector2(motor.Direction, 0f) : aim.normalized;
            projectiles.Spawn(ProjectileId.LightArrow, m_Owner, motor.Center + direction * stats.LightSpawnOffsetPx,
                              direction * stats.LightSpeed, stats.LightDamage, 0f, 0);
            Kit.OnLightFired(tick);
            LastLightTick = tick;
        }

        /// <summary>Tick of the latest light arrow, for animation.</summary>
        public int LastLightTick { get; private set; } = int.MinValue / 2;

        void Fire(int tick, ItemDef item, ChargeBowStats stats, bool primary, PlayerMotor motor)
        {
            ProjectileSystem projectiles = ProjectileSystem.Instance;
            if (projectiles == null) return;

            float charge = Mathf.Min(Charge, stats.MaxCharge);
            bool curved = !primary && Charge < stats.SecondaryCurveBelowCharge;
            float speedPenalty = curved ? stats.SecondaryCurvedSpeedPenalty : 0f;
            int damagePenalty = primary ? stats.PrimaryDamagePenalty : 0;
            int extraUpdates = stats.ArrowExtraUpdates;
            if (IsLuminite)
            {
                // The Luminite arrow: faster, harder-hitting, and straight from the draw (no curve penalty).
                curved = false;
                speedPenalty = -Kit.Stats.LuminiteSpeedBonus;
                damagePenalty = -Kit.Stats.LuminiteDamageBonus;
                extraUpdates = Kit.Stats.LuminiteExtraUpdates;
                Kit.OnLuminiteFired();
            }
            else
            {
                Kit?.OnHeavyFired(tick);
            }

            float powerRange = 1f - stats.MinPower;
            float speedPower = stats.MinPower + charge / stats.MaxCharge * powerRange;
            float damagePower = stats.MinPower + charge / (stats.MaxCharge / powerRange);

            var direction = new Vector2(Mathf.Cos(AimAngle), Mathf.Sin(AimAngle));
            Vector2 velocity = direction * item.ShootSpeed * speedPower * (stats.SpeedMultiplier - speedPenalty);
            int damage = (int)((item.Damage - damagePenalty) * damagePower);
            Vector2 spawn = motor.Center + direction * stats.SpawnOffsetPx;

            projectiles.Spawn(Arrow, m_Owner, spawn, velocity, damage, item.Knockback, extraUpdates);
        }
    }
}
