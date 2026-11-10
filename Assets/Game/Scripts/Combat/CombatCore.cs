using System.Collections.Generic;
using CTG.Items;
using UnityEngine;

namespace CTG.Combat
{
    /// <summary>Teams: 0 = neutral (hittable by everyone), 1 = red, 2 = blue, 3 = monsters.</summary>
    public enum Team
    {
        Neutral = 0,
        Red = 1,
        Blue = 2,
        /// <summary>Hostile creatures (mobs): hostile to every player, never to each other.</summary>
        Monster = 3,
    }

    public static class TeamColors
    {
        public static Color For(Team team) => team switch
        {
            Team.Red => new Color32(225, 55, 55, 255),
            Team.Blue => new Color32(60, 110, 255, 255),
            Team.Monster => new Color32(160, 80, 200, 255),
            _ => new Color32(70, 70, 80, 255),
        };
    }

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
