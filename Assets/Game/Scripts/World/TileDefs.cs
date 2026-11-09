using UnityEngine;

namespace CTG.World
{
    /// <summary>Tile type ids. Maps refer to tiles by name, so values only need to stay unique.</summary>
    public enum TileId : ushort
    {
        Air = ushort.MaxValue,
        /// <summary>What lies outside the world; also unbreakable.</summary>
        Stone = 1,
        // CTG's own tiles get ids from 10000 up so they can't collide with a carried-over map id.
        BlueBrick = 10000,
        RedBrick = 10001,
        Ward = 10002,
        RedDirt = 10003,
        BlueDirt = 10004,
        /// <summary>Glass platform: solid only from above, and can be dropped through.</summary>
        Platform = 10005,
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
        /// <summary>Solid only from above: players land on it when falling, pass up and sideways through it, and can drop through it.</summary>
        public readonly bool OneWay;

        public TileDef(TileId id, string name, bool solid, bool breakable, bool placeable,
                       float pickDamageMultiplier, Items.ItemId drop, Color color, bool oneWay = false)
        {
            Id = id;
            Name = name;
            Solid = solid;
            Breakable = breakable;
            Placeable = placeable;
            PickDamageMultiplier = pickDamageMultiplier;
            Drop = drop;
            Color = color;
            OneWay = oneWay;
        }
    }

    public static class TileDefs
    {
        public const int HitPoints = 100;

        static readonly TileDef s_Air = new(TileId.Air, "Air", false, false, false, 0f, Items.ItemId.None, Color.clear);
        // Dirt always belongs to a team: maps place it in team colours and players place their own team's.
        // Both mine the same and drop the same Dirt Block.
        static readonly TileDef s_RedDirt = new(TileId.RedDirt, "Red Dirt", true, true, true, 2f, Items.ItemId.DirtBlock,
                                                new Color32(176, 84, 70, 255));
        static readonly TileDef s_BlueDirt = new(TileId.BlueDirt, "Blue Dirt", true, true, true, 2f, Items.ItemId.DirtBlock,
                                                 new Color32(80, 100, 172, 255));
        static readonly TileDef s_Stone = new(TileId.Stone, "Stone", true, false, false, 1f, Items.ItemId.None,
                                              new Color32(128, 128, 128, 255));

        // Unbreakable and not placeable by players (no pick damage can ever break them).
        static readonly TileDef s_BlueBrick = new(TileId.BlueBrick, "Blue Brick", true, false, false, 0f, Items.ItemId.None,
                                                  new Color32(52, 86, 178, 255));
        static readonly TileDef s_RedBrick = new(TileId.RedBrick, "Red Brick", true, false, false, 0f, Items.ItemId.None,
                                                 new Color32(178, 52, 58, 255));

        // Ward Block: players walk and shoot through it, and nobody can place a block on the tile it
        // occupies. It can be mined and placed like dirt (the placement check refuses any occupied tile).
        static readonly TileDef s_Ward = new(TileId.Ward, "Ward Block", false, true, true, 2f, Items.ItemId.WardBlock,
                                             new Color32(120, 220, 255, 255));

        // Map-only glass platform. Not Solid, so projectiles, hooks and building treat it as open; only
        // player collision gives it a walkable top.
        static readonly TileDef s_Platform = new(TileId.Platform, "Glass Platform", false, false, false, 0f, Items.ItemId.None,
                                                 new Color32(150, 215, 235, 255), oneWay: true);

        public static TileDef Get(TileId id) => id switch
        {
            TileId.Platform => s_Platform,
            TileId.Ward => s_Ward,
            TileId.BlueBrick => s_BlueBrick,
            TileId.RedBrick => s_RedBrick,
            TileId.RedDirt => s_RedDirt,
            TileId.BlueDirt => s_BlueDirt,
            TileId.Stone => s_Stone,
            _ => s_Air,
        };
    }
}
