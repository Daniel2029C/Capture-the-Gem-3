using System;
using System.Collections.Generic;
using CTG.Gameplay;
using CTG.Items;

namespace CTG.Classes
{
    public enum ClassId
    {
        None = 0,
        Archer = 1,
        Tree = 2,
    }

    /// <summary>One starting item: hotbar/inventory slot, item and stack size.</summary>
    [Serializable]
    public readonly struct LoadoutEntry
    {
        public readonly int Slot;
        public readonly ItemId Item;
        public readonly int Count;

        public LoadoutEntry(int slot, ItemId item, int count = 1)
        {
            Slot = slot;
            Item = item;
            Count = count;
        }
    }

    /// <summary>
    /// A playable class: the stats and starting loadout a player takes on when they pick it. Pure data,
    /// so a class is added by describing it in <see cref="ClassDefs"/>. Abilities a class has built in
    /// (double jump, hook) are described here too; the rest will be attached the same way.
    /// </summary>
    public sealed class ClassDef
    {
        public ClassId Id;
        public string Name;
        public string Description;

        public int MaxLife = 100;
        /// <summary>0 = the class has no mana (and no mana bar).</summary>
        public int MaxMana;
        /// <summary>Flat defense added to the player.</summary>
        public int BonusDefense;
        /// <summary>Fraction added to the top running speed: 0.1 = 10% faster.</summary>
        public float BonusMoveSpeed;
        /// <summary>Flat addition to run acceleration, px/tick².</summary>
        public float BonusRunAcceleration;

        /// <summary>One extra jump in mid-air. Null = none.</summary>
        public DoubleJumpStats DoubleJump;

        /// <summary>Ability 1: grappling hooks fired toward the cursor. Null = none.</summary>
        public GrappleStats Hook;

        /// <summary>Items placed in the inventory on selection, replacing whatever was there.</summary>
        public IReadOnlyList<LoadoutEntry> Loadout = Array.Empty<LoadoutEntry>();
    }

    public static class ClassDefs
    {
        // Archer: 140 life, no mana, double jump, Shardstone Pickaxe in slot 1 and Rancor in slot 3. The rest of its
        // CTG2 loadout (potions, armor, accessories, dyes) is left out until those items exist.
        static readonly ClassDef s_Archer = new()
        {
            Id = ClassId.Archer, Name = "Archer", Description = "Charge bow: Hellfire and Shimmer arrows.",
            MaxLife = 140, MaxMana = 0,
            DoubleJump = new DoubleJumpStats(),
            Loadout = new[]
            {
                new LoadoutEntry(0, ItemId.ShardstonePickaxe),
                new LoadoutEntry(2, ItemId.Rancor),
            },
        };

        // Tree: 150 life, no mana, and only the Upgraded Shardstone Pickaxe (plus the blocks every class
        // gets). Its ability 1 is the Foliage Tendrils hook.
        static readonly ClassDef s_Tree = new()
        {
            Id = ClassId.Tree, Name = "Tree", Description = "Foliage Tendrils: up to 4 grappling hooks.",
            MaxLife = 150, MaxMana = 0,
            Hook = new GrappleStats(),
            Loadout = new[]
            {
                new LoadoutEntry(0, ItemId.UpgradedShardstonePickaxe),
            },
        };

        static readonly ClassDef[] s_All = { s_Archer, s_Tree };

        /// <summary>Classes offered in the selection screen, in display order.</summary>
        public static IReadOnlyList<ClassDef> All => s_All;

        public static ClassDef Get(ClassId id)
        {
            for (int i = 0; i < s_All.Length; i++)
            {
                if (s_All[i].Id == id) return s_All[i];
            }

            return null;
        }
    }
}
