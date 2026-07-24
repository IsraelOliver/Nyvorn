using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Decorations
{
    // Scatters small 1-tile decorations (mushrooms for now) across the world. Mushrooms only
    // appear in thematic spots: shaded grass near tree trunks, and cave floors beside the
    // underground pools the Hydrology pass created.
    public sealed class SurfaceDecorationGenerator
    {
        private const int MinTreeDistanceTiles = 2;
        private const int MaxTreeDistanceTiles = 7;
        private const int TreeMushroomChancePercent = 30;
        private const int TreeSecondMushroomChancePercent = 10;
        private const int PlacementAttemptsPerMushroom = 6;

        private const float WaterMushroomChancePerCell = 0.008f;
        private const int WaterSearchRadiusX = 5;
        private const int WaterSearchRadiusY = 4;
        private const int MinWaterMushrooms = 30;
        private const int MaxWaterMushrooms = 300;
        private const int WaterMushroomAreaDivisor = 15000;

        public List<SurfaceDecorationInstance> Generate(WorldGenContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            List<SurfaceDecorationInstance> decorations = new();
            HashSet<long> occupiedTiles = new();
            Random random = context.CreateRandom(SeedHash.Derive(context.Seeds.DecorationSeed, "surface-decorations"));

            GenerateNearTrees(context, random, decorations, occupiedTiles);
            GenerateNearWater(context, random, decorations, occupiedTiles);

            return decorations;
        }

        private static void GenerateNearTrees(
            WorldGenContext context,
            Random random,
            List<SurfaceDecorationInstance> decorations,
            HashSet<long> occupiedTiles)
        {
            WorldMap worldMap = context.WorldMap;
            if (worldMap.Trees.Count == 0)
                return;

            // Trunks and their roots reserve the columns right at the tree base so mushrooms never
            // render behind a trunk sprite.
            HashSet<long> treeBaseTiles = new();
            for (int i = 0; i < worldMap.Trees.Count; i++)
            {
                Point baseTile = worldMap.Trees[i].BaseTile;
                for (int dx = -1; dx <= 1; dx++)
                    treeBaseTiles.Add(CreateKey(worldMap.WrapTileX(baseTile.X + dx), baseTile.Y));
            }

            for (int i = 0; i < worldMap.Trees.Count; i++)
            {
                if (random.Next(0, 100) >= TreeMushroomChancePercent)
                    continue;

                int mushroomCount = random.Next(0, 100) < TreeSecondMushroomChancePercent ? 2 : 1;
                Point treeBase = worldMap.Trees[i].BaseTile;

                for (int c = 0; c < mushroomCount; c++)
                {
                    for (int attempt = 0; attempt < PlacementAttemptsPerMushroom; attempt++)
                    {
                        int distance = random.Next(MinTreeDistanceTiles, MaxTreeDistanceTiles + 1);
                        int direction = random.Next(0, 2) == 0 ? -1 : 1;
                        int x = worldMap.WrapTileX(treeBase.X + direction * distance);

                        if (TryPlaceOnSurfaceGrass(context, x, random, decorations, occupiedTiles, treeBaseTiles))
                            break;
                    }
                }
            }
        }

        private static bool TryPlaceOnSurfaceGrass(
            WorldGenContext context,
            int x,
            Random random,
            List<SurfaceDecorationInstance> decorations,
            HashSet<long> occupiedTiles,
            HashSet<long> treeBaseTiles)
        {
            WorldMap worldMap = context.WorldMap;
            int[] surfaceHeights = context.SurfaceHeights;
            if (surfaceHeights == null || x < 0 || x >= surfaceHeights.Length)
                return false;

            int groundY = surfaceHeights[x];
            if (worldMap.GetTile(x, groundY) != TileType.Grass || worldMap.IsSolidAt(x, groundY - 1))
                return false;

            int tileY = groundY - 1;
            long key = CreateKey(x, tileY);
            if (treeBaseTiles.Contains(key) || !occupiedTiles.Add(key))
                return false;

            decorations.Add(new SurfaceDecorationInstance
            {
                Tile = new Point(x, tileY),
                Type = SurfaceDecorationType.Mushroom,
                Seed = random.Next()
            });
            return true;
        }

        private static void GenerateNearWater(
            WorldGenContext context,
            Random random,
            List<SurfaceDecorationInstance> decorations,
            HashSet<long> occupiedTiles)
        {
            WorldMap worldMap = context.WorldMap;
            IReadOnlyList<WorldGenLiquidPlacement> liquidPlacements = context.LiquidPlacements;
            if (liquidPlacements.Count == 0)
                return;

            HashSet<long> waterKeys = new();
            for (int i = 0; i < liquidPlacements.Count; i++)
                waterKeys.Add(CreateKey(liquidPlacements[i].X, liquidPlacements[i].Y));

            int area = Math.Max(1, worldMap.Width * worldMap.Height);
            int budget = Math.Clamp(area / WaterMushroomAreaDivisor, MinWaterMushrooms, MaxWaterMushrooms);
            int placed = 0;

            for (int i = 0; i < liquidPlacements.Count && placed < budget; i++)
            {
                if (random.NextSingle() > WaterMushroomChancePerCell)
                    continue;

                WorldGenLiquidPlacement placement = liquidPlacements[i];
                for (int attempt = 0; attempt < PlacementAttemptsPerMushroom; attempt++)
                {
                    int x = worldMap.WrapTileX(placement.X + random.Next(-WaterSearchRadiusX, WaterSearchRadiusX + 1));
                    int y = placement.Y + random.Next(-WaterSearchRadiusY, WaterSearchRadiusY + 1);

                    if (!TryPlaceOnCaveFloor(worldMap, x, y, waterKeys, random, decorations, occupiedTiles))
                        continue;

                    placed++;
                    break;
                }
            }
        }

        private static bool TryPlaceOnCaveFloor(
            WorldMap worldMap,
            int x,
            int y,
            HashSet<long> waterKeys,
            Random random,
            List<SurfaceDecorationInstance> decorations,
            HashSet<long> occupiedTiles)
        {
            if (!worldMap.InBounds(x, y) || !worldMap.InBounds(x, y + 1))
                return false;

            // Needs a dry air cell sitting on a natural floor - not submerged in the pool itself.
            if (worldMap.IsSolidAt(x, y) || waterKeys.Contains(CreateKey(x, y)))
                return false;

            TileType floor = worldMap.GetTile(x, y + 1);
            if (floor != TileType.Dirt && floor != TileType.Stone && floor != TileType.Grass)
                return false;

            long key = CreateKey(x, y);
            if (!occupiedTiles.Add(key))
                return false;

            decorations.Add(new SurfaceDecorationInstance
            {
                Tile = new Point(x, y),
                Type = SurfaceDecorationType.Mushroom,
                Seed = random.Next()
            });
            return true;
        }

        private static long CreateKey(int x, int y)
        {
            return ((long)y << 32) | (uint)x;
        }
    }
}
