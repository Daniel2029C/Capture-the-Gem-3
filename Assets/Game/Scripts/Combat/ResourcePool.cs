using CTG.Core;

namespace CTG.Combat
{
    /// <summary>
    /// A pool of a resource with a maximum, regeneration and drain, advanced one tick at a time. Life and mana are
    /// both one of these, and any other meter that fills and empties can be too. Regeneration and drain are rates
    /// per second; the owner sets <see cref="DrainPerSecond"/>, <see cref="RegenBlocked"/> and any extra regeneration
    /// each tick from whatever affects the pool (status effects, the area the player is in).
    /// </summary>
    public class ResourcePool
    {
        public int Max { get; set; }
        public int Current { get; protected set; }

        /// <summary>Restored per second while below the maximum.</summary>
        public float RegenPerSecond { get; set; }
        /// <summary>Lost per second (set each tick by the owner from its status effects).</summary>
        public float DrainPerSecond { get; set; }
        /// <summary>No regeneration this tick (set each tick by the owner from its status effects).</summary>
        public bool RegenBlocked { get; set; }

        // Progress is counted in 1/60ths of a point so whole-number rates add up exactly
        // (summing 1/60 sixty times in floating point falls just short of 1).
        float m_RegenProgress;
        float m_DrainProgress;

        public ResourcePool(int max, float regenPerSecond)
        {
            Max = max;
            Current = max;
            RegenPerSecond = regenPerSecond;
        }

        /// <summary>Adds up to <paramref name="amount"/>, not past the maximum. Returns the amount actually added.</summary>
        public int Restore(int amount)
        {
            if (amount <= 0) return 0;

            int restored = System.Math.Max(0, System.Math.Min(amount, Max - Current));
            Current += restored;
            return restored;
        }

        /// <summary>Fills the pool and forgets any partial regeneration or drain.</summary>
        public virtual void Refill()
        {
            Current = Max;
            ResetProgress();
        }

        protected void ResetProgress()
        {
            m_RegenProgress = 0f;
            m_DrainProgress = 0f;
        }

        /// <summary>Called on the tick the drain takes the pool to zero. Nothing by default.</summary>
        protected virtual void OnDepleted() { }

        /// <summary>
        /// Applies one tick of drain and regeneration.
        /// </summary>
        /// <param name="extraRegenPerSecond">Regeneration on top of <see cref="RegenPerSecond"/> (for example from the area).</param>
        /// <param name="drainBlocksRegen">Whether draining stops regeneration (life) or the two net out (mana).</param>
        protected void TickFlow(float extraRegenPerSecond, bool drainBlocksRegen)
        {
            if (DrainPerSecond > 0f && Current > 0)
            {
                m_DrainProgress += DrainPerSecond;
                while (m_DrainProgress >= SimUnits.TicksPerSecond && Current > 0)
                {
                    m_DrainProgress -= SimUnits.TicksPerSecond;
                    Current--;
                    if (Current <= 0)
                    {
                        OnDepleted();
                        break;
                    }
                }
            }
            else
            {
                m_DrainProgress = 0f;
            }

            bool blocked = RegenBlocked || (drainBlocksRegen && DrainPerSecond > 0f);
            float regenPerSecond = blocked ? 0f : RegenPerSecond + extraRegenPerSecond;
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
