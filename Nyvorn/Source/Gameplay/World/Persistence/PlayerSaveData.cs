using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlayerSaveData
    {
        public int Version { get; init; } = 1;
        public required string WorldId { get; init; }
        public DateTime SavedAtUtc { get; init; } = DateTime.UtcNow;
        public float PositionX { get; init; }
        public float PositionY { get; init; }
        public int SelectedHotbarIndex { get; init; }
        public List<PlayerInventorySlotSaveData> HotbarSlots { get; init; } = new();
        public List<PlayerInventorySlotSaveData> InventorySlots { get; init; } = new();
    }
}
