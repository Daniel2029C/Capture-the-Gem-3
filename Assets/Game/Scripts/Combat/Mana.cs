using CTG.Core;

namespace CTG.Combat
{
    /// <summary>Mana pool with flat per-second regeneration, advanced per tick.</summary>
    public class Mana
    {
        public int Max { get; set; }
        public int Current { get; private set; }
        public float RegenPerSecond { get; set; }

        float m_RegenProgress;

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
        }

        public void Tick()
        {
            if (Current >= Max || RegenPerSecond <= 0f)
            {
                if (Current > Max) Current = Max;
                m_RegenProgress = 0f;
                return;
            }

            // Progress is counted in 1/60ths of a point so whole-number rates add up exactly
            // (summing 1/60 sixty times in floating point falls just short of 1).
            m_RegenProgress += RegenPerSecond;
            while (m_RegenProgress >= SimUnits.TicksPerSecond && Current < Max)
            {
                m_RegenProgress -= SimUnits.TicksPerSecond;
                Current++;
            }
        }
    }
}
