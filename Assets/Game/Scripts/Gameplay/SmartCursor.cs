using System;
using System.Collections.Generic;
using CTG.Combat;
using CTG.Core;
using CTG.Items;
using CTG.World;
using UnityEngine;

namespace CTG.Gameplay
{
    /// <summary>
    /// Always-on cursor assist. Turns the tile under the mouse into the tile the item will actually act on.
    ///
    /// Solid blocks: when the mouse is over an empty tile within reach, the cursor snaps to the nearest
    /// empty tile (to the mouse) that touches a solid tile and isn't overlapped by a player or target. Over
    /// an occupied tile, or beyond reach, it doesn't snap.
    ///
    /// Pickaxe: when the mouse is beyond reach (over a block or empty space), a line is walked from the
    /// player's leading edge toward the mouse and the cursor snaps to the nearest mineable block it runs into,
    /// if that is within reach. Within reach nothing changes: the tile under the mouse is used.
    ///
    /// The line walk is done in a y-down grid, with the same stepping rules the original design used, so
    /// its snapping choices carry over unchanged.
    /// </summary>
    public sealed class SmartCursor
    {
        readonly TileWorld m_World;
        readonly TileEditor m_Editor;

        // Scratch lists, reused between calls.
        readonly List<Vector2Int> m_Points = new();
        readonly List<Vector2Int> m_Endpoints = new();
        readonly List<Vector2Int> m_Targets = new();

        /// <summary>Whether the owner may mine a tile (set by the owner); null = anything breakable. Tiles it refuses are never picked.</summary>
        public System.Func<Vector2Int, bool> CanMineTile;

        bool CanMine(Vector2Int tile) => CanMineTile != null ? CanMineTile(tile) : m_Editor.CanMine(tile);

        public SmartCursor(TileWorld world, TileEditor editor)
        {
            m_World = world;
            m_Editor = editor;
        }

        /// <param name="mousePx">Mouse position in pixels, y-up.</param>
        /// <param name="hovered">The tile under the mouse.</param>
        public Vector2Int Resolve(ItemDef def, Team team, PlayerMotor motor, int bonusReach, Vector2 mousePx, Vector2Int hovered)
        {
            if (def == null) return hovered;

            int boost = def.TileBoost + bonusReach;

            if (def.PlacesTile && TileDefs.Get(def.TileFor(team)).Solid)
            {
                return SnapBlock(motor, boost, mousePx, hovered);
            }

            if (def.IsPickaxe && !ItemUser.InTileRange(motor, hovered, boost))
            {
                return SnapPickaxe(motor, boost, mousePx, hovered);
            }

            return hovered;
        }

        // ---- Blocks ---------------------------------------------------------------------------------------

        Vector2Int SnapBlock(PlayerMotor motor, int boost, Vector2 mousePx, Vector2Int hovered)
        {
            // No snapping over an occupied tile or outside reach.
            if (m_World.Get(hovered.x, hovered.y) != TileId.Air) return hovered;
            if (!ItemUser.InTileRange(motor, hovered, boost)) return hovered;

            int rangeX = ItemUser.TileRangeX + boost;
            int rangeY = ItemUser.TileRangeY + boost;
            int x0 = Mathf.FloorToInt(motor.Position.x / SimUnits.PixelsPerTile) - rangeX - 1;
            int x1 = Mathf.FloorToInt((motor.Position.x + motor.Width) / SimUnits.PixelsPerTile) + rangeX + 1;
            int y0 = Mathf.FloorToInt(motor.Position.y / SimUnits.PixelsPerTile) - rangeY - 1;
            int y1 = Mathf.FloorToInt((motor.Position.y + motor.Height) / SimUnits.PixelsPerTile) + rangeY + 1;

            float best = -1f;
            Vector2Int bestTile = hovered;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var tile = new Vector2Int(x, y);
                    if (!m_World.InBounds(x, y) || m_World.Get(x, y) != TileId.Air) continue;
                    if (!ItemUser.InTileRange(motor, tile, boost)) continue;
                    if (!m_Editor.HasSolidNeighbour(tile)) continue;
                    if (OverlapsAnyone(tile)) continue;

                    float distance = Vector2.Distance(TileCenterPx(tile), mousePx);
                    if (best < 0f || distance < best)
                    {
                        best = distance;
                        bestTile = tile;
                    }
                }
            }

