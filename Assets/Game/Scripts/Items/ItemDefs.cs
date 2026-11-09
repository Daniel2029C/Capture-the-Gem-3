using CTG.World;
using UnityEngine;

namespace CTG.Items
{
    /// <summary>
    /// Item type ids. Vanilla-derived items keep Terraria's ItemID so the wiki stays a reference; CTG2's own
    /// items get ids from 10000 up so they can never collide with a vanilla id.
    /// </summary>
    public enum ItemId
    {
        None = 0,
        DirtBlock = 2,
        ShardstonePickaxe = 10000,
        UpgradedShardstonePickaxe = 10001,
    }

    public enum UseStyle
    {
        None,
        /// <summary>Terraria ItemUseStyleID.Swing: pickaxes, swords, placing blocks.</summary>
        Swing,
    }

    /// <summary>
    /// The subset of Terraria's Item fields this build uses. Durations are in ticks, as in Terraria.
    /// Becomes a ScriptableObject once there are enough items to want authoring in the editor.
    /// </summary>
    public sealed class ItemDef
    {
        public ItemId Id;
        public string Name;
        public int MaxStack = 1;
        public UseStyle UseStyle = UseStyle.None;
        /// <summary>Terraria useAnimation: ticks one use (swing) lasts.</summary>
        public int UseAnimation;
        /// <summary>Terraria useTime: ticks between actions (tile hits/placements) within a use.</summary>
        public int UseTime;
        /// <summary>Terraria autoReuse: keep using while the button is held.</summary>
        public bool AutoReuse;
        /// <summary>Terraria pick: pickaxe power. 0 = not a pickaxe.</summary>
        public int PickPower;
        /// <summary>Terraria createTile: the tile this item places, if any.</summary>
        public TileId CreateTile = TileId.Air;
        /// <summary>Terraria tileBoost: extra tile reach.</summary>
        public int TileBoost;
        public bool Consumable;
        public Color IconColor = Color.white;

        public bool IsPickaxe => PickPower > 0;
        public bool PlacesTile => CreateTile != TileId.Air;
    }

    public static class ItemDefs
    {
        static readonly ItemDef s_None = new() { Id = ItemId.None, Name = "" };

        // Dirt Block: Terraria useTime 10, useAnimation 15, autoReuse, max stack 9999.
        static readonly ItemDef s_Dirt = new()
        {
            Id = ItemId.DirtBlock, Name = "Dirt Block", MaxStack = 9999, UseStyle = UseStyle.Swing,
            UseAnimation = 15, UseTime = 10, AutoReuse = true, CreateTile = TileId.Dirt, Consumable = true,
            IconColor = new Color32(151, 107, 75, 255),
        };

        // CTG2 ShardstonePickaxe: CloneDefaults(Titanium Pickaxe) then useTime 7 / useAnimation 14.
        // Pick power 190 is Titanium Pickaxe's. Verify tileBoost against the Titanium Pickaxe defaults.
        static readonly ItemDef s_Shardstone = new()
        {
            Id = ItemId.ShardstonePickaxe, Name = "Shardstone Pickaxe", UseStyle = UseStyle.Swing,
            UseAnimation = 14, UseTime = 7, AutoReuse = true, PickPower = 190,
            IconColor = new Color32(120, 200, 220, 255),
        };

        // CTG2 UpgradedShardstonePickaxe: useTime 6 / useAnimation 12.
        static readonly ItemDef s_UpgradedShardstone = new()
        {
            Id = ItemId.UpgradedShardstonePickaxe, Name = "Upgraded Shardstone Pickaxe", UseStyle = UseStyle.Swing,
            UseAnimation = 12, UseTime = 6, AutoReuse = true, PickPower = 190,
            IconColor = new Color32(170, 120, 230, 255),
        };

        public static ItemDef Get(ItemId id) => id switch
        {
            ItemId.DirtBlock => s_Dirt,
            ItemId.ShardstonePickaxe => s_Shardstone,
            ItemId.UpgradedShardstonePickaxe => s_UpgradedShardstone,
            _ => s_None,
        };
    }
}
