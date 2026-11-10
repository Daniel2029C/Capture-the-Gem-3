using System.Collections.Generic;
using CTG.Core;

namespace CTG.Economy
{
    /// <summary>
    /// What a player has bought: artifacts (owned or not) and the spell book. A player holds at most
    /// <see cref="SpellBook.SlotCount"/> different spells; buying one already held adds a use.
    /// </summary>
    public sealed class Purchases
    {
        readonly HashSet<ArtifactId> m_Artifacts = new();
        public SpellBook Spells { get; } = new();

        public bool Has(ArtifactId id) => m_Artifacts.Contains(id);
        public IEnumerable<ArtifactId> Artifacts => m_Artifacts;
        public void Give(ArtifactId id) => m_Artifacts.Add(id);

        /// <summary>A new match or class: nothing owned, and the free starting healing potion.</summary>
        public void Reset()
        {
            m_Artifacts.Clear();
            Spells.Reset();
            Spells.Add(SpellId.HealingPotion, 1);
        }
    }

    /// <summary>The spell slots: which spell each holds, how many uses are left, and cooldowns per spell.</summary>
    public sealed class SpellBook
    {
        public const int SlotCount = 2;

        readonly SpellId?[] m_Slots = new SpellId?[SlotCount];
        readonly int[] m_Counts = new int[SlotCount];
        readonly int[] m_Cooldowns = new int[2]; // by SpellId

        public SpellId? SlotSpell(int slot) => m_Slots[slot];
        public int Uses(int slot) => m_Counts[slot];
        public int Cooldown(SpellId id) => m_Cooldowns[(int)id];
        public bool IsReady(SpellId id) => m_Cooldowns[(int)id] <= 0;

        public void Reset()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                m_Slots[i] = null;
                m_Counts[i] = 0;
            }

            m_Cooldowns[0] = 0;
            m_Cooldowns[1] = 0;
        }

        /// <summary>Counts cooldowns down; once per tick.</summary>
        public void Tick()
        {
            for (int i = 0; i < m_Cooldowns.Length; i++)
            {
                if (m_Cooldowns[i] > 0) m_Cooldowns[i]--;
            }
        }

        /// <summary>The slot already holding this spell, else a free one, else -1 (no room).</summary>
        public int SlotFor(SpellId id)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (m_Slots[i] == id) return i;
            }

            for (int i = 0; i < SlotCount; i++)
            {
                if (!m_Slots[i].HasValue) return i;
            }

            return -1;
        }

        public bool CanAdd(SpellId id) => SlotFor(id) >= 0;

        public bool Add(SpellId id, int uses)
        {
            int slot = SlotFor(id);
            if (slot < 0) return false;

            m_Slots[slot] = id;
            m_Counts[slot] += uses;
            return true;
        }

        /// <summary>Spends one use of the slot's spell and starts its cooldown. False if empty or cooling down.</summary>
        public bool TryConsume(int slot, int cooldownTicks, out SpellId id)
        {
            id = default;
            if (!m_Slots[slot].HasValue || m_Counts[slot] <= 0) return false;

            id = m_Slots[slot].Value;
            if (!IsReady(id)) return false;

            m_Counts[slot]--;
            m_Cooldowns[(int)id] = cooldownTicks;
            if (m_Counts[slot] <= 0) m_Slots[slot] = null; // a used-up spell frees its slot
            return true;
        }
    }

    /// <summary>Buying: checks the rules and spends the shards.</summary>
    public static class Shop
    {
        public static bool CanBuy(Gameplay.PlayerCharacter player, ShopItem item, out string reason)
        {
            reason = null;
            if (item.Kind == ShopItemKind.Artifact && player.Purchases.Has(item.Artifact))
            {
                reason = "Owned";
                return false;
            }

            if (item.Kind == ShopItemKind.Spell && !player.Purchases.Spells.CanAdd(item.Spell))
            {
                reason = "Spell slots full";
                return false;
            }

            if (player.Progress.Shards < item.Cost)
            {
                reason = "Not enough shards";
                return false;
            }

            return true;
        }

        public static bool TryBuy(Gameplay.PlayerCharacter player, ShopItem item)
        {
            if (!CanBuy(player, item, out _)) return false;
            if (!player.Progress.TrySpend(item.Cost)) return false;

            if (item.Kind == ShopItemKind.Artifact) player.Purchases.Give(item.Artifact);
            else player.Purchases.Spells.Add(item.Spell, 1);
            return true;
        }
    }
}