            return best >= 0f ? bestTile : hovered;
        }

        static Vector2 TileCenterPx(Vector2Int tile) =>
            new((tile.x + 0.5f) * SimUnits.PixelsPerTile, (tile.y + 0.5f) * SimUnits.PixelsPerTile);

        static bool OverlapsAnyone(Vector2Int tile)
        {
            var rect = new Rect(tile.x * SimUnits.PixelsPerTile, tile.y * SimUnits.PixelsPerTile,
                                SimUnits.PixelsPerTile, SimUnits.PixelsPerTile);
            IReadOnlyList<IDamageable> all = DamageableRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (rect.Overlaps(all[i].HitboxPx)) return true;
            }

            return false;
        }

        // ---- Pickaxe --------------------------------------------------------------------------------------
        // Everything below works in a y-down grid (row 0 at the top), converting at the edges.

        int Down(int y) => m_World.Height - 1 - y;
        float DownPx(float yPx) => m_World.Height * SimUnits.PixelsPerTile - yPx;

        bool SolidDown(int x, int yDown) => m_World.InBounds(x, Down(yDown)) && m_World.IsSolid(x, Down(yDown));

        Vector2Int SnapPickaxe(PlayerMotor motor, int boost, Vector2 mousePx, Vector2Int hovered)
        {
            int width = Mathf.RoundToInt(motor.Width);
            int height = Mathf.RoundToInt(motor.Height);
            float posX = motor.Position.x;
            float posY = DownPx(motor.Position.y + motor.Height); // hitbox top-left, y-down
            var center = new Vector2(posX + width * 0.5f, posY + height * 0.5f);
            var mouse = new Vector2(mousePx.x, DownPx(mousePx.y));

            Vector2 toMouse = mouse - center;
            int dirX = Math.Sign(toMouse.x);
            int dirY = Math.Sign(toMouse.y);

            // Mostly horizontal or mostly vertical aim is treated as exactly that.
            if (Mathf.Abs(toMouse.x) > Mathf.Abs(toMouse.y) * 3f)
            {
                dirY = 0;
                mouse.y = center.y;
            }

            if (Mathf.Abs(toMouse.y) > Mathf.Abs(toMouse.x) * 3f)
            {
                dirX = 0;
                mouse.x = center.x;
            }

            // Aiming up and sideways starts the line from the feet and walks upward.
            int rowStep = dirY == -1 && dirX != 0 ? -1 : 1;
            int startColumn = (int)((posX + width / 2 + (width / 2 - 1) * dirX) / SimUnits.PixelsPerTile);
            int startRow = (int)((posY + 0.1) / SimUnits.PixelsPerTile);
            if (rowStep == -1) startRow = (int)((posY + height - 1f) / SimUnits.PixelsPerTile);

            int tilesWide = width / 16 + (width % 16 != 0 ? 1 : 0);
            int tilesHigh = height / 16 + (height % 16 != 0 ? 1 : 0);

            m_Points.Clear();
            m_Endpoints.Clear();
            m_Targets.Clear();

            if (dirX != 0)
            {
                for (int i = 0; i < tilesHigh; i++) m_Points.Add(new Vector2Int(startColumn, startRow + i * rowStep));
            }

            if (dirY != 0)
            {
                for (int i = 0; i < tilesWide; i++)
                    m_Points.Add(new Vector2Int((int)(posX / SimUnits.PixelsPerTile) + i, startRow));
            }

            int endColumn = (int)((mouse.x + (width / 2 - 1) * dirX) / SimUnits.PixelsPerTile);
            int endRow = (int)((mouse.y + 0.1 - (height / 2 + 1)) / SimUnits.PixelsPerTile);
            if (rowStep == -1) endRow = (int)((mouse.y + height / 2 - 1f) / SimUnits.PixelsPerTile);

            if (dirX != 0)
            {
                for (int i = 0; i < tilesHigh; i++) m_Endpoints.Add(new Vector2Int(endColumn, endRow + i * rowStep));
            }

            if (dirY != 0)
            {
                for (int i = 0; i < tilesWide; i++)
                    m_Endpoints.Add(new Vector2Int((int)((mouse.x - width / 2) / SimUnits.PixelsPerTile) + i, endRow));
            }

            while (m_Points.Count > 0 && m_Endpoints.Count > 0)
            {
                Vector2Int point = m_Points[0];
                Vector2Int end = m_Endpoints[0];
                m_Points.RemoveAt(0);
                m_Endpoints.RemoveAt(0);

                if (!HitLine(point.x, point.y, end.x, end.y, dirX, -dirY, out Vector2Int hit)) continue;

                if (hit != end) m_Targets.Add(hit);
                if (SolidDown(hit.x, hit.y)) m_Targets.Add(hit);
            }

            // Keep only what the pickaxe can actually break, and take the one closest to the player.
            float bestDistance = -1f;
            Vector2Int best = hovered;
            foreach (Vector2Int target in m_Targets)
            {
                Vector2Int tile = new(target.x, Down(target.y));
                if (!CanMine(tile)) continue;

                var tileCenterDown = new Vector2((target.x + 0.5f) * SimUnits.PixelsPerTile,
                                                 (target.y + 0.5f) * SimUnits.PixelsPerTile);
                float distance = Vector2.Distance(tileCenterDown, center);
                if (bestDistance < 0f || distance < bestDistance)
                {
                    bestDistance = distance;
                    best = tile;
                }
            }

            m_Targets.Clear();
            return bestDistance >= 0f && ItemUser.InTileRange(motor, best, boost) ? best : hovered;
        }

        /// <summary>
        /// Walks the tile line from (x1, y1) toward (x2, y2), alternating steps along both axes in proportion
        /// to the line's slope, and stops at the first solid tile (also checking the tile beside the path in the
        /// direction of travel). Returns false only if the walk itself could not run.
        /// </summary>
        bool HitLine(int x1, int y1, int x2, int y2, int ignoreX, int ignoreY, out Vector2Int hit)
        {
            int maxX = m_World.Width - 1, maxY = m_World.Height - 1;
            x1 = Mathf.Clamp(x1, 1, maxX);
            x2 = Mathf.Clamp(x2, 1, maxX);
            y1 = Mathf.Clamp(y1, 1, maxY);
            y2 = Mathf.Clamp(y2, 1, maxY);

            float deltaX = Mathf.Abs(x1 - x2);
            float deltaY = Mathf.Abs(y1 - y2);
            if (deltaX == 0f && deltaY == 0f)
            {
                hit = new Vector2Int(x1, y1);
                return true;
            }

            float stepX = 1f, stepY = 1f;
            if (deltaX == 0f || deltaY == 0f)
            {
                if (deltaX == 0f) stepX = 0f;
                if (deltaY == 0f) stepY = 0f;
            }
            else if (deltaX > deltaY)
            {
                stepX = deltaX / deltaY;
            }
            else
            {
                stepY = deltaY / deltaX;
            }

            float accumulatedX = 0f, accumulatedY = 0f;
            int mode = y1 < y2 ? 2 : 1;
            int remainingX = (int)deltaX, remainingY = (int)deltaY;
            int signX = Math.Sign(x2 - x1), signY = Math.Sign(y2 - y1);
            bool finished = false, finishedLate = false;
            int x = x1, y = y1;

            for (int guard = 0; guard < 100000; guard++)
            {
                if (mode == 2)
                {
                    accumulatedX += stepX;
                    int steps = (int)accumulatedX;
                    accumulatedX %= 1f;
                    for (int i = 0; i < steps; i++)
                    {
                        if (ignoreY != -1 && signY < 0 && SolidDown(x, y - 1)) { hit = new Vector2Int(x, y - 1); return true; }
                        if (ignoreY != 1 && signY > 0 && SolidDown(x, y + 1)) { hit = new Vector2Int(x, y + 1); return true; }
                        if (SolidDown(x, y)) { hit = new Vector2Int(x, y); return true; }

                        if (remainingX == 0 && remainingY == 0)
                        {
                            finished = true;
                            break;
                        }

                        x += signX;
                        remainingX--;
                        if (remainingX == 0 && remainingY == 0 && steps == 1) finishedLate = true;
                    }

                    if (remainingY != 0) mode = 1;
                }
                else
                {
                    accumulatedY += stepY;
                    int steps = (int)accumulatedY;
                    accumulatedY %= 1f;
                    for (int i = 0; i < steps; i++)
                    {
                        if (ignoreX != -1 && signX < 0 && SolidDown(x - 1, y)) { hit = new Vector2Int(x - 1, y); return true; }
                        if (ignoreX != 1 && signX > 0 && SolidDown(x + 1, y)) { hit = new Vector2Int(x + 1, y); return true; }
                        if (SolidDown(x, y)) { hit = new Vector2Int(x, y); return true; }

                        if (remainingX == 0 && remainingY == 0)
                        {
                            finished = true;
                            break;
                        }

                        y += signY;
                        remainingY--;
                        if (remainingX == 0 && remainingY == 0 && steps == 1) finishedLate = true;
                    }

                    if (remainingX != 0) mode = 2;
                }

                if (SolidDown(x, y)) { hit = new Vector2Int(x, y); return true; }
                if (finished || finishedLate) break;
            }

            hit = new Vector2Int(x, y);
            return true;
        }
    }
}
