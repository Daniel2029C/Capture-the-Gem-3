using CTG.Items;

namespace CTG.Combat
{
    /// <summary>
    /// How hard a hit pushes, as multipliers on the standard launch (<see cref="CombatRules.BaseKnockbackVelocity"/>):
    /// X scales the horizontal push (away from the attacker) and Y the upward launch independently. (1, 1) is a
    /// standard hit, (0, 0) pushes nothing, (1, 0) is a flat shove, (0.5, 2) pops the target up. A plain number
    /// converts to the same value on both axes, so a weapon can still just say <c>Knockback = 0.7f</c>.
    /// </summary>
    [System.Serializable]
    public struct KnockbackScale
    {
        public float X;
        public float Y;

        public KnockbackScale(float x, float y)
        {
            X = x;
            Y = y;
        }

        public bool IsZero => X == 0f && Y == 0f;

        public static implicit operator KnockbackScale(float uniform) => new(uniform, uniform);
    }

    /// <summary>One attack landing on a target. Damage is deterministic: no variance, no crits.</summary>
    public struct HitInfo
    {
        public int Damage;
        /// <summary>Weapon knockback stat per axis. (1, 1) = the standard player knockback; (0, 0) = none.</summary>
        public KnockbackScale Knockback;
        /// <summary>+1 pushes the target right, -1 left.</summary>
        public int Direction;
        public IDamageable Source;
        /// <summary>The item whose swing dealt this hit (melee); None for projectiles, mobs and everything else.</summary>
        public ItemId Weapon;
    }
}
