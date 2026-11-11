using System.Collections.Generic;
using UnityEngine;

namespace CTG.Combat
{
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
            return Teams.AreHostile(a.Team, b.Team);
        }
    }
}
