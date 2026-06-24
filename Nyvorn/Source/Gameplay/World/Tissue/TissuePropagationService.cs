using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissuePropagationService : ITissuePropagationService
    {
        private const float SignalEpsilon = 0.00001f;
        private const float ComparisonEpsilon = 0.000001f;

        private readonly WorldMap worldMap;
        private readonly TissueField tissueField;
        private readonly TissueNetwork tissueNetwork;
        private readonly int[][] branchTileKeys;
        private readonly float[][] conductivityByChannel;
        private int cachedFieldRevision = -1;

        public TissuePropagationService(
            WorldMap worldMap,
            TissueField tissueField,
            TissueNetwork tissueNetwork)
        {
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
            this.tissueField = tissueField ?? throw new ArgumentNullException(nameof(tissueField));
            this.tissueNetwork = tissueNetwork ?? throw new ArgumentNullException(nameof(tissueNetwork));

            if (tissueField.Width != worldMap.Width || tissueField.Height != worldMap.Height)
            {
                throw new ArgumentException(
                    "TissueField e WorldMap precisam ter as mesmas dimensoes.",
                    nameof(tissueField));
            }

            branchTileKeys = BuildBranchTileKeys();
            conductivityByChannel = new float[Enum.GetValues<TissueSignalChannel>().Length][];
            for (int i = 0; i < conductivityByChannel.Length; i++)
            {
                conductivityByChannel[i] = new float[tissueNetwork.Branches.Count];
                Array.Fill(conductivityByChannel[i], float.NaN);
            }
        }

        public bool TryPropagate(
            TissuePropagationRequest request,
            out TissuePropagationResult result)
        {
            result = null;
            if (!IsValid(request) ||
                !tissueNetwork.TryGetNode(request.OriginNodeId, out TissueNode originNode))
            {
                return false;
            }

            RefreshConductivityCacheIfNeeded();

            Dictionary<int, NodeLabel> labels = new()
            {
                [originNode.Id] = new NodeLabel(
                    Distance: 0f,
                    Strength: request.InitialStrength,
                    PathConductivity: 1f,
                    PredecessorBranchId: -1)
            };
            PriorityQueue<QueueEntry, (float NegativeStrength, float Distance, int NodeId)> queue = new();
            queue.Enqueue(
                new QueueEntry(originNode.Id, request.InitialStrength, 0f),
                (-request.InitialStrength, 0f, originNode.Id));

            while (queue.TryDequeue(out QueueEntry entry, out _))
            {
                if (!labels.TryGetValue(entry.NodeId, out NodeLabel current) ||
                    !Approximately(entry.Strength, current.Strength) ||
                    !Approximately(entry.Distance, current.Distance))
                {
                    continue;
                }

                IReadOnlyList<int> branchIndices = tissueNetwork.GetConnectedBranchIndices(entry.NodeId);
                for (int i = 0; i < branchIndices.Count; i++)
                {
                    int branchIndex = branchIndices[i];
                    TissueBranch branch = tissueNetwork.Branches[branchIndex];
                    if (branch.Kind != TissueBranch.TissueBranchKind.Main || branch.EndNodeId < 0)
                        continue;

                    int neighborId = branch.StartNodeId == entry.NodeId
                        ? branch.EndNodeId
                        : branch.StartNodeId;
                    if (!tissueNetwork.TryGetNode(neighborId, out _))
                        continue;

                    float conductivity = GetBranchConductivity(branchIndex, request.Channel);
                    if (conductivity < request.MinimumConductivity || conductivity <= SignalEpsilon)
                        continue;

                    float candidateDistance = current.Distance + tissueNetwork.GetBranchLength(branchIndex);
                    if (candidateDistance > request.MaximumDistance)
                        continue;

                    float pathConductivity = current.PathConductivity * conductivity;
                    float candidateStrength = CalculateStrength(
                        request,
                        candidateDistance,
                        pathConductivity);
                    if (candidateStrength <= SignalEpsilon)
                        continue;

                    NodeLabel candidate = new(
                        candidateDistance,
                        candidateStrength,
                        pathConductivity,
                        branch.Id);
                    if (labels.TryGetValue(neighborId, out NodeLabel known) &&
                        !IsBetter(candidate, known))
                    {
                        continue;
                    }

                    labels[neighborId] = candidate;
                    queue.Enqueue(
                        new QueueEntry(neighborId, candidate.Strength, candidate.Distance),
                        (-candidate.Strength, candidate.Distance, neighborId));
                }
            }

            List<TissueReachedNode> reachedNodes = BuildReachedNodes(labels);
            List<TissueReachedBranch> reachedBranches = BuildReachedBranches(request, labels);
            result = new TissuePropagationResult(
                request,
                tissueField.Revision,
                reachedNodes,
                reachedBranches);
            return true;
        }

        private List<TissueReachedNode> BuildReachedNodes(Dictionary<int, NodeLabel> labels)
        {
            List<TissueReachedNode> nodes = new(labels.Count);
            foreach (KeyValuePair<int, NodeLabel> pair in labels)
            {
                if (!tissueNetwork.TryGetNode(pair.Key, out TissueNode node))
                    continue;

                nodes.Add(new TissueReachedNode(
                    CreateNodeInfo(node),
                    pair.Value.Distance,
                    pair.Value.Strength));
            }

            nodes.Sort((a, b) =>
            {
                int distanceComparison = a.ArrivalDistance.CompareTo(b.ArrivalDistance);
                return distanceComparison != 0
                    ? distanceComparison
                    : a.Node.Id.CompareTo(b.Node.Id);
            });
            return nodes;
        }

        private List<TissueReachedBranch> BuildReachedBranches(
            TissuePropagationRequest request,
            Dictionary<int, NodeLabel> labels)
        {
            List<TissueReachedBranch> branches = new();
            for (int branchIndex = 0; branchIndex < tissueNetwork.Branches.Count; branchIndex++)
            {
                TissueBranch branch = tissueNetwork.Branches[branchIndex];
                bool startReached = labels.TryGetValue(branch.StartNodeId, out NodeLabel start);
                NodeLabel end = default;
                bool endReached = branch.EndNodeId >= 0 &&
                    labels.TryGetValue(branch.EndNodeId, out end);
                if (!startReached && !endReached)
                    continue;

                bool reverse = ShouldReverse(branch, startReached, start, endReached, end);
                int fromNodeId = reverse ? branch.EndNodeId : branch.StartNodeId;
                NodeLabel from = reverse ? end : start;
                if (from.Distance >= request.MaximumDistance)
                    continue;

                float conductivity = branch.Kind == TissueBranch.TissueBranchKind.Micro
                    ? 1f
                    : GetBranchConductivity(branchIndex, request.Channel);
                if (conductivity < request.MinimumConductivity || conductivity <= SignalEpsilon)
                    continue;

                float length = tissueNetwork.GetBranchLength(branchIndex);
                if (length <= 0f)
                    continue;

                float endDistance = from.Distance + length;
                float exitStrength = CalculateStrength(
                    request,
                    endDistance,
                    from.PathConductivity * conductivity);
                branches.Add(new TissueReachedBranch(
                    branch.Id,
                    fromNodeId,
                    from.Distance,
                    endDistance,
                    from.Strength,
                    exitStrength,
                    conductivity,
                    reverse));
            }

            branches.Sort((a, b) =>
            {
                int distanceComparison = a.StartDistance.CompareTo(b.StartDistance);
                return distanceComparison != 0
                    ? distanceComparison
                    : a.BranchId.CompareTo(b.BranchId);
            });
            return branches;
        }

        private static bool ShouldReverse(
            TissueBranch branch,
            bool startReached,
            NodeLabel start,
            bool endReached,
            NodeLabel end)
        {
            if (!startReached)
                return true;
            if (!endReached)
                return false;
            if (end.Distance != start.Distance)
                return end.Distance < start.Distance;
            if (end.Strength != start.Strength)
                return end.Strength > start.Strength;
            return branch.EndNodeId < branch.StartNodeId;
        }

        private float GetBranchConductivity(int branchIndex, TissueSignalChannel channel)
        {
            float[] channelCache = conductivityByChannel[(int)channel];
            if (!float.IsNaN(channelCache[branchIndex]))
                return channelCache[branchIndex];

            int[] keys = branchTileKeys[branchIndex];
            float bottleneck = 1f;
            bool sampledBaseTissue = false;
            for (int i = 0; i < keys.Length; i++)
            {
                int tileX = keys[i] % worldMap.Width;
                int tileY = keys[i] / worldMap.Width;
                if (!tissueField.TryGetBaseState(tileX, tileY, out TissueCellState baseState) ||
                    baseState.SignalCapacity <= TissueCellState.PresenceThreshold)
                {
                    continue;
                }

                sampledBaseTissue = true;
                TissueCellState current = tissueField.GetState(tileX, tileY);
                float signal = channel switch
                {
                    TissueSignalChannel.Native => current.NativeConductivity,
                    TissueSignalChannel.Corrupted => current.CorruptedConductivity,
                    TissueSignalChannel.Raw => current.SignalCapacity,
                    _ => 0f
                };
                float ratio = MathHelper.Clamp(signal / baseState.SignalCapacity, 0f, 1f);
                bottleneck = MathF.Min(bottleneck, ratio);
                if (bottleneck <= 0f)
                    break;
            }

            float conductivity = sampledBaseTissue ? bottleneck : 1f;
            channelCache[branchIndex] = conductivity;
            return conductivity;
        }

        private int[][] BuildBranchTileKeys()
        {
            int[][] keysByBranch = new int[tissueNetwork.Branches.Count][];
            for (int branchIndex = 0; branchIndex < tissueNetwork.Branches.Count; branchIndex++)
            {
                TissueBranch branch = tissueNetwork.Branches[branchIndex];
                if (branch.Kind == TissueBranch.TissueBranchKind.Micro || branch.Points == null)
                {
                    keysByBranch[branchIndex] = Array.Empty<int>();
                    continue;
                }

                HashSet<int> seen = new();
                List<int> keys = new();
                for (int i = 0; i < branch.Points.Count; i++)
                {
                    Vector2 point = branch.Points[i];
                    int pixelY = (int)MathF.Round(point.Y);
                    if (pixelY < 0 || pixelY >= worldMap.Height * worldMap.TileSize)
                        continue;

                    int pixelX = WrapPixel((int)MathF.Round(point.X), worldMap.PixelWidth);
                    int tileX = pixelX / worldMap.TileSize;
                    int tileY = pixelY / worldMap.TileSize;
                    int key = (tileY * worldMap.Width) + tileX;
                    if (seen.Add(key))
                        keys.Add(key);
                }

                keysByBranch[branchIndex] = keys.ToArray();
            }

            return keysByBranch;
        }

        private void RefreshConductivityCacheIfNeeded()
        {
            if (cachedFieldRevision == tissueField.Revision)
                return;

            for (int i = 0; i < conductivityByChannel.Length; i++)
                Array.Fill(conductivityByChannel[i], float.NaN);
            cachedFieldRevision = tissueField.Revision;
        }

        private static bool IsBetter(NodeLabel candidate, NodeLabel known)
        {
            if (candidate.Strength > known.Strength + ComparisonEpsilon)
                return true;
            if (!Approximately(candidate.Strength, known.Strength))
                return false;
            if (candidate.Distance < known.Distance - ComparisonEpsilon)
                return true;
            if (!Approximately(candidate.Distance, known.Distance))
                return false;
            return candidate.PredecessorBranchId < known.PredecessorBranchId;
        }

        private static float CalculateStrength(
            TissuePropagationRequest request,
            float distance,
            float pathConductivity)
        {
            float remaining = 1f - MathHelper.Clamp(
                distance / request.MaximumDistance,
                0f,
                1f);
            return request.InitialStrength *
                MathF.Pow(remaining, request.AttenuationPower) *
                pathConductivity;
        }

        private static bool IsValid(TissuePropagationRequest request)
        {
            return IsFinite(request.MaximumDistance) && request.MaximumDistance > 0f &&
                   IsFinite(request.InitialStrength) && request.InitialStrength > 0f && request.InitialStrength <= 1f &&
                   IsFinite(request.AttenuationPower) && request.AttenuationPower > 0f &&
                   IsFinite(request.MinimumConductivity) &&
                   request.MinimumConductivity >= 0f && request.MinimumConductivity <= 1f &&
                   Enum.IsDefined(request.Channel);
        }

        private static TissueNodeInfo CreateNodeInfo(TissueNode node)
        {
            return new TissueNodeInfo(
                node.Id,
                node.Position,
                node.IsPrimary,
                node.Strength,
                node.Degree,
                node.NestInfluence);
        }

        private static int WrapPixel(int pixelX, int width)
        {
            int wrapped = pixelX % width;
            return wrapped < 0 ? wrapped + width : wrapped;
        }

        private static bool Approximately(float a, float b)
        {
            return MathF.Abs(a - b) <= ComparisonEpsilon;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly record struct NodeLabel(
            float Distance,
            float Strength,
            float PathConductivity,
            int PredecessorBranchId);

        private readonly record struct QueueEntry(
            int NodeId,
            float Strength,
            float Distance);
    }
}
