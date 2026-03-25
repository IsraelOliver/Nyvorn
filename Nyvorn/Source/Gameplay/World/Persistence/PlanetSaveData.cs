using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlanetSaveData
    {
        public int Version { get; init; } = 1;
        public required PlanetWorldMetadata Metadata { get; init; }
        public DateTime SavedAtUtc { get; init; } = DateTime.UtcNow;
        public List<WorldTileChange> TileChanges { get; init; } = new();
    }
}
