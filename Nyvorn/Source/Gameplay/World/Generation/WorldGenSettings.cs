namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenSettings
    {
        public int Seed { get; init; } = 1337;
        public float SurfaceFrequency { get; init; } = 0.045f;
        public int SurfaceAmplitude { get; init; } = 6;
        public int BaseGroundLevel { get; init; } = 30;
        public float SurfaceWarpFrequency { get; init; } = 0.02f;
        public float SurfaceWarpStrength { get; init; } = 16f;
        public float CaveFrequency { get; init; } = 0.09f;
        public float CaveThreshold { get; init; } = 0.64f;
        public int CaveStartDepth { get; init; } = 5;
        public float BiomeFrequency { get; init; } = 0.018f;
        public float SandBiomeThreshold { get; init; } = 0.18f;
        public int StoneDepth { get; init; } = 6;
        public int BorderThickness { get; init; } = 1;
    }
}
