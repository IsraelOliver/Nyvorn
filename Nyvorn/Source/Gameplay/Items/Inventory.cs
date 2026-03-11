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

        public bool TryAdd(ItemDefinition definition, int amount = 1)
        {
            if (definition == null || amount <= 0)
                return false;

            int remaining = amount;

            if (definition.Stackable)
            {
                for (int i = 0; i < slots.Length && remaining > 0; i++)
                {
                    if (!slots[i].CanMerge(definition))
                        continue;

                    remaining -= slots[i].Add(definition, remaining);
                }
            }

            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                if (!slots[i].IsEmpty)
                    continue;

                remaining -= slots[i].Add(definition, remaining);
            }

            return remaining == 0;
        }
    }
}
