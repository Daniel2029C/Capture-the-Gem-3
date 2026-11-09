using System.Collections.Generic;
using CTG.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CTG.Rendering
{
    /// <summary>
    /// Mirrors a <see cref="TileWorld"/> into Unity Tilemaps (cell size 1 = one tile) and keeps it in sync
    /// as tiles change. Rendering only: collision never reads the Tilemap.
    /// </summary>
    [RequireComponent(typeof(Tilemap))]
    public class TileWorldRenderer : MonoBehaviour
    {
        readonly Dictionary<(TileId, TileShape, int, int), TileBase> m_TileAssets = new();
        readonly Dictionary<(LiquidType, bool), TileBase> m_LiquidAssets = new();
        Tilemap m_Tilemap;
        Tilemap m_LiquidMap;
        Tilemap m_FringeMap;
        readonly Dictionary<(FringeKind, int), TileBase> m_FringeAssets = new();
        TileWorld m_World;

        public void Bind(TileWorld world)
        {
            m_Tilemap = GetComponent<Tilemap>();
            // Tile sprites are pivoted at their bottom-left corner, so anchor them at the cell corner too;
            // the default (0.5, 0.5) anchor would draw every tile half a cell off its collision.
            m_Tilemap.tileAnchor = Vector3.zero;

            // Liquids are drawn on their own layer in front of the player so a swimmer shows through them.
            var liquidGo = new GameObject("Liquids");
            liquidGo.transform.SetParent(transform, false);
            m_LiquidMap = liquidGo.AddComponent<Tilemap>();
            m_LiquidMap.tileAnchor = Vector3.zero;
            liquidGo.AddComponent<TilemapRenderer>().sortingOrder = 20;

            // Decoration in the empty tiles beside terrain (moss, roots, liquid glow), behind the player.
            var fringeGo = new GameObject("Fringe");
            fringeGo.transform.SetParent(transform, false);
            m_FringeMap = fringeGo.AddComponent<Tilemap>();
            m_FringeMap.tileAnchor = Vector3.zero;
            fringeGo.AddComponent<TilemapRenderer>().sortingOrder = 2;

            m_World = world;
            m_World.TileChanged += HandleTileChanged;
            m_World.LiquidChanged += HandleLiquidChanged;
            RebuildAll();
        }

        void OnDestroy()
        {
            if (m_World == null) return;

            m_World.TileChanged -= HandleTileChanged;
            m_World.LiquidChanged -= HandleLiquidChanged;
        }

        void RebuildAll()
        {
            m_Tilemap.ClearAllTiles();
            m_LiquidMap.ClearAllTiles();
            m_FringeMap.ClearAllTiles();

            var positions = new List<Vector3Int>();
            var tiles = new List<TileBase>();
            var liquidPositions = new List<Vector3Int>();
            var liquidTiles = new List<TileBase>();
            for (int y = 0; y < m_World.Height; y++)
            {
                for (int x = 0; x < m_World.Width; x++)
                {
                    TileId id = m_World.Get(x, y);
                    if (id != TileId.Air)
                    {
                        positions.Add(new Vector3Int(x, y, 0));
                        tiles.Add(GetTileAsset(x, y, id));
                    }

                    LiquidType liquid = m_World.GetLiquid(x, y);
                    if (liquid != LiquidType.None)
                    {
                        liquidPositions.Add(new Vector3Int(x, y, 0));
                        liquidTiles.Add(GetLiquidAsset(liquid, m_World.GetLiquid(x, y + 1) != liquid));
                    }
                }
            }

            m_Tilemap.SetTiles(positions.ToArray(), tiles.ToArray());
            m_LiquidMap.SetTiles(liquidPositions.ToArray(), liquidTiles.ToArray());
            for (int y = 0; y < m_World.Height; y++)
            {
                for (int x = 0; x < m_World.Width; x++) RefreshFringe(x, y);
            }
        }

        void HandleTileChanged(int x, int y, TileId oldId, TileId newId)
        {
            // A tile's look depends on which neighbours are the same kind, so the neighbours redraw too.
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    RefreshTile(x + dx, y + dy);
                    RefreshFringe(x + dx, y + dy);
                }
            }
        }

        void RefreshTile(int x, int y)
        {
            if (!m_World.InBounds(x, y)) return;

            TileId id = m_World.Get(x, y);
            m_Tilemap.SetTile(new Vector3Int(x, y, 0), id == TileId.Air ? null : GetTileAsset(x, y, id));
        }

        void HandleLiquidChanged(int x, int y)
        {
            RefreshLiquid(x, y);
            RefreshLiquid(x, y - 1); // the tile below may gain or lose its bright surface
            RefreshFringe(x, y);
            RefreshFringe(x, y + 1);
        }

        void RefreshLiquid(int x, int y)
        {
            LiquidType liquid = m_World.GetLiquid(x, y);
            m_LiquidMap.SetTile(new Vector3Int(x, y, 0),
                liquid == LiquidType.None ? null : GetLiquidAsset(liquid, m_World.GetLiquid(x, y + 1) != liquid));
        }

        static bool IsEarth(TileId id) => id == TileId.BlueDirt || id == TileId.RedDirt;

        /// <summary>Picks the decoration for an empty, liquid-free tile: glow over a liquid surface, else moss on earth below, else roots from earth above.</summary>
        void RefreshFringe(int x, int y)
        {
            if (!m_World.InBounds(x, y)) return;

            FringeKind? kind = null;
            if (m_World.Get(x, y) == TileId.Air && m_World.GetLiquid(x, y) == LiquidType.None)
            {
                LiquidType below = m_World.InBounds(x, y - 1) ? m_World.GetLiquid(x, y - 1) : LiquidType.None;
                if (below == LiquidType.Lava) kind = FringeKind.LavaGlow;
                else if (below == LiquidType.Nectar) kind = FringeKind.NectarGlow;
                else if (m_World.InBounds(x, y - 1) && IsEarth(m_World.Get(x, y - 1))) kind = FringeKind.Tuft;
                else if (m_World.InBounds(x, y + 1) && IsEarth(m_World.Get(x, y + 1))) kind = FringeKind.Root;
            }

            Vector3Int cell = new Vector3Int(x, y, 0);
            if (kind == null) { m_FringeMap.SetTile(cell, null); return; }

            int variant = (int)(((uint)x * 73856093u ^ (uint)y * 19349663u) >> 4) % FringeArt.Variants;
            if (!m_FringeAssets.TryGetValue((kind.Value, variant), out TileBase asset))
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = FringeArt.Get(kind.Value, variant);
                tile.colliderType = Tile.ColliderType.None;
                m_FringeAssets[(kind.Value, variant)] = asset = tile;
            }

            m_FringeMap.SetTile(cell, asset);
        }

        TileBase GetLiquidAsset(LiquidType liquid, bool surface)
        {
            if (m_LiquidAssets.TryGetValue((liquid, surface), out TileBase asset)) return asset;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = surface ? $"{liquid} Surface" : liquid.ToString();
            tile.sprite = PlaceholderSprites.Liquid(liquid, surface);
            tile.colliderType = Tile.ColliderType.None;
            m_LiquidAssets[(liquid, surface)] = tile;
            return tile;
        }

        const int k_SpeckleVariants = 4;

        /// <summary>The tile asset for (x, y): its kind and shape, which neighbours match it, and a speckle variant picked by position.</summary>
        TileBase GetTileAsset(int x, int y, TileId id)
        {
            TileShape shape = m_World.GetShape(x, y);
            int mask = 0;
            if (SameKind(x - 1, y, id)) mask |= TileArt.SameLeft;
            if (SameKind(x + 1, y, id)) mask |= TileArt.SameRight;
            if (SameKind(x, y - 1, id)) mask |= TileArt.SameBelow;
            if (SameKind(x, y + 1, id)) mask |= TileArt.SameAbove;
            if (SameKind(x - 1, y - 1, id)) mask |= TileArt.SameBelowLeft;
            if (SameKind(x + 1, y - 1, id)) mask |= TileArt.SameBelowRight;
            if (SameKind(x - 1, y + 1, id)) mask |= TileArt.SameAboveLeft;
            if (SameKind(x + 1, y + 1, id)) mask |= TileArt.SameAboveRight;

            int variant = (int)(((uint)x * 73856093u ^ (uint)y * 19349663u) >> 4) % k_SpeckleVariants;
            return GetTileAsset(id, shape, mask, variant);
        }

        /// <summary>Beyond the world edge counts as the same kind, so no border is drawn there.</summary>
        bool SameKind(int x, int y, TileId id) => !m_World.InBounds(x, y) || m_World.Get(x, y) == id;

        TileBase GetTileAsset(TileId id, TileShape shape, int mask, int variant)
        {
            if (id == TileId.Ward) mask = variant = 0; // one look, whatever surrounds it
            if (m_TileAssets.TryGetValue((id, shape, mask, variant), out TileBase asset)) return asset;

            TileDef def = TileDefs.Get(id);
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = def.Name;
            tile.sprite = id == TileId.Ward ? PlaceholderSprites.Ward() : TileArt.Tile(id, def.Color, shape, mask, variant);
            tile.colliderType = Tile.ColliderType.None;
            m_TileAssets[(id, shape, mask, variant)] = tile;
            return tile;
        }
    }
}
