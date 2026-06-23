using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nyvorn.Source.World.Tissue
{
    public readonly record struct TissueNodePropagation(
        TissueNodeInfo Node,
        float ArrivalDistance);

    public readonly record struct TissueBranchPropagation(
        int BranchId,
        int FromNodeId,
        float StartDistance,
        float Length,
        bool Reverse);

    public sealed class TissuePropagationMap
    {
        private readonly Dictionary<int, TissueNodePropagation> nodesById;
        private readonly Dictionary<int, TissueBranchPropagation> branchesById;

        internal TissuePropagationMap(
            float maximumDistance,
            List<TissueNodePropagation> nodes,
            List<TissueBranchPropagation> branches)
        {
            MaximumDistance = maximumDistance;
            Nodes = new ReadOnlyCollection<TissueNodePropagation>(nodes);
            Branches = new ReadOnlyCollection<TissueBranchPropagation>(branches);
            nodesById = new Dictionary<int, TissueNodePropagation>(nodes.Count);
            branchesById = new Dictionary<int, TissueBranchPropagation>(branches.Count);

            for (int i = 0; i < nodes.Count; i++)
                nodesById[nodes[i].Node.Id] = nodes[i];
            for (int i = 0; i < branches.Count; i++)
                branchesById[branches[i].BranchId] = branches[i];
        }

        public float MaximumDistance { get; }
        public IReadOnlyList<TissueNodePropagation> Nodes { get; }
        public IReadOnlyList<TissueBranchPropagation> Branches { get; }

        public bool TryGetNode(int nodeId, out TissueNodePropagation node)
        {
            return nodesById.TryGetValue(nodeId, out node);
        }

        public bool TryGetBranch(int branchId, out TissueBranchPropagation branch)
        {
            return branchesById.TryGetValue(branchId, out branch);
        }
    }
}
