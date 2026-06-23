using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueNetwork
    {
        private readonly Dictionary<long, List<int>> branchChunks = new();
        private readonly Dictionary<long, List<int>> nodeChunks = new();

        public TissueNetwork(int seed, Rectangle worldBounds, IReadOnlyList<TissueNode> nodes, IReadOnlyList<TissueBranch> branches)
        {
            Seed = seed;
            WorldBounds = worldBounds;
            Nodes = nodes;
            Branches = branches;
            BuildSpatialIndex();
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
