using System;
using CTG.Core;
using UnityEngine;

namespace CTG.World
{
    /// <summary>
    /// The authoritative tile grid. Tile (x, y) covers world units [x, x+1) × [y, y+1), i.e. pixels
    /// [x*16, x*16+16) × [y*16, y*16+16), y-up. Outside the world counts as solid, like Terraria's edges.
    /// Pure data plus edit rules; rendering listens to <see cref="TileChanged"/>.
    /// </summary>
    public class TileWorld
    {
        readonly TileId[] m_Tiles;

        public int Width { get; }
        public int Height { get; }

        /// <summary>Raised after a tile changes: (x, y, oldId, newId).</summary>
        public event Action<int, int, TileId, TileId> TileChanged;

        public TileWorld(int width, int height)
        {
            Width = width;
            Height = height;
            m_Tiles = new TileId[width * height];
            Array.Fill(m_Tiles, TileId.Air);
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public TileId Get(int x, int y) => InBounds(x, y) ? m_Tiles[y * Width + x] : TileId.Stone;

        public bool IsSolid(int x, int y) => !InBounds(x, y) || TileDefs.Get(m_Tiles[y * Width + x]).Solid;

        /// <summary>Writes a tile without any gameplay rules (world generation, map loading).</summary>
        public void SetRaw(int x, int y, TileId id)
        {
            if (!InBounds(x, y)) return;

            int index = y * Width + x;
            TileId old = m_Tiles[index];
            if (old == id) return;

            m_Tiles[index] = id;
            TileChanged?.Invoke(x, y, old, id);
        }

        public bool AnySolidInPixelRect(float left, float bottom, float width, float height)
        {
            const float epsilon = 0.001f;
            int x0 = TerrariaUnits.PixelToTile(left + epsilon);
            int x1 = TerrariaUnits.PixelToTile(left + width - epsilon);
            int y0 = TerrariaUnits.PixelToTile(bottom + epsilon);
            int y1 = TerrariaUnits.PixelToTile(bottom + height - epsilon);

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
