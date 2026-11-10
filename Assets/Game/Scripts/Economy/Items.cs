using System.Collections.Generic;

namespace CTG.Economy
{
    public enum ArtifactId
    {
        Chaos,
        Divine,
    }

    public enum SpellId
    {
        HealingPotion,
        Endurance,
    }

    public enum ShopItemKind
    {
        Artifact,
        Spell,
    }

    /// <summary>One thing the shop sells. An artifact is bought once; a spell can be bought again to add a use.</summary>
    public sealed class ShopItem
    {
        public ShopItemKind Kind;
        public ArtifactId Artifact;
        public SpellId Spell;
        public string Name;
        public string Description;
        public int Cost;
    }

    public static class ShopCatalog
    {
        static readonly ShopItem[] s_Items =
        {
            new() { Kind = ShopItemKind.Artifact, Artifact = ArtifactId.Chaos, Name = "Chaos Artifact", Cost = 750,
                    Description = "+20% damage with all weapons except the pickaxe. Ability cooldowns -5%." },
            new() { Kind = ShopItemKind.Artifact, Artifact = ArtifactId.Divine, Name = "Divine Artifact", Cost = 750,
                    Description = "+0.5 life/s regeneration. 10% damage reduction. Spell cooldowns -10%." },
            new() { Kind = ShopItemKind.Spell, Spell = SpellId.HealingPotion, Name = "Healing Potion", Cost = 30,
                    Description = "Spell. Instantly restores 50 life. 1 minute cooldown." },
            new() { Kind = ShopItemKind.Spell, Spell = SpellId.Endurance, Name = "Endurance", Cost = 50,
                    Description = "Spell. 10% damage reduction for 5 seconds. 1 minute cooldown." },
        };

        public static IReadOnlyList<ShopItem> All => s_Items;

        public static string SpellName(SpellId id) => id == SpellId.HealingPotion ? "Healing Potion" : "Endurance";
        public static string ArtifactName(ArtifactId id) => id == ArtifactId.Chaos ? "Chaos Artifact" : "Divine Artifact";
    }

    /// <summary>The player's multipliers from artifacts, level and active effects, recomputed each tick.</summary>
    public struct PlayerModifiers
    {
        /// <summary>Damage from every weapon except the pickaxe.</summary>
        public float WeaponDamageMultiplier;
        /// <summary>Fraction of incoming damage removed (0.1 = 10%).</summary>
        public float DamageReduction;
        /// <summary>Extra life per second.</summary>
        public float RegenBonusPerSecond;
        /// <summary>Multiplier on ability cooldown time (0.95 = 5% shorter).</summary>
        public float AbilityCooldownScale;
        /// <summary>Multiplier on spell cooldown time.</summary>
        public float SpellCooldownScale;
        /// <summary>Pickaxe use speed (from level).</summary>
        public float MiningSpeedMultiplier;
        /// <summary>Life multiplier (from level).</summary>
        public float LifeMultiplier;

        public const float ChaosDamageBonus = 0.2f;
        public const float ChaosAbilityCooldownReduction = 0.05f;
        public const float DivineRegen = 0.5f;
        public const float DivineDamageReduction = 0.1f;
        public const float DivineSpellCooldownReduction = 0.1f;
        public const float EnduranceDamageReduction = 0.1f;

        public static PlayerModifiers Compute(Purchases purchases, PlayerProgress progress, bool enduranceActive)
        {
            float level = progress.LevelScale;
            var m = new PlayerModifiers
            {
                WeaponDamageMultiplier = level,
                DamageReduction = 0f,
                RegenBonusPerSecond = 0f,
                AbilityCooldownScale = 1f,
                SpellCooldownScale = 1f,
                MiningSpeedMultiplier = level,
                LifeMultiplier = level,
            };

            if (purchases.Has(ArtifactId.Chaos))
            {
                m.WeaponDamageMultiplier *= 1f + ChaosDamageBonus;
                m.AbilityCooldownScale *= 1f - ChaosAbilityCooldownReduction;
            }

            if (purchases.Has(ArtifactId.Divine))
            {
                m.RegenBonusPerSecond += DivineRegen;
                m.DamageReduction += DivineDamageReduction;
                m.SpellCooldownScale *= 1f - DivineSpellCooldownReduction;
            }

            if (enduranceActive) m.DamageReduction += EnduranceDamageReduction;
            return m;
        }
    }
}
