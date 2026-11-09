using System;

namespace CTG.Gameplay
{
    /// <summary>
    /// Tuning for a class's double jump: one extra jump in mid-air, available again after touching the
    /// ground (or walking off a ledge and pressing jump counts as the extra jump). While the extra jump is
    /// rising, running is faster.
    /// </summary>
    [Serializable]
    public sealed class DoubleJumpStats
    {
        /// <summary>Rise duration as a multiple of a normal jump's (15 ticks → 18 at 1.25).</summary>
        public float DurationMultiplier = 1.25f;
        /// <summary>Run acceleration multiplier while the extra jump is rising.</summary>
        public float RunAccelerationMultiplier = 1.5f;
        /// <summary>Top run speed multiplier while the extra jump is rising.</summary>
        public float MaxRunSpeedMultiplier = 1.25f;
    }
}
