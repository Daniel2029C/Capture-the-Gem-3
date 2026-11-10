using CTG.Core;
using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// Aim assist for the pickaxe: when the tile under the mouse is out of reach, finds the
    /// mineable block the player is effectively pointing at from their body.
    /// Works in whole tiles: one line per occupied body row (when aiming sideways) and one line
    /// from the leading column (when aiming up or down), each walked towards the mouse.
    /// </summary>
    public static class PickaxeSnap
    {
        const float k_AxisRatio = 3f;

        public static Vector2Int Snap(TileWorld world, TileEditor editor, PlayerMotor motor, int boost, Vector2 mousePx, Vector2Int hovered)
        {
            float ppt = SimUnits.PixelsPerTile;
            Vector2 centre = motor.Position + new Vector2(motor.Width, motor.Height) * 0.5f;

            // Near-axis aims snap to exactly horizontal / vertical.
            Vector2 aim = mousePx - centre;
            float ax = Mathf.Abs(aim.x), ay = Mathf.Abs(aim.y);
            if (ax > k_AxisRatio * ay) aim.y = 0f;
            else if (ay > k_AxisRatio * ax) aim.x = 0f;
            if (aim.sqrMagnitude < 0.0001f) return hovered;
            Vector2 target = centre + aim;

            int left = Mathf.FloorToInt(motor.Position.x / ppt);
            int right = Mathf.FloorToInt((motor.Position.x + motor.Width - 0.01f) / ppt);
            int bottom = Mathf.FloorToInt(motor.Position.y / ppt);
            int top = Mathf.FloorToInt((motor.Position.y + motor.Height - 0.01f) / ppt);
            Vector2Int centreTile = new Vector2Int(Mathf.FloorToInt(centre.x / ppt), Mathf.FloorToInt(centre.y / ppt));
            Vector2Int mouseTile = new Vector2Int(Mathf.FloorToInt(target.x / ppt), Mathf.FloorToInt(target.y / ppt));

            bool found = false;
            Vector2Int best = hovered;
            float bestDist = float.MaxValue;

            // Each start tile is paired with the mouse tile shifted by the same offset from the body centre.
            void Cast(Vector2Int start, bool rowLine)
            {
                Vector2Int end = mouseTile + (start - centreTile);
                if (!FirstSolid(world, start, end, rowLine, out Vector2Int tile)) return;
                if (!editor.CanMine(tile)) return;
                Vector2 tileCentre = (new Vector2(tile.x, tile.y) + new Vector2(0.5f, 0.5f)) * ppt;
                float d = (tileCentre - centre).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = tile; found = true; }
            }

            if (aim.x != 0f)
            {
                int column = aim.x > 0f ? right : left;
                for (int row = bottom; row <= top; row++) Cast(new Vector2Int(column, row), true);
            }
            if (aim.y != 0f)
            {
                int row = aim.y > 0f ? top : bottom;
                if (aim.x == 0f)
                {
                    for (int col = left; col <= right; col++) Cast(new Vector2Int(col, row), false);
                }
                else
                {
                    Cast(new Vector2Int(aim.x > 0f ? right : left, row), false);
                }
            }

            if (found && ItemUser.InTileRange(motor, best, boost)) return best;
            return hovered;
        }

        /// <summary>
        /// Walks tiles from start towards end and returns the first solid one. Each visited tile's
        /// neighbour one step further along the line's main travel axis is checked right after it.
        /// </summary>
        static bool FirstSolid(TileWorld world, Vector2Int start, Vector2Int end, bool rowLine, out Vector2Int tile)
        {
            int dx = Mathf.Abs(end.x - start.x), dy = Mathf.Abs(end.y - start.y);
            int sx = start.x < end.x ? 1 : -1, sy = start.y < end.y ? 1 : -1;
            int err = dx - dy;
            int x = start.x, y = start.y;
            while (world.InBounds(x, y))
            {
                if (world.IsSolid(x, y)) { tile = new Vector2Int(x, y); return true; }
                int bx = rowLine ? x + sx : x;
                int by = rowLine ? y : y + sy;
                if (world.InBounds(bx, by) && world.IsSolid(bx, by)) { tile = new Vector2Int(bx, by); return true; }
                if (x == end.x && y == end.y) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
            tile = default;
            return false;
        }
    }
}
