using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueGenerator
    {
        private const int SampleCellSize = 128;
        private const float SurfaceDepthStart = 0.22f;
        private const float PointChanceSurface = 0.035f;
        private const float PointChanceDepth = 0.82f;
        private const float DensityCurvePower = 2.2f;
        private const float MinPointDistanceSurface = 150f;
        private const float MinPointDistanceDepth = 64f;
        private const float ConnectionDistanceMin = 48f;
        private const float ConnectionDistanceMax = 280f;
        private const float RepairConnectionDistanceMax = 448f;
        private const float MinAngularSeparation = MathF.PI * (32f / 180f);
        private const int NodeDegreeThreshold = 4;
        private const int PointSpatialCellSize = 64;
        private const int GraphSpatialCellSize = 280;

        private readonly int seed;
        private Random random;

        private sealed class PointDraft
        {
            public int Id;
            public Vector2 Position;
            public float Depth;
            public float NestInfluence;
        }

        private readonly record struct NestField(Vector2 Center, float Radius, float Multiplier);
        private readonly record struct EdgeDraft(int A, int B, float Distance);
        private readonly record struct NeighborCandidate(int Index, float Distance, float Angle);

        public TissueGenerator(int seed)
        {
            this.seed = seed;
            random = new Random(seed);
        }

        public TissueGenerationResult Generate(WorldMap worldMap)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            random = new Random(seed);

            int pixelWidth = worldMap.PixelWidth;
            int pixelHeight = worldMap.Height * worldMap.TileSize;
            List<NestField> nests = GenerateNestFields(pixelWidth, pixelHeight);
            List<PointDraft> points = GeneratePointField(pixelWidth, pixelHeight, nests);
            List<EdgeDraft> edges = BuildSpatialGraph(points, pixelWidth);
            RepairConnectivity(points, edges, pixelWidth);

            int[] degrees = ComputeDegrees(points.Count, edges);
            List<TissueNode> nodes = BuildNodes(points, degrees);
            List<TissueBranch> branches = BuildBranches(points, edges, degrees, pixelWidth);
            int mainBranchCount = branches.Count;
            AddMicroFilaments(points, degrees, branches, pixelWidth, pixelHeight);

            TissueField rasterizedField = Rasterize(worldMap, nodes, branches, out int rasterizedTileCount);
            float dominantRatio = GetDominantComponentRatio(points.Count, edges);
            TissueGenerationStats stats = BuildStats(points, degrees, mainBranchCount, branches.Count - mainBranchCount, nests.Count, rasterizedTileCount, dominantRatio, edges);

            return new TissueGenerationResult
            {
                Network = new TissueNetwork(
                    seed,
                    new Rectangle(0, 0, pixelWidth, pixelHeight),
                    nodes,
                    branches),
                RasterizedField = rasterizedField,
                Stats = stats
            };
        }

        private List<NestField> GenerateNestFields(int width, int height)
        {
            int nestCount = Math.Clamp(width / 12000, 2, 5);
            List<NestField> nests = new(nestCount);
            for (int i = 0; i < nestCount; i++)
            {
                nests.Add(new NestField(
                    new Vector2(random.Next(width), height * (0.72f + ((float)random.NextDouble() * 0.23f))),
                    random.Next(280, 561),
                    1.45f + ((float)random.NextDouble() * 0.55f)));
            }
            return nests;
        }

        private List<PointDraft> GeneratePointField(int width, int height, List<NestField> nests)
        {
            List<PointDraft> points = new();
            Dictionary<long, List<int>> spatial = new();
            int minY = (int)MathF.Round(height * SurfaceDepthStart);

            for (int cellY = minY; cellY < height; cellY += SampleCellSize)
            {
                for (int cellX = 0; cellX < width; cellX += SampleCellSize)
                {
                    int x = Math.Min(width - 1, cellX + random.Next(SampleCellSize));
                    int y = Math.Min(height - 1, cellY + random.Next(SampleCellSize));
                    float depth = y / (float)Math.Max(1, height - 1);
                    float densityFactor = MathF.Pow(depth, DensityCurvePower);
                    float nestInfluence = GetNestInfluence(x, y, width, nests);
                    float chance = MathHelper.Lerp(PointChanceSurface, PointChanceDepth, densityFactor);
                    chance = Math.Clamp(chance * (1f + nestInfluence), 0f, 0.98f);
                    if (random.NextDouble() > chance)
                        continue;

                    float minDistance = MathHelper.Lerp(MinPointDistanceSurface, MinPointDistanceDepth, densityFactor);
                    minDistance /= 1f + (nestInfluence * 0.28f);
                    Vector2 position = new(x, y);
                    if (!IsFarEnough(position, minDistance, points, spatial, width))
                        continue;

                    PointDraft point = new()
                    {
                        Id = points.Count,
                        Position = position,
                        Depth = depth,
                        NestInfluence = nestInfluence
                    };
                    points.Add(point);
                    AddPointToSpatial(point.Id, position, PointSpatialCellSize, spatial, width);
                }
            }

            return points;
        }

        private float GetNestInfluence(float x, float y, int width, List<NestField> nests)
        {
            float influence = 0f;
            for (int i = 0; i < nests.Count; i++)
            {
                NestField nest = nests[i];
                float dx = GetWrappedDeltaX(x, nest.Center.X, width);
                float dy = y - nest.Center.Y;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                if (distance >= nest.Radius)
                    continue;
                float t = 1f - (distance / nest.Radius);
                influence += t * t * nest.Multiplier;
            }
            return Math.Clamp(influence, 0f, 2.5f);
        }

        private bool IsFarEnough(Vector2 position, float minDistance, List<PointDraft> points, Dictionary<long, List<int>> spatial, int width)
        {
            int radius = (int)MathF.Ceiling(minDistance / PointSpatialCellSize);
            Point cell = GetSpatialCell(position, PointSpatialCellSize, width);
            int bucketCountX = Math.Max(1, (int)MathF.Ceiling(width / (float)PointSpatialCellSize));
            float minDistanceSq = minDistance * minDistance;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int cellX = WrapIndex(cell.X + dx, bucketCountX);
                    if (!spatial.TryGetValue(CreateSpatialKey(cellX, cell.Y + dy), out List<int> indices))
                        continue;
                    for (int i = 0; i < indices.Count; i++)
                    {
                        Vector2 other = points[indices[i]].Position;
                        float deltaX = GetWrappedDeltaX(position.X, other.X, width);
                        float deltaY = position.Y - other.Y;
                        if ((deltaX * deltaX) + (deltaY * deltaY) < minDistanceSq)
                            return false;
                    }
                }
            }
            return true;
        }

        private List<EdgeDraft> BuildSpatialGraph(List<PointDraft> points, int width)
        {
            List<EdgeDraft> edges = new();
            HashSet<long> edgeKeys = new();
            HashSet<int>[] adjacency = CreateAdjacency(points.Count);
            Dictionary<long, List<int>> spatial = BuildSpatialIndex(points, GraphSpatialCellSize, width);

            for (int sourceIndex = 0; sourceIndex < points.Count; sourceIndex++)
            {
                PointDraft source = points[sourceIndex];
                List<NeighborCandidate> candidates = FindCandidates(sourceIndex, points, spatial, width, ConnectionDistanceMax, 1);
                candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                GetConnectionRange(source, out int minConnections, out int maxConnections);
                int desired = random.Next(minConnections, maxConnections + 1);
                List<float> chosenAngles = new();

                for (int i = 0; i < candidates.Count && adjacency[sourceIndex].Count < desired; i++)
                {
                    NeighborCandidate candidate = candidates[i];
                    if (candidate.Distance < ConnectionDistanceMin || adjacency[candidate.Index].Count >= 8)
                        continue;
                    if (HasSimilarAngle(candidate.Angle, chosenAngles) && adjacency[sourceIndex].Count >= minConnections)
                        continue;
                    if (ClosesShortTriangle(sourceIndex, candidate.Index, adjacency) && adjacency[sourceIndex].Count >= minConnections)
                        continue;

                    AddEdge(sourceIndex, candidate.Index, candidate.Distance, edges, edgeKeys, adjacency);
                    chosenAngles.Add(candidate.Angle);
                }

                if (adjacency[sourceIndex].Count < minConnections)
                {
                    for (int i = 0; i < candidates.Count && adjacency[sourceIndex].Count < minConnections; i++)
                        AddEdge(sourceIndex, candidates[i].Index, candidates[i].Distance, edges, edgeKeys, adjacency);
                }
            }

            return edges;
        }

        private void RepairConnectivity(List<PointDraft> points, List<EdgeDraft> edges, int width)
        {
            if (points.Count <= 1)
                return;

            UnionFind union = BuildUnion(points.Count, edges);
            Dictionary<long, List<int>> spatial = BuildSpatialIndex(points, GraphSpatialCellSize, width);
            List<EdgeDraft> candidates = new();
            HashSet<long> candidateKeys = new();

            for (int i = 0; i < points.Count; i++)
            {
                List<NeighborCandidate> nearby = FindCandidates(i, points, spatial, width, RepairConnectionDistanceMax, 2);
                for (int j = 0; j < nearby.Count; j++)
                {
                    int other = nearby[j].Index;
                    if (union.Find(i) == union.Find(other))
                        continue;
                    long key = CreateEdgeKey(i, other);
                    if (candidateKeys.Add(key))
                        candidates.Add(new EdgeDraft(i, other, nearby[j].Distance));
                }
            }

            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            HashSet<long> existing = new();
            for (int i = 0; i < edges.Count; i++)
                existing.Add(CreateEdgeKey(edges[i].A, edges[i].B));

            for (int i = 0; i < candidates.Count && GetLargestComponentSize(union, points.Count) < points.Count * 0.99f; i++)
            {
                EdgeDraft edge = candidates[i];
                if (union.Find(edge.A) == union.Find(edge.B))
                    continue;
                if (existing.Add(CreateEdgeKey(edge.A, edge.B)))
                {
                    edges.Add(edge);
                    union.Union(edge.A, edge.B);
                }
            }
        }

        private List<TissueNode> BuildNodes(List<PointDraft> points, int[] degrees)
        {
            List<TissueNode> nodes = new(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                PointDraft point = points[i];
                float strength = Math.Clamp((point.Depth * 0.55f) + (degrees[i] / 8f * 0.30f) + (point.NestInfluence * 0.15f), 0.15f, 1f);
                nodes.Add(new TissueNode(i, point.Position, degrees[i] >= NodeDegreeThreshold, strength, degrees[i], point.NestInfluence));
            }
            return nodes;
        }

        private List<TissueBranch> BuildBranches(List<PointDraft> points, List<EdgeDraft> edges, int[] degrees, int width)
        {
            List<TissueBranch> branches = new(edges.Count);
            for (int i = 0; i < edges.Count; i++)
            {
                EdgeDraft edge = edges[i];
                PointDraft a = points[edge.A];
                PointDraft b = points[edge.B];
                float depth = (a.Depth + b.Depth) * 0.5f;
                float strength = Math.Clamp((depth * 0.62f) + ((degrees[edge.A] + degrees[edge.B]) / 16f * 0.24f) + ((a.NestInfluence + b.NestInfluence) * 0.07f), 0.2f, 1f);
                float thickness = MathHelper.Lerp(0.65f, 2.1f, strength);
                List<Vector2> path = BuildOrganicPath(a.Position, b.Position, width, i);
                branches.Add(new TissueBranch(i, edge.A, edge.B, strength >= 0.72f, thickness, strength, TissueBranch.TissueBranchKind.Main, path));
            }
            return branches;
        }

        private List<Vector2> BuildOrganicPath(Vector2 start, Vector2 end, int width, int edgeId)
        {
            float wrappedDeltaX = GetWrappedDeltaX(end.X, start.X, width);
            Vector2 unwrappedEnd = new(start.X + wrappedDeltaX, end.Y);
            Vector2 current = start;
            List<Vector2> path = new() { current };
            OpenSimplexNoise noise = new(seed + 7001 + edgeId);
            float wander = 0f;
            int maxSteps = Math.Max(12, (int)MathF.Ceiling(Vector2.Distance(start, unwrappedEnd) / 2f));

            for (int stepIndex = 0; stepIndex < maxSteps; stepIndex++)
            {
                Vector2 toTarget = unwrappedEnd - current;
                float distance = toTarget.Length();
                if (distance <= 3f)
                    break;
                Vector2 direction = toTarget / distance;
                Vector2 perpendicular = new(-direction.Y, direction.X);
                float sample = (float)noise.Evaluate(current.X * 0.008, current.Y * 0.008);
                wander = (wander * 0.86f) + (sample * 0.82f);
                float stepSize = Math.Clamp(3.4f + (sample * 0.75f), 2.2f, 4.4f);
                Vector2 next = current + (direction * stepSize) + (perpendicular * wander);
                if (Vector2.DistanceSquared(next, unwrappedEnd) > distance * distance)
                    next = unwrappedEnd;
                current = next;
                path.Add(current);
            }
            path.Add(unwrappedEnd);
            return path;
        }

        private void AddMicroFilaments(List<PointDraft> points, int[] degrees, List<TissueBranch> branches, int width, int height)
        {
            int id = branches.Count;
            for (int i = 0; i < points.Count; i++)
            {
                if (degrees[i] < NodeDegreeThreshold || random.NextDouble() > 0.46)
                    continue;
                int count = degrees[i] >= 6 ? 2 : 1;
                for (int filament = 0; filament < count; filament++)
                {
                    float angle = (float)(random.NextDouble() * Math.PI * 2.0);
                    float length = random.Next(18, 71);
                    Vector2 start = points[i].Position;
                    Vector2 end = start + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;
                    end.Y = Math.Clamp(end.Y, height * SurfaceDepthStart, height - 1f);
                    List<Vector2> path = BuildOrganicPath(start, end, width, id);
                    branches.Add(new TissueBranch(id++, i, -1, false, 0.45f, 0.28f + (points[i].Depth * 0.22f), TissueBranch.TissueBranchKind.Micro, path));
                }
            }
        }

        private TissueField Rasterize(WorldMap worldMap, List<TissueNode> nodes, List<TissueBranch> branches, out int tileCount)
        {
            TissueField field = new(worldMap.Width, worldMap.Height);
            for (int i = 0; i < branches.Count; i++)
            {
                TissueBranch branch = branches[i];
                if (branch.Kind == TissueBranch.TissueBranchKind.Micro)
                    continue;
                for (int p = 0; p < branch.Points.Count; p++)
                    SetRasterizedTile(worldMap, field, branch.Points[p], branch.Intensity, vitality: 0.72f, flow: 0.78f);
            }
            for (int i = 0; i < nodes.Count; i++)
                SetRasterizedTile(worldMap, field, nodes[i].Position, nodes[i].Strength, vitality: 0.92f, flow: 0.88f);
            tileCount = field.CountActiveTiles();
            return field;
        }

        private static void SetRasterizedTile(WorldMap worldMap, TissueField field, Vector2 position, float presence, float vitality, float flow)
        {
            int pixelX = WrapPixel((int)MathF.Round(position.X), worldMap.PixelWidth);
            int pixelY = (int)MathF.Round(position.Y);
            if (pixelY < 0 || pixelY >= worldMap.Height * worldMap.TileSize)
                return;
            int tileX = pixelX / worldMap.TileSize;
            int tileY = pixelY / worldMap.TileSize;
            if (!worldMap.IsSolidAt(tileX, tileY))
                return;
            TissueCellState current = field.GetState(tileX, tileY);
            if (current.Presence >= presence)
                return;
            field.SetState(tileX, tileY, new TissueCellState(presence, vitality, 0f, 0f, flow));
        }

        private TissueGenerationStats BuildStats(List<PointDraft> points, int[] degrees, int edgeCount, int microCount, int nestCount, int rasterizedTiles, float dominantRatio, List<EdgeDraft> edges)
        {
            int minDegree = points.Count == 0 ? 0 : int.MaxValue;
            int maxDegree = 0;
            long degreeTotal = 0;
            int upper = 0;
            int middle = 0;
            int deep = 0;
            for (int i = 0; i < points.Count; i++)
            {
                minDegree = Math.Min(minDegree, degrees[i]);
                maxDegree = Math.Max(maxDegree, degrees[i]);
                degreeTotal += degrees[i];
                if (points[i].Depth < 0.48f) upper++;
                else if (points[i].Depth < 0.76f) middle++;
                else deep++;
            }
            return new TissueGenerationStats
            {
                PointCount = points.Count,
                EdgeCount = edgeCount,
                MicroFilamentCount = microCount,
                NestCount = nestCount,
                RasterizedTileCount = rasterizedTiles,
                MinDegree = minDegree,
                MaxDegree = maxDegree,
                AverageDegree = points.Count == 0 ? 0f : degreeTotal / (float)points.Count,
                DominantComponentRatio = dominantRatio,
                UpperPointCount = upper,
                MiddlePointCount = middle,
                DeepPointCount = deep,
                DeterministicHash = ComputeHash(points, edges)
            };
        }

        private static ulong ComputeHash(List<PointDraft> points, List<EdgeDraft> edges)
        {
            ulong hash = 14695981039346656037UL;
            void Mix(uint value) { hash ^= value; hash *= 1099511628211UL; }
            for (int i = 0; i < points.Count; i++)
            {
                Mix((uint)MathF.Round(points[i].Position.X));
                Mix((uint)MathF.Round(points[i].Position.Y));
            }
            for (int i = 0; i < edges.Count; i++)
            {
                Mix((uint)edges[i].A);
                Mix((uint)edges[i].B);
            }
            return hash;
        }

        private static int[] ComputeDegrees(int pointCount, List<EdgeDraft> edges)
        {
            int[] degrees = new int[pointCount];
            for (int i = 0; i < edges.Count; i++)
            {
                degrees[edges[i].A]++;
                degrees[edges[i].B]++;
            }
            return degrees;
        }

        private static float GetDominantComponentRatio(int pointCount, List<EdgeDraft> edges)
        {
            if (pointCount == 0)
                return 1f;
            UnionFind union = BuildUnion(pointCount, edges);
            return GetLargestComponentSize(union, pointCount) / (float)pointCount;
        }

        private static UnionFind BuildUnion(int pointCount, List<EdgeDraft> edges)
        {
            UnionFind union = new(pointCount);
            for (int i = 0; i < edges.Count; i++)
                union.Union(edges[i].A, edges[i].B);
            return union;
        }

        private static int GetLargestComponentSize(UnionFind union, int pointCount)
        {
            Dictionary<int, int> sizes = new();
            int largest = 0;
            for (int i = 0; i < pointCount; i++)
            {
                int root = union.Find(i);
                sizes.TryGetValue(root, out int size);
                size++;
                sizes[root] = size;
                largest = Math.Max(largest, size);
            }
            return largest;
        }

        private static HashSet<int>[] CreateAdjacency(int count)
        {
            HashSet<int>[] result = new HashSet<int>[count];
            for (int i = 0; i < count; i++) result[i] = new HashSet<int>();
            return result;
        }

        private static bool ClosesShortTriangle(int a, int b, HashSet<int>[] adjacency)
        {
            foreach (int neighbor in adjacency[a])
            {
                if (adjacency[b].Contains(neighbor))
                    return true;
            }
            return false;
        }

        private static bool HasSimilarAngle(float angle, List<float> chosen)
        {
            for (int i = 0; i < chosen.Count; i++)
            {
                float difference = MathF.Abs(MathHelper.WrapAngle(angle - chosen[i]));
                if (difference < MinAngularSeparation)
                    return true;
            }
            return false;
        }

        private static void GetConnectionRange(PointDraft point, out int min, out int max)
        {
            if (point.Depth < 0.48f) { min = 1; max = 2; }
            else if (point.Depth < 0.76f) { min = 2; max = 4; }
            else { min = 3; max = 6; }
            if (point.NestInfluence > 0.35f) max = Math.Min(8, max + 2);
        }

        private static void AddEdge(int a, int b, float distance, List<EdgeDraft> edges, HashSet<long> keys, HashSet<int>[] adjacency)
        {
            if (a == b || !keys.Add(CreateEdgeKey(a, b)))
                return;
            edges.Add(new EdgeDraft(a, b, distance));
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        private List<NeighborCandidate> FindCandidates(int sourceIndex, List<PointDraft> points, Dictionary<long, List<int>> spatial, int width, float maxDistance, int bucketRadius)
        {
            List<NeighborCandidate> candidates = new();
            PointDraft source = points[sourceIndex];
            Point cell = GetSpatialCell(source.Position, GraphSpatialCellSize, width);
            int bucketCountX = Math.Max(1, (int)MathF.Ceiling(width / (float)GraphSpatialCellSize));
            float maxDistanceSq = maxDistance * maxDistance;
            for (int dy = -bucketRadius; dy <= bucketRadius; dy++)
            {
                for (int dx = -bucketRadius; dx <= bucketRadius; dx++)
                {
                    int cellX = WrapIndex(cell.X + dx, bucketCountX);
                    if (!spatial.TryGetValue(CreateSpatialKey(cellX, cell.Y + dy), out List<int> indices))
                        continue;
                    for (int i = 0; i < indices.Count; i++)
                    {
                        int otherIndex = indices[i];
                        if (otherIndex == sourceIndex)
                            continue;
                        Vector2 other = points[otherIndex].Position;
                        float deltaX = GetWrappedDeltaX(other.X, source.Position.X, width);
                        float deltaY = other.Y - source.Position.Y;
                        float distanceSq = (deltaX * deltaX) + (deltaY * deltaY);
                        if (distanceSq > maxDistanceSq)
                            continue;
                        candidates.Add(new NeighborCandidate(otherIndex, MathF.Sqrt(distanceSq), MathF.Atan2(deltaY, deltaX)));
                    }
                }
            }
            return candidates;
        }

        private static Dictionary<long, List<int>> BuildSpatialIndex(List<PointDraft> points, int cellSize, int width)
        {
            Dictionary<long, List<int>> spatial = new();
            for (int i = 0; i < points.Count; i++)
                AddPointToSpatial(i, points[i].Position, cellSize, spatial, width);
            return spatial;
        }

        private static void AddPointToSpatial(int index, Vector2 position, int cellSize, Dictionary<long, List<int>> spatial, int width)
        {
            Point cell = GetSpatialCell(position, cellSize, width);
            long key = CreateSpatialKey(cell.X, cell.Y);
            if (!spatial.TryGetValue(key, out List<int> indices))
            {
                indices = new List<int>();
                spatial[key] = indices;
            }
            indices.Add(index);
        }

        private static Point GetSpatialCell(Vector2 position, int cellSize, int width)
        {
            int bucketCountX = Math.Max(1, (int)MathF.Ceiling(width / (float)cellSize));
            return new Point(WrapIndex((int)MathF.Floor(position.X / cellSize), bucketCountX), (int)MathF.Floor(position.Y / cellSize));
        }

        private static long CreateSpatialKey(int x, int y) => ((long)y << 32) | (uint)x;
        private static long CreateEdgeKey(int a, int b) { int min = Math.Min(a, b); int max = Math.Max(a, b); return ((long)min << 32) | (uint)max; }
        private static int WrapIndex(int value, int count) { int wrapped = value % count; return wrapped < 0 ? wrapped + count : wrapped; }
        private static int WrapPixel(int value, int width) { int wrapped = value % width; return wrapped < 0 ? wrapped + width : wrapped; }
        private static float GetWrappedDeltaX(float targetX, float sourceX, int width)
        {
            float delta = targetX - sourceX;
            if (delta > width * 0.5f) delta -= width;
            else if (delta < -width * 0.5f) delta += width;
            return delta;
        }

        private sealed class UnionFind
        {
            private readonly int[] parent;
            private readonly byte[] rank;
            public UnionFind(int count) { parent = new int[count]; rank = new byte[count]; for (int i = 0; i < count; i++) parent[i] = i; }
            public int Find(int value) { while (parent[value] != value) { parent[value] = parent[parent[value]]; value = parent[value]; } return value; }
            public void Union(int a, int b) { int rootA = Find(a); int rootB = Find(b); if (rootA == rootB) return; if (rank[rootA] < rank[rootB]) parent[rootA] = rootB; else if (rank[rootA] > rank[rootB]) parent[rootB] = rootA; else { parent[rootB] = rootA; rank[rootA]++; } }
        }
    }
}
