using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Decorations
{
    public sealed class TreeInstance
    {
        public Point BaseTile { get; init; }
        public int Height { get; init; }
        public TreeVariant Variant { get; init; }
        public int RootStyleRow { get; init; }
        public int BranchHeight { get; init; } = -1;
        public int BranchDirection { get; init; }
        public int Seed { get; init; }
        public List<TreePartPlacement> Parts { get; init; } = new();
        public TreePartPlacement Canopy { get; init; }
    }
}
