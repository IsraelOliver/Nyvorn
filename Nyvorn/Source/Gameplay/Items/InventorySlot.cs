namespace Nyvorn.Source.Gameplay.Items
{
    public sealed class InventorySlot
    {
        public ItemId ItemId { get; private set; } = ItemId.None;
        public int Quantity { get; private set; }

        public bool IsEmpty => ItemId == ItemId.None || Quantity <= 0;

        public bool CanMerge(ItemDefinition definition)
        {
            if (definition == null || !definition.Stackable)
                return false;

            return ItemId == definition.Id && Quantity < definition.MaxStack;
        }

        public bool CanAccept(ItemDefinition definition)
        {
            if (definition == null)
                return false;

            return IsEmpty || CanMerge(definition);
        }

        public int Add(ItemDefinition definition, int amount)
        {
            if (definition == null || amount <= 0)
                return 0;

            if (IsEmpty)
            {
                ItemId = definition.Id;
                Quantity = 0;
            }
            else if (ItemId != definition.Id)
            {
                return 0;
            }

            int maxStack = definition.Stackable ? definition.MaxStack : 1;
            int spaceLeft = maxStack - Quantity;
            int added = amount > spaceLeft ? spaceLeft : amount;
            Quantity += added;

            if (Quantity <= 0)
                Clear();

            return added;
        }

        public void Clear()
        {
            ItemId = ItemId.None;
            Quantity = 0;
        }

        public void Set(ItemId itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;

            if (IsEmpty)
                Clear();
        }

        public void CopyFrom(InventorySlot other)
        {
            if (other == null)
            {
                Clear();
                return;
            }

            Set(other.ItemId, other.Quantity);
        }

        public InventorySlot Clone()
        {
            InventorySlot clone = new InventorySlot();
            clone.Set(ItemId, Quantity);
            return clone;
        }

        public bool RemoveOne()
        {
            if (IsEmpty)
                return false;

            Quantity--;
            if (Quantity <= 0)
                Clear();

            return true;
        }
    }
}
