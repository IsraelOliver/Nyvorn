using Nyvorn.Source.World;

namespace Nyvorn.Source.World.Generation.Biomes
{
    public readonly record struct BiomeDefinition(
        BiomeType Type,
        float TerrainAmplitudeScale,
        float TerrainDetailScale,
        float TerrainSmoothness,
        TileType SurfaceTile,
        TileType SubsurfaceTile,
        int SubsurfaceDepth,
        float ShallowStoneThresholdOffset,
        float DeepDirtThresholdOffset,
        float CaveThresholdOffset,
        float TreeSpawnMultiplier)
    {
        public static readonly BiomeDefinition Forest = new(
            BiomeType.Forest,
            1f,
            1f,
            0f,
            TileType.Grass,
            TileType.Dirt,
            0,
            0f,
            0f,
            0f,
            1f);

        public static readonly BiomeDefinition Desert = new(
            BiomeType.Desert,
            0.55f,
            0.35f,
            0.65f,
            TileType.HardenedSand,
            TileType.HardenedSand,
            48,
            0.12f,
            -0.05f,
            -0.03f,
            0.05f);

        public static BiomeDefinition Get(BiomeType type)
        {
            return type switch
            {
                BiomeType.Desert => Desert,
                _ => Forest
            };
        }
    }
}
