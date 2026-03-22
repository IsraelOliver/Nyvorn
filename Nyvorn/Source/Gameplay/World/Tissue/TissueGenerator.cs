using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueGenerator
    {
        private readonly int seed;
        private readonly FastNoiseLite nodeNoise;
        private readonly FastNoiseLite shapeNoise;
        private readonly FastNoiseLite branchNoise;

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
        }

        public TissueNetwork Generate(WorldMap worldMap)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            List<TissueNode> nodes = new();
            List<TissueBranch> branches = new();
            List<TissueNode> upperPrimaryNodes = GeneratePrimaryNodes(
                worldMap,
                nodes,
                minDepthRatio: 0.08f,
                maxDepthRatio: 0.22f,
                horizontalJitterTiles: 6f,
                strength: 1f);
            List<TissueNode> deepPrimaryNodes = GeneratePrimaryNodes(
                worldMap,
                nodes,
                minDepthRatio: 0.24f,
                maxDepthRatio: 0.48f,
                horizontalJitterTiles: 4f,
                strength: 1.12f);

            ConnectPrimaryBand(branches, upperPrimaryNodes, worldMap.TileSize * 5f, 1.55f);
            ConnectPrimaryBand(branches, deepPrimaryNodes, worldMap.TileSize * 4.2f, 1.75f);
            ConnectDepthTrunks(branches, upperPrimaryNodes, deepPrimaryNodes, worldMap.TileSize * 3.2f);

            AddSecondaryBranches(worldMap, nodes, branches, upperPrimaryNodes, branchIndexOffset: 0, favorDepths: false);
            AddSecondaryBranches(worldMap, nodes, branches, deepPrimaryNodes, branchIndexOffset: 1000, favorDepths: true);

            AddStarterNetwork(worldMap, nodes, branches);

            Rectangle worldBounds = new Rectangle(0, 0, worldMap.Width * worldMap.TileSize, worldMap.Height * worldMap.TileSize);
            return new TissueNetwork(seed, worldBounds, nodes, branches);
        }

        private List<TissueNode> GeneratePrimaryNodes(
            WorldMap worldMap,
            List<TissueNode> nodes,
            float minDepthRatio,
            float maxDepthRatio,
            float horizontalJitterTiles,
            float strength)
        {
            List<TissueNode> primaryNodes = new();
            int margin = Math.Max(6, worldMap.Width / 24);
            int primaryCount = Math.Max(7, worldMap.Width / 128);

            for (int index = 0; index < primaryCount; index++)
            {
                float normalized = primaryCount == 1 ? 0.5f : (float)index / (primaryCount - 1);
                float baseTileX = MathHelper.Lerp(margin, worldMap.Width - margin - 1, normalized);
                float jitter = nodeNoise.GetNoise(index * 13.1f, minDepthRatio * 100f) * horizontalJitterTiles;
                int tileX = Math.Clamp((int)MathF.Round(baseTileX + jitter), margin, worldMap.Width - margin - 1);

                int surfaceY = FindSurfaceTileY(worldMap, tileX);
                int minDepth = Math.Max(12, (int)MathF.Round(worldMap.Height * minDepthRatio));
                int maxDepth = Math.Max(minDepth + 10, (int)MathF.Round(worldMap.Height * maxDepthRatio));
                minDepth += (int)MathF.Round(MathF.Abs(nodeNoise.GetNoise(tileX, minDepthRatio * 1000f + 11.7f)) * 12f);
                maxDepth += (int)MathF.Round(MathF.Abs(nodeNoise.GetNoise(tileX, maxDepthRatio * 1000f + 29.4f)) * 18f);
                int minTileY = Math.Clamp(surfaceY + minDepth, 8, worldMap.Height - 20);
                int maxTileY = Math.Clamp(surfaceY + maxDepth, minTileY + 8, worldMap.Height - 6);
                int tileY = FindBestTissueTileY(worldMap, tileX, minTileY, maxTileY);

                Vector2 position = worldMap.GetTileCenter(tileX, tileY);
                TissueNode node = new TissueNode(nodes.Count, position, isPrimary: true, strength: strength);
                nodes.Add(node);
                primaryNodes.Add(node);
            }

            return primaryNodes;
        }

        private void ConnectPrimaryBand(List<TissueBranch> branches, List<TissueNode> nodes, float curveAmplitude, float thickness)
        {
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                TissueNode start = nodes[i];
                TissueNode end = nodes[i + 1];
                branches.Add(CreateBranch(branches.Count, start, end, isPrimary: true, thickness: thickness, curveAmplitude: curveAmplitude));
            }
        }

        private void ConnectDepthTrunks(List<TissueBranch> branches, List<TissueNode> upperNodes, List<TissueNode> deepNodes, float curveAmplitude)
        {
            int count = Math.Min(upperNodes.Count, deepNodes.Count);
            for (int i = 0; i < count; i++)
            {
                TissueNode upper = upperNodes[i];
                TissueNode deep = deepNodes[i];
                branches.Add(CreateBranch(branches.Count, upper, deep, isPrimary: true, thickness: 1.25f, curveAmplitude: curveAmplitude));
            }
        }

        private void AddSecondaryBranches(
            WorldMap worldMap,
            List<TissueNode> nodes,
            List<TissueBranch> branches,
            List<TissueNode> sources,
            int branchIndexOffset,
            bool favorDepths)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                TissueNode source = sources[i];
                float branchNoiseValue = MathF.Abs(branchNoise.GetNoise(source.Position.X, source.Position.Y + branchIndexOffset));
                int branchCount = favorDepths
                    ? 2 + (int)MathF.Round(branchNoiseValue)
                    : 1 + (int)MathF.Round(branchNoiseValue * 2f);

                for (int branchIndex = 0; branchIndex < branchCount; branchIndex++)
                {
                    TissueNode child = CreateSecondaryNode(worldMap, nodes, source, branchIndexOffset + i * 7 + branchIndex, favorDepths);
                    float thickness = favorDepths ? 0.95f : 0.85f;
                    float curveAmplitude = favorDepths ? worldMap.TileSize * 2.6f : worldMap.TileSize * 3f;
                    branches.Add(CreateBranch(branches.Count, source, child, isPrimary: false, thickness: thickness, curveAmplitude: curveAmplitude));
                }
            }
        }

        private TissueNode CreateSecondaryNode(WorldMap worldMap, List<TissueNode> nodes, TissueNode source, int branchIndex, bool favorDepths)
        {
            float directionNoise = branchNoise.GetNoise(source.Position.X * 0.5f + branchIndex * 19f, source.Position.Y * 0.5f);
            float horizontalDirection = directionNoise >= 0f ? 1f : -1f;
            float horizontalTiles = favorDepths
                ? 6f + MathF.Abs(directionNoise) * 10f
                : 8f + MathF.Abs(directionNoise) * 14f;
            float verticalTiles = favorDepths
                ? 10f + MathF.Abs(branchNoise.GetNoise(source.Position.X, source.Position.Y + branchIndex * 23f)) * 18f
                : 4f + MathF.Abs(branchNoise.GetNoise(source.Position.X, source.Position.Y + branchIndex * 23f)) * 10f;

            float targetTileX = (source.Position.X / worldMap.TileSize) + (horizontalDirection * horizontalTiles);
            float targetTileY = (source.Position.Y / worldMap.TileSize) + verticalTiles;

            int tileX = WrapColumn((int)MathF.Round(targetTileX), worldMap.Width);
            int surfaceY = FindSurfaceTileY(worldMap, tileX);
            int minTileY = Math.Clamp(Math.Max((int)MathF.Round(targetTileY), surfaceY + 6), 4, worldMap.Height - 12);
            int maxTileY = Math.Clamp(
                minTileY + (favorDepths ? Math.Max(18, worldMap.Height / 20) : Math.Max(12, worldMap.Height / 30)),
                minTileY + 1,
                worldMap.Height - 5);
            int tileY = FindBestTissueTileY(worldMap, tileX, minTileY, maxTileY);

            TissueNode node = new TissueNode(
                nodes.Count,
                worldMap.GetTileCenter(tileX, tileY),
                isPrimary: false,
                strength: favorDepths ? 0.68f : 0.55f);

            nodes.Add(node);
            return node;
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
                float swayNoise = shapeNoise.GetNoise(point.X * 0.65f, point.Y * 0.65f);
                float driftNoise = branchNoise.GetNoise(point.X * 0.35f, point.Y * 0.35f + id * 7.3f);

                point += normal * swayNoise * curveAmplitude * envelope;
                point += direction * driftNoise * curveAmplitude * 0.2f * envelope;
                points.Add(point);
            }

            points.Add(end.Position);
            List<Vector2> smoothed = SmoothPath(points, 2);
            return new TissueBranch(id, start.Id, end.Id, isPrimary, thickness, smoothed);
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

        private void AddStarterNetwork(WorldMap worldMap, List<TissueNode> nodes, List<TissueBranch> branches)
        {
            int entryTileX = FindStarterEntryTileX(worldMap);
            int surfaceY = FindSurfaceTileY(worldMap, entryTileX);
            int chamberTileY = FindBestTissueTileY(worldMap, entryTileX + 24, surfaceY + 12, Math.Min(worldMap.Height - 6, surfaceY + Math.Max(28, worldMap.Height / 18)));

            TissueNode entryNode = CreateNode(
                nodes,
                worldMap.GetTileCenter(WrapColumn(entryTileX + 4, worldMap.Width), surfaceY + 8),
                isPrimary: true,
                strength: 1f);

            TissueNode chamberNode = CreateNode(
                nodes,
                worldMap.GetTileCenter(WrapColumn(entryTileX + 24, worldMap.Width), chamberTileY),
                isPrimary: true,
                strength: 1f);

            TissueNode chamberDeepNode = CreateNode(
                nodes,
                worldMap.GetTileCenter(WrapColumn(entryTileX + 36, worldMap.Width), Math.Min(worldMap.Height - 5, chamberTileY + Math.Max(4, worldMap.Height / 90))),
                isPrimary: false,
                strength: 0.78f);

            TissueNode chamberSideNode = CreateNode(
                nodes,
                worldMap.GetTileCenter(WrapColumn(entryTileX + 31, worldMap.Width), Math.Max(surfaceY + 10, chamberTileY - 6)),
                isPrimary: false,
                strength: 0.68f);

            branches.Add(CreateBranch(branches.Count, entryNode, chamberNode, isPrimary: true, thickness: 1.95f, curveAmplitude: worldMap.TileSize * 4.25f));
            branches.Add(CreateBranch(branches.Count, chamberNode, chamberDeepNode, isPrimary: false, thickness: 1.15f, curveAmplitude: worldMap.TileSize * 2.6f));
            branches.Add(CreateBranch(branches.Count, chamberNode, chamberSideNode, isPrimary: false, thickness: 1.05f, curveAmplitude: worldMap.TileSize * 2.2f));
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
