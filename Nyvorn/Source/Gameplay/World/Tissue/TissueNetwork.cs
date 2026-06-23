using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueNetwork
    {
        private readonly Dictionary<long, List<int>> branchChunks = new();
        private readonly Dictionary<long, List<int>> nodeChunks = new();
        private readonly Dictionary<int, int> nodeIndicesById = new();
        private readonly Dictionary<int, List<int>> connectedBranchIndices = new();
        private readonly float[] branchLengths;

        public TissueNetwork(int seed, Rectangle worldBounds, IReadOnlyList<TissueNode> nodes, IReadOnlyList<TissueBranch> branches)
        {
            Seed = seed;
            WorldBounds = worldBounds;
            Nodes = nodes;
            Branches = branches;
            branchLengths = new float[Branches.Count];
            BuildSpatialIndex();
            BuildTopologyIndex();
        }

        public int Seed { get; }
        public Rectangle WorldBounds { get; }
        public IReadOnlyList<TissueNode> Nodes { get; }
        public IReadOnlyList<TissueBranch> Branches { get; }

        public void CollectVisibleBranches(Rectangle bounds, List<TissueBranch> destination, bool includeMicro = true)
        {
            destination.Clear();
            HashSet<int> seen = new();
            CollectChunkIndices(bounds, branchChunks, seen);
            foreach (int index in seen)
            {
                TissueBranch branch = Branches[index];
                if ((!includeMicro && branch.Kind == TissueBranch.TissueBranchKind.Micro) || !branch.Bounds.Intersects(bounds))
                    continue;
                destination.Add(branch);
            }
        }

        public void CollectVisibleNodes(Rectangle bounds, List<TissueNode> destination)
        {
            destination.Clear();
            HashSet<int> seen = new();
            CollectChunkIndices(bounds, nodeChunks, seen);
            foreach (int index in seen)
            {
                TissueNode node = Nodes[index];
                if (bounds.Contains(node.Position))
                    destination.Add(node);
            }
        }

        internal bool TryFindNearestNode(Vector2 position, float maximumDistance, out TissueNode nearestNode)
        {
            return TryFindNearestNode(position, maximumDistance, requireConnected: false, out nearestNode);
        }

        internal bool TryFindNearestConnectedNode(Vector2 position, float maximumDistance, out TissueNode nearestNode)
        {
            return TryFindNearestNode(position, maximumDistance, requireConnected: true, out nearestNode);
        }

        private bool TryFindNearestNode(
            Vector2 position,
            float maximumDistance,
            bool requireConnected,
            out TissueNode nearestNode)
        {
            nearestNode = null;
            if (Nodes.Count == 0 || maximumDistance <= 0f)
                return false;

            float worldLeft = WorldBounds.Left;
            float worldRight = WorldBounds.Right;
            float worldWidth = WorldBounds.Width;
            if (worldWidth <= 0f)
                return false;

            float wrappedX = WrapCoordinate(position.X, worldLeft, worldWidth);
            float minimumY = MathF.Max(WorldBounds.Top, position.Y - maximumDistance);
            float maximumY = MathF.Min(WorldBounds.Bottom, position.Y + maximumDistance);
            if (minimumY > maximumY)
                return false;

            HashSet<int> candidates = new();
            if (maximumDistance * 2f >= worldWidth)
            {
                CollectChunkIndices(worldLeft, minimumY, worldRight, maximumY, nodeChunks, candidates);
            }
            else
            {
                float minimumX = wrappedX - maximumDistance;
                float maximumX = wrappedX + maximumDistance;
                if (minimumX < worldLeft)
                {
                    CollectChunkIndices(worldLeft, minimumY, maximumX, maximumY, nodeChunks, candidates);
                    CollectChunkIndices(minimumX + worldWidth, minimumY, worldRight, maximumY, nodeChunks, candidates);
                }
                else if (maximumX > worldRight)
                {
                    CollectChunkIndices(minimumX, minimumY, worldRight, maximumY, nodeChunks, candidates);
                    CollectChunkIndices(worldLeft, minimumY, maximumX - worldWidth, maximumY, nodeChunks, candidates);
                }
                else
                {
                    CollectChunkIndices(minimumX, minimumY, maximumX, maximumY, nodeChunks, candidates);
                }
            }

            double maximumDistanceSquared = (double)maximumDistance * maximumDistance;
            double nearestDistanceSquared = double.MaxValue;
            foreach (int index in candidates)
            {
                TissueNode candidate = Nodes[index];
                if (requireConnected && !HasGraphConnection(candidate.Id))
                    continue;

                float candidateX = WrapCoordinate(candidate.Position.X, worldLeft, worldWidth);
                double deltaX = Math.Abs(candidateX - wrappedX);
                deltaX = Math.Min(deltaX, worldWidth - deltaX);
                double deltaY = candidate.Position.Y - position.Y;
                double distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                if (distanceSquared > maximumDistanceSquared)
                    continue;

                if (nearestNode == null ||
                    distanceSquared < nearestDistanceSquared ||
                    (distanceSquared == nearestDistanceSquared && candidate.Id < nearestNode.Id))
                {
                    nearestNode = candidate;
                    nearestDistanceSquared = distanceSquared;
                }
            }

            return nearestNode != null;
        }

        private bool HasGraphConnection(int nodeId)
        {
            if (!connectedBranchIndices.TryGetValue(nodeId, out List<int> indices))
                return false;

            for (int i = 0; i < indices.Count; i++)
            {
                TissueBranch branch = Branches[indices[i]];
                if (branch.Kind == TissueBranch.TissueBranchKind.Main && branch.EndNodeId >= 0)
                    return true;
            }

            return false;
        }

        internal bool TryBuildPropagation(
            int originNodeId,
            float maximumDistance,
            out TissuePropagationMap propagation)
        {
            propagation = null;
            if (!nodeIndicesById.ContainsKey(originNodeId) || maximumDistance <= 0f)
                return false;

            Dictionary<int, float> distances = new()
            {
                [originNodeId] = 0f
            };
            PriorityQueue<int, float> queue = new();
            queue.Enqueue(originNodeId, 0f);

            while (queue.TryDequeue(out int nodeId, out float queuedDistance))
            {
                if (!distances.TryGetValue(nodeId, out float currentDistance) || queuedDistance > currentDistance)
                    continue;
                if (!connectedBranchIndices.TryGetValue(nodeId, out List<int> branchIndices))
                    continue;

                for (int i = 0; i < branchIndices.Count; i++)
                {
                    int branchIndex = branchIndices[i];
                    TissueBranch branch = Branches[branchIndex];
                    if (branch.Kind != TissueBranch.TissueBranchKind.Main || branch.EndNodeId < 0)
                        continue;

                    int neighborId = branch.StartNodeId == nodeId
                        ? branch.EndNodeId
                        : branch.StartNodeId;
                    if (!nodeIndicesById.ContainsKey(neighborId))
                        continue;

                    float candidateDistance = currentDistance + branchLengths[branchIndex];
                    if (candidateDistance > maximumDistance)
                        continue;
                    if (distances.TryGetValue(neighborId, out float knownDistance) && candidateDistance >= knownDistance)
                        continue;

                    distances[neighborId] = candidateDistance;
                    queue.Enqueue(neighborId, candidateDistance);
                }
            }

            List<TissueNodePropagation> nodes = new(distances.Count);
            foreach (KeyValuePair<int, float> pair in distances)
            {
                TissueNode node = Nodes[nodeIndicesById[pair.Key]];
                nodes.Add(new TissueNodePropagation(CreateNodeInfo(node), pair.Value));
            }
            nodes.Sort((a, b) =>
            {
                int distanceComparison = a.ArrivalDistance.CompareTo(b.ArrivalDistance);
                return distanceComparison != 0
                    ? distanceComparison
                    : a.Node.Id.CompareTo(b.Node.Id);
            });

            List<TissueBranchPropagation> branches = new();
            for (int i = 0; i < Branches.Count; i++)
            {
                TissueBranch branch = Branches[i];
                bool startReached = distances.TryGetValue(branch.StartNodeId, out float startDistance);
                float endDistance = 0f;
                bool endReached = branch.EndNodeId >= 0 &&
                    distances.TryGetValue(branch.EndNodeId, out endDistance);
                if (!startReached && !endReached)
                    continue;

                int fromNodeId;
                float propagationStart;
                bool reverse;
                if (!endReached ||
                    (startReached && (startDistance < endDistance ||
                    (startDistance == endDistance && branch.StartNodeId < branch.EndNodeId))))
                {
                    fromNodeId = branch.StartNodeId;
                    propagationStart = startDistance;
                    reverse = false;
                }
                else
                {
                    fromNodeId = branch.EndNodeId;
                    propagationStart = endDistance;
                    reverse = true;
                }

                float length = branchLengths[i];
                if (length <= 0f || propagationStart >= maximumDistance)
                    continue;

                branches.Add(new TissueBranchPropagation(
                    branch.Id,
                    fromNodeId,
                    propagationStart,
                    length,
                    reverse));
            }
            branches.Sort((a, b) =>
            {
                int distanceComparison = a.StartDistance.CompareTo(b.StartDistance);
                return distanceComparison != 0
                    ? distanceComparison
                    : a.BranchId.CompareTo(b.BranchId);
            });

            propagation = new TissuePropagationMap(maximumDistance, nodes, branches);
            return true;
        }

        private void BuildSpatialIndex()
        {
            for (int i = 0; i < Branches.Count; i++)
                AddBoundsToIndex(Branches[i].Bounds, i, branchChunks);

            for (int i = 0; i < Nodes.Count; i++)
            {
                Point chunk = GetChunk(Nodes[i].Position.X, Nodes[i].Position.Y);
                AddIndex(chunk.X, chunk.Y, i, nodeChunks);
            }
        }

        private void BuildTopologyIndex()
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                nodeIndicesById[Nodes[i].Id] = i;
                connectedBranchIndices[Nodes[i].Id] = new List<int>();
            }

            for (int i = 0; i < Branches.Count; i++)
            {
                TissueBranch branch = Branches[i];
                branchLengths[i] = CalculatePathLength(branch.Points);
                AddConnectedBranch(branch.StartNodeId, i);
                if (branch.EndNodeId >= 0 && branch.EndNodeId != branch.StartNodeId)
                    AddConnectedBranch(branch.EndNodeId, i);
            }
        }

        private void AddConnectedBranch(int nodeId, int branchIndex)
        {
            if (connectedBranchIndices.TryGetValue(nodeId, out List<int> indices))
                indices.Add(branchIndex);
        }

        private static float CalculatePathLength(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 2)
                return 0f;

            float length = 0f;
            for (int i = 1; i < points.Count; i++)
                length += Vector2.Distance(points[i - 1], points[i]);
            return length;
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

        private static void AddBoundsToIndex(Rectangle bounds, int index, Dictionary<long, List<int>> chunks)
        {
            Point min = GetChunk(bounds.Left, bounds.Top);
            Point max = GetChunk(bounds.Right, bounds.Bottom);
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                    AddIndex(x, y, index, chunks);
            }
        }

        private static void CollectChunkIndices(Rectangle bounds, Dictionary<long, List<int>> chunks, HashSet<int> destination)
        {
            Point min = GetChunk(bounds.Left, bounds.Top);
            Point max = GetChunk(bounds.Right, bounds.Bottom);
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    if (!chunks.TryGetValue(CreateChunkKey(x, y), out List<int> indices))
                        continue;
                    for (int i = 0; i < indices.Count; i++)
                        destination.Add(indices[i]);
                }
            }
        }

        private static void CollectChunkIndices(
            float minimumX,
            float minimumY,
            float maximumX,
            float maximumY,
            Dictionary<long, List<int>> chunks,
            HashSet<int> destination)
        {
            Point min = GetChunk(minimumX, minimumY);
            Point max = GetChunk(maximumX, maximumY);
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    if (!chunks.TryGetValue(CreateChunkKey(x, y), out List<int> indices))
                        continue;

                    for (int i = 0; i < indices.Count; i++)
                        destination.Add(indices[i]);
                }
            }
        }

        private static void AddIndex(int x, int y, int index, Dictionary<long, List<int>> chunks)
        {
            long key = CreateChunkKey(x, y);
            if (!chunks.TryGetValue(key, out List<int> indices))
            {
                indices = new List<int>();
                chunks[key] = indices;
            }
            indices.Add(index);
        }

        private static Point GetChunk(float x, float y)
        {
            return new Point(
                (int)MathF.Floor(x / TissueConfig.Network.RenderSpatialChunkSize),
                (int)MathF.Floor(y / TissueConfig.Network.RenderSpatialChunkSize));
        }

        private static long CreateChunkKey(int x, int y)
        {
            return ((long)y << 32) | (uint)x;
        }

        private static float WrapCoordinate(float value, float origin, float width)
        {
            float wrapped = (value - origin) % width;
            if (wrapped < 0f)
                wrapped += width;
            return origin + wrapped;
        }
    }
}
