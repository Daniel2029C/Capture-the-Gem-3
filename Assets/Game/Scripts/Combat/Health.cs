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

        /// <summary>Ticks of the burning debuff left. While burning, life drains and doesn't regenerate.</summary>
        public int BurnTicksLeft { get; private set; }
        public bool IsBurning => BurnTicksLeft > 0;

        /// <summary>Burning drains this much life per second.</summary>
        public const int BurnLifePerSecond = 4;

        /// <summary>Ticks of the regeneration buff left (from nectar). While active, life regenerates faster.</summary>
        public int RegenBuffTicksLeft { get; private set; }
        public bool HasRegenBuff => RegenBuffTicksLeft > 0;

        /// <summary>Extra life per second while the regeneration buff is active.</summary>
        public const int RegenBuffLifePerSecond = 1;

        /// <summary>
        /// Burn drain removed while the regeneration buff is active, life per second: the buff cancels
        /// 2 of the burn's 4 and then adds its own regeneration on top, leaving 1 per second.
        /// </summary>
        public const int RegenBuffBurnReductionPerSecond = 3;

        public int ImmunityTicksLeft { get; private set; }
        public int RespawnTicksLeft { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsImmune => ImmunityTicksLeft > 0;

        public event Action<int> Damaged;
        public event Action Died;
        public event Action Respawned;

        float m_RegenProgress;
        int m_BurnProgress;

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
            Extinguish();
            RegenBuffTicksLeft = 0;
            ImmunityTicksLeft = 0;
            m_RegenProgress = 0f;
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

        /// <summary>Starts or extends burning; a longer remaining burn is never shortened.</summary>
        public void Ignite(int ticks)
        {
            if (IsDead) return;
            BurnTicksLeft = System.Math.Max(BurnTicksLeft, ticks);
        }

        /// <summary>Starts or refreshes the regeneration buff; a longer remaining time is never shortened.</summary>
        public void GrantRegenBuff(int ticks)
        {
            if (IsDead) return;
            RegenBuffTicksLeft = System.Math.Max(RegenBuffTicksLeft, ticks);
        }

        public void Extinguish()
        {
            BurnTicksLeft = 0;
            m_BurnProgress = 0;
        }

        public void Kill()
        {
            if (IsDead) return;

            Extinguish();
            RegenBuffTicksLeft = 0;
            Current = 0;
            IsDead = true;
            ImmunityTicksLeft = 0;
            m_RegenProgress = 0f;
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
            // The buff counts for every tick it has left, including the last one.
            bool regenBuff = RegenBuffTicksLeft > 0;
            if (regenBuff) RegenBuffTicksLeft--;

            if (BurnTicksLeft > 0)
            {
                BurnTicksLeft--;
                // 8 units per tick against a 120-unit life point: 4 life per second (1 with the regeneration buff).
                int burnPerSecond = BurnLifePerSecond - (regenBuff ? RegenBuffBurnReductionPerSecond : 0);
                m_BurnProgress += 2 * burnPerSecond;
                while (m_BurnProgress >= 2 * SimUnits.TicksPerSecond && !IsDead)
                {
                    m_BurnProgress -= 2 * SimUnits.TicksPerSecond;
                    Current--;
                    if (Current <= 0) Kill();
                }

                m_RegenProgress = 0f;
                if (BurnTicksLeft == 0) m_BurnProgress = 0;
                return false;
            }

            float regenPerSecond = RegenPerSecond + ZoneRegenPerSecond + (regenBuff ? RegenBuffLifePerSecond : 0);
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
