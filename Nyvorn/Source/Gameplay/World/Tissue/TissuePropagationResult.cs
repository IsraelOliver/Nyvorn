using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nyvorn.Source.World.Tissue
{
    public enum TissueSignalChannel
    {
        Native,
        Corrupted,
        Raw
    }

    public readonly record struct TissuePropagationRequest(
        int OriginNodeId,
        float MaximumDistance,
        float InitialStrength,
        float AttenuationPower,
        float MinimumConductivity,
        TissueSignalChannel Channel = TissueSignalChannel.Native);

    public readonly record struct TissueReachedNode(
        TissueNodeInfo Node,
        float ArrivalDistance,
        float ArrivalStrength);

    public readonly record struct TissueReachedBranch(
        int BranchId,
        int FromNodeId,
        float StartDistance,
        float EndDistance,
        float EntryStrength,
        float ExitStrength,
        float Conductivity,
        bool Reverse)
    {
        public float Length => EndDistance - StartDistance;
    }

    public sealed class TissuePropagationResult
    {
        private readonly Dictionary<int, TissueReachedNode> nodesById;
        private readonly Dictionary<int, TissueReachedBranch> branchesById;

        internal TissuePropagationResult(
            TissuePropagationRequest request,
            int fieldRevision,
            List<TissueReachedNode> nodes,
            List<TissueReachedBranch> branches)
        {
            Request = request;
            FieldRevision = fieldRevision;
            Nodes = new ReadOnlyCollection<TissueReachedNode>(nodes);
            Branches = new ReadOnlyCollection<TissueReachedBranch>(branches);
            nodesById = new Dictionary<int, TissueReachedNode>(nodes.Count);
            branchesById = new Dictionary<int, TissueReachedBranch>(branches.Count);

            for (int i = 0; i < nodes.Count; i++)
                nodesById[nodes[i].Node.Id] = nodes[i];
            for (int i = 0; i < branches.Count; i++)
                branchesById[branches[i].BranchId] = branches[i];
        }

        public TissuePropagationRequest Request { get; }
        public int FieldRevision { get; }
        public float MaximumDistance => Request.MaximumDistance;
        public IReadOnlyList<TissueReachedNode> Nodes { get; }
        public IReadOnlyList<TissueReachedBranch> Branches { get; }

        public bool TryGetNode(int nodeId, out TissueReachedNode node)
        {
            return nodesById.TryGetValue(nodeId, out node);
        }

        public bool TryGetBranch(int branchId, out TissueReachedBranch branch)
        {
            return branchesById.TryGetValue(branchId, out branch);
        }
    }
}
