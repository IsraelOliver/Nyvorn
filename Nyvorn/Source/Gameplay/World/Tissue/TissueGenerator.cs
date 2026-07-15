using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueGenerator
    {
        public const int AlgorithmVersion = 1;

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
            AddMicroFilaments(points, degrees, branches, pixelWidth, pixelHeight);

            TissueField rasterizedField = Rasterize(worldMap, nodes, branches, out _);
            TissueGenerationStats stats = BuildStats(points, edges);

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
            int nestCount = Math.Clamp(
                width / TissueConfig.Generation.NestWidthDivisor,
                TissueConfig.Generation.NestCountMin,
                TissueConfig.Generation.NestCountMax);
            List<NestField> nests = new(nestCount);
            for (int i = 0; i < nestCount; i++)
            {
                nests.Add(new NestField(
                    new Vector2(
                        random.Next(width),
                        height * MathHelper.Lerp(TissueConfig.Generation.NestDepthMin, TissueConfig.Generation.NestDepthMax, (float)random.NextDouble())),
                    random.Next(TissueConfig.Generation.NestRadiusMin, TissueConfig.Generation.NestRadiusMax + 1),
                    MathHelper.Lerp(TissueConfig.Generation.NestMultiplierMin, TissueConfig.Generation.NestMultiplierMax, (float)random.NextDouble())));
            }
            return nests;
        }

        private List<PointDraft> GeneratePointField(int width, int height, List<NestField> nests)
        {
            List<PointDraft> points = new();
            Dictionary<long, List<int>> spatial = new();
            int minY = (int)MathF.Round(height * TissueConfig.Generation.SurfaceDepthStart);

            for (int cellY = minY; cellY < height; cellY += TissueConfig.Generation.SampleCellSize)
            {
                for (int cellX = 0; cellX < width; cellX += TissueConfig.Generation.SampleCellSize)
                {
                    int x = Math.Min(width - 1, cellX + random.Next(TissueConfig.Generation.SampleCellSize));
                    int y = Math.Min(height - 1, cellY + random.Next(TissueConfig.Generation.SampleCellSize));
                    float depth = y / (float)Math.Max(1, height - 1);
                    float densityFactor = MathF.Pow(depth, TissueConfig.Generation.DensityCurvePower);
                    float nestInfluence = GetNestInfluence(x, y, width, nests);
                    float chance = MathHelper.Lerp(TissueConfig.Generation.PointChanceSurface, TissueConfig.Generation.PointChanceDepth, densityFactor);
                    chance = Math.Clamp(chance * (1f + nestInfluence), 0f, TissueConfig.Generation.MaxPointChance);
                    if (random.NextDouble() > chance)
                        continue;

                    float minDistance = MathHelper.Lerp(TissueConfig.Generation.MinPointDistanceSurface, TissueConfig.Generation.MinPointDistanceDepth, densityFactor);
                    minDistance /= 1f + (nestInfluence * TissueConfig.Generation.NestDistanceReduction);
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
                    AddPointToSpatial(point.Id, position, TissueConfig.Generation.PointSpatialCellSize, spatial, width);
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
            return Math.Clamp(influence, 0f, TissueConfig.Generation.NestInfluenceMax);
        }

        private bool IsFarEnough(Vector2 position, float minDistance, List<PointDraft> points, Dictionary<long, List<int>> spatial, int width)
        {
            int radius = (int)MathF.Ceiling(minDistance / TissueConfig.Generation.PointSpatialCellSize);
            Point cell = GetSpatialCell(position, TissueConfig.Generation.PointSpatialCellSize, width);
            int bucketCountX = Math.Max(1, (int)MathF.Ceiling(width / (float)TissueConfig.Generation.PointSpatialCellSize));
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
            Dictionary<long, List<int>> spatial = BuildSpatialIndex(points, TissueConfig.Network.GraphSpatialCellSize, width);

            for (int sourceIndex = 0; sourceIndex < points.Count; sourceIndex++)
            {
                PointDraft source = points[sourceIndex];
                List<NeighborCandidate> candidates = FindCandidates(sourceIndex, points, spatial, width, TissueConfig.Network.ConnectionDistanceMax, 1);
                candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                GetConnectionRange(source, out int minConnections, out int maxConnections);
                int desired = random.Next(minConnections, maxConnections + 1);
                List<float> chosenAngles = new();

                for (int i = 0; i < candidates.Count && adjacency[sourceIndex].Count < desired; i++)
                {
                    NeighborCandidate candidate = candidates[i];
                    if (candidate.Distance < TissueConfig.Network.ConnectionDistanceMin || adjacency[candidate.Index].Count >= TissueConfig.Network.MaximumNodeDegree)
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
            Dictionary<long, List<int>> spatial = BuildSpatialIndex(points, TissueConfig.Network.GraphSpatialCellSize, width);
            List<EdgeDraft> candidates = new();
            HashSet<long> candidateKeys = new();

            for (int i = 0; i < points.Count; i++)
            {
                List<NeighborCandidate> nearby = FindCandidates(i, points, spatial, width, TissueConfig.Network.RepairConnectionDistanceMax, 2);
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

            for (int i = 0; i < candidates.Count && GetLargestComponentSize(union, points.Count) < points.Count * TissueConfig.Network.ConnectivityTarget; i++)
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
                float strength = Math.Clamp(
                    (point.Depth * TissueConfig.Nodes.StrengthDepthWeight) +
                    (degrees[i] / (float)TissueConfig.Network.MaximumNodeDegree * TissueConfig.Nodes.StrengthDegreeWeight) +
                    (point.NestInfluence * TissueConfig.Nodes.StrengthNestWeight),
                    TissueConfig.Nodes.StrengthMin,
                    TissueConfig.Nodes.StrengthMax);
                nodes.Add(new TissueNode(i, point.Position, degrees[i] >= TissueConfig.Nodes.PrimaryDegreeThreshold, strength, degrees[i], point.NestInfluence));
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
                float strength = Math.Clamp(
                    (depth * TissueConfig.Branches.StrengthDepthWeight) +
                    ((degrees[edge.A] + degrees[edge.B]) / (TissueConfig.Network.MaximumNodeDegree * 2f) * TissueConfig.Branches.StrengthDegreeWeight) +
                    ((a.NestInfluence + b.NestInfluence) * TissueConfig.Branches.StrengthNestWeight),
                    TissueConfig.Branches.StrengthMin,
                    TissueConfig.Branches.StrengthMax);
                float thickness = MathHelper.Lerp(TissueConfig.Branches.ThicknessMin, TissueConfig.Branches.ThicknessMax, strength);
                List<Vector2> path = BuildOrganicPath(a.Position, b.Position, width, i);
                branches.Add(new TissueBranch(i, edge.A, edge.B, strength >= TissueConfig.Branches.PrimaryStrengthThreshold, thickness, strength, TissueBranch.TissueBranchKind.Main, path));
            }
            return branches;
        }

        private List<Vector2> BuildOrganicPath(Vector2 start, Vector2 end, int width, int edgeId)
        {
            float wrappedDeltaX = GetWrappedDeltaX(end.X, start.X, width);
            Vector2 unwrappedEnd = new(start.X + wrappedDeltaX, end.Y);
            Vector2 current = start;
            List<Vector2> path = new() { current };
            OpenSimplexNoise noise = new(seed + TissueConfig.Paths.NoiseSeedOffset + edgeId);
            float wander = 0f;
            int maxSteps = Math.Max(
                TissueConfig.Paths.MinimumSteps,
                (int)MathF.Ceiling(Vector2.Distance(start, unwrappedEnd) * TissueConfig.Paths.StepsPerPixel));

            for (int stepIndex = 0; stepIndex < maxSteps; stepIndex++)
            {
                Vector2 toTarget = unwrappedEnd - current;
                float distance = toTarget.Length();
                if (distance <= TissueConfig.Paths.TargetSnapDistance)
                    break;
                Vector2 direction = toTarget / distance;
                Vector2 perpendicular = new(-direction.Y, direction.X);
                float sample = (float)noise.Evaluate(current.X * TissueConfig.Paths.NoiseScale, current.Y * TissueConfig.Paths.NoiseScale);
                wander = (wander * TissueConfig.Paths.WanderRetention) + (sample * TissueConfig.Paths.WanderStrength);
                float stepSize = Math.Clamp(
                    TissueConfig.Paths.StepSize + (sample * TissueConfig.Paths.StepNoiseStrength),
                    TissueConfig.Paths.StepSizeMin,
                    TissueConfig.Paths.StepSizeMax);
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
                if (degrees[i] < TissueConfig.Nodes.PrimaryDegreeThreshold || random.NextDouble() > TissueConfig.MicroFilaments.GenerationChance)
                    continue;
                int count = degrees[i] >= TissueConfig.MicroFilaments.MultipleFilamentDegree
                    ? TissueConfig.MicroFilaments.DenseFilamentCount
                    : TissueConfig.MicroFilaments.FilamentCount;
                for (int filament = 0; filament < count; filament++)
                {
                    float angle = (float)(random.NextDouble() * Math.PI * 2.0);
                    float length = random.Next(TissueConfig.MicroFilaments.LengthMin, TissueConfig.MicroFilaments.LengthMax + 1);
                    Vector2 start = points[i].Position;
                    Vector2 end = start + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length;
                    end.Y = Math.Clamp(end.Y, height * TissueConfig.Generation.SurfaceDepthStart, height - 1f);
                    List<Vector2> path = BuildOrganicPath(start, end, width, id);
                    branches.Add(new TissueBranch(
                        id++,
                        i,
                        -1,
                        false,
                        TissueConfig.MicroFilaments.Thickness,
                        TissueConfig.MicroFilaments.IntensityBase + (points[i].Depth * TissueConfig.MicroFilaments.IntensityDepthWeight),
                        TissueBranch.TissueBranchKind.Micro,
                        path));
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
                    SetRasterizedTile(
                        worldMap,
                        field,
                        branch.Points[p],
                        branch.Intensity,
                        vitality: TissueConfig.Rasterization.BranchVitality,
                        flow: TissueConfig.Rasterization.BranchFlow);
            }
            for (int i = 0; i < nodes.Count; i++)
                SetRasterizedTile(
                    worldMap,
                    field,
                    nodes[i].Position,
                    nodes[i].Strength,
                    vitality: TissueConfig.Rasterization.NodeVitality,
                    flow: TissueConfig.Rasterization.NodeFlow);
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
            field.SetBaseState(tileX, tileY, new TissueCellState(presence, vitality, 0f, 0f, flow));
        }

        private TissueGenerationStats BuildStats(List<PointDraft> points, List<EdgeDraft> edges)
        {
            return new TissueGenerationStats
            {
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
                if (difference < TissueConfig.Network.MinAngularSeparation)
                    return true;
            }
            return false;
        }

        private static void GetConnectionRange(PointDraft point, out int min, out int max)
        {
            if (point.Depth < TissueConfig.Network.MiddleLayerStart)
            {
                min = TissueConfig.Network.UpperConnectionsMin;
                max = TissueConfig.Network.UpperConnectionsMax;
            }
            else if (point.Depth < TissueConfig.Network.DeepLayerStart)
            {
                min = TissueConfig.Network.MiddleConnectionsMin;
                max = TissueConfig.Network.MiddleConnectionsMax;
            }
            else
            {
                min = TissueConfig.Network.DeepConnectionsMin;
                max = TissueConfig.Network.DeepConnectionsMax;
            }

            if (point.NestInfluence > TissueConfig.Network.NestConnectionBoostThreshold)
                max = Math.Min(TissueConfig.Network.MaximumNodeDegree, max + TissueConfig.Network.NestConnectionBonus);
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
            Point cell = GetSpatialCell(source.Position, TissueConfig.Network.GraphSpatialCellSize, width);
            int bucketCountX = Math.Max(1, (int)MathF.Ceiling(width / (float)TissueConfig.Network.GraphSpatialCellSize));
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
