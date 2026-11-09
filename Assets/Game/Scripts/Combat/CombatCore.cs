using System.Collections.Generic;
using UnityEngine;

namespace CTG.Combat
{
    /// <summary>Teams use CTG2's numbering: 0 = neutral (hittable by everyone), 1 = red, 3 = blue.</summary>
    public enum Team
    {
        Neutral = 0,
        Red = 1,
        Blue = 3,
    }

    public static class TeamColors
    {
        public static Color For(Team team) => team switch
        {
            Team.Red => new Color32(225, 55, 55, 255),
            Team.Blue => new Color32(60, 110, 255, 255),
            _ => new Color32(70, 70, 80, 255),
        };
    }

    /// <summary>One attack landing on a target. Damage is deterministic: no variance, no crits.</summary>
    public struct HitInfo
    {
        public int Damage;
        /// <summary>Weapon knockback stat. 1 = the standard player knockback; 0 = none.</summary>
        public float Knockback;
        /// <summary>+1 pushes the target right, -1 left.</summary>
        public int Direction;
        public IDamageable Source;
        /// <summary>Dealt by a projectile (arrows, blasts) rather than a melee swing.</summary>
        public bool FromProjectile;
    }

    public interface IDamageable
    {
        Team Team { get; }
        /// <summary>Hitbox in pixels (bottom-left origin, y-up).</summary>
        Rect HitboxPx { get; }
        bool CanBeHit { get; }
        /// <summary>Returns the damage actually dealt (0 if the hit was ignored, e.g. immunity).</summary>
        int ApplyHit(in HitInfo hit);
    }

    /// <summary>All live hittable entities, for attacks to test against.</summary>
    public static class DamageableRegistry
    {
        static readonly List<IDamageable> s_All = new();

        public static IReadOnlyList<IDamageable> All => s_All;

        public static void Register(IDamageable target)
        {
            if (!s_All.Contains(target)) s_All.Add(target);
        }

        public static void Unregister(IDamageable target) => s_All.Remove(target);

        public static bool AreHostile(IDamageable a, IDamageable b)
        {
            if (a == b) return false;
            if (a.Team == Team.Neutral || b.Team == Team.Neutral) return true;
            return a.Team != b.Team;
        }
    }

    public static class CombatRules
    {
        /// <summary>
        /// Launch velocity (px/tick, y-up) of a knockback-1 hit. A weapon's knockback stat scales both
        /// components: 0.5 knockback launches half as far, 2 twice as far.
        /// </summary>
        public static readonly Vector2 BaseKnockbackVelocity = new(4.5f, 3.5f);

        /// <summary>Each point of defense removes half a point of damage. Hits always deal at least 1.</summary>
        public const float DefenseFactor = 0.5f;

        public static int ComputeDamage(int rawDamage, int defense)
        {
            if (rawDamage <= 0) return 0;

            int reduced = Mathf.RoundToInt(rawDamage - defense * DefenseFactor);
            return Mathf.Max(1, reduced);
        }

        public static Vector2 KnockbackVelocity(float knockback, int direction) =>
            new(BaseKnockbackVelocity.x * knockback * direction, BaseKnockbackVelocity.y * knockback);
    }
}
