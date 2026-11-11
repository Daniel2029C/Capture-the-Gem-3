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

        /// <summary>
        /// Speed of a standard hit that has a heading: it pushes along <see cref="HitInfo.Aim"/> at this speed (px/tick),
        /// the same as the fixed launch's horizontal speed, so a level hit pushes exactly as far as before.
        /// </summary>
        public const float AimedKnockbackSpeed = 4.5f;

        /// <summary>
        /// Launch velocity (px/tick, y-up) for a hit. With a heading (<see cref="HitInfo.Aim"/>) the push goes along it,
        /// scaled per axis by the weapon's knockback; without one it is the fixed launch (away from the attacker, up).
        /// </summary>
        public static Vector2 KnockbackVelocity(in HitInfo hit)
        {
            if (hit.Aim.sqrMagnitude > 0.0001f)
            {
                Vector2 aim = hit.Aim.normalized;
                return new Vector2(aim.x * hit.Knockback.X, aim.y * hit.Knockback.Y) * AimedKnockbackSpeed;
            }

            return new Vector2(BaseKnockbackVelocity.x * hit.Knockback.X * hit.Direction, BaseKnockbackVelocity.y * hit.Knockback.Y);
        }
    }
}
