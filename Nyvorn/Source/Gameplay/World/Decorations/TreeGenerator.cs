using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation.Biomes;
using Nyvorn.Source.World.Generation;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Decorations
{
    public sealed class TreeGenerator
    {
        private readonly TreeGenerationSettings settings;

        public TreeGenerator(TreeGenerationSettings settings = null)
        {
            this.settings = settings ?? TreeGenerationSettings.Default;
        }

        public List<TreeInstance> GenerateWithGroups(WorldGenContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            List<TreeInstance> trees = new();
            WorldMap worldMap = context.WorldMap;
            Random random = context.CreateRandom(context.Seeds.DecorationSeed);

            int minX = Math.Max(context.Config.BorderThickness + 2, 2);
            int maxX = worldMap.Width - Math.Max(context.Config.BorderThickness + 3, 3);

            TreeGroup[] groups = GenerateGroups(random, minX, maxX);

            foreach (var group in groups)
            {
                BiomeDefinition biome = context.SampleBiome(group.CenterX).PrimaryDefinition;
                if (biome.TreeSpawnMultiplier <= 0f)
                    continue;

                GenerateTreesInGroup(context, worldMap, random, group, trees);
            }

            GenerateIsolatedTrees(context, worldMap, random, trees);

            return trees;
        }

        private TreeGroup[] GenerateGroups(Random random, int minX, int maxX)
        {
            int worldWidth = maxX - minX;
            int groupCount = random.Next(15, 50);

            List<TreeGroup> groupsList = new();

            for (int i = 0; i < groupCount; i++)
            {
                int centerX = random.Next(minX + 1, maxX);
                int groupRadius = random.Next(settings.MinGroupRadius, settings.MaxGroupRadius + 1);
                int minHeight = random.Next(settings.MinTreeHeight, settings.MaxTreeHeight - 5);
                int maxHeight = random.Next(minHeight + 3, settings.MaxTreeHeight + 1);
                minHeight = Math.Clamp(minHeight, settings.MinTreeHeight, maxHeight - 2);

                groupsList.Add(new TreeGroup
                {
                    CenterX = centerX,
                    MinHeight = minHeight,
                    MaxHeight = maxHeight,
                    GroupRadius = groupRadius
                });
            }

            return groupsList.ToArray();
        }

        private void GenerateTreesInGroup(
            WorldGenContext context,
            WorldMap worldMap,
            Random random,
            TreeGroup group,
            List<TreeInstance> trees)
        {
            int minGroupX = Math.Max(group.CenterX - group.GroupRadius, 0);
            int maxGroupX = Math.Min(group.CenterX + group.GroupRadius, worldMap.Width - 1);

            int treesPerGroup = random.Next(settings.MinTreesPerGroup, settings.MaxTreesPerGroup + 1);
            int attempts = 0;
            int maxAttempts = treesPerGroup * 3;

            const int HeightVariationRange = 6;

            while (attempts < maxAttempts && CountTreesInGroup(trees, group) < treesPerGroup)
            {
                int x = random.Next(minGroupX, maxGroupX + 1);
                int groundY = FindSurfaceGrassY(worldMap, context.SurfaceHeights, x);
                if (groundY < 0)
                {
                    attempts++;
                    continue;
                }

                int baseY = groundY - 1;
                int baseHeight = random.Next(group.MinHeight, group.MaxHeight + 1);
                int heightVariation = random.Next(-HeightVariationRange, HeightVariationRange + 1);
                int height = Math.Clamp(baseHeight + heightVariation, settings.MinTreeHeight, settings.MaxTreeHeight);

                TreeVariant variant = PickVariant(random);
                int rootStyleRow = random.Next(0, 2) == 0 ? 3 : 4;
                int branchDirection = random.Next(0, 2) == 0 ? -1 : 1;
                int branchHeight = variant == TreeVariant.Branch ? random.Next(1, Math.Max(2, height - 1)) : -1;

                if (!CanPlaceTree(worldMap, x, groundY, baseY, height, variant, branchDirection, branchHeight, settings.IntraGroupSpacing, trees))
                {
                    attempts++;
                    continue;
                }

                trees.Add(CreateTree(x, baseY, height, variant, rootStyleRow, branchDirection, branchHeight, random.Next()));
            }
        }

        private void GenerateIsolatedTrees(WorldGenContext context, WorldMap worldMap, Random random, List<TreeInstance> trees)
        {
            int minX = Math.Max(context.Config.BorderThickness + 2, 2);
            int maxX = worldMap.Width - Math.Max(context.Config.BorderThickness + 3, 3);

            for (int x = minX; x <= maxX; x++)
            {
                if (random.NextDouble() > settings.IsolatedTreeSpawnChance)
                    continue;

                BiomeDefinition biome = context.SampleBiome(x).PrimaryDefinition;
                if (biome.TreeSpawnMultiplier <= 0f)
                    continue;

                int groundY = FindSurfaceGrassY(worldMap, context.SurfaceHeights, x);
                if (groundY < 0)
                    continue;

                int baseY = groundY - 1;
                int height = random.Next(settings.MinTreeHeight, settings.MaxTreeHeight + 1);

                TreeVariant variant = PickVariant(random);
                int rootStyleRow = random.Next(0, 2) == 0 ? 3 : 4;
                int branchDirection = random.Next(0, 2) == 0 ? -1 : 1;
                int branchHeight = variant == TreeVariant.Branch ? random.Next(1, Math.Max(2, height - 1)) : -1;

                if (!CanPlaceTree(worldMap, x, groundY, baseY, height, variant, branchDirection, branchHeight, settings.IntraGroupSpacing, trees))
                    continue;

                trees.Add(CreateTree(x, baseY, height, variant, rootStyleRow, branchDirection, branchHeight, random.Next()));
            }
        }

        private int CountTreesInGroup(List<TreeInstance> trees, TreeGroup group)
        {
            int count = 0;
            for (int i = 0; i < trees.Count; i++)
            {
                if (Math.Abs(trees[i].BaseTile.X - group.CenterX) <= group.GroupRadius)
                    count++;
            }
            return count;
        }

        public List<TreeInstance> Generate(WorldGenContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            List<TreeInstance> trees = new();
            WorldMap worldMap = context.WorldMap;
            Random random = context.CreateRandom(context.Seeds.DecorationSeed);

            OpenSimplexNoise densityNoise = new(SeedHash.ToIntSeed(SeedHash.Derive(context.Seeds.DecorationSeed, "tree-cluster-density")));
            OpenSimplexNoise heightNoise = new(SeedHash.ToIntSeed(SeedHash.Derive(context.Seeds.DecorationSeed, "tree-cluster-height")));

            int minX = Math.Max(context.Config.BorderThickness + 2, 2);
            int maxX = worldMap.Width - Math.Max(context.Config.BorderThickness + 3, 3);

            for (int x = minX; x <= maxX; x++)
            {
                // Low-frequency fields turn what would be a uniform scatter into loose
                // forest clusters: some stretches grow dense and tall, others sparse and short.
                float densityT = (context.SampleTerrain1D(densityNoise, x, settings.ClusterDensityNoiseFrequency, 0f) + 1f) * 0.5f;
                float densityFactor = MathHelper.Lerp(settings.SparseClusterSpawnMultiplier, settings.DenseClusterSpawnMultiplier, densityT);
                int clusterSpacing = Math.Max(1, (int)MathF.Round(MathHelper.Lerp(settings.SparseClusterSpacingTiles, settings.DenseClusterSpacingTiles, densityT)));

                BiomeDefinition biome = context.SampleBiome(x).PrimaryDefinition;
                float spawnChance = settings.TreeSpawnChance * biome.TreeSpawnMultiplier * densityFactor;
                if (spawnChance <= 0f || random.NextDouble() > spawnChance)
                    continue;

                int groundY = FindSurfaceGrassY(worldMap, context.SurfaceHeights, x);
                if (groundY < 0)
                    continue;

                // The logical base is the air tile above the supporting grass tile.
                int baseY = groundY - 1;

                float heightT = (context.SampleTerrain1D(heightNoise, x, settings.ClusterHeightNoiseFrequency, 500f) + 1f) * 0.5f;
                int clusterHeight = (int)MathF.Round(MathHelper.Lerp(settings.MinTreeHeight, settings.MaxTreeHeight, heightT));
                int height = Math.Clamp(
                    clusterHeight + random.Next(-settings.ClusterHeightJitter, settings.ClusterHeightJitter + 1),
                    settings.MinTreeHeight,
                    settings.MaxTreeHeight);

                TreeVariant variant = PickVariant(random);
                int rootStyleRow = random.Next(0, 2) == 0 ? 3 : 4;
                int branchDirection = random.Next(0, 2) == 0 ? -1 : 1;
                int branchHeight = variant == TreeVariant.Branch ? random.Next(1, Math.Max(2, height - 1)) : -1;

                if (!CanPlaceTree(worldMap, x, groundY, baseY, height, variant, branchDirection, branchHeight, clusterSpacing, trees))
                    continue;

                trees.Add(CreateTree(x, baseY, height, variant, rootStyleRow, branchDirection, branchHeight, random.Next()));
            }

            return trees;
        }

        private bool CanPlaceTree(
            WorldMap worldMap,
            int baseX,
            int groundY,
            int baseY,
            int height,
            TreeVariant variant,
            int branchDirection,
            int branchHeight,
            int clusterSpacing,
            IReadOnlyList<TreeInstance> existingTrees)
        {
            if (worldMap.GetTile(baseX, groundY) != TileType.Grass)
                return false;

            if (!worldMap.InBounds(baseX, baseY) || worldMap.IsSolidAt(baseX, baseY))
                return false;

            if (baseY <= height + settings.CanopyClearanceTiles)
                return false;

            if (IsTooClose(baseX, clusterSpacing, existingTrees))
                return false;

            int minClearX = baseX - 2;
            int maxClearX = baseX + 2;
            int topY = baseY - height - settings.CanopyClearanceTiles;
            for (int y = topY; y <= baseY; y++)
            {
                for (int x = minClearX; x <= maxClearX; x++)
                {
                    if (worldMap.IsSolidAt(x, y))
                        return false;
                }
            }

            if (variant == TreeVariant.SingleRoot || variant == TreeVariant.DoubleRoot)
            {
                if (settings.RequireFlatGroundForRoots && !HasFlatRootGround(worldMap, baseX, groundY, variant, branchDirection))
                    return false;
            }

            if (variant == TreeVariant.Branch && branchHeight > 0)
            {
                int branchX = baseX + branchDirection;
                int branchY = baseY - branchHeight;
                if (worldMap.IsSolidAt(branchX, branchY))
                    return false;
            }

            return true;
        }

        private bool HasFlatRootGround(WorldMap worldMap, int baseX, int groundY, TreeVariant variant, int rootDirection)
        {
            if (variant == TreeVariant.DoubleRoot)
                return worldMap.GetTile(baseX - 1, groundY) == TileType.Grass
                    && worldMap.GetTile(baseX + 1, groundY) == TileType.Grass
                    && !worldMap.IsSolidAt(baseX - 1, groundY - 1)
                    && !worldMap.IsSolidAt(baseX + 1, groundY - 1);

            int rootX = baseX + (rootDirection < 0 ? -1 : 1);
            return worldMap.GetTile(rootX, groundY) == TileType.Grass
                && !worldMap.IsSolidAt(rootX, groundY - 1);
        }

        private static bool IsTooClose(int baseX, int clusterSpacing, IReadOnlyList<TreeInstance> existingTrees)
        {
            for (int i = 0; i < existingTrees.Count; i++)
            {
                int distance = Math.Abs(baseX - existingTrees[i].BaseTile.X);
                if (distance < clusterSpacing)
                    return true;
            }

            return false;
        }

        private static int FindSurfaceGrassY(WorldMap worldMap, int[] surfaceHeights, int x)
        {
            if (surfaceHeights != null && x >= 0 && x < surfaceHeights.Length)
            {
                int surfaceY = surfaceHeights[x];
                if (worldMap.GetTile(x, surfaceY) == TileType.Grass && !worldMap.IsSolidAt(x, surfaceY - 1))
                    return surfaceY;

                return -1;
            }

            for (int y = 1; y < worldMap.Height - 1; y++)
            {
                if (worldMap.GetTile(x, y) == TileType.Grass && !worldMap.IsSolidAt(x, y - 1))
                    return y;
            }

            return -1;
        }

        private static TreeVariant PickVariant(Random random)
        {
            int roll = random.Next(0, 100);
            if (roll < 35)
                return TreeVariant.Simple;
            if (roll < 60)
                return TreeVariant.SingleRoot;
            if (roll < 80)
                return TreeVariant.DoubleRoot;

            return TreeVariant.Branch;
        }

        private TreeInstance CreateTree(
            int baseX,
            int groundY,
            int height,
            TreeVariant variant,
            int rootStyleRow,
            int branchDirection,
            int branchHeight,
            int seed)
        {
            Random branchRandom = new(seed);
            List<TreePartPlacement> parts = new();
            List<TreeBranch> branches = new();

            // For SingleRoot, branchDirection is the root side: -1 places the root left, +1 places it right.
            TreePartType basePart = variant switch
            {
                TreeVariant.SingleRoot when branchDirection < 0 => TreePartType.TrunkBaseLeftRootSocket,
                TreeVariant.SingleRoot => TreePartType.TrunkBaseRightRootSocket,
                TreeVariant.DoubleRoot => TreePartType.RootBothSocket,
                _ => TreePartType.TrunkStraight
            };

            parts.Add(new TreePartPlacement(basePart, Point.Zero));

            if (variant == TreeVariant.SingleRoot)
            {
                TreePartType rootPart = branchDirection < 0 ? TreePartType.RootLeft : TreePartType.RootRight;
                parts.Add(new TreePartPlacement(rootPart, new Point(branchDirection, 0)));
            }
            else if (variant == TreeVariant.DoubleRoot)
            {
                parts.Add(new TreePartPlacement(TreePartType.RootLeft, new Point(-1, 0)));
                parts.Add(new TreePartPlacement(TreePartType.RootRight, new Point(1, 0)));
            }

            for (int trunkOffset = 1; trunkOffset < height; trunkOffset++)
            {
                parts.Add(new TreePartPlacement(TreePartType.TrunkStraight, new Point(0, -trunkOffset)));
            }

            if (variant == TreeVariant.Branch && branchHeight > 0)
            {
                TreePartType branchPart = branchDirection > 0 ? TreePartType.BranchRight : TreePartType.BranchLeft;
                parts.Add(new TreePartPlacement(branchPart, new Point(branchDirection, -branchHeight)));
                branches.Add(new TreeBranch { Height = branchHeight, Direction = branchDirection });
            }

            if (height >= settings.MinHeightForBranches)
            {
                int branchCount = Math.Min(settings.MaxBranchesPerTree, 1 + (height - settings.MinHeightForBranches) / 5);
                const int MinBranchSpacing = 4;
                const int MaxBranchSpacing = 7;

                for (int i = 0; i < branchCount * 3; i++)
                {
                    int branchH = branchRandom.Next(Math.Max(1, height / 4), Math.Max(2, height - 2));
                    int branchDir = branchRandom.Next(0, 2) == 0 ? -1 : 1;

                    if (branches.Exists(b => b.Height == branchH))
                        continue;

                    int minSpacing = branchRandom.Next(MinBranchSpacing, MaxBranchSpacing + 1);
                    bool tooClose = branches.Exists(b => Math.Abs(b.Height - branchH) < minSpacing);
                    if (tooClose)
                        continue;

                    if (branches.Count >= branchCount)
                        break;

                    TreePartType sockType = branchDir > 0 ? TreePartType.BranchSocketRight : TreePartType.BranchSocketLeft;
                    TreePartType branchPart = branchDir > 0 ? TreePartType.BranchRight : TreePartType.BranchLeft;

                    parts.Add(new TreePartPlacement(sockType, new Point(0, -branchH)));
                    parts.Add(new TreePartPlacement(branchPart, new Point(branchDir, -branchH)));
                    branches.Add(new TreeBranch { Height = branchH, Direction = branchDir });
                }
            }

            return new TreeInstance
            {
                BaseTile = new Point(baseX, groundY),
                Height = height,
                Variant = variant,
                RootStyleRow = rootStyleRow,
                BranchDirection = variant == TreeVariant.Branch || variant == TreeVariant.SingleRoot ? branchDirection : 0,
                BranchHeight = branchHeight,
                Seed = seed,
                Parts = parts,
                Canopy = new TreePartPlacement(TreePartType.Canopy, new Point(-2, -height - 4)),
                Branches = branches
            };
        }
    }
}
