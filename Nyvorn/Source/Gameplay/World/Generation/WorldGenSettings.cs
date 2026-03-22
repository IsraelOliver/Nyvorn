namespace Nyvorn.Source.World.Generation
{
    public enum WorldSizePreset
    {
        Small = 0,
        Medium = 1,
        Large = 2
    }

    public sealed class WorldGenSettings
    {
        public WorldSizePreset SizePreset { get; init; } = WorldSizePreset.Medium;
        public int Seed { get; init; } = 1337;
        public int WorldWidth { get; init; } = 240;
        public int WorldHeight { get; init; } = 80;
        public int TileSize { get; init; } = 8;
        public int SpawnApproximateTileX { get; init; } = 20;
        public float SurfaceFrequency { get; init; } = 0.02f;
        public int SurfaceAmplitude { get; init; } = 6;
        public float SurfaceDetailFrequency { get; init; } = 0.075f;
        public float SurfaceDetailAmplitude { get; init; } = 1.5f;
        public int SurfaceSmoothingPasses { get; init; } = 3;
        public int MaxAdjacentSurfaceStep { get; init; } = 1;
        public int SpawnFlatHalfWidth { get; init; } = 12;
        public int SpawnRampWidth { get; init; } = 5;
        public int BaseGroundLevel { get; init; } = 30;
        public float SurfaceWarpFrequency { get; init; } = 0.02f;
        public float SurfaceWarpStrength { get; init; } = 10f;
        public float CaveFrequency { get; init; } = 0.09f;
        public float CaveThreshold { get; init; } = 0.64f;
        public int CaveStartDepth { get; init; } = 5;
        public float CaveRoomFrequency { get; init; } = 0.045f;
        public float CaveRoomThreshold { get; init; } = 0.58f;
        public int NaturalEntranceCount { get; init; } = 3;
        public int NaturalEntranceJitter { get; init; } = 10;
        public int ShallowUndergroundDepth { get; init; } = 12;
        public int CavernLayerDepth { get; init; } = 28;
        public int DepthsLayerDepth { get; init; } = 52;
        public float BiomeFrequency { get; init; } = 0.018f;
        public float SandBiomeThreshold { get; init; } = 0.18f;
        public float MaterialFrequency { get; init; } = 0.06f;
        public float SurfaceStoneChance { get; init; } = 0.05f;
        public float DeepStoneChance { get; init; } = 0.92f;
        public int StoneTransitionStartDepth { get; init; } = 4;
        public int StoneTransitionEndDepth { get; init; } = 28;
        public int SurfaceTopsoilDepth { get; init; } = 2;
        public int BorderThickness { get; init; } = 1;

        public WorldDepthLayer GetLayerForDepth(int depth)
        {
            if (depth <= SurfaceTopsoilDepth)
                return WorldDepthLayer.Surface;

            if (depth < ShallowUndergroundDepth)
                return WorldDepthLayer.ShallowUnderground;

            if (depth < CavernLayerDepth)
                return WorldDepthLayer.Cavern;

            return depth < DepthsLayerDepth
                ? WorldDepthLayer.Cavern
                : WorldDepthLayer.Depths;
        }

        public static WorldGenSettings CreatePreset(WorldSizePreset sizePreset, int seed = 1337)
        {
            return sizePreset switch
            {
                WorldSizePreset.Small => CreateScaledPreset(sizePreset, seed, 0.60f),
                WorldSizePreset.Medium => CreateScaledPreset(sizePreset, seed, 0.70f),
                _ => CreateScaledPreset(sizePreset, seed, 1.0f)
            };
        }

        private static WorldGenSettings CreateScaledPreset(WorldSizePreset sizePreset, int seed, float sizeScale)
        {
            float frequencyScale = 1f / sizeScale;

            return new WorldGenSettings
            {
                SizePreset = sizePreset,
                Seed = seed,
                WorldWidth = ScaleInt(6400, sizeScale),
                WorldHeight = ScaleInt(1800, sizeScale),
                TileSize = 8,
                SpawnApproximateTileX = ScaleInt(420, sizeScale),
                SurfaceFrequency = 0.0016f * frequencyScale,
                SurfaceAmplitude = ScaleInt(72, sizeScale),
                SurfaceDetailFrequency = 0.0065f * frequencyScale,
                SurfaceDetailAmplitude = 10f * sizeScale,
                SurfaceSmoothingPasses = 4,
                MaxAdjacentSurfaceStep = 1,
                SpawnFlatHalfWidth = ScaleInt(26, sizeScale, min: 12),
                SpawnRampWidth = ScaleInt(10, sizeScale, min: 5),
                BaseGroundLevel = ScaleInt(620, sizeScale),
                SurfaceWarpFrequency = 0.0018f * frequencyScale,
                SurfaceWarpStrength = 18f * sizeScale,
                CaveFrequency = 0.009f * frequencyScale,
                CaveThreshold = 0.60f,
                CaveStartDepth = ScaleInt(18, sizeScale, min: 10),
                CaveRoomFrequency = 0.0045f * frequencyScale,
                CaveRoomThreshold = 0.53f,
                NaturalEntranceCount = 3,
                NaturalEntranceJitter = ScaleInt(36, sizeScale, min: 18),
                ShallowUndergroundDepth = ScaleInt(42, sizeScale, min: 24),
                CavernLayerDepth = ScaleInt(180, sizeScale, min: 96),
                DepthsLayerDepth = ScaleInt(520, sizeScale, min: 300),
                BiomeFrequency = 0.0028f * frequencyScale,
                SandBiomeThreshold = 0.22f,
                MaterialFrequency = 0.006f * frequencyScale,
                SurfaceStoneChance = 0.03f,
                DeepStoneChance = 0.97f,
                StoneTransitionStartDepth = ScaleInt(10, sizeScale, min: 6),
                StoneTransitionEndDepth = ScaleInt(240, sizeScale, min: 140),
                SurfaceTopsoilDepth = ScaleInt(4, sizeScale, min: 3),
                BorderThickness = 1
            };
        }

        private static int ScaleInt(int baseValue, float scale, int min = 1)
        {
            return System.Math.Max(min, (int)System.MathF.Round(baseValue * scale));
        }
    }
}
