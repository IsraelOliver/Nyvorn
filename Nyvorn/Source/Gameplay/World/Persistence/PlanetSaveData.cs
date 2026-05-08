using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlanetSaveData
    {
        public int Version { get; init; } = 7;
        public required PlanetWorldMetadata Metadata { get; set; }
        public DateTime SavedAtUtc { get; init; } = DateTime.UtcNow;
        public List<WorldTileChange> TileChanges { get; init; } = new();
        public List<WorldItemSaveData> WorldItems { get; init; } = new();
        public List<TreeSaveData> Trees { get; init; } = new();
        public byte[] WorldTileSnapshot { get; init; }
        public byte[] SandSnapshot { get; init; }
        public byte[] TissueFieldSnapshot { get; init; }
        public byte[] TissueAnalysisSnapshot { get; init; }
    }
}
