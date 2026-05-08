using Microsoft.Xna.Framework;
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

        public List<TreeInstance> Generate(WorldGenContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            List<TreeInstance> trees = new();
            WorldMap worldMap = context.WorldMap;
            Random random = new(context.Config.Seed ^ unchecked((int)0x6D2B79F5));

            int minX = Math.Max(context.Config.BorderThickness + 2, 2);
            int maxX = worldMap.Width - Math.Max(context.Config.BorderThickness + 3, 3);

            for (int x = minX; x <= maxX; x++)
            {
                if (random.NextDouble() > settings.TreeSpawnChance)
                    continue;

                int groundY = FindSurfaceGrassY(worldMap, context.SurfaceHeights, x);
                if (groundY < 0)
                    continue;

                int height = random.Next(settings.MinTreeHeight, settings.MaxTreeHeight + 1);
                TreeVariant variant = PickVariant(random);
                int rootStyleRow = random.Next(0, 2) == 0 ? 3 : 4;
                int branchDirection = random.Next(0, 2) == 0 ? -1 : 1;
                int branchHeight = variant == TreeVariant.Branch ? random.Next(1, Math.Max(2, height - 1)) : -1;

                if (!CanPlaceTree(worldMap, x, groundY, height, variant, branchDirection, branchHeight, trees))
                    continue;

                trees.Add(CreateTree(x, groundY, height, variant, rootStyleRow, branchDirection, branchHeight, random.Next()));
            }

            return trees;
        }

        private bool CanPlaceTree(
            WorldMap worldMap,
            int baseX,
            int groundY,
            int height,
            TreeVariant variant,
            int branchDirection,
            int branchHeight,
            IReadOnlyList<TreeInstance> existingTrees)
        {
            if (worldMap.GetTile(baseX, groundY) != TileType.Grass)
                return false;

            if (groundY <= height + settings.CanopyClearanceTiles)
                return false;

            if (worldMap.IsSolidAt(baseX, groundY - 1))
                return false;

            if (IsTooClose(baseX, existingTrees))
                return false;

            int minClearX = baseX - 2;
            int maxClearX = baseX + 2;
            int topY = groundY - height - settings.CanopyClearanceTiles;
            for (int y = topY; y <= groundY - 1; y++)
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
                int branchY = groundY - branchHeight;
                if (worldMap.IsSolidAt(branchX, branchY))
                    return false;
            }

            return true;
        }

        private bool HasFlatRootGround(WorldMap worldMap, int baseX, int groundY, TreeVariant variant, int rootDirection)
        {
            if (variant == TreeVariant.DoubleRoot)
                return worldMap.GetTile(baseX - 1, groundY) == TileType.Grass
                    && worldMap.GetTile(baseX + 1, groundY) == TileType.Grass;

            int rootX = baseX + (rootDirection < 0 ? -1 : 1);
            return worldMap.GetTile(rootX, groundY) == TileType.Grass;
        }

        private bool IsTooClose(int baseX, IReadOnlyList<TreeInstance> existingTrees)
        {
            for (int i = 0; i < existingTrees.Count; i++)
            {
                int distance = Math.Abs(baseX - existingTrees[i].BaseTile.X);
                if (distance < settings.MinTreeSpacingTiles)
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
            if (roll < 38)
                return TreeVariant.Simple;
            if (roll < 68)
                return TreeVariant.SingleRoot;
            if (roll < 88)
                return TreeVariant.DoubleRoot;

            return TreeVariant.Branch;
        }

        private static TreeInstance CreateTree(
            int baseX,
            int groundY,
            int height,
            TreeVariant variant,
            int rootStyleRow,
            int branchDirection,
            int branchHeight,
            int seed)
        {
            List<TreePartPlacement> parts = new();
            TreePartType basePart = variant switch
            {
                TreeVariant.SingleRoot when branchDirection < 0 => TreePartType.TrunkBaseRightRootSocket,
                TreeVariant.SingleRoot => TreePartType.TrunkBaseLeftRootSocket,
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
                TreePartType trunkType = TreePartType.TrunkStraight;
                if (variant == TreeVariant.Branch && trunkOffset == branchHeight)
                {
                    trunkType = branchDirection > 0
                        ? TreePartType.BranchSocketRight
                        : TreePartType.BranchSocketLeft;
                }

                parts.Add(new TreePartPlacement(trunkType, new Point(0, -trunkOffset)));
            }

            if (variant == TreeVariant.Branch && branchHeight > 0)
            {
                TreePartType branchPart = branchDirection > 0 ? TreePartType.BranchRight : TreePartType.BranchLeft;
                parts.Add(new TreePartPlacement(branchPart, new Point(branchDirection, -branchHeight)));
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
                Canopy = new TreePartPlacement(TreePartType.Canopy, new Point(-2, -height - 2))
            };
        }
    }
}
