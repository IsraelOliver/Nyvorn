using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Decorations
{
    public sealed class TreePartAtlas
    {
        public const int SmallPartPixelSize = 10;
        public const int SmallPartSpacing = 1;

        public const int SmallGridStartX = 1;
        public const int SmallGridStartY = 0;

        public const int CanopyPixelWidth = 42;
        public const int CanopyPixelHeight = 41;
        public const int CanopySourceX = 67;
        public const int CanopySourceY = 2;

        private readonly Dictionary<TreePartType, TreePartDefinition> parts = new();

        private static readonly Point SingleTile = new(1, 1);
        private static readonly Point NoDrawOffset = Point.Zero;

        public TreePartAtlas()
        {
            Add(TreePartType.TrunkStraight, 1, 1, isBase: false, isRoot: false, isBranch: false, canReceiveBranch: false, canContinueVertically: true);

            // SocketRight/Left describe the side of the trunk socket. RootLeft/Right describe placement around the trunk.
            Add(TreePartType.TrunkBaseRightRootSocket, 1, 3, isBase: true, isRoot: false, isBranch: false, canReceiveBranch: false, canContinueVertically: true, complementaryPartType: TreePartType.RootRight);
            Add(TreePartType.TrunkBaseLeftRootSocket, 4, 3, isBase: true, isRoot: false, isBranch: false, canReceiveBranch: false, canContinueVertically: true, complementaryPartType: TreePartType.RootLeft);
            Add(TreePartType.RootLeft, 3, 3, isBase: false, isRoot: true, isBranch: false, canReceiveBranch: false, canContinueVertically: false, complementaryPartType: TreePartType.TrunkBaseLeftRootSocket);
            Add(TreePartType.RootRight, 2, 3, isBase: false, isRoot: true, isBranch: false, canReceiveBranch: false, canContinueVertically: false, complementaryPartType: TreePartType.TrunkBaseRightRootSocket);
            Add(TreePartType.RootBothSocket, 5, 3, isBase: true, isRoot: false, isBranch: false, canReceiveBranch: false, canContinueVertically: true);
            Add(TreePartType.BranchSocketRight, 5, 1, isBase: false, isRoot: false, isBranch: false, canReceiveBranch: true, canContinueVertically: true, complementaryPartType: TreePartType.BranchRight);
            Add(TreePartType.BranchSocketLeft, 5, 2, isBase: false, isRoot: false, isBranch: false, canReceiveBranch: true, canContinueVertically: true, complementaryPartType: TreePartType.BranchLeft);
            Add(TreePartType.BranchRight, 6, 1, isBase: false, isRoot: false, isBranch: true, canReceiveBranch: false, canContinueVertically: false, complementaryPartType: TreePartType.BranchSocketRight);
            Add(TreePartType.BranchLeft, 6, 2, isBase: false, isRoot: false, isBranch: true, canReceiveBranch: false, canContinueVertically: false, complementaryPartType: TreePartType.BranchSocketLeft);
            Add(TreePartType.TrunkCutSupport, 4, 1, isBase: false, isRoot: false, isBranch: false, canReceiveBranch: false, canContinueVertically: true);
            Add(TreePartType.TrunkContinuation, 6, 3, isBase: false, isRoot: false, isBranch: false, canReceiveBranch: false, canContinueVertically: true);
            Add(TreePartType.TrunkBaseCut, 4, 1, isBase: true, isRoot: false, isBranch: false, canReceiveBranch: false, canContinueVertically: false);
            Add(TreePartType.TrunkUpperCut, 6, 3, isBase: false, isRoot: false, isBranch: false, canReceiveBranch: false, canContinueVertically: false);
            Add(TreePartType.TrunkBareBase, 1, 1, isBase: true, isRoot: false, isBranch: false, canReceiveBranch: false, canContinueVertically: true);
            Add(TreePartType.TrunkBaseRightRootCutSocket, 1, 4, isBase: true, isRoot: false, isBranch: false, canReceiveBranch: false, canContinueVertically: true, complementaryPartType: TreePartType.RootRight);

            parts[TreePartType.Canopy] = new TreePartDefinition(
                TreePartType.Canopy,
                new Rectangle(CanopySourceX, CanopySourceY, CanopyPixelWidth, CanopyPixelHeight),
                new Point(6, 6),
                NoDrawOffset,
                IsBase: false,
                IsRoot: false,
                IsBranch: false,
                CanReceiveBranch: false,
                CanContinueVertically: false);
        }

        public TreePartDefinition Get(TreePartType partType)
        {
            return parts[partType];
        }

        public Rectangle GetSourceRectangle(TreePartType partType)
        {
            return parts[partType].SourceRectangle;
        }

        public Rectangle GetSourceRectangle(TreeInstance tree, TreePartType partType, int placementIndex)
        {
            int rootLine = tree.RootStyleRow == 4 ? 4 : 3;

            return partType switch
            {
                TreePartType.TrunkStraight => GetStraightTrunkSource(tree.Seed, placementIndex),
                TreePartType.TrunkBaseRightRootSocket => GetSmallCell(1, rootLine),
                TreePartType.TrunkBaseLeftRootSocket => GetSmallCell(4, rootLine),
                TreePartType.RootLeft => GetSmallCell(3, rootLine),
                TreePartType.RootRight => GetSmallCell(2, rootLine),
                TreePartType.RootBothSocket => GetSmallCell(5, rootLine),
                TreePartType.TrunkBaseCut => GetSmallCell(4, 1 + (System.Math.Abs(tree.Seed) % 2)),
                TreePartType.TrunkUpperCut => GetSmallCell(6, 3 + (System.Math.Abs(tree.Seed) % 2)),
                TreePartType.TrunkBareBase => GetSmallCell(1, 1 + (System.Math.Abs(tree.Seed) % 2)),
                TreePartType.TrunkBaseRightRootCutSocket => GetSmallCell(1, 4 + (System.Math.Abs(tree.Seed) % 2)),
                _ => GetSourceRectangle(partType)
            };
        }

        private void Add(
            TreePartType type,
            int column,
            int line,
            bool isBase,
            bool isRoot,
            bool isBranch,
            bool canReceiveBranch,
            bool canContinueVertically,
            TreePartType? complementaryPartType = null,
            Point? drawOffsetPixels = null)
        {
            parts[type] = new TreePartDefinition(
                type,
                GetSmallCell(column, line),
                SingleTile,
                drawOffsetPixels ?? NoDrawOffset,
                isBase,
                isRoot,
                isBranch,
                canReceiveBranch,
                canContinueVertically,
                complementaryPartType);
        }

        public static Rectangle GetSmallCell(int column, int line)
        {
            int x = SmallGridStartX + (column - 1) * (SmallPartPixelSize + SmallPartSpacing);
            int y = SmallGridStartY + (line - 1) * (SmallPartPixelSize + SmallPartSpacing);

            return new Rectangle(x, y, SmallPartPixelSize, SmallPartPixelSize);
        }

        private static Rectangle GetStraightTrunkSource(int seed, int placementIndex)
        {
            if (placementIndex == 0)
            {
                int baseLine = 1 + (System.Math.Abs(seed) % 2);
                return GetSmallCell(1, baseLine);
            }

            int variant = System.Math.Abs(seed + (placementIndex * 37)) % 6;
            int column = 1 + (variant % 3);
            int line = 1 + (variant / 3);
            return GetSmallCell(column, line);
        }
    }
}
