using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlanetSaveData
    {
        public int Version { get; init; } = 13;
        public required PlanetWorldMetadata Metadata { get; set; }
        public DateTime SavedAtUtc { get; init; } = DateTime.UtcNow;
        public float TimeOfDay01 { get; init; } = 0.25f;
        public List<WorldTileChange> TileChanges { get; init; } = new();
        public List<WorldItemSaveData> WorldItems { get; init; } = new();
        public List<WorkbenchSaveData> Workbenches { get; init; } = new();
        public List<DoorSaveData> Doors { get; init; } = new();
        public List<TreeSaveData> Trees { get; init; } = new();
        public List<string> ConsoleCommandHistory { get; init; } = new();
        public byte[] WorldTileSnapshot { get; init; }
        public byte[] BackgroundTileSnapshot { get; init; }
        public byte[] SandSnapshot { get; init; }
        public byte[] TissueFieldDeltaSnapshot { get; init; }
        // Campos legados mantidos apenas para desserializar saves anteriores ao v11.
        public byte[] TissueFieldSnapshot { get; init; }
        public byte[] TissueAnalysisSnapshot { get; init; }
    }
}
