namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenConfig
    {
        public WorldSizePreset SizePreset { get; init; } = WorldSizePreset.Medium;
        public int Seed { get; init; } = 1337;
        public int WorldWidth { get; init; } = 240;
        public int WorldHeight { get; init; } = 80;
        public int TileSize { get; init; } = 8;
        public int SpawnApproximateTileX { get; init; } = 20;

        public int SurfaceBaseHeight { get; init; } = 30;
        public int SurfaceAmplitude { get; init; } = 6;
        public float SurfaceFrequency { get; init; } = 0.02f;
        public float SurfaceDetailFrequency { get; init; } = 0.075f;
        public float SurfaceDetailAmplitude { get; init; } = 1.5f;
        public float SurfaceWarpFrequency { get; init; } = 0.02f;
        public float SurfaceWarpStrength { get; init; } = 10f;
        public int SurfaceSmoothingPasses { get; init; } = 3;
        public int MaxSurfaceStepPerColumn { get; init; } = 1;
        public int SurfaceSpawnFlattenHalfWidth { get; init; } = 24;
        public int SurfaceSpawnFlattenBlendWidth { get; init; } = 18;

        public int DirtDepthBelowSurface { get; init; } = 4;
        public int StoneStartDepth { get; init; } = 5;
        public int StoneFullDepth { get; init; } = 18;

        public int SandRegionCount { get; init; } = 1;
        public int SandRegionMinWidth { get; init; } = 120;
        public int SandRegionMaxWidth { get; init; } = 240;
        public int SandRegionMaxDepth { get; init; } = 12;

        public float CaveFieldFrequency { get; init; } = 0.0105f;
        public float CaveFieldWarpFrequency { get; init; } = 0.019f;
        public float CaveFieldWarpStrengthX { get; init; } = 10f;
        public float CaveFieldWarpStrengthY { get; init; } = 8f;
        public float CaveFieldThresholdShallowMin { get; init; } = 0.020f;
        public float CaveFieldThresholdShallowMax { get; init; } = 0.050f;
        public float CaveFieldThresholdCavernMin { get; init; } = 0.055f;
        public float CaveFieldThresholdCavernMax { get; init; } = 0.095f;
        public float CaveFieldThresholdDeepMin { get; init; } = 0.040f;
        public float CaveFieldThresholdDeepMax { get; init; } = 0.075f;
        public float CaveFieldPreservationScale { get; init; } = 0.36f;
        public float CaveFieldPreservationThresholdShallow { get; init; } = 0.97f;
        public float CaveFieldPreservationThresholdCavern { get; init; } = 0.74f;
        public float CaveFieldPreservationThresholdDeep { get; init; } = 0.70f;

        public int CaveWormCountShallow { get; init; } = 0;
        public int CaveWormCountMid { get; init; } = 0;
        public int CaveWormCountDeep { get; init; } = 0;
        public int CaveWormLengthMin { get; init; } = 18;
        public int CaveWormLengthMax { get; init; } = 52;
        public int CaveWormRadiusMin { get; init; } = 1;
        public int CaveWormRadiusMax { get; init; } = 3;
        public int CaveHorizontalSegments { get; init; } = 8;
        public int CaveLayerOvershootShallow { get; init; } = 10;
        public int CaveLayerOvershootCavern { get; init; } = 16;
        public int CaveLayerOvershootDeep { get; init; } = 18;
        public int CaveChamberCountShallow { get; init; } = 0;
        public int CaveChamberCountCavern { get; init; } = 0;
        public int CaveChamberCountDeep { get; init; } = 0;
        public int CaveGuaranteedLargeChambersCavern { get; init; } = 0;
        public int CaveGuaranteedLargeChambersDeep { get; init; } = 0;
        public int CaveMacroChamberCountCavern { get; init; } = 0;
        public int CaveMacroChamberCountDeep { get; init; } = 0;
        public int CaveChamberSmallRadiusMin { get; init; } = 3;
        public int CaveChamberSmallRadiusMax { get; init; } = 6;
        public int CaveChamberMediumRadiusMin { get; init; } = 6;
        public int CaveChamberMediumRadiusMax { get; init; } = 10;
        public int CaveChamberLargeRadiusMin { get; init; } = 10;
        public int CaveChamberLargeRadiusMax { get; init; } = 16;
        public int CaveMacroChamberRadiusXMin { get; init; } = 22;
        public int CaveMacroChamberRadiusXMax { get; init; } = 40;
        public int CaveMacroChamberRadiusYMin { get; init; } = 14;
        public int CaveMacroChamberRadiusYMax { get; init; } = 26;
        public int CavernRegionCount { get; init; } = 0;
        public int DeepRegionCount { get; init; } = 0;
        public int CavernRegionRadiusXMin { get; init; } = 24;
        public int CavernRegionRadiusXMax { get; init; } = 48;
        public int CavernRegionRadiusYMin { get; init; } = 14;
        public int CavernRegionRadiusYMax { get; init; } = 28;
        public int DeepRegionRadiusXMin { get; init; } = 18;
        public int DeepRegionRadiusXMax { get; init; } = 38;
        public int DeepRegionRadiusYMin { get; init; } = 12;
        public int DeepRegionRadiusYMax { get; init; } = 24;
        public float CavernRegionCenterChance { get; init; } = 0.82f;
        public float CavernRegionEdgeChance { get; init; } = 0.28f;
        public float DeepRegionCenterChance { get; init; } = 0.72f;
        public float DeepRegionEdgeChance { get; init; } = 0.22f;

        public int RefinementPasses { get; init; } = 1;
        public int SpawnSearchRange { get; init; } = 96;

        public float SpaceLayerEndPercent { get; init; } = 0.12f;
        public float SurfaceLayerEndPercent { get; init; } = 0.22f;
        public float ShallowLayerEndPercent { get; init; } = 0.42f;
        public float CavernLayerEndPercent { get; init; } = 0.72f;

        public WorldGenDebugOptions Debug { get; init; } = new WorldGenDebugOptions();
        public float BiomeFrequency { get; init; } = 0.0028f;
        public float MaterialFrequency { get; init; } = 0.006f;
        public float CaveFrequency { get; init; } = 0.009f;
        public float CaveRoomFrequency { get; init; } = 0.0045f;
        public int BorderThickness { get; init; } = 1;

        public static WorldGenConfig CreatePreset(WorldSizePreset sizePreset, int seed = 1337)
        {
            return sizePreset switch
            {
                WorldSizePreset.Small => CreateScaledPreset(sizePreset, seed, 0.60f),
                WorldSizePreset.Medium => CreateScaledPreset(sizePreset, seed, 0.70f),
                _ => CreateScaledPreset(sizePreset, seed, 1.0f)
            };
        }

        private static WorldGenConfig CreateScaledPreset(WorldSizePreset sizePreset, int seed, float sizeScale)
        {
            float frequencyScale = 1f / sizeScale;
            int worldWidth = ScaleInt(6400, sizeScale);
            int worldHeight = ScaleInt(1800, sizeScale);
            int spawnApproximateTileX = ScaleInt(420, sizeScale);

            int spaceEnd = (int)System.MathF.Round(worldHeight * 0.12f);
            int surfaceEnd = (int)System.MathF.Round(worldHeight * 0.22f);
            int surfaceStartY = spaceEnd + 1;
            int surfaceEndY = System.Math.Max(surfaceStartY, surfaceEnd);
            int safeSurfaceMinY = System.Math.Min(surfaceEndY, surfaceStartY + 2);
            int safeSurfaceMaxY = System.Math.Max(safeSurfaceMinY, surfaceEndY - 2);
            int surfaceBaseHeight = (safeSurfaceMinY + safeSurfaceMaxY) / 2;
            int maxSurfaceDeviation = System.Math.Max(2, (safeSurfaceMaxY - safeSurfaceMinY) / 2);
            int surfaceAmplitude = System.Math.Max(2, (int)System.MathF.Round(maxSurfaceDeviation * 0.7f));
            float surfaceDetailAmplitude = System.MathF.Max(1f, maxSurfaceDeviation * 0.3f);

            return new WorldGenConfig
            {
                SizePreset = sizePreset,
                Seed = seed,
                WorldWidth = worldWidth,
                WorldHeight = worldHeight,
                TileSize = 8,
                SpawnApproximateTileX = spawnApproximateTileX,
                SurfaceBaseHeight = surfaceBaseHeight,
                SurfaceAmplitude = surfaceAmplitude,
                SurfaceFrequency = 0.0016f * frequencyScale,
                SurfaceDetailFrequency = 0.0065f * frequencyScale,
                SurfaceDetailAmplitude = surfaceDetailAmplitude,
                SurfaceWarpFrequency = 0.0018f * frequencyScale,
                SurfaceWarpStrength = System.MathF.Max(4f, maxSurfaceDeviation * 1.25f),
                SurfaceSmoothingPasses = 4,
                MaxSurfaceStepPerColumn = 1,
                SurfaceSpawnFlattenHalfWidth = ScaleInt(30, sizeScale, min: 14),
                SurfaceSpawnFlattenBlendWidth = ScaleInt(20, sizeScale, min: 10),
                DirtDepthBelowSurface = ScaleInt(4, sizeScale, min: 3),
                StoneStartDepth = ScaleInt(10, sizeScale, min: 6),
                StoneFullDepth = ScaleInt(240, sizeScale, min: 140),
                SandRegionCount = 1,
                SandRegionMinWidth = ScaleInt(180, sizeScale, min: 90),
                SandRegionMaxWidth = ScaleInt(360, sizeScale, min: 180),
                SandRegionMaxDepth = ScaleInt(14, sizeScale, min: 8),
                CaveFieldFrequency = 0.0064f * frequencyScale,
                CaveFieldWarpFrequency = 0.0125f * frequencyScale,
                CaveFieldWarpStrengthX = System.MathF.Max(6f, 10f * sizeScale),
                CaveFieldWarpStrengthY = System.MathF.Max(5f, 8f * sizeScale),
                CaveFieldThresholdShallowMin = 0.016f,
                CaveFieldThresholdShallowMax = 0.040f,
                CaveFieldThresholdCavernMin = 0.036f,
                CaveFieldThresholdCavernMax = 0.070f,
                CaveFieldThresholdDeepMin = 0.024f,
                CaveFieldThresholdDeepMax = 0.052f,
                CaveFieldPreservationScale = 0.34f,
                CaveFieldPreservationThresholdShallow = 0.98f,
                CaveFieldPreservationThresholdCavern = 0.76f,
                CaveFieldPreservationThresholdDeep = 0.72f,
                CaveWormCountShallow = ScaleInt(34, sizeScale, min: 16),
                CaveWormCountMid = ScaleInt(80, sizeScale, min: 36),
                CaveWormCountDeep = ScaleInt(42, sizeScale, min: 20),
                CaveWormLengthMin = ScaleInt(42, sizeScale, min: 24),
                CaveWormLengthMax = ScaleInt(136, sizeScale, min: 72),
                CaveWormRadiusMin = 2,
                CaveWormRadiusMax = ScaleInt(7, sizeScale, min: 4),
                CaveHorizontalSegments = ScaleInt(10, sizeScale, min: 8),
                CaveLayerOvershootShallow = ScaleInt(10, sizeScale, min: 8),
                CaveLayerOvershootCavern = ScaleInt(16, sizeScale, min: 12),
                CaveLayerOvershootDeep = ScaleInt(18, sizeScale, min: 14),
                CaveChamberCountShallow = ScaleInt(4, sizeScale, min: 2),
                CaveChamberCountCavern = ScaleInt(24, sizeScale, min: 14),
                CaveChamberCountDeep = ScaleInt(12, sizeScale, min: 8),
                CaveGuaranteedLargeChambersCavern = ScaleInt(4, sizeScale, min: 3),
                CaveGuaranteedLargeChambersDeep = ScaleInt(3, sizeScale, min: 2),
                CaveMacroChamberCountCavern = ScaleInt(4, sizeScale, min: 3),
                CaveMacroChamberCountDeep = ScaleInt(2, sizeScale, min: 1),
                CaveChamberSmallRadiusMin = ScaleInt(6, sizeScale, min: 4),
                CaveChamberSmallRadiusMax = ScaleInt(12, sizeScale, min: 7),
                CaveChamberMediumRadiusMin = ScaleInt(12, sizeScale, min: 8),
                CaveChamberMediumRadiusMax = ScaleInt(20, sizeScale, min: 12),
                CaveChamberLargeRadiusMin = ScaleInt(22, sizeScale, min: 18),
                CaveChamberLargeRadiusMax = ScaleInt(34, sizeScale, min: 24),
                CaveMacroChamberRadiusXMin = ScaleInt(28, sizeScale, min: 22),
                CaveMacroChamberRadiusXMax = ScaleInt(52, sizeScale, min: 36),
                CaveMacroChamberRadiusYMin = ScaleInt(18, sizeScale, min: 14),
                CaveMacroChamberRadiusYMax = ScaleInt(34, sizeScale, min: 24),
                CavernRegionCount = ScaleInt(5, sizeScale, min: 3),
                DeepRegionCount = ScaleInt(2, sizeScale, min: 1),
                CavernRegionRadiusXMin = ScaleInt(32, sizeScale, min: 24),
                CavernRegionRadiusXMax = ScaleInt(60, sizeScale, min: 42),
                CavernRegionRadiusYMin = ScaleInt(18, sizeScale, min: 14),
                CavernRegionRadiusYMax = ScaleInt(34, sizeScale, min: 24),
                DeepRegionRadiusXMin = ScaleInt(24, sizeScale, min: 18),
                DeepRegionRadiusXMax = ScaleInt(44, sizeScale, min: 30),
                DeepRegionRadiusYMin = ScaleInt(16, sizeScale, min: 12),
                DeepRegionRadiusYMax = ScaleInt(28, sizeScale, min: 20),
                CavernRegionCenterChance = 0.82f,
                CavernRegionEdgeChance = 0.28f,
                DeepRegionCenterChance = 0.70f,
                DeepRegionEdgeChance = 0.20f,
                RefinementPasses = 1,
                SpawnSearchRange = ScaleInt(96, sizeScale, min: 48)
            };
        }

        private static int ScaleInt(int baseValue, float scale, int min = 1)
        {
            return System.Math.Max(min, (int)System.MathF.Round(baseValue * scale));
        }
    }
}
