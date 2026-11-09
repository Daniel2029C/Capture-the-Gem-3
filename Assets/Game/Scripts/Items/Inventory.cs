using System;

namespace CTG.Items
{
    [Serializable]
    public struct ItemStack
    {
        public ItemId Id;
        public int Count;

        public bool IsEmpty => Id == ItemId.None || Count <= 0;
        public ItemDef Def => ItemDefs.Get(Id);

        public static ItemStack Empty => default;
    }

    /// <summary>
    /// Inventory: 50 main slots, the first 10 being the hotbar.
    /// </summary>
    public class Inventory
    {
        public const int SlotCount = 50;
        public const int HotbarSize = 10;

        readonly ItemStack[] m_Slots = new ItemStack[SlotCount];

        public int SelectedSlot { get; private set; }
        public event Action Changed;

        public ItemStack this[int slot] => m_Slots[slot];
        public ItemStack Selected => m_Slots[SelectedSlot];

        public void Select(int hotbarSlot)
        {
            int clamped = ((hotbarSlot % HotbarSize) + HotbarSize) % HotbarSize;
            if (clamped == SelectedSlot) return;

            SelectedSlot = clamped;
            Changed?.Invoke();
        }

        public void Set(int slot, ItemId id, int count)
        {
            m_Slots[slot] = count > 0 ? new ItemStack { Id = id, Count = count } : ItemStack.Empty;
            Changed?.Invoke();
        }

        /// <summary>
        /// Adds items, topping up existing stacks first and then filling empty slots in order.
        /// Returns how many didn't fit; the caller decides what happens to those (CTG rule: lost).
        /// </summary>
        public int Add(ItemId id, int count)
        {
            if (id == ItemId.None || count <= 0) return 0;

            int maxStack = ItemDefs.Get(id).MaxStack;
            int remaining = count;

            for (int i = 0; i < SlotCount && remaining > 0; i++)
            {
                if (m_Slots[i].Id != id || m_Slots[i].Count >= maxStack) continue;

                int moved = Math.Min(maxStack - m_Slots[i].Count, remaining);
                m_Slots[i].Count += moved;
                remaining -= moved;
            }

            for (int i = 0; i < SlotCount && remaining > 0; i++)
            {
                if (!m_Slots[i].IsEmpty) continue;

                int moved = Math.Min(maxStack, remaining);
                m_Slots[i] = new ItemStack { Id = id, Count = moved };
                remaining -= moved;
            }

            if (remaining != count) Changed?.Invoke();
            return remaining;
        }

        public void ConsumeFromSlot(int slot, int amount = 1)
        {
            ItemStack stack = m_Slots[slot];
            stack.Count -= amount;
            m_Slots[slot] = stack.Count > 0 ? stack : ItemStack.Empty;
            Changed?.Invoke();
        }
    }
}
