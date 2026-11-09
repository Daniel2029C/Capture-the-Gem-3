using System.Collections.Generic;
using CTG.World;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CTG.Rendering
{
    /// <summary>
    /// Mirrors a <see cref="TileWorld"/> into a Unity Tilemap (cell size 1 = one tile) and keeps it in sync
    /// as tiles change. Rendering only: collision never reads the Tilemap.
    /// </summary>
    [RequireComponent(typeof(Tilemap))]
    public class TileWorldRenderer : MonoBehaviour
    {
        readonly Dictionary<TileId, TileBase> m_TileAssets = new();
        Tilemap m_Tilemap;
        TileWorld m_World;

        public void Bind(TileWorld world)
        {
            m_Tilemap = GetComponent<Tilemap>();
            // Tile sprites are pivoted at their bottom-left corner, so anchor them at the cell corner too;
            // the default (0.5, 0.5) anchor would draw every tile half a cell off its collision.
            m_Tilemap.tileAnchor = Vector3.zero;
            m_World = world;
            m_World.TileChanged += HandleTileChanged;
            RebuildAll();
        }

        void OnDestroy()
        {
            if (m_World != null) m_World.TileChanged -= HandleTileChanged;
        }

        void RebuildAll()
        {
            m_Tilemap.ClearAllTiles();

            var positions = new List<Vector3Int>();
            var tiles = new List<TileBase>();
            for (int y = 0; y < m_World.Height; y++)
            {
                for (int x = 0; x < m_World.Width; x++)
                {
                    TileId id = m_World.Get(x, y);
                    if (id == TileId.Air) continue;

                    positions.Add(new Vector3Int(x, y, 0));
                    tiles.Add(GetTileAsset(id));
                }
            }

            m_Tilemap.SetTiles(positions.ToArray(), tiles.ToArray());
        }

        void HandleTileChanged(int x, int y, TileId oldId, TileId newId)
        {
            m_Tilemap.SetTile(new Vector3Int(x, y, 0), newId == TileId.Air ? null : GetTileAsset(newId));
        }

        TileBase GetTileAsset(TileId id)
        {
            if (m_TileAssets.TryGetValue(id, out TileBase asset)) return asset;

            TileDef def = TileDefs.Get(id);
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = def.Name;
            tile.sprite = PlaceholderSprites.Tile($"tile_{def.Name}", def.Color);
            tile.colliderType = Tile.ColliderType.None;
            m_TileAssets[id] = tile;
            return tile;
        }
    }
}
