using UnityEngine;

namespace CTG.Combat
{
    public static class CombatRules
    {
        /// <summary>
        /// Launch velocity (px/tick, y-up) of a standard (1, 1) knockback hit. A weapon's knockback stat scales the
        /// horizontal and vertical components separately: (0.5, 0.5) launches half as far, (1, 2) twice as high.
        /// </summary>
        public static readonly Vector2 BaseKnockbackVelocity = new(4.5f, 3.5f);

        /// <summary>Each point of defense removes this many points of damage (1:1).</summary>
        public const float DefenseFactor = 1f;

        /// <summary>
        /// Damage taken from a hit: defense is subtracted first (never below 1), then the percentage reduction (0 to 1)
        /// scales what is left, rounded to the nearest whole number and never below 1, except that a reduction of 1
        /// (or more) makes the hit deal 0.
        /// </summary>
        public static int ComputeDamage(int rawDamage, int defense, float reduction = 0f)
        {
            if (rawDamage <= 0) return 0;

            reduction = Mathf.Clamp01(reduction);
            if (reduction >= 1f) return 0;

            int damage = Mathf.Max(1, Mathf.RoundToInt(rawDamage - defense * DefenseFactor));
            if (reduction > 0f) damage = Mathf.Max(1, Mathf.RoundToInt(damage * (1f - reduction)));
            return damage;
        }

        /// <param name="direction">+1 pushes right, -1 left; it flips only the horizontal part.</param>
        public static Vector2 KnockbackVelocity(KnockbackScale knockback, int direction) =>
            new(BaseKnockbackVelocity.x * knockback.X * direction, BaseKnockbackVelocity.y * knockback.Y);
    }
}
