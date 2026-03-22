using System;
using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation.Passes;

namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenerator
    {
        private readonly WorldGenSettings settings;
        private readonly FastNoiseLite surfaceNoise;
        private readonly FastNoiseLite surfaceDetailNoise;
        private readonly FastNoiseLite surfaceWarpNoise;
        private readonly FastNoiseLite caveNoise;
        private readonly FastNoiseLite caveRoomNoise;
        private readonly FastNoiseLite biomeNoise;
        private readonly FastNoiseLite materialNoise;
        private readonly IWorldGenPass[] generationPasses;

        public sealed class StarterRegion
        {
            public required int PlayerSpawnTileX { get; init; }
            public required int ItemSpawnTileX { get; init; }
            public required int EnemySpawnTileX { get; init; }
            public required int EntranceTileX { get; init; }
            public required int SurfaceTileY { get; init; }
        }

        public WorldGenerator(WorldGenSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            surfaceNoise = new FastNoiseLite(settings.Seed);
            surfaceNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            surfaceNoise.SetFrequency(settings.SurfaceFrequency);
            surfaceNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            surfaceNoise.SetFractalOctaves(4);
            surfaceNoise.SetFractalGain(0.5f);

            surfaceDetailNoise = new FastNoiseLite(settings.Seed + 57);
            surfaceDetailNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            surfaceDetailNoise.SetFrequency(settings.SurfaceDetailFrequency);
            surfaceDetailNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            surfaceDetailNoise.SetFractalOctaves(2);
            surfaceDetailNoise.SetFractalGain(0.45f);

            surfaceWarpNoise = new FastNoiseLite(settings.Seed + 101);
            surfaceWarpNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            surfaceWarpNoise.SetFrequency(settings.SurfaceWarpFrequency);

            caveNoise = new FastNoiseLite(settings.Seed + 202);
            caveNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            caveNoise.SetFrequency(settings.CaveFrequency);
            caveNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            caveNoise.SetFractalOctaves(3);
            caveNoise.SetFractalGain(0.55f);

            caveRoomNoise = new FastNoiseLite(settings.Seed + 233);
            caveRoomNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            caveRoomNoise.SetFrequency(settings.CaveRoomFrequency);
            caveRoomNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            caveRoomNoise.SetFractalOctaves(2);
            caveRoomNoise.SetFractalGain(0.45f);

            biomeNoise = new FastNoiseLite(settings.Seed + 303);
            biomeNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
            biomeNoise.SetFrequency(settings.BiomeFrequency);
            biomeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            biomeNoise.SetFractalOctaves(2);

            materialNoise = new FastNoiseLite(settings.Seed + 404);
            materialNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            materialNoise.SetFrequency(settings.MaterialFrequency);
            materialNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            materialNoise.SetFractalOctaves(2);
            materialNoise.SetFractalGain(0.5f);

            generationPasses = new IWorldGenPass[]
            {
                new ClearWorldPass(),
                new BaseShapePass(),
                new MaterialGradientPass(),
                new CaveNetworkPass(),
                new SubterraneanDetailPass(),
                new NaturalEntrancePass(),
                new SurfacePolishPass(),
                new WorldBoundsPass()
            };
        }

        public void Generate(WorldMap worldMap)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            WorldGenContext context = new WorldGenContext
            {
                WorldMap = worldMap,
                Settings = settings,
                SurfaceNoise = surfaceNoise,
                SurfaceDetailNoise = surfaceDetailNoise,
                SurfaceWarpNoise = surfaceWarpNoise,
                CaveNoise = caveNoise,
                CaveRoomNoise = caveRoomNoise,
                BiomeNoise = biomeNoise,
                MaterialNoise = materialNoise
            };

            for (int i = 0; i < generationPasses.Length; i++)
                generationPasses[i].Apply(context);
        }

        public int GetSurfaceTileY(WorldMap worldMap, int tileX)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            float warpedX = tileX + (surfaceWarpNoise.GetNoise(tileX, 0f) * settings.SurfaceWarpStrength);
            float macroValue = surfaceNoise.GetNoise(warpedX, 0f) * settings.SurfaceAmplitude;
            float detailValue = surfaceDetailNoise.GetNoise(warpedX, 0f) * settings.SurfaceDetailAmplitude;
            int surfaceY = settings.BaseGroundLevel + (int)MathF.Round(macroValue + detailValue);
            return Math.Clamp(surfaceY, 8, worldMap.Height - 10);
        }

        public Vector2 GetSurfaceSpawnPosition(WorldMap worldMap, int tileX, int tilesAboveSurface = 0)
        {
            int clampedX = Math.Clamp(tileX, 0, worldMap.Width - 1);
            int surfaceY = FindSurfaceTileY(worldMap, clampedX);
            float x = worldMap.GetTileCenter(clampedX, surfaceY).X;
            float y = (surfaceY - tilesAboveSurface) * worldMap.TileSize;
            return new Vector2(x, y);
        }

        public void CarveStarterSubterranean(WorldMap worldMap, int entryTileX)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            int startX = Math.Clamp(entryTileX, 6, worldMap.Width - 28);
            int surfaceY = FindSurfaceTileY(worldMap, startX);

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

        public StarterRegion PrepareStarterRegion(WorldMap worldMap, int approximateStartX)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            int centerX = Math.Clamp(approximateStartX, 14, worldMap.Width - 30);
            int targetSurfaceY = FindStarterSurfaceY(worldMap, centerX);

            FlattenStarterPlatform(worldMap, centerX, targetSurfaceY);

            int entranceTileX = centerX + 12;
            ShapeStarterEntranceSlope(worldMap, centerX + 8, entranceTileX, targetSurfaceY);
            CarveStarterSubterranean(worldMap, entranceTileX);

            return new StarterRegion
            {
                PlayerSpawnTileX = centerX,
                ItemSpawnTileX = centerX + 5,
                EnemySpawnTileX = centerX + 16,
                EntranceTileX = entranceTileX,
                SurfaceTileY = targetSurfaceY
            };
        }

        private int ClampSurfaceHeight(int surfaceY, int worldHeight)
        {
            return Math.Clamp(surfaceY, 8, worldHeight - 10);
        }

        private int WrapColumn(int x, int width)
        {
            int wrapped = x % width;
            return wrapped < 0 ? wrapped + width : wrapped;
        }

        private int FindSurfaceTileY(WorldMap worldMap, int tileX)
        {
            for (int y = 0; y < worldMap.Height; y++)
            {
                if (worldMap.IsSolidAt(tileX, y))
                    return y;
            }

            return GetSurfaceTileY(worldMap, tileX);
        }

        private int FindStarterSurfaceY(WorldMap worldMap, int centerX)
        {
            int minY = int.MaxValue;
            int maxY = 0;

            for (int offset = -5; offset <= 5; offset++)
            {
                int surfaceY = FindSurfaceTileY(worldMap, centerX + offset);
                minY = Math.Min(minY, surfaceY);
                maxY = Math.Max(maxY, surfaceY);
            }

            return ClampSurfaceHeight((minY + maxY) / 2, worldMap.Height);
        }

        private void FlattenStarterPlatform(WorldMap worldMap, int centerX, int targetSurfaceY)
        {
            int halfWidth = Math.Max(6, settings.SpawnFlatHalfWidth);
            int rampWidth = Math.Max(2, settings.SpawnRampWidth);

            for (int offset = -(halfWidth + rampWidth); offset <= halfWidth + rampWidth; offset++)
            {
                int x = centerX + offset;
                int surfaceY = targetSurfaceY;

                if (offset < -halfWidth)
                    surfaceY += Math.Min(2, ((-halfWidth - offset) + rampWidth - 1) / rampWidth);
                else if (offset > halfWidth)
                    surfaceY += Math.Min(2, ((offset - halfWidth) + rampWidth - 1) / rampWidth);

                RebuildSurfaceColumn(worldMap, x, surfaceY);
            }
        }

        private void ShapeStarterEntranceSlope(WorldMap worldMap, int fromX, int entranceX, int targetSurfaceY)
        {
            int width = Math.Max(1, entranceX - fromX);

            for (int x = fromX; x <= entranceX; x++)
            {
                float t = (x - fromX) / (float)width;
                int localSurfaceY = targetSurfaceY + (int)MathF.Round(t * 3f);
                RebuildSurfaceColumn(worldMap, x, localSurfaceY);
            }
        }

        private void RebuildSurfaceColumn(WorldMap worldMap, int tileX, int surfaceY)
        {
            int x = WrapColumn(tileX, worldMap.Width);
            int clampedSurfaceY = ClampSurfaceHeight(surfaceY, worldMap.Height);

            for (int y = 0; y < worldMap.Height; y++)
            {
                if (y < clampedSurfaceY)
                {
                    worldMap.SetTile(x, y, TileType.Empty);
                    continue;
                }

                int depth = y - clampedSurfaceY;
                if (depth == 0)
                {
                    worldMap.SetTile(x, y, TileType.Grass);
                    continue;
                }

                if (depth <= settings.SurfaceTopsoilDepth + 1)
                {
                    worldMap.SetTile(x, y, TileType.Dirt);
                    continue;
                }

                worldMap.SetTile(x, y, depth >= settings.StoneTransitionStartDepth + 2 ? TileType.Stone : TileType.Dirt);
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

        private void CarveAirEllipse(WorldMap worldMap, int centerX, int centerY, int radiusX, int radiusY)
        {
            for (int y = centerY - radiusY; y <= centerY + radiusY; y++)
            {
                if (y < 0 || y >= worldMap.Height)
                    continue;

                for (int x = centerX - radiusX; x <= centerX + radiusX; x++)
                {
                    float normalizedX = (x - centerX) / (float)Math.Max(1, radiusX);
                    float normalizedY = (y - centerY) / (float)Math.Max(1, radiusY);
                    if ((normalizedX * normalizedX) + (normalizedY * normalizedY) > 1f)
                        continue;

                    worldMap.SetTile(x, y, TileType.Empty);
                }
            }
        }
    }
}
