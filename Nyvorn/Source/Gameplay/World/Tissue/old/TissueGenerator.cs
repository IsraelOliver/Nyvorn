using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueGenerator
    {
        private readonly int seed;
        private readonly FastNoiseLite nodeNoise;
        private readonly FastNoiseLite shapeNoise;
        private readonly FastNoiseLite branchNoise;
        private readonly FastNoiseLite affinityNoise;
        private readonly FastNoiseLite affinityDetailNoise;

        private enum TissueTopologyMode
        {
            Colonies
        }

        private readonly record struct ColonySpec(
            WorldLayerType LayerType,
            int ColonyCount,
            float MinDepthRatio,
            float MaxDepthRatio,
            float RadiusTilesMin,
            float RadiusTilesMax,
            int NodeCountMin,
            int NodeCountMax,
            float PrimaryStrength,
            float SecondaryStrength,
            float PrimaryThickness,
            float SecondaryThickness,
            float CurveAmplitude,
            float BridgeChance);

        private readonly record struct ColonySeed(
            Vector2 Center,
            float RadiusPixels,
            float Strength,
            bool IsPrimary,
            WorldLayerType LayerType);

        private sealed class TissueGenerationState
        {
            public required WorldMap WorldMap { get; init; }
            public required List<TissueNode> Nodes { get; init; }
            public required List<TissueBranch> Branches { get; init; }
            public required Rectangle WorldBounds { get; init; }
        }

        public TissueGenerator(int seed)
        {
            this.seed = seed;

            nodeNoise = new FastNoiseLite(seed + 901);
            nodeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            nodeNoise.SetFrequency(0.025f);

            shapeNoise = new FastNoiseLite(seed + 902);
            shapeNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            shapeNoise.SetFrequency(0.018f);
            shapeNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            shapeNoise.SetFractalOctaves(3);

            branchNoise = new FastNoiseLite(seed + 903);
            branchNoise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
            branchNoise.SetFrequency(0.06f);
            branchNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            branchNoise.SetFractalOctaves(2);

            affinityNoise = new FastNoiseLite(seed + 904);
            affinityNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            affinityNoise.SetFrequency(0.010f);
            affinityNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            affinityNoise.SetFractalOctaves(3);

            affinityDetailNoise = new FastNoiseLite(seed + 905);
            affinityDetailNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
            affinityDetailNoise.SetFrequency(0.022f);
            affinityDetailNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
            affinityDetailNoise.SetFractalOctaves(2);
        }

        public TissueNetwork Generate(WorldMap worldMap)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            TissueGenerationState state = CreateGenerationState(worldMap);
            GenerateTopology(state);
            AddStarterNetwork(state);
            return BuildNetwork(state);
        }

        private TissueGenerationState CreateGenerationState(WorldMap worldMap)
        {
            return new TissueGenerationState
            {
                WorldMap = worldMap,
                Nodes = new List<TissueNode>(),
                Branches = new List<TissueBranch>(),
                WorldBounds = new Rectangle(0, 0, worldMap.Width * worldMap.TileSize, worldMap.Height * worldMap.TileSize)
            };
        }

        private void GenerateTopology(TissueGenerationState state)
        {
            switch (GetTopologyMode())
            {
                case TissueTopologyMode.Colonies:
                default:
                    GenerateLegacyBandTopology(state);
                    break;
            }
        }

        private TissueTopologyMode GetTopologyMode()
        {
            return TissueTopologyMode.Colonies;
        }

        private void GenerateLegacyBandTopology(TissueGenerationState state)
        {
            ColonySpec cavernColonies = new(
                LayerType: WorldLayerType.Cavern,
                ColonyCount: Math.Max(3, state.WorldMap.Width / 320),
                MinDepthRatio: 0.08f,
                MaxDepthRatio: 0.52f,
                RadiusTilesMin: 7f,
                RadiusTilesMax: 13f,
                NodeCountMin: 4,
                NodeCountMax: 7,
                PrimaryStrength: 0.80f,
                SecondaryStrength: 0.48f,
                PrimaryThickness: 1.00f,
                SecondaryThickness: 0.72f,
                CurveAmplitude: state.WorldMap.TileSize * 2.2f,
                BridgeChance: 0.74f);

            ColonySpec deepColonies = new(
                LayerType: WorldLayerType.DeepCavern,
                ColonyCount: Math.Max(9, state.WorldMap.Width / 125),
                MinDepthRatio: 0.06f,
                MaxDepthRatio: 0.94f,
                RadiusTilesMin: 16f,
                RadiusTilesMax: 30f,
                NodeCountMin: 13,
                NodeCountMax: 24,
                PrimaryStrength: 1.36f,
                SecondaryStrength: 0.96f,
                PrimaryThickness: 2.15f,
                SecondaryThickness: 1.30f,
                CurveAmplitude: state.WorldMap.TileSize * 4.1f,
                BridgeChance: 0.34f);

            List<List<TissueNode>> cavernGroups = GenerateColonies(state, cavernColonies, seedOffset: 0);
            List<List<TissueNode>> deepGroups = GenerateColonies(state, deepColonies, seedOffset: 1000);

            ConnectColonyGroups(state, cavernGroups, cavernColonies);
            ConnectColonyGroups(state, deepGroups, deepColonies);
            ConnectNearbyColonies(state, cavernGroups, state.WorldMap.TileSize * 16f, 0.82f, state.WorldMap.TileSize * 1.9f, cavernColonies.BridgeChance);
            ConnectNearbyColonies(state, deepGroups, state.WorldMap.TileSize * 26f, 1.35f, state.WorldMap.TileSize * 2.9f, deepColonies.BridgeChance);
            ConnectNearbyColonies(state, cavernGroups, deepGroups, state.WorldMap.TileSize * 18f, 0.96f, state.WorldMap.TileSize * 2.2f, 0.78f);
        }

        private TissueNetwork BuildNetwork(TissueGenerationState state)
        {
            return new TissueNetwork(seed, state.WorldBounds, state.Nodes, state.Branches);
        }

        private List<List<TissueNode>> GenerateColonies(TissueGenerationState state, ColonySpec spec, int seedOffset)
        {
            List<List<TissueNode>> groups = new();

            for (int colonyIndex = 0; colonyIndex < spec.ColonyCount; colonyIndex++)
            {
                ColonySeed seed = CreateColonySeed(state, spec, colonyIndex, seedOffset);
                if (!ShouldAcceptColonySeed(state, seed))
                    continue;

                List<TissueNode> colonyNodes = GenerateColonyNodes(state, spec, seed, colonyIndex, seedOffset);
                if (colonyNodes.Count > 0)
                    groups.Add(colonyNodes);
            }

            return groups;
        }

        private ColonySeed CreateColonySeed(TissueGenerationState state, ColonySpec spec, int colonyIndex, int seedOffset)
        {
            WorldLayerDefinition layer = state.WorldMap.Height > 0
                ? GetLayerForType(state, spec.LayerType)
                : default;

            int margin = Math.Max(10, state.WorldMap.Width / 18);
            float normalized = spec.ColonyCount <= 1 ? 0.5f : (float)colonyIndex / (spec.ColonyCount - 1);
            float baseTileX = MathHelper.Lerp(margin, state.WorldMap.Width - margin - 1, normalized);
            float xNoise = nodeNoise.GetNoise((colonyIndex + seedOffset) * 17.3f, spec.MinDepthRatio * 100f);
            int tileX = Math.Clamp((int)MathF.Round(baseTileX + (xNoise * state.WorldMap.Width * 0.06f)), margin, state.WorldMap.Width - margin - 1);

            int minTileY = layer.StartY + (int)MathF.Round(layer.Height * spec.MinDepthRatio);
            int maxTileY = layer.StartY + (int)MathF.Round(layer.Height * spec.MaxDepthRatio);
            minTileY = Math.Clamp(minTileY, layer.StartY, layer.EndY);
            maxTileY = Math.Clamp(Math.Max(minTileY + 4, maxTileY), minTileY + 1, layer.EndY);

            float yBiasNoise = MathF.Abs(nodeNoise.GetNoise(tileX * 0.8f, (seedOffset + colonyIndex) * 31.7f));
            int biasedMinY = minTileY + (int)MathF.Round((maxTileY - minTileY) * yBiasNoise * 0.25f);
            int centerTileY = FindBestTissueTileY(state.WorldMap, tileX, biasedMinY, maxTileY);

            float radiusNoise = MathF.Abs(shapeNoise.GetNoise(tileX * 0.25f, centerTileY * 0.25f + seedOffset));
            float radiusTiles = MathHelper.Lerp(spec.RadiusTilesMin, spec.RadiusTilesMax, radiusNoise);
            float strengthNoise = MathF.Abs(branchNoise.GetNoise(tileX * 0.15f, centerTileY * 0.15f + seedOffset));
            float strength = MathHelper.Lerp(spec.PrimaryStrength * 0.88f, spec.PrimaryStrength * 1.12f, strengthNoise);
            Vector2 center = state.WorldMap.GetTileCenter(tileX, centerTileY);
            float affinity = SampleAffinity(state, center, spec.LayerType);

            radiusTiles *= MathHelper.Lerp(0.78f, 1.24f, affinity);
            strength *= MathHelper.Lerp(0.72f, 1.32f, affinity);

            return new ColonySeed(
                Center: center,
                RadiusPixels: radiusTiles * state.WorldMap.TileSize,
                Strength: strength,
                IsPrimary: spec.LayerType == WorldLayerType.DeepCavern,
                LayerType: spec.LayerType);
        }

        private List<TissueNode> GenerateColonyNodes(TissueGenerationState state, ColonySpec spec, ColonySeed seed, int colonyIndex, int seedOffset)
        {
            List<TissueNode> colonyNodes = new();
            float seedAffinity = SampleAffinity(state, seed.Center, seed.LayerType);
            int nodeCount = spec.NodeCountMin + (int)MathF.Round(MathF.Abs(nodeNoise.GetNoise((seedOffset + colonyIndex) * 12.4f, 77.1f)) * (spec.NodeCountMax - spec.NodeCountMin));
            nodeCount = Math.Max(1, (int)MathF.Round(nodeCount * MathHelper.Lerp(0.65f, 1.45f, seedAffinity)));

            TissueNode colonyCore = CreateNode(state.Nodes, seed.Center, isPrimary: true, seed.Strength);
            colonyNodes.Add(colonyCore);

            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                float angleNoise = nodeNoise.GetNoise((seedOffset + colonyIndex) * 9.1f, nodeIndex * 13.7f);
                float angle = (angleNoise + 1f) * MathF.PI;
                float distanceNoise = MathF.Abs(shapeNoise.GetNoise(nodeIndex * 7.7f + seedOffset, colonyIndex * 5.9f));
                float distance = MathHelper.Lerp(seed.RadiusPixels * 0.22f, seed.RadiusPixels, distanceNoise);

                float offsetX = MathF.Cos(angle) * distance;
                float offsetY = MathF.Sin(angle) * distance * 0.82f;
                float driftX = branchNoise.GetNoise((seed.Center.X * 0.02f) + nodeIndex, seed.Center.Y * 0.02f) * state.WorldMap.TileSize * 2.5f;
                float driftY = branchNoise.GetNoise((seed.Center.X * 0.02f) - nodeIndex, seed.Center.Y * 0.02f + 111f) * state.WorldMap.TileSize * 2.0f;

                int tileX = WrapColumn((int)MathF.Round((seed.Center.X + offsetX + driftX) / state.WorldMap.TileSize), state.WorldMap.Width);
                int seedTileY = (int)MathF.Round((seed.Center.Y + offsetY + driftY) / state.WorldMap.TileSize);

                WorldLayerDefinition layer = GetLayerForType(state, seed.LayerType);
                int minTileY = Math.Clamp(seedTileY - 6, layer.StartY, layer.EndY - 1);
                int maxTileY = Math.Clamp(seedTileY + 6, minTileY + 1, layer.EndY);
                int tileY = FindBestTissueTileY(state.WorldMap, tileX, minTileY, maxTileY);
                Vector2 nodePosition = state.WorldMap.GetTileCenter(tileX, tileY);
                float localAffinity = SampleAffinity(state, nodePosition, seed.LayerType);

                if (!ShouldAcceptNode(localAffinity, seed.LayerType, nodeIndex))
                    continue;

                float strength = seed.LayerType == WorldLayerType.DeepCavern
                    ? spec.SecondaryStrength * 1.05f
                    : spec.SecondaryStrength;
                strength *= MathHelper.Lerp(0.72f, 1.20f, localAffinity);

                colonyNodes.Add(CreateNode(
                    state.Nodes,
                    nodePosition,
                    isPrimary: false,
                    strength));
            }

            return colonyNodes;
        }

        private void ConnectColonyGroups(TissueGenerationState state, List<List<TissueNode>> groups, ColonySpec spec)
        {
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                List<TissueNode> colonyNodes = groups[groupIndex];
                if (colonyNodes.Count < 2)
                    continue;

                ConnectColonyByProximity(state, colonyNodes, spec, groupIndex);
            }
        }

        private void ConnectNearbyColonies(TissueGenerationState state, List<List<TissueNode>> groups, float maxBridgeDistance, float thickness, float curveAmplitude, float bridgeNoiseThreshold)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                List<TissueNode> sourceGroup = groups[i];
                for (int j = i + 1; j < groups.Count; j++)
                {
                    List<TissueNode> targetGroup = groups[j];
                    (TissueNode source, TissueNode target, float distance) = FindClosestNodesBetweenGroups(sourceGroup, targetGroup);
                    if (distance > maxBridgeDistance)
                        continue;

                    float bridgeNoise = MathF.Abs(branchNoise.GetNoise(source.Position.X * 0.01f + j, source.Position.Y * 0.01f - i));
                    if (bridgeNoise < bridgeNoiseThreshold)
                        continue;

                    state.Branches.Add(CreateBranch(
                        state.Branches.Count,
                        source,
                        target,
                        isPrimary: true,
                        thickness: thickness,
                        curveAmplitude: curveAmplitude));
                }
            }
        }

        private void ConnectNearbyColonies(TissueGenerationState state, List<List<TissueNode>> sourceGroups, List<List<TissueNode>> targetGroups, float maxBridgeDistance, float thickness, float curveAmplitude, float bridgeNoiseThreshold)
        {
            for (int i = 0; i < sourceGroups.Count; i++)
            {
                List<TissueNode> sourceGroup = sourceGroups[i];
                for (int j = 0; j < targetGroups.Count; j++)
                {
                    List<TissueNode> targetGroup = targetGroups[j];
                    (TissueNode source, TissueNode target, float distance) = FindClosestNodesBetweenGroups(sourceGroup, targetGroup);
                    if (distance > maxBridgeDistance)
                        continue;

                    float bridgeNoise = MathF.Abs(branchNoise.GetNoise(source.Position.X * 0.01f + j + 41.7f, source.Position.Y * 0.01f - i - 19.3f));
                    if (bridgeNoise < bridgeNoiseThreshold)
                        continue;

                    state.Branches.Add(CreateBranch(
                        state.Branches.Count,
                        source,
                        target,
                        isPrimary: true,
                        thickness: thickness,
                        curveAmplitude: curveAmplitude));
                }
            }
        }

        private void ConnectColonyByProximity(TissueGenerationState state, List<TissueNode> colonyNodes, ColonySpec spec, int groupIndex)
        {
            HashSet<long> existingEdges = new();
            float radius = GetColonyConnectionRadius(colonyNodes, spec);
            int preferredNeighbors = spec.LayerType == WorldLayerType.DeepCavern ? 4 : 2;

            for (int sourceIndex = 0; sourceIndex < colonyNodes.Count; sourceIndex++)
            {
                TissueNode source = colonyNodes[sourceIndex];
                List<TissueNode> neighbors = colonyNodes
                    .Where((candidate, candidateIndex) => candidateIndex != sourceIndex)
                    .Select(candidate => new { Node = candidate, DistanceSq = Vector2.DistanceSquared(source.Position, candidate.Position) })
                    .Where(entry => entry.DistanceSq <= radius * radius)
                    .OrderBy(entry => entry.DistanceSq)
                    .Take(preferredNeighbors + 2)
                    .Select(entry => entry.Node)
                    .ToList();

                if (neighbors.Count == 0)
                    continue;

                int connected = 0;
                for (int neighborIndex = 0; neighborIndex < neighbors.Count; neighborIndex++)
                {
                    TissueNode target = neighbors[neighborIndex];
                    if (!ShouldCreateLocalConnection(source, target, spec, groupIndex, connected, neighborIndex))
                        continue;

                    long edgeKey = GetEdgeKey(source.Id, target.Id);
                    if (!existingEdges.Add(edgeKey))
                        continue;

                    bool isPrimary = source.IsPrimary || target.IsPrimary;
                    float thickness = isPrimary ? spec.PrimaryThickness : spec.SecondaryThickness;
                    state.Branches.Add(CreateBranch(
                        state.Branches.Count,
                        source,
                        target,
                        isPrimary,
                        thickness,
                        spec.CurveAmplitude));

                    connected++;
                    if (connected >= preferredNeighbors)
                        break;
                }
            }
        }

        private bool ShouldCreateLocalConnection(TissueNode source, TissueNode target, ColonySpec spec, int groupIndex, int connectedCount, int neighborRank)
        {
            if (neighborRank == 0)
                return true;

            float noise = MathF.Abs(branchNoise.GetNoise(
                (source.Position.X + target.Position.X) * 0.014f + groupIndex,
                (source.Position.Y + target.Position.Y) * 0.014f - groupIndex));

            float threshold = spec.LayerType == WorldLayerType.DeepCavern
                ? (connectedCount == 0 ? 0.16f : 0.34f)
                : (connectedCount == 0 ? 0.42f : 0.68f);

            return noise >= threshold;
        }

        private float GetColonyConnectionRadius(List<TissueNode> colonyNodes, ColonySpec spec)
        {
            if (colonyNodes.Count < 2)
                return spec.CurveAmplitude * 1.8f;

            TissueNode core = colonyNodes[0];
            float totalDistance = 0f;
            int count = 0;

            for (int i = 1; i < colonyNodes.Count; i++)
            {
                totalDistance += Vector2.Distance(core.Position, colonyNodes[i].Position);
                count++;
            }

            float averageDistance = count > 0 ? totalDistance / count : spec.CurveAmplitude * 1.5f;
            float radiusMultiplier = spec.LayerType == WorldLayerType.DeepCavern ? 1.30f : 0.84f;
            return Math.Max(spec.CurveAmplitude * 1.25f, averageDistance * radiusMultiplier);
        }

        private (TissueNode Source, TissueNode Target, float Distance) FindClosestNodesBetweenGroups(List<TissueNode> sourceGroup, List<TissueNode> targetGroup)
        {
            TissueNode bestSource = sourceGroup[0];
            TissueNode bestTarget = targetGroup[0];
            float bestDistanceSq = float.MaxValue;

            for (int i = 0; i < sourceGroup.Count; i++)
            {
                TissueNode source = sourceGroup[i];
                for (int j = 0; j < targetGroup.Count; j++)
                {
                    TissueNode target = targetGroup[j];
                    float distanceSq = Vector2.DistanceSquared(source.Position, target.Position);
                    if (distanceSq >= bestDistanceSq)
                        continue;

                    bestDistanceSq = distanceSq;
                    bestSource = source;
                    bestTarget = target;
                }
            }

            return (bestSource, bestTarget, MathF.Sqrt(bestDistanceSq));
        }

        private long GetEdgeKey(int a, int b)
        {
            int min = Math.Min(a, b);
            int max = Math.Max(a, b);
            return ((long)min << 32) | (uint)max;
        }

        private bool ShouldAcceptColonySeed(TissueGenerationState state, ColonySeed seed)
        {
            float affinity = SampleAffinity(state, seed.Center, seed.LayerType);
            float threshold = seed.LayerType == WorldLayerType.DeepCavern ? 0.24f : 0.46f;
            return affinity >= threshold;
        }

        private bool ShouldAcceptNode(float affinity, WorldLayerType layerType, int nodeIndex)
        {
            float threshold = layerType == WorldLayerType.DeepCavern ? 0.18f : 0.42f;
            float bias = MathF.Abs(nodeNoise.GetNoise(nodeIndex * 2.7f, affinity * 19.3f)) * 0.18f;
            return affinity + bias >= threshold;
        }

        private TissueBranch CreateBranch(int id, TissueNode start, TissueNode end, bool isPrimary, float thickness, float curveAmplitude)
        {
            Vector2 delta = end.Position - start.Position;
            float distance = delta.Length();
            int pointCount = Math.Max(6, (int)(distance / 20f));
            List<Vector2> points = new(pointCount + 2)
            {
                start.Position
            };

            Vector2 direction = delta == Vector2.Zero ? Vector2.UnitX : Vector2.Normalize(delta);
            Vector2 normal = new Vector2(-direction.Y, direction.X);

            for (int i = 1; i < pointCount; i++)
            {
                float t = (float)i / pointCount;
                Vector2 point = Vector2.Lerp(start.Position, end.Position, t);
                float envelope = MathF.Sin(t * MathF.PI);
                float swayNoise = shapeNoise.GetNoise((point.X * 0.65f) + (id * 0.17f), (point.Y * 0.65f) - (id * 0.11f));
                float driftNoise = branchNoise.GetNoise((point.X * 0.35f) + (id * 0.21f), (point.Y * 0.35f) + id * 7.3f);
                float roughNoise = branchNoise.GetNoise((point.X * 0.95f) - (id * 0.13f), (point.Y * 0.95f) + (t * 19.7f));
                float pocketNoise = shapeNoise.GetNoise((point.X * 0.18f) + 91.4f, (point.Y * 0.18f) - 37.8f);

                float localCurveAmplitude = curveAmplitude * MathHelper.Lerp(0.72f, 1.18f, (pocketNoise + 1f) * 0.5f);
                float tangentialDrift = driftNoise * localCurveAmplitude * 0.16f * envelope;
                float lateralSway = swayNoise * localCurveAmplitude * envelope;
                float irregularity = roughNoise * localCurveAmplitude * 0.22f * envelope;

                point += normal * lateralSway;
                point += direction * tangentialDrift;
                point += new Vector2(normal.X + direction.X, normal.Y + direction.Y) * irregularity * 0.5f;
                points.Add(point);
            }

            points.Add(end.Position);
            List<Vector2> smoothed = SmoothPath(points, isPrimary ? 1 : 0);
            List<Vector2> refined = ApplyBranchIrregularity(smoothed, id, curveAmplitude, isPrimary);
            return new TissueBranch(id, start.Id, end.Id, isPrimary, thickness, refined);
        }

        private List<Vector2> SmoothPath(List<Vector2> points, int iterations)
        {
            List<Vector2> current = new(points);

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                if (current.Count < 3)
                    break;

                List<Vector2> next = new(current.Count * 2)
                {
                    current[0]
                };

                for (int i = 0; i < current.Count - 1; i++)
                {
                    Vector2 a = current[i];
                    Vector2 b = current[i + 1];
                    next.Add(Vector2.Lerp(a, b, 0.25f));
                    next.Add(Vector2.Lerp(a, b, 0.75f));
                }

                next.Add(current[^1]);
                current = next;
            }

            return current;
        }

        private List<Vector2> ApplyBranchIrregularity(List<Vector2> points, int branchId, float curveAmplitude, bool isPrimary)
        {
            if (points.Count <= 2)
                return points;

            List<Vector2> refined = new(points.Count)
            {
                points[0]
            };

            float amplitude = isPrimary ? curveAmplitude * 0.18f : curveAmplitude * 0.24f;

            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector2 previous = points[i - 1];
                Vector2 current = points[i];
                Vector2 next = points[i + 1];
                Vector2 tangent = next - previous;
                if (tangent == Vector2.Zero)
                {
                    refined.Add(current);
                    continue;
                }

                tangent.Normalize();
                Vector2 normal = new Vector2(-tangent.Y, tangent.X);
                float t = i / (float)(points.Count - 1);
                float envelope = MathF.Sin(t * MathF.PI);
                float micro = branchNoise.GetNoise((current.X * 1.4f) + (branchId * 0.31f), (current.Y * 1.4f) - (branchId * 0.19f));
                float wobble = shapeNoise.GetNoise((current.X * 0.42f) - (branchId * 0.07f), (current.Y * 0.42f) + (branchId * 0.13f));

                Vector2 offset = normal * micro * amplitude * envelope;
                offset += tangent * wobble * amplitude * 0.12f * envelope;
                refined.Add(current + offset);
            }

            refined.Add(points[^1]);
            return refined;
        }

        private int FindSurfaceTileY(WorldMap worldMap, int tileX)
        {
            for (int y = 0; y < worldMap.Height; y++)
            {
                if (worldMap.IsSolidAt(tileX, y))
                    return y;
            }

            return worldMap.Height / 2;
        }

        private int FindBestTissueTileY(WorldMap worldMap, int tileX, int minTileY, int maxTileY)
        {
            int bestY = Math.Clamp(minTileY, 0, worldMap.Height - 1);
            float bestScore = float.MinValue;

            for (int y = minTileY; y <= maxTileY; y++)
            {
                float openness = CountOpenTilesAround(worldMap, tileX, y, radiusX: 3, radiusY: 2);
                float depthScore = y * 0.08f;
                float noiseBias = MathF.Abs(shapeNoise.GetNoise(tileX * 0.75f, y * 0.75f)) * 1.4f;
                float score = openness + depthScore + noiseBias;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestY = y;
            }

            return bestY;
        }

        private float CountOpenTilesAround(WorldMap worldMap, int centerX, int centerY, int radiusX, int radiusY)
        {
            float openness = 0f;

            for (int y = centerY - radiusY; y <= centerY + radiusY; y++)
            {
                if (y < 0 || y >= worldMap.Height)
                    continue;

                for (int x = centerX - radiusX; x <= centerX + radiusX; x++)
                {
                    float normalizedX = (x - centerX) / (float)Math.Max(1, radiusX);
                    float normalizedY = (y - centerY) / (float)Math.Max(1, radiusY);
                    if ((normalizedX * normalizedX) + (normalizedY * normalizedY) > 1f)
                        continue;

                    if (!worldMap.IsSolidAt(x, y))
                        openness += 1f;
                }
            }

            return openness;
        }

        private int WrapColumn(int x, int width)
        {
            int wrapped = x % width;
            return wrapped < 0 ? wrapped + width : wrapped;
        }

        private float SampleAffinity(TissueGenerationState state, Vector2 worldPosition, WorldLayerType layerType)
        {
            float macro = (affinityNoise.GetNoise(worldPosition.X, worldPosition.Y) + 1f) * 0.5f;
            float detail = (affinityDetailNoise.GetNoise(worldPosition.X, worldPosition.Y) + 1f) * 0.5f;
            float combined = (macro * 0.72f) + (detail * 0.28f);
            float normalizedDepth = Math.Clamp(worldPosition.Y / Math.Max(1f, state.WorldBounds.Height), 0f, 1f);
            float depthBias = layerType switch
            {
                WorldLayerType.DeepCavern => MathHelper.Lerp(0.16f, 0.34f, normalizedDepth),
                WorldLayerType.Cavern => MathHelper.Lerp(-0.06f, 0.08f, normalizedDepth),
                _ => 0f
            };

            return Math.Clamp(combined + depthBias, 0f, 1f);
        }

        private WorldLayerDefinition GetLayerForType(TissueGenerationState state, WorldLayerType layerType)
        {
            return FindLayerDefinition(state.WorldMap, layerType);
        }

        private WorldLayerDefinition FindLayerDefinition(WorldMap worldMap, WorldLayerType layerType)
        {
            int worldHeight = worldMap.Height;
            int lastRow = worldHeight - 1;
            int spaceEnd = ClampBoundary((int)MathF.Round(worldHeight * 0.12f), 0, lastRow - 4);
            int surfaceEnd = ClampBoundary((int)MathF.Round(worldHeight * 0.22f), spaceEnd + 1, lastRow - 3);
            int shallowEnd = ClampBoundary((int)MathF.Round(worldHeight * 0.30f), surfaceEnd + 1, lastRow - 2);
            int cavernEnd = ClampBoundary((int)MathF.Round(worldHeight * 0.85f), shallowEnd + 1, lastRow - 1);

            return layerType switch
            {
                WorldLayerType.Space => new WorldLayerDefinition(WorldLayerType.Space, 0, spaceEnd),
                WorldLayerType.Surface => new WorldLayerDefinition(WorldLayerType.Surface, spaceEnd + 1, surfaceEnd),
                WorldLayerType.ShallowUnderground => new WorldLayerDefinition(WorldLayerType.ShallowUnderground, surfaceEnd + 1, shallowEnd),
                WorldLayerType.Cavern => new WorldLayerDefinition(WorldLayerType.Cavern, shallowEnd + 1, cavernEnd),
                _ => new WorldLayerDefinition(WorldLayerType.DeepCavern, cavernEnd + 1, lastRow)
            };
        }

        private int ClampBoundary(int value, int min, int max)
        {
            return Math.Clamp(value, min, max);
        }

        private void AddStarterNetwork(TissueGenerationState state)
        {
            int entryTileX = FindStarterEntryTileX(state.WorldMap);
            int surfaceY = FindSurfaceTileY(state.WorldMap, entryTileX);
            int chamberTileY = FindBestTissueTileY(
                state.WorldMap,
                entryTileX + 24,
                surfaceY + 12,
                Math.Min(state.WorldMap.Height - 6, surfaceY + Math.Max(28, state.WorldMap.Height / 18)));

            TissueNode entryNode = CreateNode(
                state.Nodes,
                state.WorldMap.GetTileCenter(WrapColumn(entryTileX + 4, state.WorldMap.Width), surfaceY + 8),
                isPrimary: true,
                strength: 1f);

            TissueNode chamberNode = CreateNode(
                state.Nodes,
                state.WorldMap.GetTileCenter(WrapColumn(entryTileX + 24, state.WorldMap.Width), chamberTileY),
                isPrimary: true,
                strength: 1f);

            TissueNode chamberDeepNode = CreateNode(
                state.Nodes,
                state.WorldMap.GetTileCenter(
                    WrapColumn(entryTileX + 36, state.WorldMap.Width),
                    Math.Min(state.WorldMap.Height - 5, chamberTileY + Math.Max(4, state.WorldMap.Height / 90))),
                isPrimary: false,
                strength: 0.78f);

            TissueNode chamberSideNode = CreateNode(
                state.Nodes,
                state.WorldMap.GetTileCenter(
                    WrapColumn(entryTileX + 31, state.WorldMap.Width),
                    Math.Max(surfaceY + 10, chamberTileY - 6)),
                isPrimary: false,
                strength: 0.68f);

            state.Branches.Add(CreateBranch(
                state.Branches.Count,
                entryNode,
                chamberNode,
                isPrimary: true,
                thickness: 1.95f,
                curveAmplitude: state.WorldMap.TileSize * 4.25f));

            state.Branches.Add(CreateBranch(
                state.Branches.Count,
                chamberNode,
                chamberDeepNode,
                isPrimary: false,
                thickness: 1.15f,
                curveAmplitude: state.WorldMap.TileSize * 2.6f));

            state.Branches.Add(CreateBranch(
                state.Branches.Count,
                chamberNode,
                chamberSideNode,
                isPrimary: false,
                thickness: 1.05f,
                curveAmplitude: state.WorldMap.TileSize * 2.2f));
        }

        private int FindStarterEntryTileX(WorldMap worldMap)
        {
            int searchStart = 12;
            int searchEnd = Math.Min(worldMap.Width - 12, Math.Max(64, worldMap.Width / 8));
            int bestX = Math.Min(24, worldMap.Width - 12);
            float bestScore = float.MinValue;

            for (int x = searchStart; x < searchEnd; x++)
            {
                int surfaceY = FindSurfaceTileY(worldMap, x);
                float opennessBelow = CountOpenTilesAround(worldMap, x, Math.Min(worldMap.Height - 4, surfaceY + 8), radiusX: 3, radiusY: 5);
                float elevationScore = (worldMap.Height - surfaceY) * 0.15f;
                float score = opennessBelow + elevationScore;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestX = x;
            }

            return bestX;
        }

        private static TissueNode CreateNode(List<TissueNode> nodes, Vector2 position, bool isPrimary, float strength)
        {
            TissueNode node = new TissueNode(nodes.Count, position, isPrimary, strength);
            nodes.Add(node);
            return node;
        }
    }
}
