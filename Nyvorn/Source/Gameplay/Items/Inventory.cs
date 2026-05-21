using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Items
{
    public class Inventory
    {
        private readonly InventorySlot[] slots;

        public Inventory(int capacity)
        {
            slots = new InventorySlot[capacity];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = new InventorySlot();
        }

        public int Capacity => slots.Length;
        public IReadOnlyList<InventorySlot> Slots => slots;

        public InventorySlot GetSlot(int index)
        {
            return slots[index];
        }

        public bool ContainsItem(ItemId itemId)
        {
            if (itemId == ItemId.None)
                return false;

            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty && slots[i].ItemId == itemId)
                    return true;
            }

            return false;
        }

        public int CountItem(ItemId itemId)
        {
            if (itemId == ItemId.None)
                return 0;

            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].IsEmpty && slots[i].ItemId == itemId)
                    count += slots[i].Quantity;
            }

            return count;
        }

        public bool TryRemove(ItemId itemId, int amount)
        {
            if (itemId == ItemId.None || amount <= 0 || CountItem(itemId) < amount)
                return false;

            int remaining = amount;
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (slots[i].IsEmpty || slots[i].ItemId != itemId)
                    continue;

                remaining -= slots[i].RemoveUpTo(remaining);
            }

            return true;
        }

        public int AddToExistingStacks(ItemDefinition definition, int amount)
        {
            if (definition == null || amount <= 0 || !definition.Stackable)
                return 0;

            int remaining = amount;
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (!slots[i].CanMerge(definition))
                    continue;

                remaining -= slots[i].Add(definition, remaining);
            }

            return amount - remaining;
        }

        public int AddToEmptySlots(ItemDefinition definition, int amount)
        {
            if (definition == null || amount <= 0)
                return 0;

            int remaining = amount;
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (!slots[i].IsEmpty)
                    continue;

                remaining -= slots[i].Add(definition, remaining);
            }

            return amount - remaining;
        }

        public bool TryAdd(ItemDefinition definition, int amount = 1)
        {
            if (definition == null || amount <= 0)
                return false;

            int remaining = amount;
            remaining -= AddToExistingStacks(definition, remaining);
            remaining -= AddToEmptySlots(definition, remaining);
            return remaining == 0;
        }
    }
}
