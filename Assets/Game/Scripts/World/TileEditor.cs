using System.Collections.Generic;
using CTG.Core;
using CTG.Items;
using UnityEngine;

namespace CTG.World
{
    /// <summary>
    /// Gameplay rules for changing tiles: mining with accumulated damage and placing
    /// blocks. Kept separate from item-use timing so a server can run exactly this code on requests later.
    /// CTG2 rules: only breakable tile types can be mined, blocks can't replace an existing tile, and
    /// mined blocks go straight into the miner's inventory instead of dropping on the ground.
    /// </summary>
    public class TileEditor
    {
        /// <summary>
        /// Damage on a tile that isn't hit again within this many ticks is forgotten.
        /// Tuned by feel.
        /// </summary>
        const int k_DamageForgetTicks = 3 * SimUnits.TicksPerSecond;

        struct TileDamage
        {
            public float Damage;
            public int LastHitTick;
        }

        readonly TileWorld m_World;
        readonly Dictionary<Vector2Int, TileDamage> m_Damage = new();
        readonly List<Vector2Int> m_Expired = new();

        public TileEditor(TileWorld world)
        {
            m_World = world;
            m_World.TileChanged += (x, y, _, _) => m_Damage.Remove(new Vector2Int(x, y));
        }

        /// <summary>0..1 crack progress for rendering.</summary>
        public float GetDamageFraction(Vector2Int tile) =>
            m_Damage.TryGetValue(tile, out TileDamage d) ? Mathf.Clamp01(d.Damage / TileDefs.HitPoints) : 0f;

        public IEnumerable<KeyValuePair<Vector2Int, float>> DamagedTiles()
        {
            foreach (var pair in m_Damage)
            {
                yield return new KeyValuePair<Vector2Int, float>(pair.Key, pair.Value.Damage / TileDefs.HitPoints);
            }
        }

        public bool CanMine(Vector2Int tile)
        {
            if (!m_World.InBounds(tile.x, tile.y)) return false;
            return TileDefs.Get(m_World.Get(tile.x, tile.y)).Breakable;
        }

        /// <summary>
        /// One pickaxe hit. Returns true if the tile broke; the drop goes to <paramref name="miner"/>.
        /// </summary>
        public bool Hit(Vector2Int tile, int pickPower, int tick, Inventory miner)
        {
            if (!CanMine(tile)) return false;

            TileDef def = TileDefs.Get(m_World.Get(tile.x, tile.y));
            m_Damage.TryGetValue(tile, out TileDamage damage);
            damage.Damage += pickPower * def.PickDamageMultiplier;
            damage.LastHitTick = tick;

            if (damage.Damage < TileDefs.HitPoints)
            {
                m_Damage[tile] = damage;
                return false;
            }

            m_World.SetRaw(tile.x, tile.y, TileId.Air);

            // CTG rule: straight into the miner's inventory; whatever doesn't fit is lost.
            miner?.Add(def.Drop, 1);
            return true;
        }

        /// <summary>
        /// A block needs a neighbouring tile (or wall) to place against: blocks can't float in mid-air.
        /// CTG2 additionally forbids replacing an existing tile (which is also what makes a Ward Block
        /// protect its tile). A solid tile also can't overlap a player; non-solid ones can.
        /// </summary>
        public bool CanPlace(Vector2Int tile, TileId type, Rect blockerPixels)
        {
            if (!m_World.InBounds(tile.x, tile.y)) return false;
            if (m_World.Get(tile.x, tile.y) != TileId.Air) return false;
            if (!TileDefs.Get(type).Placeable) return false;

            var tileRect = new Rect(tile.x * SimUnits.PixelsPerTile, tile.y * SimUnits.PixelsPerTile,
                                    SimUnits.PixelsPerTile, SimUnits.PixelsPerTile);
            if (TileDefs.Get(type).Solid && tileRect.Overlaps(blockerPixels)) return false;

            return HasSolidNeighbour(tile);
        }

        public bool Place(Vector2Int tile, TileId type, Rect blockerPixels)
        {
            if (!CanPlace(tile, type, blockerPixels)) return false;

            m_World.SetRaw(tile.x, tile.y, type);
            return true;
        }

        public bool HasSolidNeighbour(Vector2Int tile)
        {
            bool Solid(int x, int y) => m_World.InBounds(x, y) && TileDefs.Get(m_World.Get(x, y)).Solid;

            return Solid(tile.x - 1, tile.y) || Solid(tile.x + 1, tile.y) ||
                   Solid(tile.x, tile.y - 1) || Solid(tile.x, tile.y + 1);
        }

        public void Tick(int tick)
        {
            m_Expired.Clear();
            foreach (var pair in m_Damage)
            {
                if (tick - pair.Value.LastHitTick > k_DamageForgetTicks) m_Expired.Add(pair.Key);
            }

            foreach (Vector2Int key in m_Expired) m_Damage.Remove(key);
        }
    }
}
