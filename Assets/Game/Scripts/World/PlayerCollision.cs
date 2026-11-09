using CTG.Core;
using UnityEngine;

namespace CTG.World
{
    /// <summary>
    /// Player-vs-tiles collision with real slopes and half blocks, step-up and step-down. It is a port of the
    /// original tile collision stack, so its movement feel is kept: slopes are handled by a separate pass that
    /// moves the box along the surface, a pull or release can hop a one-tile ledge, and so on. Its quirks can
    /// also leave the box inside a wall, so every result is checked and any move that would end inside solid
    /// tiles is redone without the step-up or replaced by plain sliding: players never clip into walls.
    ///
    /// The ported code works in y-down pixels with the box position at its top-left, so everything is
    /// converted on the way in and out; tile rows are flipped the same way. Outside the world counts as solid.
    /// Platforms and gravity flips don't exist in this game and are left out.
    /// </summary>
    public sealed class PlayerCollision
    {
        public struct Result
        {
            /// <summary>Bottom-left, px, y-up.</summary>
            public Vector2 Position;
            /// <summary>px/tick, y-up.</summary>
            public Vector2 Velocity;
            /// <summary>Standing on something: was pressing down and is now stopped.</summary>
            public bool Grounded;
            public bool HitCeiling;
            /// <summary>Visual lag to add after a step: positive = draw the box lower than it is, px.</summary>
            public float StepVisual;
        }

        readonly TileWorld m_World;
        bool m_SkipStepDown;
        readonly int m_WorldHeightTiles;
        readonly float m_WorldHeightPx;

        /// <summary>Set by the last tile collision when the box was stopped by a ceiling.</summary>
        bool m_Up;
        float m_StepVisual;

        public PlayerCollision(TileWorld world)
        {
            m_World = world;
            m_WorldHeightTiles = world.Height;
            m_WorldHeightPx = world.Height * SimUnits.PixelsPerTile;
        }

        // ---- Tile access, in y-down tile rows -------------------------------------------------------

        bool InWorld(int x, int yd) => x >= 0 && x < m_World.Width && yd >= 0 && yd < m_WorldHeightTiles;

        bool Solid(int x, int yd) => m_World.IsSolid(x, m_WorldHeightTiles - 1 - yd);

        TileShape Shape(int x, int yd) =>
            InWorld(x, yd) && Solid(x, yd) ? m_World.GetShape(x, m_WorldHeightTiles - 1 - yd) : TileShape.Full;

        bool Half(int x, int yd) => Shape(x, yd) == TileShape.Half;

        /// <summary>1 = floor sloping down to the right, 2 = down to the left, 3 and 4 = ceiling slopes; 0 = none.</summary>
        int Slope(int x, int yd) => Shape(x, yd) switch
        {
            TileShape.SlopeDownLeft => 1,
            TileShape.SlopeDownRight => 2,
            TileShape.SlopeUpLeft => 3,
            TileShape.SlopeUpRight => 4,
            _ => 0,
        };

        bool TopSlope(int x, int yd)
        {
            int slope = Slope(x, yd);
            return slope == 1 || slope == 2;
        }

        // ---- Entry point ----------------------------------------------------------------------------

        /// <summary>
        /// One tick of collision: slope walking, step-down, step-up (if allowed), tile collision with the
        /// current liquid's movement scale, then slope collision.
        /// </summary>
        /// <param name="gravity">Gravity this tick, px/tick² (positive).</param>
        /// <param name="liquid">Scales the displacement: lava half, nectar a quarter.</param>
        /// <param name="allowStepUp">False while hooked.</param>
        public Result Move(Vector2 positionUp, Vector2 velocityUp, float width, float height, float gravity,
                           LiquidType liquid, bool allowStepUp, bool dropThrough, bool ignorePlatforms)
        {
            // Standing on a platform, the step-down onto whatever solid lies below must not run: the platform
            // is what the player stands on, and stepping off it only to be put back would pile up visual lag.
            m_SkipStepDown = !dropThrough && !ignorePlatforms && StandingOnPlatform(positionUp, width);
            Result result = MoveSolid(positionUp, velocityUp, width, height, gravity, liquid, allowStepUp);
            m_SkipStepDown = false;
            if (!ignorePlatforms) LandOnPlatforms(positionUp, velocityUp, width, ref result, dropThrough);
            return result;
        }

