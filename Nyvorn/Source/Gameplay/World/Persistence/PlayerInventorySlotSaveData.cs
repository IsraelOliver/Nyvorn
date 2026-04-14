using Nyvorn.Source.Gameplay.Items;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlayerInventorySlotSaveData
    {
        public int SlotIndex { get; init; }
        public ItemId ItemId { get; init; }
        public int Quantity { get; init; }
    }
}
