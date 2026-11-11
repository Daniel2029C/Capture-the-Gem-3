using System;
using UnityEngine;

namespace CTG.Combat
{
    /// <summary>
    /// The shape of one weapon's swing: how far it turns, where it is gripped, and which part of the swing can hit.
    /// Part of the item's data, so each melee weapon can swing its own way.
    /// </summary>
    [Serializable]
    public struct SwingProfile
    {
        /// <summary>Angle of the item (degrees, 0 = pointing forward, positive = up) at the start of the use.</summary>
        public float StartAngle;
        /// <summary>Angle at the end of the use. The item turns evenly from start to end.</summary>
        public float EndAngle;
        /// <summary>Hand position relative to the hitbox's bottom-centre, px.</summary>
        public Vector2 HandOffsetPx;
        /// <summary>The swing only hits between these fractions of the use (0 = start, 1 = end).</summary>
        public float HitStart;
        public float HitEnd;

        /// <summary>
        /// The overhead swing: the item is a square sprite gripped at its bottom-left corner and pointing up-right, and
        /// rotates from straight up (+45°) to down in front (-100°), hitting throughout.
        /// </summary>
        public static readonly SwingProfile Overhead = new()
        {
            StartAngle = 45f, EndAngle = -100f, HandOffsetPx = new Vector2(0f, 26f), HitStart = 0f, HitEnd = 1f,
        };
    }

    /// <summary>
    /// Geometry of a swing, shared by the hit test and the held-item visual so what you see is what hits. The item is
    /// mirrored when facing left.
    /// </summary>
    public static class MeleeSwing
    {
        /// <param name="ticksLeft">Ticks left in the use (fractional for render interpolation).</param>
        static float Progress(float ticksLeft, int ticksTotal) =>
            Mathf.Clamp01(1f - ticksLeft / Mathf.Max(1, ticksTotal));

        /// <param name="ticksLeft">Ticks left in the use (fractional for render interpolation).</param>
        public static float Angle(float ticksLeft, int ticksTotal, in SwingProfile profile) =>
            Mathf.Lerp(profile.StartAngle, profile.EndAngle, Progress(ticksLeft, ticksTotal));

        /// <summary>Whether this point of the use is inside the part of the swing that can hit.</summary>
        public static bool CanHit(float ticksLeft, int ticksTotal, in SwingProfile profile)
        {
            float progress = Progress(ticksLeft, ticksTotal);
            return progress >= profile.HitStart && progress <= profile.HitEnd;
        }

        /// <summary>Axis-aligned bounds of the swung item this tick, px.</summary>
        public static Rect Hitbox(Rect ownerHitboxPx, int direction, float angleDegrees, float lengthPx, in SwingProfile profile)
        {
            Vector2 hand = new Vector2(ownerHitboxPx.center.x, ownerHitboxPx.yMin) + profile.HandOffsetPx;
            float radians = angleDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);

            Vector2 min = hand;
            Vector2 max = hand;
            Vector2[] corners = { new(lengthPx, 0f), new(0f, lengthPx), new(lengthPx, lengthPx) };
            foreach (Vector2 c in corners)
            {
                var rotated = new Vector2(c.x * cos - c.y * sin, c.x * sin + c.y * cos);
                rotated.x *= direction;
                Vector2 p = hand + rotated;
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
