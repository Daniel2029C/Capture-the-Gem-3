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
    /// A class's tunable numbers, grouped so they can be compared and changed in one place. Immutable; build one with
    /// named arguments (anything left out takes its neutral default), e.g. <c>new ClassStats(maxLife: 140)</c>.
    /// </summary>
    public readonly struct ClassStats
    {
        /// <summary>Maximum life.</summary>
        public readonly int MaxLife;
        /// <summary>0 = the class has no mana (and no mana bar).</summary>
        public readonly int MaxMana;
        /// <summary>
        /// Flat defense: each point removes one point of damage from every hit (1:1), applied first. A hit that would be
        /// reduced below 1 deals 1, unless the damage reduction below is total.
        /// </summary>
        public readonly int Defense;
        /// <summary>
        /// Fraction of damage taken that is removed, 0 to 1, applied after defense: <c>damage = round(damage * (1 - reduction))</c>.
        /// 0 removes nothing and 1 makes every attack deal 0. It adds to the Divine artifact (0.10), the Endurance spell
        /// (0.10) and the Apex buff (0.15), and the total is capped at 1. Below 1 a hit still deals at least 1.
        /// Does not apply to debuffs: burning drains life directly, and lava hits use defense but not this.
        /// </summary>
        public readonly float DamageReduction;
        /// <summary>Fraction added to the top running speed: 0.1 = 10% faster.</summary>
        public readonly float MoveSpeedBonus;
        /// <summary>Flat addition to run acceleration, px/tick².</summary>
        public readonly float RunAccelerationBonus;
        /// <summary>Pickaxe use speed multiplier: 7/6 shortens the pickaxe's use time and animation by that factor.</summary>
        public readonly float MiningSpeedMultiplier;

        public ClassStats(int maxLife = 100, int maxMana = 0, int defense = 0, float damageReduction = 0f,
                          float moveSpeedBonus = 0f, float runAccelerationBonus = 0f, float miningSpeedMultiplier = 1f)
        {
            MaxLife = maxLife;
            MaxMana = maxMana;
            Defense = defense;
            DamageReduction = damageReduction;
            MoveSpeedBonus = moveSpeedBonus;
            RunAccelerationBonus = runAccelerationBonus;
            MiningSpeedMultiplier = miningSpeedMultiplier;
        }

        /// <summary>100 life, no mana, no defense or reduction, normal speed and mining.</summary>
        public static readonly ClassStats Default = new(maxLife: 100);
    }

    /// <summary>
    /// A playable class: the stats and starting loadout a player takes on when they pick it. Pure data,
    /// so a class is added by describing it in <see cref="ClassDefs"/>: its numbers (<see cref="ClassStats"/>), movement
    /// features (<see cref="Mobility"/>), abilities and starting items.
    /// </summary>
    public sealed class ClassDef
    {
        public ClassId Id { get; init; }
        public string Name { get; init; }
        public string Description { get; init; }

        /// <summary>The class's numbers: life, mana, defense, damage reduction and movement/mining bonuses.</summary>
        public ClassStats Stats { get; init; } = ClassStats.Default;

        /// <summary>
        /// Movement features the class has: double jump, grappling hooks (ability 1), fast fall and so on. List at most
        /// one of each kind.
        /// </summary>
        public IReadOnlyList<Mobility> Mobilities { get; init; } = Array.Empty<Mobility>();

        /// <summary>The abilities on slots 1-3 (default keys Shift, E, C). Missing entries are empty slots.</summary>
        public IReadOnlyList<AbilityId> Abilities { get; init; } = Array.Empty<AbilityId>();

        /// <summary>Makes the state this class's abilities and items share (see <see cref="ClassKit"/>). Null = the class has none.</summary>
        public Func<ClassKit> CreateKit { get; init; }

        /// <summary>Items placed in the inventory on selection, replacing whatever was there.</summary>
        public IReadOnlyList<LoadoutEntry> Loadout { get; init; } = Array.Empty<LoadoutEntry>();
    }

    public static class ClassDefs
    {
        // Archer: 140 life, no mana, double jump, Shardstone Pickaxe in slot 1 and Rancor in slot 3, with sprint, light arrow and Luminite abilities. The rest of its
        // CTG2 loadout (potions, armor, accessories, dyes) is left out until those items exist.
        static readonly ClassDef s_Archer = new()
        {
            Id = ClassId.Archer, Name = "Archer", Description = "",
            Stats = new ClassStats(maxLife: 140),
            Mobilities = new Mobility[] { new DoubleJumpMobility(), new FastFallMobility() },
            Abilities = new[] { AbilityId.ArcherSprint, AbilityId.ArcherLightArrow, AbilityId.ArcherLuminite },
            CreateKit = () => new ArcherKit(),
            Loadout = new[]
            {
                new LoadoutEntry(0, ItemId.ShardstonePickaxe),
                new LoadoutEntry(2, ItemId.Rancor),
            },
        };

        // Tree: 150 life, no mana, the Shardstone Pickaxe (mined at the upgraded speed) and the Grove Boomerang (plus the
        // blocks every class gets). Its ability 1 is the Foliage Tendrils hook.
        static readonly ClassDef s_Tree = new()
        {
            Id = ClassId.Tree, Name = "Tree", Description = "",
            Stats = new ClassStats(maxLife: 150, miningSpeedMultiplier: 7f / 6f),
            Mobilities = new Mobility[] { new GrappleMobility() },
            Loadout = new[]
            {
                new LoadoutEntry(0, ItemId.ShardstonePickaxe),
                new LoadoutEntry(2, ItemId.GroveBoomerang),
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
