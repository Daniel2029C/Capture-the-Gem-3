using UnityEngine;

namespace CTG.World
{
    /// <summary>Tile type ids. Values match the tile type ids used in the CTG2 map files.</summary>
    public enum TileId : ushort
    {
        Air = ushort.MaxValue,
        Dirt = 0,
        Stone = 1,
    }

    public readonly struct TileDef
    {
        public readonly TileId Id;
        public readonly string Name;
        public readonly bool Solid;
        /// <summary>CTG2 rule: only a few tile types can be mined at all; the map itself is unbreakable.</summary>
        public readonly bool Breakable;
        /// <summary>CTG2 rule: only a few tile types can be placed by players.</summary>
        public readonly bool Placeable;
        /// <summary>
        /// Multiplier on pickaxe power per hit. A tile breaks once accumulated damage reaches 100.
        /// Soft tiles like dirt and mud take double pickaxe damage.
        /// </summary>
        public readonly float PickDamageMultiplier;
        /// <summary>Item granted to the miner when the tile breaks.</summary>
        public readonly Items.ItemId Drop;
        public readonly Color Color;

        public TileDef(TileId id, string name, bool solid, bool breakable, bool placeable,
                       float pickDamageMultiplier, Items.ItemId drop, Color color)
        {
            Id = id;
            Name = name;
            Solid = solid;
            Breakable = breakable;
            Placeable = placeable;
            PickDamageMultiplier = pickDamageMultiplier;
            Drop = drop;
            Color = color;
        }
    }

    public static class TileDefs
    {
        public const int HitPoints = 100;

        static readonly TileDef s_Air = new(TileId.Air, "Air", false, false, false, 0f, Items.ItemId.None, Color.clear);
        static readonly TileDef s_Dirt = new(TileId.Dirt, "Dirt", true, true, true, 2f, Items.ItemId.DirtBlock,
                                             new Color32(151, 107, 75, 255));
        static readonly TileDef s_Stone = new(TileId.Stone, "Stone", true, false, false, 1f, Items.ItemId.None,
                                              new Color32(128, 128, 128, 255));

        public static TileDef Get(TileId id) => id switch
        {
            TileId.Dirt => s_Dirt,
            TileId.Stone => s_Stone,
            _ => s_Air,
        };
    }
}
