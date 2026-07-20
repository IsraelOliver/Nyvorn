using System;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlayerSaveSummary
    {
        public required string FilePath { get; init; }
        public required string PlayerId { get; init; }
        public required string Name { get; init; }
        public required DateTime SavedAtUtc { get; init; }
    }
}
