using Nyvorn.Source.Engine.Physics.Liquids;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class HydrologyPass : IWorldGenPass
    {
        private const int MinCaveRegionCells = 45;
        private const int MaxWholeCavePoolCells = 1600;
        private const int MaxCatalogStoredCells = 5000;
        private const int MaxCatalogFloorSamples = 900;
        private const int MinCaveFloorCells = 6;
        private const int MinCavePoolWaterCells = 12;
        private const int MaxBottomPoolCells = 320;
        private const int MaxPartialPoolCells = 900;
        private const int MaxFullCaveCells = 520;
        private const int MaxBigLakeCells = 3000;
        private const float BigLakeChance = 0.35f;

        public string Name => "Hydrology";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Distribuindo agua inicial");
            context.LiquidPlacements.Clear();

            HashSet<long> liquidKeys = new();
            List<WaterSite> createdSites = new();
            int undergroundWaterCells = 0;
            UndergroundWaterStats undergroundStats = new();

            if (context.Config.GenerateUndergroundWater)
            {
                undergroundWaterCells = GenerateUndergroundWater(
                    context,
                    liquidKeys,
                    createdSites,
                    undergroundStats);
                context.ProgressReporter?.Report(Name, 0.90f, "Agua subterranea posicionada");
            }

            context.DebugStats["Hydrology.UndergroundPuddles"] = undergroundStats.TotalPools.ToString();
            context.DebugStats["Hydrology.UndergroundCavePools"] = undergroundStats.TotalPools.ToString();
            context.DebugStats["Hydrology.UndergroundBottomPools"] = undergroundStats.BottomPools.ToString();
            context.DebugStats["Hydrology.UndergroundPartialCaves"] = undergroundStats.PartialCaves.ToString();
            context.DebugStats["Hydrology.UndergroundFullCaves"] = undergroundStats.FullCaves.ToString();
            context.DebugStats["Hydrology.UndergroundBigLakes"] = undergroundStats.BigLakes.ToString();
            context.DebugStats["Hydrology.UndergroundAquifers"] = "0";
            context.DebugStats["Hydrology.UndergroundWaterCells"] = undergroundWaterCells.ToString();
            context.DebugStats["Hydrology.TotalWaterCells"] = context.LiquidPlacements.Count.ToString();
            context.ProgressReporter?.Complete(Name, "Agua inicial distribuida");
        }

        private static int GenerateUndergroundWater(
            WorldGenContext context,
            HashSet<long> liquidKeys,
            List<WaterSite> createdSites,
            UndergroundWaterStats stats)
        {
            List<CaveRegion> regions = BuildCaveRegionCatalog(context);
            if (regions.Count == 0)
                return 0;

            Random random = context.CreateRandom(context.Seeds.HydrologySeed);
            Shuffle(regions, random);
            int area = Math.Max(1, context.WorldMap.Width * context.WorldMap.Height);
            int density = Math.Max(1, context.Config.UndergroundWaterDensityArea);
            int targetFeatureCount = Math.Clamp(area / density, 28, 700);
            int targetFullCaves = Math.Clamp(targetFeatureCount / 8, 2, 90);
            int waterCellBudget = Math.Clamp(area / 150, 7000, 90000);
            int waterCells = 0;

            for (int i = 0; i < regions.Count && stats.TotalPools < targetFeatureCount && waterCells < waterCellBudget; i++)
            {
                CaveRegion region = regions[i];
                bool preferFull = stats.FullCaves < targetFullCaves;
                if (!TryCreateCavePoolPlan(context, random, region, preferFull, out CavePoolPlan plan))
                    continue;

                if (!CanPlaceUndergroundSite(context, createdSites, plan.CenterX, plan.CenterY, plan.Radius))
                    continue;

                int remainingBudget = waterCellBudget - waterCells;
                if (plan.WaterCellCount > remainingBudget)
                    continue;

                int added = ApplyCavePoolPlan(context, liquidKeys, plan);
                if (added < MinCavePoolWaterCells)
                    continue;

                createdSites.Add(new WaterSite(plan.CenterX, plan.CenterY, plan.Radius + 8));
                waterCells += added;
                stats.Record(plan.Mode);
            }

            return waterCells;
        }

        private static List<CaveRegion> BuildCaveRegionCatalog(WorldGenContext context)
        {
            List<CaveRegion> regions = new();
            WorldLayerDefinition cavernLayer = context.GetLayerDefinition(WorldLayerType.Cavern);
            WorldLayerDefinition deepLayer = context.GetLayerDefinition(WorldLayerType.DeepCavern);
            int minY = Math.Max(cavernLayer.StartY + 2, 2);
            int maxY = Math.Min(deepLayer.EndY - context.Config.BorderThickness - 2, context.WorldMap.Height - 3);
            int height = maxY - minY + 1;
            if (height <= 0)
                return regions;

            bool[,] visited = new bool[context.WorldMap.Width, height];
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = 0; x < context.WorldMap.Width; x++)
                {
                    if (visited[x, y - minY] ||
                        context.WorldMap.IsSolidAt(x, y))
                    {
                        continue;
                    }

                    CaveRegion region = CollectCaveRegion(context, x, y, minY, maxY, visited);
                    if (region.Cells.Count < MinCaveRegionCells ||
                        region.FloorCells.Count < MinCaveFloorCells)
                    {
                        continue;
                    }

                    regions.Add(region);
                }
            }

            return regions;
        }

        private static CaveRegion CollectCaveRegion(
            WorldGenContext context,
            int startX,
            int startY,
            int minY,
            int maxY,
            bool[,] visited)
        {
            List<CaveCell> cells = new(128);
            List<CaveCell> floorCells = new(32);
            Queue<CaveCell> queue = new();
            queue.Enqueue(new CaveCell(startX, startY));
            visited[startX, startY - minY] = true;

            int minX = startX;
            int maxX = startX;
            int regionMinY = startY;
            int regionMaxY = startY;
            int processedCells = 0;
            bool touchesScanTop = false;
            bool oversized = false;

            while (queue.Count > 0)
            {
                CaveCell cell = queue.Dequeue();
                processedCells++;
                if (cells.Count < MaxCatalogStoredCells)
                    cells.Add(cell);
                else
                    oversized = true;

                minX = Math.Min(minX, cell.X);
                maxX = Math.Max(maxX, cell.X);
                regionMinY = Math.Min(regionMinY, cell.Y);
                regionMaxY = Math.Max(regionMaxY, cell.Y);
                touchesScanTop |= cell.Y <= minY + 1;

                if (context.WorldMap.IsSolidAt(cell.X, cell.Y + 1) && floorCells.Count < MaxCatalogFloorSamples)
                    floorCells.Add(cell);

                EnqueueCatalogNeighbor(context, visited, queue, cell.X - 1, cell.Y, minY, maxY);
                EnqueueCatalogNeighbor(context, visited, queue, cell.X + 1, cell.Y, minY, maxY);
                EnqueueCatalogNeighbor(context, visited, queue, cell.X, cell.Y - 1, minY, maxY);
                EnqueueCatalogNeighbor(context, visited, queue, cell.X, cell.Y + 1, minY, maxY);
            }

            return new CaveRegion(cells, floorCells, new List<CaveCell>(), minX, maxX, regionMinY, regionMaxY, touchesScanTop, oversized, processedCells);
        }

        private static void EnqueueCatalogNeighbor(
            WorldGenContext context,
            bool[,] visited,
            Queue<CaveCell> queue,
            int x,
            int y,
            int minY,
            int maxY)
        {
            if (x < 0 || x >= context.WorldMap.Width || y < minY || y > maxY)
                return;

            if (visited[x, y - minY] ||
                context.WorldMap.IsSolidAt(x, y))
            {
                return;
            }

            visited[x, y - minY] = true;
            queue.Enqueue(new CaveCell(x, y));
        }

        private static bool TryCreateCavePoolPlan(
            WorldGenContext context,
            Random random,
            CaveRegion sourceRegion,
            bool preferFull,
            out CavePoolPlan plan)
        {
            plan = null;
            CaveRegion region = sourceRegion;
            bool isBigCandidate = sourceRegion.IsLarge || sourceRegion.ProcessedCellCount > MaxWholeCavePoolCells;
            bool useBigLake = isBigCandidate && random.NextDouble() < BigLakeChance;

            if (isBigCandidate && !useBigLake)
            {
                if (!TryCreateLocalCaveBay(context, random, sourceRegion, out region))
                    return false;
            }

            if (region.Cells.Count < MinCaveRegionCells || region.FloorCells.Count < MinCaveFloorCells)
                return false;

            int width = region.MaxX - region.MinX + 1;
            int height = region.MaxY - region.MinY + 1;
            if (width < 8 || height < 4)
                return false;

            // Big, wide caverns keep their full shape here (instead of being shrunk to a small local
            // bay) and get flooded roughly halfway up, so some of the large deep caves end up as
            // sizeable lakes instead of every cavern only getting a small puddle.
            CaveFillMode mode = useBigLake ? CaveFillMode.BigLake : PickCaveFillMode(random, region, preferFull);
            int fillY = CalculateCaveFillY(random, region, mode);
            int maxCells = mode switch
            {
                CaveFillMode.Full => MaxFullCaveCells,
                CaveFillMode.Partial => MaxPartialPoolCells,
                CaveFillMode.BigLake => MaxBigLakeCells,
                _ => MaxBottomPoolCells
            };

            int waterCells = CountCaveWaterCells(region.Cells, fillY);
            if (mode == CaveFillMode.Full && waterCells > maxCells)
                return false;

            while ((waterCells > maxCells || HasOpenBoundaryBelowFill(region, fillY)) && fillY < region.MaxY)
            {
                fillY++;
                waterCells = CountCaveWaterCells(region.Cells, fillY);
            }

            if (waterCells < MinCavePoolWaterCells || HasOpenBoundaryBelowFill(region, fillY))
                return false;

            int radius = Math.Max(width, height) / 2 + 8;
            int centerX = (region.MinX + region.MaxX) / 2;
            int centerY = (region.MinY + region.MaxY) / 2;
            plan = new CavePoolPlan(centerX, centerY, radius, fillY, waterCells, region.Cells, mode);
            return true;
        }

        private static bool TryCreateLocalCaveBay(WorldGenContext context, Random random, CaveRegion sourceRegion, out CaveRegion bay)
        {
            bay = null;
            if (sourceRegion.FloorCells.Count == 0)
                return false;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                CaveCell anchor = sourceRegion.FloorCells[random.Next(0, sourceRegion.FloorCells.Count)];
                int halfWidth = random.Next(18, 46);
                int halfHeight = random.Next(10, 30);
                if (TryCollectLocalCaveRegion(context, anchor.X, anchor.Y, halfWidth, halfHeight, out CaveRegion localBay) &&
                    localBay.Cells.Count >= MinCaveRegionCells &&
                    localBay.FloorCells.Count >= MinCaveFloorCells)
                {
                    bay = localBay;
                    return true;
                }
            }

            return false;
        }

        private static bool TryCollectLocalCaveRegion(
            WorldGenContext context,
            int centerX,
            int startY,
            int halfWidth,
            int halfHeight,
            out CaveRegion region)
        {
            region = null;
            if (context.WorldMap.IsSolidAt(centerX, startY))
            {
                return false;
            }

            WorldLayerDefinition cavernLayer = context.GetLayerDefinition(WorldLayerType.Cavern);
            WorldLayerDefinition deepLayer = context.GetLayerDefinition(WorldLayerType.DeepCavern);
            int minY = Math.Max(cavernLayer.StartY + 2, Math.Max(2, startY - halfHeight));
            int maxY = Math.Min(deepLayer.EndY - context.Config.BorderThickness - 2, Math.Min(context.WorldMap.Height - 3, startY + halfHeight));
            int minX = Math.Max(0, centerX - halfWidth);
            int maxX = Math.Min(context.WorldMap.Width - 1, centerX + halfWidth);
            if (minX > maxX || minY > maxY)
                return false;

            bool[,] visited = new bool[maxX - minX + 1, maxY - minY + 1];
            List<CaveCell> cells = new(128);
            List<CaveCell> floorCells = new(32);
            List<CaveCell> openBoundaryCells = new(32);
            Queue<CaveCell> queue = new();
            queue.Enqueue(new CaveCell(centerX, startY));
            visited[centerX - minX, startY - minY] = true;

            int regionMinX = centerX;
            int regionMaxX = centerX;
            int regionMinY = startY;
            int regionMaxY = startY;
            bool touchesTop = false;

            while (queue.Count > 0)
            {
                CaveCell cell = queue.Dequeue();
                cells.Add(cell);
                if (cells.Count > MaxWholeCavePoolCells)
                    return false;

                regionMinX = Math.Min(regionMinX, cell.X);
                regionMaxX = Math.Max(regionMaxX, cell.X);
                regionMinY = Math.Min(regionMinY, cell.Y);
                regionMaxY = Math.Max(regionMaxY, cell.Y);
                touchesTop |= cell.Y <= minY + 1;
                if (cell.X == minX || cell.X == maxX || cell.Y == minY || cell.Y == maxY)
                    openBoundaryCells.Add(cell);

                if (context.WorldMap.IsSolidAt(cell.X, cell.Y + 1))
                    floorCells.Add(cell);

                EnqueueLocalCaveNeighbor(context, minX, maxX, minY, maxY, visited, queue, cell.X - 1, cell.Y);
                EnqueueLocalCaveNeighbor(context, minX, maxX, minY, maxY, visited, queue, cell.X + 1, cell.Y);
                EnqueueLocalCaveNeighbor(context, minX, maxX, minY, maxY, visited, queue, cell.X, cell.Y - 1);
                EnqueueLocalCaveNeighbor(context, minX, maxX, minY, maxY, visited, queue, cell.X, cell.Y + 1);
            }

            region = new CaveRegion(cells, floorCells, openBoundaryCells, regionMinX, regionMaxX, regionMinY, regionMaxY, touchesTop, isLarge: false, cells.Count);
            return true;
        }

        private static void EnqueueLocalCaveNeighbor(
            WorldGenContext context,
            int minX,
            int maxX,
            int minY,
            int maxY,
            bool[,] visited,
            Queue<CaveCell> queue,
            int x,
            int y)
        {
            if (x < minX || x > maxX || y < minY || y > maxY)
                return;

            int localX = x - minX;
            int localY = y - minY;
            if (visited[localX, localY] ||
                context.WorldMap.IsSolidAt(x, y))
            {
                return;
            }

            visited[localX, localY] = true;
            queue.Enqueue(new CaveCell(x, y));
        }

        private static CaveFillMode PickCaveFillMode(Random random, CaveRegion region, bool preferFull)
        {
            int width = region.MaxX - region.MinX + 1;
            int height = region.MaxY - region.MinY + 1;
            bool canFillCompletely = !region.TouchesScanTop &&
                !region.HasOpenBoundaries &&
                region.Cells.Count <= MaxFullCaveCells &&
                width <= 54 &&
                height <= 24;

            if (canFillCompletely && preferFull && random.Next(0, 100) < 70)
                return CaveFillMode.Full;

            int roll = random.Next(0, 100);
            if (canFillCompletely && roll < 18)
                return CaveFillMode.Full;
            if (roll < 72)
                return CaveFillMode.Partial;

            return CaveFillMode.Bottom;
        }

        private static int CalculateCaveFillY(Random random, CaveRegion region, CaveFillMode mode)
        {
            int height = Math.Max(1, region.MaxY - region.MinY + 1);
            return mode switch
            {
                CaveFillMode.Full => region.MinY,
                CaveFillMode.BigLake => region.MaxY - Math.Max(2, (int)MathF.Round(height * (0.45f + (float)random.NextDouble() * 0.15f))) + 1,
                CaveFillMode.Partial => region.MaxY - Math.Max(2, (int)MathF.Round(height * (0.40f + (float)random.NextDouble() * 0.30f))) + 1,
                _ => region.MaxY - random.Next(2, Math.Min(7, height) + 1) + 1
            };
        }

        private static int CountCaveWaterCells(IReadOnlyList<CaveCell> cells, int fillY)
        {
            int count = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].Y >= fillY)
                    count++;
            }

            return count;
        }

        private static bool HasOpenBoundaryBelowFill(CaveRegion region, int fillY)
        {
            for (int i = 0; i < region.OpenBoundaryCells.Count; i++)
            {
                if (region.OpenBoundaryCells[i].Y >= fillY)
                    return true;
            }

            return false;
        }

        private static int ApplyCavePoolPlan(WorldGenContext context, HashSet<long> liquidKeys, CavePoolPlan plan)
        {
            int addedCells = 0;
            for (int i = 0; i < plan.Cells.Count; i++)
            {
                CaveCell cell = plan.Cells[i];
                if (cell.Y < plan.FillY)
                    continue;

                if (AddLiquidPlacement(context, liquidKeys, cell.X, cell.Y, LiquidRules.Default.MaxLiquidAmount))
                    addedCells++;
            }

            return addedCells;
        }

        private static bool CanPlaceUndergroundSite(WorldGenContext context, IReadOnlyList<WaterSite> createdSites, int x, int y, int radius)
        {
            if (IsProtectedSpawnArea(context, x, y))
                return false;

            for (int i = 0; i < createdSites.Count; i++)
            {
                WaterSite site = createdSites[i];
                int distanceX = WrappedDistanceX(context.WorldMap.Width, x, site.X);
                int distanceY = Math.Abs(y - site.Y);
                if (distanceX + distanceY < radius + site.Radius)
                    return false;
            }

            return true;
        }

        private static bool AddLiquidPlacement(WorldGenContext context, HashSet<long> liquidKeys, int x, int y, int amount)
        {
            int wrappedX = context.WorldMap.WrapTileX(x);
            if (!context.WorldMap.InBounds(wrappedX, y) || context.WorldMap.IsSolidAt(wrappedX, y))
                return false;

            long key = CreateKey(wrappedX, y);
            if (!liquidKeys.Add(key))
                return false;

            context.LiquidPlacements.Add(new WorldGenLiquidPlacement(wrappedX, y, LiquidType.Water, amount));
            return true;
        }

        private static bool IsProtectedSpawnArea(WorldGenContext context, int x, int y)
        {
            int radius = Math.Max(0, context.Config.HydrologySpawnProtectionRadiusTiles);
            if (radius <= 0)
                return false;

            int distanceX = WrappedDistanceX(context.WorldMap.Width, context.Config.SpawnApproximateTileX, x);
            int surfaceY = context.SurfaceHeights != null && context.SurfaceHeights.Length > 0
                ? context.SurfaceHeights[context.WorldMap.WrapTileX(context.Config.SpawnApproximateTileX)]
                : context.Config.SurfaceBaseHeight;
            int distanceY = Math.Abs(y - surfaceY);
            return distanceX < radius && distanceY < radius;
        }

        private static void Shuffle<T>(IList<T> items, Random random)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = random.Next(0, i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        private static int WrappedDistanceX(int width, int a, int b)
        {
            if (width <= 0)
                return Math.Abs(a - b);

            int delta = Math.Abs((a % width) - (b % width));
            return Math.Min(delta, width - delta);
        }

        private static long CreateKey(int x, int y)
        {
            return ((long)y << 32) | (uint)x;
        }

        private sealed class CaveRegion
        {
            public CaveRegion(
                List<CaveCell> cells,
                List<CaveCell> floorCells,
                List<CaveCell> openBoundaryCells,
                int minX,
                int maxX,
                int minY,
                int maxY,
                bool touchesScanTop,
                bool isLarge,
                int processedCellCount)
            {
                Cells = cells;
                FloorCells = floorCells;
                OpenBoundaryCells = openBoundaryCells;
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
                TouchesScanTop = touchesScanTop;
                IsLarge = isLarge;
                ProcessedCellCount = processedCellCount;
            }

            public List<CaveCell> Cells { get; }
            public List<CaveCell> FloorCells { get; }
            public List<CaveCell> OpenBoundaryCells { get; }
            public int MinX { get; }
            public int MaxX { get; }
            public int MinY { get; }
            public int MaxY { get; }
            public bool TouchesScanTop { get; }
            public bool IsLarge { get; }
            public int ProcessedCellCount { get; }
            public bool HasOpenBoundaries => OpenBoundaryCells.Count > 0;
        }

        private sealed class CavePoolPlan
        {
            public CavePoolPlan(int centerX, int centerY, int radius, int fillY, int waterCellCount, List<CaveCell> cells, CaveFillMode mode)
            {
                CenterX = centerX;
                CenterY = centerY;
                Radius = radius;
                FillY = fillY;
                WaterCellCount = waterCellCount;
                Cells = cells;
                Mode = mode;
            }

            public int CenterX { get; }
            public int CenterY { get; }
            public int Radius { get; }
            public int FillY { get; }
            public int WaterCellCount { get; }
            public List<CaveCell> Cells { get; }
            public CaveFillMode Mode { get; }
        }

        private sealed class UndergroundWaterStats
        {
            public int BottomPools { get; private set; }
            public int PartialCaves { get; private set; }
            public int FullCaves { get; private set; }
            public int BigLakes { get; private set; }
            public int TotalPools => BottomPools + PartialCaves + FullCaves + BigLakes;

            public void Record(CaveFillMode mode)
            {
                switch (mode)
                {
                    case CaveFillMode.Full:
                        FullCaves++;
                        break;
                    case CaveFillMode.Partial:
                        PartialCaves++;
                        break;
                    case CaveFillMode.BigLake:
                        BigLakes++;
                        break;
                    default:
                        BottomPools++;
                        break;
                }
            }
        }

        private enum CaveFillMode
        {
            Bottom,
            Partial,
            Full,
            BigLake
        }

        private readonly struct CaveCell
        {
            public CaveCell(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }
            public int Y { get; }
        }

        private readonly struct WaterSite
        {
            public WaterSite(int x, int y, int radius)
            {
                X = x;
                Y = y;
                Radius = radius;
            }

            public int X { get; }
            public int Y { get; }
            public int Radius { get; }
        }
    }
}
