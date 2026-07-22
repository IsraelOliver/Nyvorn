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

        // Trunk-column sprites are 10px wide but tiles are 8px (TileSize), so they must be
        // shifted 1px left to bleed evenly onto both sides instead of only overflowing right.
        private static readonly Point TrunkColumnDrawOffset = new(-1, 0);

        public TreePartAtlas()
        {
            Add(TreePartType.TrunkStraight, 1, 1, drawOffsetPixels: TrunkColumnDrawOffset);

            // SocketRight/Left describe the side of the trunk socket. RootLeft/Right describe placement around the trunk.
            Add(TreePartType.TrunkBaseRightRootSocket, 1, 3, drawOffsetPixels: TrunkColumnDrawOffset);
            Add(TreePartType.TrunkBaseLeftRootSocket, 4, 3, drawOffsetPixels: TrunkColumnDrawOffset);
            Add(TreePartType.RootLeft, 3, 3);
            Add(TreePartType.RootRight, 2, 3);
            Add(TreePartType.RootBothSocket, 5, 3, drawOffsetPixels: TrunkColumnDrawOffset);
            Add(TreePartType.BranchSocketRight, 5, 1, drawOffsetPixels: TrunkColumnDrawOffset);
            Add(TreePartType.BranchSocketLeft, 5, 2, drawOffsetPixels: TrunkColumnDrawOffset);
            Add(TreePartType.BranchRight, 6, 1);
            Add(TreePartType.BranchLeft, 6, 2);
            Add(TreePartType.TrunkCutSupport, 4, 1, drawOffsetPixels: TrunkColumnDrawOffset);
            Add(TreePartType.TrunkContinuation, 6, 3, drawOffsetPixels: TrunkColumnDrawOffset);
            Add(TreePartType.TrunkBaseCut, 4, 1, drawOffsetPixels: TrunkColumnDrawOffset);
            Add(TreePartType.TrunkUpperCut, 6, 3, drawOffsetPixels: TrunkColumnDrawOffset);
            Add(TreePartType.TrunkBareBase, 1, 1, drawOffsetPixels: TrunkColumnDrawOffset);
            Add(TreePartType.TrunkBaseRightRootCutSocket, 1, 4, drawOffsetPixels: TrunkColumnDrawOffset);

            // Canopy is 42px wide, an even split around the trunk's centerline would need 43 -
            // without this 1px nudge left, the canopy's center sits 1px right of the trunk's.
            parts[TreePartType.Canopy] = new TreePartDefinition(
                TreePartType.Canopy,
                new Rectangle(CanopySourceX, CanopySourceY, CanopyPixelWidth, CanopyPixelHeight),
                new Point(6, 6),
                new Point(-1, 0));
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
            Point? drawOffsetPixels = null)
        {
            parts[type] = new TreePartDefinition(
                type,
                GetSmallCell(column, line),
                SingleTile,
                drawOffsetPixels ?? NoDrawOffset);
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
