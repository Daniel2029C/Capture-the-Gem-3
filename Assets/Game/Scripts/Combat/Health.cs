using System;
using CTG.Core;

namespace CTG.Combat
{
    /// <summary>
    /// Life, regeneration, post-hit immunity, death and the respawn countdown, all advanced per tick.
    /// Regeneration is a flat rate for now; respawn time is a property so class or game state can set it.
    /// </summary>
    public class Health
    {
        public int Max { get; set; }
        public int Current { get; private set; }

        /// <summary>Life restored per second while alive and below max.</summary>
        public float RegenPerSecond { get; set; }
        /// <summary>Extra life per second from the area the player is in (the spawn room); set each tick.</summary>
        public float ZoneRegenPerSecond { get; set; }

        /// <summary>Ticks of immunity after taking a hit.</summary>
        public int HitImmunityTicks { get; set; }

        /// <summary>Ticks from death to respawn.</summary>
        public int RespawnTicks { get; set; }

        /// <summary>
        /// Life lost per second (set each tick by the owner from its status effects, e.g. burning). While this is above
        /// zero or <see cref="RegenBlocked"/> is set, life does not regenerate.
        /// </summary>
        public float DrainPerSecond { get; set; }
        /// <summary>No regeneration this tick (set each tick by the owner from its status effects).</summary>
        public bool RegenBlocked { get; set; }

        public int ImmunityTicksLeft { get; private set; }
        public int RespawnTicksLeft { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsImmune => ImmunityTicksLeft > 0;

        public event Action<int> Damaged;
        public event Action Died;
        public event Action Respawned;

        float m_RegenProgress;
        float m_DrainProgress;

        public Health(int max, float regenPerSecond, int hitImmunityTicks, int respawnTicks)
        {
            Max = max;
            Current = max;
            RegenPerSecond = regenPerSecond;
            HitImmunityTicks = hitImmunityTicks;
            RespawnTicks = respawnTicks;
        }

        /// <summary>Applies damage unless dead or immune. Returns the damage dealt.</summary>
        public int TakeDamage(int amount)
        {
            if (IsDead || IsImmune || amount <= 0) return 0;

            Current -= amount;
            ImmunityTicksLeft = HitImmunityTicks;
            Damaged?.Invoke(amount);

            if (Current <= 0) Kill();
            return amount;
        }

        /// <summary>Restores life up to the maximum. Returns the amount actually restored.</summary>
        public int Heal(int amount)
        {
            if (IsDead || amount <= 0) return 0;

            int restored = System.Math.Min(amount, Max - Current);
            Current += restored;
            return restored;
        }

        /// <summary>Restores full life and clears immunity (class selection, respawn-like resets).</summary>
        public void Refill()
        {
            Current = Max;
            ImmunityTicksLeft = 0;
            m_RegenProgress = 0f;
            m_DrainProgress = 0f;
        }

        /// <summary>
        /// Damage from the environment (lava): ignores and doesn't start post-hit immunity, which stays
        /// with attacks. The caller handles its own repeat cooldown. Returns the damage dealt.
        /// </summary>
        public int TakeEnvironmentalDamage(int amount)
        {
            if (IsDead || amount <= 0) return 0;

            Current -= amount;
            Damaged?.Invoke(amount);
            if (Current <= 0) Kill();
            return amount;
        }

        public void Kill()
        {
            if (IsDead) return;

            Current = 0;
            IsDead = true;
            ImmunityTicksLeft = 0;
            m_RegenProgress = 0f;
            m_DrainProgress = 0f;
            RespawnTicksLeft = RespawnTicks;
            Died?.Invoke();
        }

        /// <summary>Returns true on the tick the respawn countdown finishes.</summary>
        public bool Tick()
        {
            if (IsDead)
            {
                if (--RespawnTicksLeft > 0) return false;

                IsDead = false;
                Current = Max;
                Respawned?.Invoke();
                return true;
            }

            if (ImmunityTicksLeft > 0) ImmunityTicksLeft--;
            if (DrainPerSecond > 0f || RegenBlocked)
            {
                // Progress is counted in 1/60ths of a point, like regeneration below.
                m_DrainProgress += DrainPerSecond;
                while (m_DrainProgress >= SimUnits.TicksPerSecond && !IsDead)
                {
                    m_DrainProgress -= SimUnits.TicksPerSecond;
                    Current--;
                    if (Current <= 0) Kill();
                }

                m_RegenProgress = 0f;
                return false;
            }

            m_DrainProgress = 0f;
            float regenPerSecond = RegenPerSecond + ZoneRegenPerSecond;
            if (Current < Max && regenPerSecond > 0f)
            {
                // Progress is counted in 1/60ths of a point so whole-number rates add up exactly
                // (summing 1/60 sixty times in floating point falls just short of 1).
                m_RegenProgress += regenPerSecond;
                while (m_RegenProgress >= SimUnits.TicksPerSecond && Current < Max)
                {
                    m_RegenProgress -= SimUnits.TicksPerSecond;
                    Current++;
                }
            }
            else
            {
                m_RegenProgress = 0f;
            }

            return false;
        }
    }
}