        bool StandingOnPlatform(Vector2 positionUp, float width)
        {
            float row = positionUp.y / 16f;
            if (Mathf.Abs(row - Mathf.Round(row)) > 0.001f) return false;

            int platformRow = Mathf.RoundToInt(row) - 1;
            int x0 = Mathf.FloorToInt(positionUp.x / 16f);
            int x1 = Mathf.FloorToInt((positionUp.x + width - 0.001f) / 16f);
            for (int x = x0; x <= x1; x++)
            {
                if (m_World.InBounds(x, platformRow) && TileDefs.Get(m_World.Get(x, platformRow)).OneWay) return true;
            }

            return false;
        }

        /// <summary>
        /// One-way platforms (glass): a platform catches a player who was entirely above its top at the start
        /// of the tick and would end the tick below it. It never blocks from below or the sides. Holding down
        /// passes through unless falling faster than 1 px/tick.
        /// </summary>
        void LandOnPlatforms(Vector2 startUp, Vector2 velocityUp, float width, ref Result result, bool dropThrough)
        {
            if (velocityUp.y > 0f || (dropThrough && -velocityUp.y <= 1f)) return;

            int x0 = Mathf.FloorToInt(result.Position.x / 16f);
            int x1 = Mathf.FloorToInt((result.Position.x + width - 0.001f) / 16f);
            int yHigh = Mathf.FloorToInt(startUp.y / 16f);
            int yLow = Mathf.FloorToInt(result.Position.y / 16f);
            for (int row = yHigh; row >= yLow; row--)
            {
                float top = (row + 1) * 16f;
                if (startUp.y < top - 0.01f || result.Position.y >= top) continue;

                for (int x = x0; x <= x1; x++)
                {
                    if (!m_World.InBounds(x, row) || !TileDefs.Get(m_World.Get(x, row)).OneWay) continue;

                    result.Position.y = top;
                    result.Velocity.y = 0f;
                    result.Grounded = true;
                    result.StepVisual = 0f;
                    return;
                }
            }
        }

        Result MoveSolid(Vector2 positionUp, Vector2 velocityUp, float width, float height, float gravity,
                         LiquidType liquid, bool allowStepUp)
        {
            Result result = Run(positionUp, velocityUp, width, height, gravity, liquid, allowStepUp);
            if (!Overlaps(result.Position, width, height) || Overlaps(positionUp, width, height)) return result;

            // The ported collision let the box end up inside solid tiles (an overhang over a step-up, a pull
            // into a corner). That is not allowed: first retry without the step-up, then fall back to plain
            // sliding along the tiles.
            if (allowStepUp)
            {
                Result retry = Run(positionUp, velocityUp, width, height, gravity, liquid, false);
                if (!Overlaps(retry.Position, width, height)) return retry;
            }

            return Slide(positionUp, velocityUp, width, height, liquid);
        }

        Result Run(Vector2 positionUp, Vector2 velocityUp, float width, float height, float gravity,
                   LiquidType liquid, bool allowStepUp)
        {
            int w = Mathf.RoundToInt(width);
            int h = Mathf.RoundToInt(height);
            Vector2 position = new(positionUp.x, m_WorldHeightPx - (positionUp.y + h));
            Vector2 velocity = new(velocityUp.x, -velocityUp.y);
            float velocityIn = velocity.y;

            m_StepVisual = 0f;
            m_Up = false;

            // Walking down a slope keeps the feet on it, and small drops are stepped down.
            Vector4 walked = WalkDownSlope(position, velocity, w, h, gravity);
            position = new Vector2(walked.x, walked.y);
            velocity = new Vector2(walked.z, walked.w);
            if (velocity.y == gravity && !m_SkipStepDown) StepDown(ref position, w, h, velocity);
            if (allowStepUp && velocity.y >= gravity) StepUp(ref position, velocity, w, h);

            switch (liquid)
            {
                case LiquidType.Nectar: LiquidCollision(ref position, ref velocity, w, h, 0.25f); break;
                case LiquidType.Lava: LiquidCollision(ref position, ref velocity, w, h, 0.5f); break;
                default: DryCollision(ref position, ref velocity, w, h, gravity); break;
            }

            Vector4 sloped = SlopeCollision(position, velocity, w, h);
            position = new Vector2(sloped.x, sloped.y);
            velocity = new Vector2(sloped.z, sloped.w);

            bool hitCeiling = m_Up;
            if (hitCeiling) velocity.y = 0.01f;

            return new Result
            {
                Position = new Vector2(position.x, m_WorldHeightPx - (position.y + h)),
                Velocity = new Vector2(velocity.x, -velocity.y),
                Grounded = velocityIn > 0f && velocity.y == 0f,
                HitCeiling = hitCeiling,
                StepVisual = m_StepVisual,
            };
        }

