using CTG.Core;

namespace CTG.World
{
    /// <summary>
    /// Moves an axis-aligned box through the tile grid, in pixels. Movement is resolved one axis at a time, the box stops
    /// flush against the first solid tile it would enter, and the blocked velocity component is zeroed.
    /// Velocities are well under a tile per tick (falling is capped at 10 px/tick), so a swept test
    /// over the tiles between the old and new edge can't tunnel.
    /// Slopes and half-blocks are decorative only and never collide differently from full tiles.
    /// </summary>
    public static class TileCollision
    {
        const float k_Epsilon = 0.001f;

        public struct Result
        {
            public bool HitLeft;
            public bool HitRight;
            public bool HitFloor;
            public bool HitCeiling;
        }

        /// <param name="x">Left edge, px.</param>
        /// <param name="y">Bottom edge, px.</param>
        public static Result Move(TileWorld world, ref float x, ref float y, float width, float height,
                                  ref float vx, ref float vy)
        {
            var result = new Result();

            if (vx != 0f)
            {
                MoveX(world, ref x, y, width, height, ref vx, ref result);
            }

            if (vy != 0f)
            {
                MoveY(world, x, ref y, width, height, ref vy, ref result);
            }

            return result;
        }

        static void MoveX(TileWorld world, ref float x, float y, float width, float height, ref float vx,
                          ref Result result)
        {
            int rowBottom = SimUnits.PixelToTile(y + k_Epsilon);
            int rowTop = SimUnits.PixelToTile(y + height - k_Epsilon);

            if (vx > 0f)
            {
                float right = x + width;
                int firstColumn = SimUnits.PixelToTile(right - k_Epsilon) + 1;
                int lastColumn = SimUnits.PixelToTile(right + vx - k_Epsilon);

                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    if (ColumnBlocked(world, column, rowBottom, rowTop))
                    {
                        x = column * SimUnits.PixelsPerTile - width;
                        vx = 0f;
                        result.HitRight = true;
                        return;
                    }
                }
            }
            else
            {
                int firstColumn = SimUnits.PixelToTile(x + k_Epsilon) - 1;
                int lastColumn = SimUnits.PixelToTile(x + vx + k_Epsilon);

                for (int column = firstColumn; column >= lastColumn; column--)
                {
                    if (ColumnBlocked(world, column, rowBottom, rowTop))
                    {
                        x = (column + 1) * SimUnits.PixelsPerTile;
                        vx = 0f;
                        result.HitLeft = true;
                        return;
                    }
                }
            }

            x += vx;
        }

        static void MoveY(TileWorld world, float x, ref float y, float width, float height, ref float vy,
                          ref Result result)
        {
            int columnLeft = SimUnits.PixelToTile(x + k_Epsilon);
            int columnRight = SimUnits.PixelToTile(x + width - k_Epsilon);

            if (vy > 0f)
            {
                float top = y + height;
                int firstRow = SimUnits.PixelToTile(top - k_Epsilon) + 1;
                int lastRow = SimUnits.PixelToTile(top + vy - k_Epsilon);

                for (int row = firstRow; row <= lastRow; row++)
                {
                    if (RowBlocked(world, row, columnLeft, columnRight))
                    {
                        y = row * SimUnits.PixelsPerTile - height;
                        vy = 0f;
                        result.HitCeiling = true;
                        return;
                    }
                }
            }
            else
            {
                int firstRow = SimUnits.PixelToTile(y + k_Epsilon) - 1;
                int lastRow = SimUnits.PixelToTile(y + vy + k_Epsilon);

                for (int row = firstRow; row >= lastRow; row--)
                {
                    if (RowBlocked(world, row, columnLeft, columnRight))
                    {
                        y = (row + 1) * SimUnits.PixelsPerTile;
                        vy = 0f;
                        result.HitFloor = true;
                        return;
                    }
                }
            }

            y += vy;
        }

        static bool ColumnBlocked(TileWorld world, int column, int rowBottom, int rowTop)
        {
            for (int row = rowBottom; row <= rowTop; row++)
            {
                if (world.IsSolid(column, row)) return true;
            }

            return false;
        }

        static bool RowBlocked(TileWorld world, int row, int columnLeft, int columnRight)
        {
            for (int column = columnLeft; column <= columnRight; column++)
            {
                if (world.IsSolid(column, row)) return true;
            }

            return false;
        }
    }
}
