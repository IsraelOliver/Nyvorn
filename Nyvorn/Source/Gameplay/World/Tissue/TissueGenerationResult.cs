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
        public int PointCount { get; init; }
        public int EdgeCount { get; init; }
        public int MicroFilamentCount { get; init; }
        public int NestCount { get; init; }
        public int RasterizedTileCount { get; init; }
        public int MinDegree { get; init; }
        public int MaxDegree { get; init; }
        public float AverageDegree { get; init; }
        public float DominantComponentRatio { get; init; }
        public int UpperPointCount { get; init; }
        public int MiddlePointCount { get; init; }
        public int DeepPointCount { get; init; }
        public ulong DeterministicHash { get; init; }
    }
}
