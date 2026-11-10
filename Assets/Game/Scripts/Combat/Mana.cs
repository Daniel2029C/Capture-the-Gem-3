using CTG.Core;

namespace CTG.Combat
{
    /// <summary>Mana pool with flat per-second regeneration, advanced per tick.</summary>
    public class Mana
    {
        public int Max { get; set; }
        public int Current { get; private set; }
        public float RegenPerSecond { get; set; }

        /// <summary>Mana lost per second (set each tick by the owner from its status effects). Regeneration still applies unless blocked.</summary>
        public float DrainPerSecond { get; set; }
        /// <summary>No regeneration this tick (set each tick by the owner from its status effects).</summary>
        public bool RegenBlocked { get; set; }

        float m_RegenProgress;
        float m_DrainProgress;

        public Mana(int max, float regenPerSecond)
        {
            Max = max;
            Current = max;
            RegenPerSecond = regenPerSecond;
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || Current < amount) return false;

            Current -= amount;
            return true;
        }

        public void Refill()
        {
            Current = Max;
            m_RegenProgress = 0f;
            m_DrainProgress = 0f;
        }

        public void Tick()
        {
            // Progress is counted in 1/60ths of a point so whole-number rates add up exactly
            // (summing 1/60 sixty times in floating point falls just short of 1).
            if (DrainPerSecond > 0f && Current > 0)
            {
                m_DrainProgress += DrainPerSecond;
                while (m_DrainProgress >= SimUnits.TicksPerSecond && Current > 0)
                {
                    m_DrainProgress -= SimUnits.TicksPerSecond;
                    Current--;
                }
            }
            else
            {
                m_DrainProgress = 0f;
            }

            float regenPerSecond = RegenBlocked ? 0f : RegenPerSecond;
            if (Current >= Max || regenPerSecond <= 0f)
            {
                if (Current > Max) Current = Max;
                m_RegenProgress = 0f;
                return;
            }

            m_RegenProgress += regenPerSecond;
            while (m_RegenProgress >= SimUnits.TicksPerSecond && Current < Max)
            {
                m_RegenProgress -= SimUnits.TicksPerSecond;
                Current++;
            }
        }
    }
}
