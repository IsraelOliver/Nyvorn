using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlayerSaveData
    {
        public int Version { get; init; } = 3;
        public required string PlayerId { get; init; }
        public required string Name { get; init; }
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
        public DateTime SavedAtUtc { get; init; } = DateTime.UtcNow;
        public int SelectedHotbarIndex { get; init; }

        // -1 = no saved health (old save predating this field, or never set) - restoring should
        // leave the player at full health instead of treating a missing value as 0/dead.
        public int CurrentHealth { get; init; } = -1;
        public List<PlayerInventorySlotSaveData> HotbarSlots { get; init; } = new();
        public List<PlayerInventorySlotSaveData> InventorySlots { get; init; } = new();
    }
}
