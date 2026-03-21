using System;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenerator
    {
        private readonly WorldGenSettings settings;
        private readonly FastNoiseLite surfaceNoise;
        private readonly FastNoiseLite surfaceWarpNoise;
        private readonly FastNoiseLite caveNoise;
        private readonly FastNoiseLite biomeNoise;

        public WorldGenerator(WorldGenSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            surfaceNoise = new FastNoiseLite(settings.Seed);
            surfaceNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            surfaceNoise.SetFrequency(settings.SurfaceFrequency);
            surfaceNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            surfaceNoise.SetFractalOctaves(4);
            surfaceNoise.SetFractalGain(0.5f);

            surfaceWarpNoise = new FastNoiseLite(settings.Seed + 101);
            surfaceWarpNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            surfaceWarpNoise.SetFrequency(settings.SurfaceWarpFrequency);

            caveNoise = new FastNoiseLite(settings.Seed + 202);
            caveNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            caveNoise.SetFrequency(settings.CaveFrequency);
            caveNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            caveNoise.SetFractalOctaves(3);
            caveNoise.SetFractalGain(0.55f);

            biomeNoise = new FastNoiseLite(settings.Seed + 303);
            biomeNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
            biomeNoise.SetFrequency(settings.BiomeFrequency);
            biomeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            biomeNoise.SetFractalOctaves(2);
        }

        public void Generate(WorldMap worldMap)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            Clear(worldMap);

            for (int x = 0; x < worldMap.Width; x++)
            {
                int surfaceY = GetSurfaceTileY(worldMap, x);
                bool useSand = biomeNoise.GetNoise(x, 0f) > settings.SandBiomeThreshold;

                for (int y = surfaceY; y < worldMap.Height; y++)
                {
                    TileType tileType = ResolveBaseTileType(surfaceY, y, useSand);
                    worldMap.SetTile(x, y, tileType);
                }
            }

            CarveCaves(worldMap);
            ApplyBorders(worldMap);
        }

        public int GetSurfaceTileY(WorldMap worldMap, int tileX)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            float warpedX = tileX + (surfaceWarpNoise.GetNoise(tileX, 0f) * settings.SurfaceWarpStrength);
            float surfaceValue = surfaceNoise.GetNoise(warpedX, 0f);
            int surfaceY = settings.BaseGroundLevel + (int)MathF.Round(surfaceValue * settings.SurfaceAmplitude);
            return Math.Clamp(surfaceY, 8, worldMap.Height - 10);
        }

        public Vector2 GetSurfaceSpawnPosition(WorldMap worldMap, int tileX, int tilesAboveSurface = 0)
        {
            int clampedX = Math.Clamp(tileX, settings.BorderThickness, worldMap.Width - settings.BorderThickness - 1);
            int surfaceY = GetSurfaceTileY(worldMap, clampedX);
            float x = worldMap.GetTileCenter(clampedX, surfaceY).X;
            float y = (surfaceY - tilesAboveSurface) * worldMap.TileSize;
            return new Vector2(x, y);
        }

        public void CarveStarterSubterranean(WorldMap worldMap, int entryTileX)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            int startX = Math.Clamp(entryTileX, settings.BorderThickness + 6, worldMap.Width - settings.BorderThickness - 28);
            int surfaceY = GetSurfaceTileY(worldMap, startX);

            CarveAirRect(worldMap, startX - 2, surfaceY - 6, 5, 6);

            const int tunnelSteps = 18;
            for (int step = 0; step < tunnelSteps; step++)
            {
                int centerX = startX + 2 + (step * 2);
                int floorY = surfaceY + 2 + step;
                CarveAirRect(worldMap, centerX - 1, floorY - 4, 4, 4);
            }

            int chamberCenterX = startX + 2 + (tunnelSteps * 2) + 4;
            int chamberTopY = surfaceY + 10;
            CarveAirRect(worldMap, chamberCenterX - 9, chamberTopY, 18, 12);

            for (int step = 0; step < 5; step++)
            {
                int sideX = chamberCenterX + 8 + (step * 2);
                int sideFloorY = chamberTopY + 8 + step;
                CarveAirRect(worldMap, sideX - 1, sideFloorY - 4, 4, 4);
            }
        }

        private void Clear(WorldMap worldMap)
        {
            for (int y = 0; y < worldMap.Height; y++)
            {
                for (int x = 0; x < worldMap.Width; x++)
                    worldMap.SetTile(x, y, TileType.Empty);
            }
        }

        private TileType ResolveBaseTileType(int surfaceY, int y, bool useSand)
        {
            if (y == surfaceY)
                return useSand ? TileType.Sand : TileType.Dirt;

            if (y <= surfaceY + 2)
                return useSand ? TileType.Sand : TileType.Dirt;

            return y >= surfaceY + settings.StoneDepth
                ? TileType.Stone
                : TileType.Dirt;
        }

        private void CarveCaves(WorldMap worldMap)
        {
            for (int x = settings.BorderThickness; x < worldMap.Width - settings.BorderThickness; x++)
            {
                int surfaceY = GetSurfaceTileY(worldMap, x);
                int caveStartY = Math.Min(worldMap.Height - 1, surfaceY + settings.CaveStartDepth);

                for (int y = caveStartY; y < worldMap.Height - 1; y++)
                {
                    float caveValue = MathF.Abs(caveNoise.GetNoise(x, y));
                    if (caveValue > settings.CaveThreshold)
                        worldMap.SetTile(x, y, TileType.Empty);
                }
            }
        }

        private void ApplyBorders(WorldMap worldMap)
        {
            for (int x = 0; x < worldMap.Width; x++)
                worldMap.SetTile(x, worldMap.Height - 1, TileType.Stone);

            for (int y = 0; y < worldMap.Height; y++)
            {
                for (int offset = 0; offset < settings.BorderThickness; offset++)
                {
                    worldMap.SetTile(offset, y, TileType.Stone);
                    worldMap.SetTile(worldMap.Width - 1 - offset, y, TileType.Stone);
                }
            }
        }

        private void CarveAirRect(WorldMap worldMap, int left, int top, int width, int height)
        {
            int right = Math.Min(worldMap.Width, left + width);
            int bottom = Math.Min(worldMap.Height, top + height);

            for (int y = Math.Max(0, top); y < bottom; y++)
            {
                for (int x = Math.Max(0, left); x < right; x++)
                    worldMap.SetTile(x, y, TileType.Empty);
            }
        }
    }
}
