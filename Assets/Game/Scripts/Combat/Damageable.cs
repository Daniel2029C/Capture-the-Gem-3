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

    /// <summary>All live hittable entities, for attacks to test against. One per match (see <see cref="MatchContext"/>).</summary>
    public sealed class DamageableRegistry
    {
        readonly List<IDamageable> m_All = new();

        public IReadOnlyList<IDamageable> All => m_All;

        public void Register(IDamageable target)
        {
            if (!m_All.Contains(target)) m_All.Add(target);
        }

        public void Unregister(IDamageable target) => m_All.Remove(target);

        public void Clear() => m_All.Clear();

        /// <summary>Whether an attack by <paramref name="a"/> may hit <paramref name="b"/>: never itself, otherwise by team.</summary>
        public static bool AreHostile(IDamageable a, IDamageable b)
        {
            if (a == b) return false;
            return Teams.AreHostile(a.Team, b.Team);
        }
    }
}
