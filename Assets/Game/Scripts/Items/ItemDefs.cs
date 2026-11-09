using CTG.World;
using UnityEngine;

namespace CTG.Items
{
    /// <summary>
    /// Item type ids. Items carried over from CTG2 loadouts keep their original ids; CTG's own
    /// items get ids from 10000 up so they can never collide with a carried-over id.
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
        /// <summary>Overhead swing: pickaxes, swords, placing blocks.</summary>
        Swing,
    }

    /// <summary>
    /// Item data used by this build. Durations are in ticks.
    /// Becomes a ScriptableObject once there are enough items to want authoring in the editor.
    /// </summary>
    public sealed class ItemDef
    {
        public ItemId Id;
        public string Name;
        public int MaxStack = 1;
        public UseStyle UseStyle = UseStyle.None;
        /// <summary>Ticks one use (swing) lasts.</summary>
        public int UseAnimation;
        /// <summary>Ticks between actions (tile hits/placements) within a use.</summary>
        public int UseTime;
        /// <summary>Keep using while the button is held.</summary>
        public bool AutoReuse;
        /// <summary>Pickaxe power. 0 = not a pickaxe.</summary>
        public int PickPower;
        /// <summary>The tile this item places, if any.</summary>
        public TileId CreateTile = TileId.Air;
        /// <summary>Extra tile reach.</summary>
        public int TileBoost;
        /// <summary>Melee damage per hit. 0 = deals no damage.</summary>
        public int Damage;
        /// <summary>Knockback applied to players hit. 1 = standard knockback.</summary>
        public float Knockback;
        /// <summary>Length of the item's sprite in pixels before scaling; sets the swing's reach.</summary>
        public int SizePx = 16;
        /// <summary>Size multiplier for the swung item (and so its melee reach).</summary>
        public float Scale = 1f;
        public bool Consumable;
        public Color IconColor = Color.white;

        public bool IsPickaxe => PickPower > 0;
        public bool PlacesTile => CreateTile != TileId.Air;
        public bool DealsDamage => Damage > 0;
        public float SwingLengthPx => SizePx * Scale;
    }

    public static class ItemDefs
    {
        static readonly ItemDef s_None = new() { Id = ItemId.None, Name = "" };

        // Dirt Block: useTime 10, useAnimation 15, autoReuse, max stack 9999.
        static readonly ItemDef s_Dirt = new()
        {
            Id = ItemId.DirtBlock, Name = "Dirt Block", MaxStack = 9999, UseStyle = UseStyle.Swing,
            UseAnimation = 15, UseTime = 10, AutoReuse = true, CreateTile = TileId.Dirt, Consumable = true,
            IconColor = new Color32(151, 107, 75, 255),
        };

        // CTG2 Shardstone Pickaxe: useTime 7 / useAnimation 14, damage 4, knockback 0.7, scale 0.8.
        // Pick power 190; no extra reach. 32 px sprite length is a placeholder until real art exists.
        static readonly ItemDef s_Shardstone = new()
        {
            Id = ItemId.ShardstonePickaxe, Name = "Shardstone Pickaxe", UseStyle = UseStyle.Swing,
            UseAnimation = 14, UseTime = 7, AutoReuse = true, PickPower = 190,
            Damage = 4, Knockback = 0.7f, SizePx = 32, Scale = 0.8f,
            IconColor = new Color32(120, 200, 220, 255),
        };

        // CTG2 Upgraded Shardstone Pickaxe: useTime 6 / useAnimation 12, damage 4, knockback 0.7, scale 0.8.
        static readonly ItemDef s_UpgradedShardstone = new()
        {
            Id = ItemId.UpgradedShardstonePickaxe, Name = "Upgraded Shardstone Pickaxe", UseStyle = UseStyle.Swing,
            UseAnimation = 12, UseTime = 6, AutoReuse = true, PickPower = 190,
            Damage = 4, Knockback = 0.7f, SizePx = 32, Scale = 0.8f,
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
