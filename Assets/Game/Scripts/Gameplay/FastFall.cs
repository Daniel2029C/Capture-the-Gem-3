using System;

namespace CTG.Gameplay
{
    /// <summary>
    /// Holding down while falling in the air speeds the fall up: gravity is multiplied while falling, and the fall
    /// speed cap is unchanged. It does nothing on the ground, in liquid, or while rising.
    /// </summary>
    [Serializable]
    public sealed class FastFallMobility : Mobility
    {
        /// <summary>Gravity multiplier while fast-falling.</summary>
        public float GravityMultiplier = 2f;

        public override void Apply(PlayerCharacter player) => player.Motor.FastFall = this;

        public override void Remove(PlayerCharacter player)
        {
            if (player.Motor.FastFall == this) player.Motor.FastFall = null;
        }
    }
}