        // ---- Overlap guard --------------------------------------------------------------------------

        /// <summary>Penetration smaller than this is rounding, not an overlap, px.</summary>
        const float k_OverlapTolerance = 0.05f;

        /// <summary>True if the box (bottom-left, px, y-up) is inside any solid tile, respecting slope and half-block shapes.</summary>
        bool Overlaps(Vector2 positionUp, float width, float height)
        {
            float left = positionUp.x, right = positionUp.x + width;
            float bottom = positionUp.y, top = positionUp.y + height;
            int x0 = Mathf.FloorToInt(left / 16f), x1 = Mathf.FloorToInt((right - 0.001f) / 16f);
            int y0 = Mathf.FloorToInt(bottom / 16f), y1 = Mathf.FloorToInt((top - 0.001f) / 16f);

            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    if (!m_World.IsSolid(x, y)) continue;

                    float tileLeft = x * 16f, tileBottom = y * 16f;
                    float clipLeft = Mathf.Max(left, tileLeft) - tileLeft;
                    float clipRight = Mathf.Min(right, tileLeft + 16f) - tileLeft;
                    float clipBottom = Mathf.Max(bottom, tileBottom) - tileBottom;
                    float clipTop = Mathf.Min(top, tileBottom + 16f) - tileBottom;
                    if (clipRight - clipLeft <= k_OverlapTolerance || clipTop - clipBottom <= k_OverlapTolerance) continue;

                    // How far the box reaches into the solid part of the tile, measured from its closest corner.
                    float depth = m_World.InBounds(x, y) ? m_World.GetShape(x, y) switch
                    {
                        TileShape.Half => 8f - clipBottom,
                        TileShape.SlopeDownLeft => 16f - (clipLeft + clipBottom),
                        TileShape.SlopeDownRight => clipRight - clipBottom,
                        TileShape.SlopeUpLeft => clipTop - clipLeft,
                        TileShape.SlopeUpRight => clipRight + clipTop - 16f,
                        _ => Mathf.Min(clipRight - clipLeft, clipTop - clipBottom),
                    } : 16f;

                    if (depth > k_OverlapTolerance) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Last-resort movement: slide the box along the tiles one axis at a time in sub-pixel steps, stopping
        /// at the first overlap. Never ends inside a tile; blocked velocity is zeroed.
        /// </summary>
        Result Slide(Vector2 positionUp, Vector2 velocityUp, float width, float height, LiquidType liquid)
        {
            float scale = liquid == LiquidType.Lava ? 0.5f : liquid == LiquidType.Nectar ? 0.25f : 1f;
            Vector2 position = positionUp;
            Vector2 velocity = velocityUp;
            bool grounded = false, hitCeiling = false;

            float dx = velocity.x * scale;
            if (dx != 0f)
            {
                position.x = SlideAxis(position, width, height, dx, true, out bool blocked);
                if (blocked) velocity.x = 0f;
            }

            float dy = velocity.y * scale;
            if (dy != 0f)
            {
                position.y = SlideAxis(position, width, height, dy, false, out bool blocked);
                if (blocked)
                {
                    grounded = dy < 0f;
                    hitCeiling = dy > 0f;
                    velocity.y = hitCeiling ? -0.01f : 0f;
                }
            }

            return new Result { Position = position, Velocity = velocity, Grounded = grounded, HitCeiling = hitCeiling };
        }

        float SlideAxis(Vector2 position, float width, float height, float delta, bool horizontal, out bool blocked)
        {
            blocked = false;
            float remaining = Mathf.Abs(delta);
            float sign = Mathf.Sign(delta);
            float value = horizontal ? position.x : position.y;
            const float step = 0.25f;

            while (remaining > 0f)
            {
                float move = Mathf.Min(step, remaining);
                float next = value + sign * move;
                Vector2 probe = horizontal ? new Vector2(next, position.y) : new Vector2(position.x, next);
                if (Overlaps(probe, width, height))
                {
                    blocked = true;
                    break;
                }

                value = next;
                remaining -= move;
            }

            return value;
        }

        // ---- Collision passes -----------------------------------------------------------------------

        void LiquidCollision(ref Vector2 position, ref Vector2 velocity, int w, int h, float scale)
        {
            Vector2 before = velocity;
            velocity = TileCollision(position, velocity, w, h);
            Vector2 displacement = velocity * scale;
            if (velocity.x != before.x) displacement.x = velocity.x;
            if (velocity.y != before.y) displacement.y = velocity.y;
            position += displacement;
        }

        void DryCollision(ref Vector2 position, ref Vector2 velocity, int w, int h, float gravity)
        {
            if (velocity.magnitude > 16f)
            {
                // Very fast: move in chunks of at most 16 px so nothing is skipped over.
                Vector2 first = TileCollision(position, velocity, w, h);
                float remaining = velocity.magnitude;
                Vector2 direction = velocity.normalized;
                if (first.y == 0f) direction.y = 0f;
                Vector2 total = Vector2.zero;

                while (remaining > 0f)
                {
                    float chunk = Mathf.Min(remaining, 16f);
                    remaining -= chunk;
                    Vector2 step = direction * chunk;
                    velocity = step;

                    Vector4 walked = WalkDownSlope(position, velocity, w, h, gravity);
                    position = new Vector2(walked.x, walked.y);
                    velocity = new Vector2(walked.z, walked.w);
                    step = velocity;
                    if (velocity.y == gravity) StepDown(ref position, w, h, step);
                    if (velocity.y >= gravity) StepUp(ref position, step, w, h);

                    Vector2 moved = TileCollision(position, step, w, h);
                    position += moved;
                    velocity = moved;

                    Vector4 sloped = SlopeCollision(position, velocity, w, h);
                    position = new Vector2(sloped.x, sloped.y);
                    velocity = new Vector2(sloped.z, sloped.w);
                    total += velocity;
                }

                velocity = total;
                return;
            }

            velocity = TileCollision(position, velocity, w, h);
            position += velocity;
        }

        /// <summary>
        /// The core box-vs-tiles test. Returns the velocity the box can actually move with. Tiles are
        /// visited column by column; the last side tile and the last floor/ceiling tile found are
        /// remembered, and a side tile in the same row as the floor/ceiling tile does not stop sideways
        /// movement (and one in the same column does not stop vertical movement).
        /// </summary>
        Vector2 TileCollision(Vector2 position, Vector2 velocity, int width, int height)
        {
            m_Up = false;
            Vector2 result = velocity;
            Vector2 next = position + velocity;

            int x0 = Mathf.FloorToInt(position.x / 16f) - 1;
            int x1 = Mathf.FloorToInt((position.x + width) / 16f) + 2;
            int y0 = Mathf.FloorToInt(position.y / 16f) - 1;
            int y1 = Mathf.FloorToInt((position.y + height) / 16f) + 2;

            int sideColumn = -1, sideRow = -1, vertColumn = -1, vertRow = -1;
            float lowestFloor = (y1 + 3) * 16;

            for (int i = x0; i < x1; i++)
            {
                for (int j = y0; j < y1; j++)
                {
                    if (!Solid(i, j)) continue;

                    float tileX = i * 16;
                    float tileY = j * 16;
                    int tileHeight = 16;
                    if (Half(i, j))
                    {
                        tileY += 8f;
                        tileHeight -= 8;
                    }

                    if (!(next.x + width > tileX) || !(next.x < tileX + 16f) ||
                        !(next.y + height > tileY) || !(next.y < tileY + tileHeight))
                    {
                        continue;
                    }

                    bool floorSlope = false;
                    bool passThrough = false;
                    int slope = Slope(i, j);
                    if (slope > 2)
                    {
                        if (slope == 3 && position.y + Mathf.Abs(velocity.x) >= tileY && position.x >= tileX) passThrough = true;
                        if (slope == 4 && position.y + Mathf.Abs(velocity.x) >= tileY && position.x + width <= tileX + 16f) passThrough = true;
                    }
                    else if (slope > 0)
                    {
                        floorSlope = true;
                        if (slope == 1 && position.y + height - Mathf.Abs(velocity.x) <= tileY + tileHeight && position.x >= tileX) passThrough = true;
                        if (slope == 2 && position.y + height - Mathf.Abs(velocity.x) <= tileY + tileHeight && position.x + width <= tileX + 16f) passThrough = true;
                    }

                    if (passThrough) continue;

                    if (position.y + height <= tileY)
                    {
                        // The box is above this tile: it is a floor.
                        if (lowestFloor > tileY)
                        {
                            vertColumn = i;
                            vertRow = j;
                            if (tileHeight < 16) vertRow++;
                            if (vertColumn != sideColumn && !floorSlope)
                            {
                                result.y = tileY - (position.y + height);
                                lowestFloor = tileY;
                            }
                        }
                    }
                    else if (position.x + width <= tileX)
                    {
                        // The box is left of this tile.
                        if (i < 1 || (Slope(i - 1, j) != 2 && Slope(i - 1, j) != 4))
                        {
                            sideColumn = i;
                            sideRow = j;
                            if (sideRow != vertRow) result.x = tileX - (position.x + width);
                            if (vertColumn == sideColumn) result.y = velocity.y;
                        }
                    }
                    else if (position.x >= tileX + 16f)
                    {
                        // The box is right of this tile.
                        if (Slope(i + 1, j) != 1 && Slope(i + 1, j) != 3)
                        {
                            sideColumn = i;
                            sideRow = j;
                            if (sideRow != vertRow) result.x = tileX + 16f - position.x;
                            if (vertColumn == sideColumn) result.y = velocity.y;
                        }
                    }
                    else if (position.y >= tileY + tileHeight)
                    {
                        // The box is below this tile: it is a ceiling.
                        m_Up = true;
                        vertColumn = i;
                        vertRow = j;
                        result.y = tileY + tileHeight - position.y + 0.01f;
                        if (vertRow == sideRow) result.x = velocity.x;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Moves the box along slope surfaces: onto a floor slope it is standing against, or under a ceiling
        /// slope. Returns the new position (x, y) and velocity (z, w).
        /// </summary>
        Vector4 SlopeCollision(Vector2 position, Vector2 velocity, int width, int height)
        {
            var onSlope = new bool[5];
            float floorY = position.y;
            float ceilingY = position.y;
            Vector2 val = position;
            Vector2 vel = velocity;

            int x0 = Mathf.FloorToInt(position.x / 16f) - 1;
            int x1 = Mathf.FloorToInt((position.x + width) / 16f) + 2;
            int y0 = Mathf.FloorToInt(position.y / 16f) - 1;
            int y1 = Mathf.FloorToInt((position.y + height) / 16f) + 2;

            for (int i = x0; i < x1; i++)
            {
                for (int j = y0; j < y1; j++)
                {
                    if (!Solid(i, j)) continue;

                    float tileX = i * 16;
                    float tileY = j * 16;
                    int tileHeight = 16;
                    if (Half(i, j))
                    {
                        tileY += 8f;
                        tileHeight -= 8;
                    }

                    if (!(position.x + width > tileX) || !(position.x < tileX + 16f) ||
                        !(position.y + height > tileY) || !(position.y < tileY + tileHeight))
                    {
                        continue;
                    }

                    int slope = Slope(i, j);
                    tileX = i * 16;
                    tileY = j * 16;
                    if (!(position.x + width > tileX) || !(position.x < tileX + 16f) ||
                        !(position.y + height > tileY) || !(position.y < tileY + 16f))
                    {
                        continue;
                    }

                    float offset = 0f;
                    if (slope == 3 || slope == 4)
                    {
                        if (slope == 3) offset = position.x - tileX;
                        if (slope == 4) offset = tileX + 16f - (position.x + width);

                        if (offset >= 0f)
                        {
                            if (position.y <= tileY + 16f - offset)
                            {
                                float lift = tileY + 16f - position.y - offset;
                                if (position.y + lift > ceilingY)
                                {
                                    val.y = position.y + lift;
                                    ceilingY = val.y;
                                    if (vel.y < 0.0101f) vel.y = 0.0101f;
                                    onSlope[slope] = true;
                                }
                            }
                        }
                        else if (position.y > tileY)
                        {
                            float below = tileY + 16f;
                            if (val.y < below)
                            {
                                val.y = below;
                                if (vel.y < 0.0101f) vel.y = 0.0101f;
                            }
                        }
                    }

                    if (slope != 1 && slope != 2) continue;

                    if (slope == 1) offset = position.x - tileX;
                    if (slope == 2) offset = tileX + 16f - (position.x + width);

                    if (offset >= 0f)
                    {
                        if (!(position.y + height >= tileY + offset)) continue;

                        float rise = tileY - (position.y + height) + offset;
                        if (!(position.y + rise < floorY)) continue;

                        val.y = position.y + rise;
                        floorY = val.y;
                        if (vel.y > 0f) vel.y = 0f;
                        onSlope[slope] = true;
                        continue;
                    }

                    float floorTop = tileY - height;
                    if (!(val.y > floorTop)) continue;

                    val.y = floorTop;
                    if (vel.y > 0f) vel.y = 0f;
                }
            }

            Vector2 wanted = val - position;
            Vector2 allowed = TileCollision(position, wanted, width, height);
            if (allowed.y > wanted.y)
            {
                float lost = wanted.y - allowed.y;
                val.y = position.y + allowed.y;
                if (onSlope[1]) val.x = position.x - lost;
                if (onSlope[2]) val.x = position.x + lost;
                vel.x = 0f;
                vel.y = 0f;
                m_Up = false;
            }
            else if (allowed.y < wanted.y)
            {
                float lost = allowed.y - wanted.y;
                val.y = position.y + allowed.y;
                if (onSlope[3]) val.x = position.x - lost;
                if (onSlope[4]) val.x = position.x + lost;
                vel.x = 0f;
                vel.y = 0f;
            }

            return new Vector4(val.x, val.y, vel.x, vel.y);
        }

        // ---- Walking aids ---------------------------------------------------------------------------

        /// <summary>Walking down a floor slope adds the sideways speed to the downward speed, so the feet follow it.</summary>
        Vector4 WalkDownSlope(Vector2 position, Vector2 velocity, int width, int height, float gravity)
        {
            if (velocity.y != gravity) return new Vector4(position.x, position.y, velocity.x, velocity.y);

            int x0 = Mathf.FloorToInt(position.x / 16f);
            int x1 = Mathf.FloorToInt((position.x + width) / 16f);
            int row = Mathf.FloorToInt((position.y + height + 4f) / 16f);
            float lowest = (row + 3) * 16;
            int bestX = -1, bestY = -1;
            int preferred = velocity.x < 0f ? 2 : 1;

            for (int i = x0; i <= x1; i++)
            {
                for (int j = row; j <= row + 1; j++)
                {
                    if (!InWorld(i, j) || !Solid(i, j)) continue;

                    int top = j * 16;
                    if (Half(i, j)) top += 8;

                    // A 16×16 box one pixel above the tile must touch the player's box.
                    if (!RectsIntersect(i * 16, j * 16 - 17, 16, 16, (int)position.x, (int)position.y, width, height) ||
                        !(top <= lowest))
                    {
                        continue;
                    }

                    if (lowest == top)
                    {
                        if (Slope(i, j) == 0) continue;

                        if (bestX != -1 && bestY != -1 && Slope(bestX, bestY) != 0)
                        {
                            if (Slope(i, j) == preferred)
                            {
                                lowest = top;
                                bestX = i;
                                bestY = j;
                            }
                        }
                        else
                        {
                            lowest = top;
                            bestX = i;
                            bestY = j;
                        }
                    }
                    else
                    {
                        lowest = top;
                        bestX = i;
                        bestY = j;
                    }
                }
            }

            if (bestX != -1 && bestY != -1 && Slope(bestX, bestY) > 0)
            {
                int slope = Slope(bestX, bestY);
                float tileX = bestX * 16;
                float tileY = bestY * 16;
                if (slope == 2)
                {
                    float offset = tileX + 16f - (position.x + width);
                    if (position.y + height >= tileY + offset && velocity.x < 0f) velocity.y += Mathf.Abs(velocity.x);
                }
                else if (slope == 1)
                {
                    float offset = position.x - tileX;
                    if (position.y + height >= tileY + offset && velocity.x > 0f) velocity.y += Mathf.Abs(velocity.x);
                }
            }

            return new Vector4(position.x, position.y, velocity.x, velocity.y);
        }

        /// <summary>Walking off a ledge 8 to 16 px high drops the box onto the lower surface in one go.</summary>
        void StepDown(ref Vector2 position, int width, int height, Vector2 velocity)
        {
            Vector2 val = position;
            val.x += velocity.x;
            val.y = Mathf.Floor((val.y + height) / 16f) * 16f - height;

            int x0 = Mathf.FloorToInt(val.x / 16f);
            int x1 = Mathf.FloorToInt((val.x + width) / 16f);
            int row = Mathf.FloorToInt((val.y + height + 4f) / 16f);
            int tilesTall = height / 16 + (height % 16 != 0 ? 1 : 0);
            float lowest = (row + tilesTall) * 16;

            for (int i = x0; i <= x1; i++)
            {
                for (int j = row; j <= row + 1; j++)
                {
                    if (!InWorld(i, j) || !Solid(i, j)) continue;

                    int top = j * 16;
                    if (Half(i, j)) top += 8;
                    if (RectsIntersectF(i * 16, j * 16 - 17, 16f, 16f, position.x, position.y, width, height) && top < lowest)
                    {
                        lowest = top;
                    }
                }
            }

            float drop = lowest - (position.y + height);
            if (drop > 7f && drop < 17f)
            {
                m_StepVisual += position.y + height - lowest;
                position.y = lowest - height;
            }
        }

        /// <summary>
        /// Walking into a ledge up to one tile high (and onto slopes' lower edges) lifts the box onto it,
        /// provided the tiles above the ledge, to the side of the box, are clear for the whole body height.
        /// </summary>
        void StepUp(ref Vector2 position, Vector2 velocity, int width, int height)
        {
            int direction = velocity.x < 0f ? -1 : velocity.x > 0f ? 1 : 0;
            Vector2 val = position;
            val.x += velocity.x;

            int column = Mathf.FloorToInt((val.x + width / 2 + (width / 2 + 1) * direction) / 16f);
            int row = Mathf.FloorToInt((val.y + height - 1f) / 16f);
            int tilesTall = height / 16 + (height % 16 != 0 ? 1 : 0);

            for (int i = 1; i < tilesTall + 2; i++)
            {
                if (!InWorld(column, row - i)) return;
            }

            if (!InWorld(column - direction, row - tilesTall)) return;

            bool headroom = true;
            for (int j = 2; j < tilesTall + 1; j++)
            {
                headroom = headroom && !Solid(column, row - j);
            }

            bool sideClear = !Solid(column - direction, row - tilesTall);

            if (!InWorld(column, row - 1) || !InWorld(column, row - (tilesTall + 1))) return;

            float centreX = position.x + width / 2;

            // The tile directly above the ledge tile must be passable, a slope's open side, or a half block with room over it.
            int above = Slope(column, row - 1);
            bool aboveClear = !Solid(column, row - 1)
                              || (above == 1 && centreX > column * 16)
                              || (above == 2 && centreX < column * 16 + 16)
                              || (Half(column, row - 1) && !Solid(column, row - (tilesTall + 1)));

            // The ledge tile itself must be solid, and not a slope the box is on the open side of.
            int ledge = Slope(column, row);
            bool ledgeSolid = (Solid(column, row)
                               && (!TopSlope(column, row)
                                   || (ledge == 1 && centreX < column * 16)
                                   || (ledge == 2 && centreX > column * 16 + 16))
                               && (!TopSlope(column, row) || position.y + height > row * 16))
                              || (Half(column, row - 1) && Solid(column, row - 1));

            if (!(column * 16 < val.x + width) || !(column * 16 + 16 > val.x)) return;
            if (!(ledgeSolid && aboveClear && headroom && sideClear)) return;

            float surface = row * 16;
            if (Half(column, row - 1)) surface -= 8f;
            else if (Half(column, row)) surface += 8f;

            if (!(surface < val.y + height)) return;

            float rise = val.y + height - surface;
            if (rise <= 16.1f)
            {
                m_StepVisual += position.y + height - surface;
                position.y = surface - height;
            }
        }

        static bool RectsIntersect(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh) =>
            ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

        static bool RectsIntersectF(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh) =>
            ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;
    }
}
