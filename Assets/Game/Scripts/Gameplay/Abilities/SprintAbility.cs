using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>Sprint tuning. Durations are in ticks.</summary>
    [System.Serializable]
    public sealed class SprintStats
    {
        /// <summary>Top running speed multiplier while sprinting.</summary>
        public float SpeedMultiplier = 1.3f;
        /// <summary>Run acceleration multiplier while sprinting.</summary>
        public float AccelerationMultiplier = 3f;
        /// <summary>Sprint time from a full reserve (1.5 s).</summary>
        public int MaxTicks = 90;
        /// <summary>Ticks of not sprinting before the reserve starts to refill.</summary>
        public int IdleTicksBeforeRecharge = 60;
        /// <summary>Reserve ticks regained per tick once refilling (full in about 2 s).</summary>
        public float RechargePerTick = 0.75f;
    }

    /// <summary>
    /// Hold the key to sprint. The reserve drains while the key is held and refills once the key has been
    /// released for <see cref="SprintStats.IdleTicksBeforeRecharge"/> ticks. Running dry ends the sprint, and
    /// it can't resume until the key is released and pressed again.
    /// </summary>
    public sealed class SprintAbility : Ability
    {
        readonly SprintStats m_Stats;
        float m_Reserve;
        int m_IdleTicks;
        bool m_Sprinting;
        bool m_Exhausted;

        public SprintAbility(SprintStats stats)
        {
            m_Stats = stats;
            m_Reserve = stats.MaxTicks;
        }

        public override string Name => "Sprint";
        public override float Fill => m_Reserve / m_Stats.MaxTicks;
        public override bool IsActive => m_Sprinting;
        public override float RunSpeedScale => m_Sprinting ? m_Stats.SpeedMultiplier : 1f;
        public override float RunAccelerationScale => m_Sprinting ? m_Stats.AccelerationMultiplier : 1f;
        public override string Status => m_Sprinting ? "Sprinting" : m_Exhausted ? "Winded" : $"{m_Reserve / 60f:0.0}s";

        public override void Reset()
        {
            m_Reserve = m_Stats.MaxTicks;
            m_IdleTicks = 0;
            m_Sprinting = false;
            m_Exhausted = false;
        }

        public override void Tick(in AbilityContext context, bool pressed, bool held)
        {
            if (!held) m_Exhausted = false;

            m_Sprinting = held && !m_Exhausted && m_Reserve > 0f;
            if (m_Sprinting)
            {
                m_IdleTicks = 0;
                m_Reserve -= 1f;
                if (m_Reserve <= 0f)
                {
                    // The last tick of reserve still counts as sprinting; the next one is winded.
                    m_Reserve = 0f;
                    m_Exhausted = true;
                }

                return;
            }

            // Winded with the key still down also counts as not sprinting, so the refill timer runs.
            if (m_IdleTicks < m_Stats.IdleTicksBeforeRecharge) m_IdleTicks++;
            else m_Reserve = Mathf.Min(m_Stats.MaxTicks, m_Reserve + m_Stats.RechargePerTick * context.CooldownRate);
        }
    }
}
