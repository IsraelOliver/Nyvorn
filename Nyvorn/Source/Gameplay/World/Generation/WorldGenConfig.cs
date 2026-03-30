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
        public int SpawnSearchRange { get; init; } = 96;

        public float SpaceLayerEndPercent { get; init; } = 0.12f;
        public float SurfaceLayerEndPercent { get; init; } = 0.22f;
        public float ShallowLayerEndPercent { get; init; } = 0.30f;
        public float CavernLayerEndPercent { get; init; } = 0.85f;

        public bool WrapHorizontally { get; init; } = true;

        public WorldGenDebugOptions Debug { get; init; } = new WorldGenDebugOptions();
        public int BorderThickness { get; init; } = 1;

        public static WorldGenConfig CreatePreset(WorldSizePreset sizePreset, int seed = 1337)
        {
            return sizePreset switch
            {
                WorldSizePreset.Small => CreatePreset(sizePreset, seed, 2800, 900),
                WorldSizePreset.Medium => CreatePreset(sizePreset, seed, 4200, 1200),
                _ => CreatePreset(sizePreset, seed, 6000, 1600)
            };
        }

        private static WorldGenConfig CreatePreset(WorldSizePreset sizePreset, int seed, int worldWidth, int worldHeight)
        {
            int spawnApproximateTileX = System.Math.Max(40, (int)System.MathF.Round(worldWidth * 0.065625f));

            int spaceEnd = (int)System.MathF.Round(worldHeight * 0.12f);
            int surfaceEnd = (int)System.MathF.Round(worldHeight * 0.22f);
            int surfaceStartY = spaceEnd + 1;
            int surfaceEndY = System.Math.Max(surfaceStartY, surfaceEnd);
            int surfaceBaseHeight = (surfaceStartY + surfaceEndY) / 2;

            return new WorldGenConfig
            {
                SizePreset = sizePreset,
                Seed = seed,
                WorldWidth = worldWidth,
                WorldHeight = worldHeight,
                TileSize = 8,
                SpawnApproximateTileX = spawnApproximateTileX,
                SurfaceBaseHeight = surfaceBaseHeight,
                SpawnSearchRange = System.Math.Max(48, (int)System.MathF.Round(worldWidth * 0.015f)),
                ShallowLayerEndPercent = 0.30f,
                CavernLayerEndPercent = 0.85f
            };
        }
    }
}
