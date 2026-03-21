using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueNetwork
    {
        public TissueNetwork(int seed, Rectangle worldBounds, IReadOnlyList<TissueNode> nodes, IReadOnlyList<TissueBranch> branches)
        {
            Seed = seed;
            WorldBounds = worldBounds;
            Nodes = nodes;
            Branches = branches;
        }

        public int Seed { get; }
        public Rectangle WorldBounds { get; }
        public IReadOnlyList<TissueNode> Nodes { get; }
        public IReadOnlyList<TissueBranch> Branches { get; }
    }
}
