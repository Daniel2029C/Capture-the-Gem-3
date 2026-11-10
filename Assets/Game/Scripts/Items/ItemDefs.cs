using System;
using CTG.Combat;
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
        Rancor = 10002,
        WardBlock = 10003,
        GroveBoomerang = 10004,
    }

    public enum UseStyle
    {
        None,
        /// <summary>Overhead swing: pickaxes, swords, placing blocks.</summary>
        Swing,
        /// <summary>Hold to draw and charge, release to loose an arrow (see <see cref="ChargeBowStats"/>).</summary>
        ChargeBow,
        /// <summary>Throws a projectile toward the cursor when used (see <see cref="ItemDef.UseDelayTicks"/>).</summary>
        Throw,
    }

    /// <summary>
    /// Tuning for charge bows, ported from CTG2's Rancor. Holding the primary button charges a primary
    /// arrow, the secondary button a secondary arrow; releasing fires it. Charge runs from 0 to
    /// <see cref="MaxCharge"/>; an arrow's power is MinPower at no charge rising linearly to 1 at full, and
    /// scales both its speed and its damage. Arrows are unlimited.
    /// </summary>
    [Serializable]
    public sealed class ChargeBowStats
    {
        /// <summary>Minimum ticks between starting two primary draws, and separately two secondary draws.
        /// Starting a secondary draw also restarts the primary timer if it had already run out.</summary>
        public int UseDelayTicks = 50;
        public float MaxCharge = 40f;
        /// <summary>Power at zero charge (speed and damage multiplier).</summary>
        public float MinPower = 0.5f;
        /// <summary>Arrow speed = ShootSpeed × power × (SpeedMultiplier − speed penalty), px/update.</summary>
        public float SpeedMultiplier = 1.8f;
        /// <summary>Updates per tick beyond the first for every arrow fired.</summary>
        public int ArrowExtraUpdates = 1;
        /// <summary>Arrows spawn this far from the player's centre along the aim, px.</summary>
        public float SpawnOffsetPx = 2f * 1.41421356f;

        [Header("Primary (left click)")]
        public ProjectileId PrimaryArrow = ProjectileId.EmberArrow;
        public float PrimaryChargePerTick = 0.8f;
        /// <summary>Subtracted from the bow's damage before the power multiplier.</summary>
        public int PrimaryDamagePenalty = 4;

        [Header("Secondary (right click)")]
        public ProjectileId SecondaryArrow = ProjectileId.GleamArrow;
        public float SecondaryChargePerTick = 1f;
        /// <summary>Released below this charge, the secondary arrow is slower so its rise curves it sharply.</summary>
        public float SecondaryCurveBelowCharge = 30f;
        public float SecondaryCurvedSpeedPenalty = 0.5f;
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
        /// <summary>Minimum ticks between throws of this item, counted from one throw to the next. Kept across item switches.</summary>
        public int UseDelayTicks;
        /// <summary>Keep using while the button is held.</summary>
        public bool AutoReuse;
        /// <summary>Pickaxe power. 0 = not a pickaxe.</summary>
        public int PickPower;
        /// <summary>The tile this item places, if any.</summary>
        public TileId CreateTile = TileId.Air;
        /// <summary>The tile placed by the blue team, for items whose block comes in team colours. Air = same as <see cref="CreateTile"/>.</summary>
        public TileId CreateTileBlue = TileId.Air;
        /// <summary>Extra tile reach.</summary>
        public int TileBoost;
        /// <summary>Melee damage per hit. 0 = deals no damage.</summary>
        public int Damage;
        /// <summary>Knockback applied to players hit. 1 = standard knockback.</summary>
        public Combat.KnockbackScale Knockback;
        /// <summary>Length of the item's sprite in pixels before scaling; sets the swing's reach.</summary>
        public int SizePx = 16;
        /// <summary>Size multiplier for the swung item (and so its melee reach).</summary>
        public float Scale = 1f;
        public bool Consumable;
        public Color IconColor = Color.white;
        /// <summary>Base projectile speed, px/update.</summary>
        public float ShootSpeed;
        /// <summary>The projectile a <see cref="UseStyle.Throw"/> item throws.</summary>
        public Combat.ProjectileId Projectile;
        /// <summary>Charge bow tuning; set for <see cref="UseStyle.ChargeBow"/> items.</summary>
        public ChargeBowStats ChargeBow;

        public bool IsPickaxe => PickPower > 0;
        public bool PlacesTile => CreateTile != TileId.Air;

        /// <summary>The tile this item places for a player on the given team.</summary>
        public TileId TileFor(Combat.Team team) =>
            team == Combat.Team.Blue && CreateTileBlue != TileId.Air ? CreateTileBlue : CreateTile;
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
            UseAnimation = 15, UseTime = 10, AutoReuse = true, CreateTile = TileId.RedDirt, CreateTileBlue = TileId.BlueDirt, Consumable = true,
            IconColor = new Color32(151, 107, 75, 255),
        };

        // Shardstone Pickaxe: useTime 7 / useAnimation 14, damage 2, knockback 0.7, scale 0.8. Classes that had the
        // upgraded version mine faster through ClassDef.MiningSpeedMultiplier (7/6 gives useTime 6 / useAnimation 12).
        // Pick power 190; no extra reach. 32 px sprite length is a placeholder until real art exists.
        static readonly ItemDef s_Shardstone = new()
        {
            Id = ItemId.ShardstonePickaxe, Name = "Shardstone Pickaxe", UseStyle = UseStyle.Swing,
            UseAnimation = 14, UseTime = 7, AutoReuse = true, PickPower = 190,
            Damage = 2, Knockback = 0.7f, SizePx = 32, Scale = 0.8f,
            IconColor = new Color32(120, 200, 220, 255),
        };

        // CTG2 Rancor (archer bow): damage 37, shoot speed 6.7. useTime 28 is how long the bow stays
        // drawn after a shot before another draw can start. Knockback is in the per-weapon player
        // knockback scale (1 = standard); CTG2's item lists 5, but there every player hit used the
        // standard knockback.
        static readonly ItemDef s_Rancor = new()
        {
            Id = ItemId.Rancor, Name = "Rancor", UseStyle = UseStyle.ChargeBow,
            UseAnimation = 28, UseTime = 28, Damage = 37, Knockback = 1f, ShootSpeed = 6.7f,
            ChargeBow = new ChargeBowStats(),
            IconColor = new Color32(190, 60, 70, 255),
        };

        // Ward Block: placed like dirt; blocks building on its tile while staying walk-through.
        static readonly ItemDef s_Ward = new()
        {
            Id = ItemId.WardBlock, Name = "Ward Block", MaxStack = 9999, UseStyle = UseStyle.Swing,
            UseAnimation = 15, UseTime = 10, AutoReuse = true, CreateTile = TileId.Ward, Consumable = true,
            IconColor = new Color32(120, 220, 255, 255),
        };

        // Tree's boomerang: damage 32, shoot speed 12, useTime/useAnimation 20, no auto-reuse, and a 52-tick delay
        // between throws. Knockback is 1 (the standard player knockback): CTG2's item lists 6, but there every
        // player hit used the standard knockback.
        static readonly ItemDef s_GroveBoomerang = new()
        {
            Id = ItemId.GroveBoomerang, Name = "Grove Boomerang", UseStyle = UseStyle.Throw,
            UseAnimation = 20, UseTime = 20, UseDelayTicks = 52, AutoReuse = false,
            Damage = 32, Knockback = 1f, ShootSpeed = 12f, Projectile = Combat.ProjectileId.GroveBoomerang,
            IconColor = new Color32(235, 205, 70, 255),
        };

        public static ItemDef Get(ItemId id) => id switch
        {
            ItemId.GroveBoomerang => s_GroveBoomerang,
            ItemId.WardBlock => s_Ward,
            ItemId.Rancor => s_Rancor,
            ItemId.DirtBlock => s_Dirt,
            ItemId.ShardstonePickaxe => s_Shardstone,
            _ => s_None,
        };
    }
}
