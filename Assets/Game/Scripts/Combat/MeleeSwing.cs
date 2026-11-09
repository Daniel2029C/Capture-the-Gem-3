using UnityEngine;

namespace CTG.Combat
{
    /// <summary>
    /// Geometry of an overhead swing, shared by the hit test and the held-item visual so what you see is
    /// what hits. The item is a square sprite gripped at its bottom-left corner and pointing up-right;
    /// over the use it rotates from straight up (+45°) to down in front (-100°), mirrored when facing left.
    /// </summary>
    public static class MeleeSwing
    {
        /// <summary>Hand position relative to the hitbox's bottom-centre, px.</summary>
        public static readonly Vector2 HandOffsetPx = new(0f, 26f);

        const float k_StartAngle = 45f;
        const float k_EndAngle = -100f;

        /// <param name="ticksLeft">Ticks left in the use (fractional for render interpolation).</param>
        public static float Angle(float ticksLeft, int ticksTotal)
        {
            float progress = 1f - ticksLeft / Mathf.Max(1, ticksTotal);
            return Mathf.Lerp(k_StartAngle, k_EndAngle, Mathf.Clamp01(progress));
        }

        /// <summary>Axis-aligned bounds of the swung item this tick, px.</summary>
        public static Rect Hitbox(Rect ownerHitboxPx, int direction, float angleDegrees, float lengthPx)
        {
            Vector2 hand = new Vector2(ownerHitboxPx.center.x, ownerHitboxPx.yMin) + HandOffsetPx;
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
