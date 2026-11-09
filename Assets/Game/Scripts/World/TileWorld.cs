using System;
using CTG.Core;
using UnityEngine;

namespace CTG.World
{
    /// <summary>
    /// The authoritative tile grid. Tile (x, y) covers world units [x, x+1) × [y, y+1), i.e. pixels
    /// [x*16, x*16+16) × [y*16, y*16+16), y-up. Outside the world counts as solid.
    /// Pure data plus edit rules; rendering listens to <see cref="TileChanged"/>.
    /// </summary>
    public class TileWorld
    {
        readonly TileId[] m_Tiles;
        readonly TileShape[] m_Shapes;
        readonly LiquidType[] m_Liquid;
        readonly bool[] m_NoBuild;

        public int Width { get; }
        public int Height { get; }

        /// <summary>Raised after a tile's type or shape changes: (x, y, oldId, newId). Read the shape from the world.</summary>
        public event Action<int, int, TileId, TileId> TileChanged;

        /// <summary>Raised after a tile's liquid changes: (x, y).</summary>
        public event Action<int, int> LiquidChanged;

        public TileWorld(int width, int height)
        {
            Width = width;
            Height = height;
            m_Tiles = new TileId[width * height];
            m_Shapes = new TileShape[width * height];
            m_Liquid = new LiquidType[width * height];
            m_NoBuild = new bool[width * height];
            Array.Fill(m_Tiles, TileId.Air);
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public TileId Get(int x, int y) => InBounds(x, y) ? m_Tiles[y * Width + x] : TileId.Stone;

        public bool IsSolid(int x, int y) => !InBounds(x, y) || TileDefs.Get(m_Tiles[y * Width + x]).Solid;

        /// <summary>Tiles where nobody can place blocks (the gem area).</summary>
        public bool IsNoBuild(int x, int y) => InBounds(x, y) && m_NoBuild[y * Width + x];

        public void SetNoBuild(int x0, int y0, int w, int h)
        {
            for (int y = y0; y < y0 + h; y++)
            {
                for (int x = x0; x < x0 + w; x++)
                {
                    if (InBounds(x, y)) m_NoBuild[y * Width + x] = true;
                }
            }
        }

        public TileShape GetShape(int x, int y) => InBounds(x, y) ? m_Shapes[y * Width + x] : TileShape.Full;

        /// <summary>Static liquid in this tile (a full tile; only on tiles without a block).</summary>
        public LiquidType GetLiquid(int x, int y) => InBounds(x, y) ? m_Liquid[y * Width + x] : LiquidType.None;

        public bool HasLava(int x, int y) => GetLiquid(x, y) == LiquidType.Lava;

        public bool HasNectar(int x, int y) => GetLiquid(x, y) == LiquidType.Nectar;

        /// <summary>Writes a full-shaped tile without any gameplay rules (world generation, map loading).</summary>
        public void SetRaw(int x, int y, TileId id) => SetTile(x, y, id, TileShape.Full);

        public void SetTile(int x, int y, TileId id, TileShape shape)
        {
            if (!InBounds(x, y)) return;

            int index = y * Width + x;
            TileId old = m_Tiles[index];
            if (old == id && m_Shapes[index] == shape) return;

            m_Tiles[index] = id;
            m_Shapes[index] = shape;
            TileChanged?.Invoke(x, y, old, id);
        }

        public void SetLiquid(int x, int y, LiquidType liquid)
        {
            if (!InBounds(x, y)) return;

            int index = y * Width + x;
            if (m_Liquid[index] == liquid) return;

            m_Liquid[index] = liquid;
            LiquidChanged?.Invoke(x, y);
        }

        /// <summary>True if the pixel rectangle overlaps any tile holding the given liquid.</summary>
        public bool OverlapsLiquid(LiquidType liquid, float left, float bottom, float width, float height)
        {
            int x0 = SimUnits.PixelToTile(left);
            int x1 = SimUnits.PixelToTile(left + width - 0.001f);
            int y0 = SimUnits.PixelToTile(bottom);
            int y1 = SimUnits.PixelToTile(bottom + height - 0.001f);

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (GetLiquid(x, y) == liquid) return true;
                }
            }

            return false;
        }

        public bool AnySolidInPixelRect(float left, float bottom, float width, float height)
        {
            const float epsilon = 0.001f;
            int x0 = SimUnits.PixelToTile(left + epsilon);
            int x1 = SimUnits.PixelToTile(left + width - epsilon);
            int y0 = SimUnits.PixelToTile(bottom + epsilon);
            int y1 = SimUnits.PixelToTile(bottom + height - epsilon);

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (IsSolid(x, y)) return true;
                }
            }

            return false;
        }

        public static Vector2Int WorldToTile(Vector2 worldUnits) =>
            new(Mathf.FloorToInt(worldUnits.x), Mathf.FloorToInt(worldUnits.y));
    }
}
