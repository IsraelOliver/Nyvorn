using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation.Passes;
using System;

namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenerator
    {
        private readonly IWorldGenPass[] generationPasses;
        private WorldGenConfig lastConfig;

        public WorldGenerator()
        {
            generationPasses = new IWorldGenPass[]
            {
                new ClearWorldPass(),
                new LayerBoundaryPass(),
                new SurfaceProfilePass(),
                new BaseTerrainFillPass(),
                new SandRegionPass(),
                new CaveMaskPass(),
                new SurfaceDecorationPass(),
                new WorldBoundsPass()
            };
        }

        public void Generate(WorldMap worldMap, WorldGenConfig config)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            lastConfig = config;

            WorldGenContext context = new WorldGenContext
            {
                WorldMap = worldMap,
                Config = config,
                Random = new Random(config.Seed),
                SurfaceNoise = CreateSurfaceNoise(config),
                SurfaceDetailNoise = CreateSurfaceDetailNoise(config),
                SurfaceWarpNoise = CreateSurfaceWarpNoise(config),
                CaveNoise = CreateCaveNoise(config),
                CaveRoomNoise = CreateCaveRoomNoise(config),
                MaterialNoise = CreateMaterialNoise(config)
            };

            for (int i = 0; i < generationPasses.Length; i++)
            {
                if (!config.Debug.IsEnabled(generationPasses[i].Name))
                    continue;

                generationPasses[i].Apply(context);
            }
        }

        public int GetSurfaceTileY(WorldMap worldMap, int tileX)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            int clampedX = Math.Clamp(tileX, 0, worldMap.Width - 1);
            for (int y = 0; y < worldMap.Height; y++)
            {
                if (worldMap.IsSolidAt(clampedX, y))
                    return y;
            }

            return lastConfig?.SurfaceBaseHeight ?? Math.Max(0, worldMap.Height / 3);
        }

        public Vector2 GetSurfaceSpawnPosition(WorldMap worldMap, int tileX, int tilesAboveSurface = 0)
        {
            int clampedX = Math.Clamp(tileX, 0, worldMap.Width - 1);
            int surfaceY = GetSurfaceTileY(worldMap, clampedX);
            float x = worldMap.GetTileCenter(clampedX, surfaceY).X;
            float y = (surfaceY - tilesAboveSurface) * worldMap.TileSize;
            return new Vector2(x, y);
        }

        private FastNoiseLite CreateSurfaceNoise(WorldGenConfig config)
        {
            FastNoiseLite noise = new FastNoiseLite(config.Seed);
            noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            noise.SetFrequency(config.SurfaceFrequency);
            noise.SetFractalType(FastNoiseLite.FractalType.FBm);
            noise.SetFractalOctaves(4);
            noise.SetFractalGain(0.5f);
            return noise;
        }

        private FastNoiseLite CreateSurfaceDetailNoise(WorldGenConfig config)
        {
            FastNoiseLite noise = new FastNoiseLite(config.Seed + 57);
            noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            noise.SetFrequency(config.SurfaceDetailFrequency);
            noise.SetFractalType(FastNoiseLite.FractalType.FBm);
            noise.SetFractalOctaves(2);
            noise.SetFractalGain(0.45f);
            return noise;
        }

        private FastNoiseLite CreateSurfaceWarpNoise(WorldGenConfig config)
        {
            FastNoiseLite noise = new FastNoiseLite(config.Seed + 101);
            noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            noise.SetFrequency(config.SurfaceWarpFrequency);
            return noise;
        }

        private FastNoiseLite CreateCaveNoise(WorldGenConfig config)
        {
            FastNoiseLite noise = new FastNoiseLite(config.Seed + 202);
            noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            noise.SetFrequency(config.CaveFrequency);
            return noise;
        }

        private FastNoiseLite CreateCaveRoomNoise(WorldGenConfig config)
        {
            FastNoiseLite noise = new FastNoiseLite(config.Seed + 233);
            noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            noise.SetFrequency(config.CaveRoomFrequency);
            return noise;
        }

        private FastNoiseLite CreateMaterialNoise(WorldGenConfig config)
        {
            FastNoiseLite noise = new FastNoiseLite(config.Seed + 404);
            noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            noise.SetFrequency(config.MaterialFrequency);
            return noise;
        }
    }
}
