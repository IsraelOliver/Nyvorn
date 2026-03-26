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

        public float CaveMaskSeedSolidChanceShallow { get; init; } = 0.82f;
        public float CaveMaskSeedSolidChanceCavern { get; init; } = 0.47f;
        public float CaveMaskSeedSolidChanceDeep { get; init; } = 0.58f;
        public float CaveMaskSpatialScaleShallow { get; init; } = 1.0f;
        public float CaveMaskSpatialScaleCavern { get; init; } = 3.0f;
        public float CaveMaskSpatialScaleDeepX { get; init; } = 2.6f;
        public float CaveMaskSpatialScaleDeepY { get; init; } = 4.2f;
        public int CaveMaskSmoothPassesShallow { get; init; } = 4;
        public int CaveMaskSmoothPassesCavern { get; init; } = 4;
        public int CaveMaskSmoothPassesDeep { get; init; } = 4;
        public int CaveMaskBirthLimitShallow { get; init; } = 4;
        public int CaveMaskBirthLimitCavern { get; init; } = 4;
        public int CaveMaskBirthLimitDeep { get; init; } = 4;
        public int CaveMaskDeathLimitShallow { get; init; } = 3;
        public int CaveMaskDeathLimitCavern { get; init; } = 3;
        public int CaveMaskDeathLimitDeep { get; init; } = 4;
        public int CaveMaskBoundaryFadeRowsShallow { get; init; } = 24;
        public int CaveMaskBoundaryFadeRowsCavern { get; init; } = 18;
        public int CaveMaskBoundaryFadeRowsDeep { get; init; } = 14;

        public int RefinementPasses { get; init; } = 1;
        public int SpawnSearchRange { get; init; } = 96;

        public float SpaceLayerEndPercent { get; init; } = 0.12f;
        public float SurfaceLayerEndPercent { get; init; } = 0.22f;
        public float ShallowLayerEndPercent { get; init; } = 0.42f;
        public float CavernLayerEndPercent { get; init; } = 0.76f;

        public WorldGenDebugOptions Debug { get; init; } = new WorldGenDebugOptions();
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
                CaveMaskSeedSolidChanceShallow = 0.84f,
                CaveMaskSeedSolidChanceCavern = 0.46f,
                CaveMaskSeedSolidChanceDeep = 0.60f,
                CaveMaskSpatialScaleShallow = 1.0f,
                CaveMaskSpatialScaleCavern = 3.0f,
                CaveMaskSpatialScaleDeepX = 2.6f,
                CaveMaskSpatialScaleDeepY = 4.2f,
                CaveMaskSmoothPassesShallow = 4,
                CaveMaskSmoothPassesCavern = 4,
                CaveMaskSmoothPassesDeep = 4,
                CaveMaskBirthLimitShallow = 4,
                CaveMaskBirthLimitCavern = 4,
                CaveMaskBirthLimitDeep = 4,
                CaveMaskDeathLimitShallow = 3,
                CaveMaskDeathLimitCavern = 3,
                CaveMaskDeathLimitDeep = 4,
                CaveMaskBoundaryFadeRowsShallow = ScaleInt(24, sizeScale, min: 12),
                CaveMaskBoundaryFadeRowsCavern = ScaleInt(18, sizeScale, min: 10),
                CaveMaskBoundaryFadeRowsDeep = ScaleInt(14, sizeScale, min: 8),
                RefinementPasses = 1,
                SpawnSearchRange = ScaleInt(96, sizeScale, min: 48),
                CavernLayerEndPercent = 0.76f
            };
        }

        private static int ScaleInt(int baseValue, float scale, int min = 1)
        {
            return System.Math.Max(min, (int)System.MathF.Round(baseValue * scale));
        }
    }
}
