using System.Collections.Generic;
using CTG.Gameplay;
using CTG.Items;
using CTG.World;
using UnityEngine;

namespace CTG.Rendering
{
    /// <summary>
    /// Highlights the tile under the cursor (white when the held tool or block can act on it, red when out
    /// of reach or invalid) and draws crack overlays on partially mined tiles.
    /// </summary>
    public class TileCursorView : MonoBehaviour
    {
        PlayerCharacter m_Player;
        TileWorld m_World;
        TileEditor m_Editor;
        SpriteRenderer m_Cursor;
        readonly List<SpriteRenderer> m_Cracks = new();
        Sprite m_CrackSprite;

        public void Bind(PlayerCharacter player, TileWorld world, TileEditor editor)
        {
            m_Player = player;
            m_World = world;
            m_Editor = editor;

            var cursor = new GameObject("Tile Cursor");
            cursor.transform.SetParent(transform, false);
            m_Cursor = cursor.AddComponent<SpriteRenderer>();
            m_Cursor.sprite = PlaceholderSprites.Outline("cursor", Color.white);
            m_Cursor.sortingOrder = 20;

            m_CrackSprite = PlaceholderSprites.Square("crack", Color.black, 16);
        }

        void LateUpdate()
        {
            if (m_Player == null || m_Player.Motor == null) return;

            Vector2Int tile = m_Player.CursorTile;
            ItemStack held = m_Player.Inventory.Selected;
            ItemDef def = held.Def;
            bool relevant = def.IsPickaxe || def.PlacesTile;

            m_Cursor.enabled = relevant && m_World.InBounds(tile.x, tile.y);
            if (m_Cursor.enabled)
            {
                bool inRange = ItemUser.InTileRange(m_Player.Motor, tile, def.TileBoost + m_Player.ItemUser.BonusTileReach);
                Rect hitbox = new Rect(m_Player.Motor.Position, new Vector2(m_Player.Motor.Width, m_Player.Motor.Height));
                bool valid = def.IsPickaxe ? m_Editor.CanMine(tile) : m_Editor.CanPlace(tile, def.TileFor(m_Player.Team), hitbox);

                m_Cursor.transform.position = new Vector3(tile.x, tile.y, 0f);
                m_Cursor.color = inRange && valid ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 0.3f, 0.3f, 0.6f);
            }

            UpdateCracks();
        }

        void UpdateCracks()
        {
            int index = 0;
            foreach (KeyValuePair<Vector2Int, float> damaged in m_Editor.DamagedTiles())
            {
                if (index >= m_Cracks.Count)
                {
                    var go = new GameObject("Crack");
                    go.transform.SetParent(transform, false);
                    SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = m_CrackSprite;
                    sr.sortingOrder = 5;
                    m_Cracks.Add(sr);
                }

                SpriteRenderer crack = m_Cracks[index++];
                crack.enabled = true;
                crack.transform.position = new Vector3(damaged.Key.x, damaged.Key.y, 0f);
                crack.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.15f, 0.6f, damaged.Value));
            }

            for (; index < m_Cracks.Count; index++)
            {
                m_Cracks[index].enabled = false;
            }
        }
    }
}
