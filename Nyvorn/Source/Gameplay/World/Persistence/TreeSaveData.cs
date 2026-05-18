using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Decorations;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class TreeSaveData
    {
        public int BaseX { get; init; }
        public int BaseY { get; init; }
        public int Height { get; init; }
        public TreeVariant Variant { get; init; }
        public int RootStyleRow { get; init; }
        public int BranchHeight { get; init; }
        public int BranchDirection { get; init; }
        public int Seed { get; init; }
        public List<TreePartSaveData> Parts { get; init; } = new();
        public TreePartSaveData Canopy { get; init; }

        public static TreeSaveData FromTree(TreeInstance tree)
        {
            List<TreePartSaveData> parts = new(tree.Parts.Count);
            for (int i = 0; i < tree.Parts.Count; i++)
                parts.Add(FromPlacement(tree.Parts[i]));

            return new TreeSaveData
            {
                BaseX = tree.BaseTile.X,
                BaseY = tree.BaseTile.Y,
                Height = tree.Height,
                Variant = tree.Variant,
                RootStyleRow = tree.RootStyleRow,
                BranchHeight = tree.BranchHeight,
                BranchDirection = tree.BranchDirection,
                Seed = tree.Seed,
                Parts = parts,
                Canopy = FromPlacement(tree.Canopy)
            };
        }

        public TreeInstance ToTree()
        {
            List<TreePartPlacement> parts = new(Parts?.Count ?? 0);
            if (Parts != null)
            {
                for (int i = 0; i < Parts.Count; i++)
                    parts.Add(ToPlacement(Parts[i]));
            }

            return new TreeInstance
            {
                BaseTile = new Point(BaseX, BaseY),
                Height = Height,
                Variant = Variant,
                RootStyleRow = RootStyleRow,
                BranchHeight = BranchHeight,
                BranchDirection = BranchDirection,
                Seed = Seed,
                Parts = parts,
                Canopy = Canopy != null ? ToPlacement(Canopy) : new TreePartPlacement(TreePartType.Canopy, new Point(-2, -Height - 4))
            };
        }

        private static TreePartSaveData FromPlacement(TreePartPlacement placement)
        {
            return new TreePartSaveData
            {
                PartType = placement.PartType,
                OffsetX = placement.OffsetTiles.X,
                OffsetY = placement.OffsetTiles.Y
            };
        }

        private static TreePartPlacement ToPlacement(TreePartSaveData part)
        {
            return new TreePartPlacement(part.PartType, new Point(part.OffsetX, part.OffsetY));
        }
    }
}
