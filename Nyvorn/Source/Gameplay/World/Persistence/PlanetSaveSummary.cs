using System;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlanetSaveSummary
    {
        public required string FilePath { get; init; }
        public required PlanetWorldMetadata Metadata { get; init; }
        public required DateTime SavedAtUtc { get; init; }
    }
}
