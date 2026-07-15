using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueGenerationResult
    {
        public required TissueNetwork Network { get; init; }
        public required TissueField RasterizedField { get; init; }
        public required TissueGenerationStats Stats { get; init; }
    }

    public sealed class TissueGenerationStats
    {
        public ulong DeterministicHash { get; init; }
    }
}
