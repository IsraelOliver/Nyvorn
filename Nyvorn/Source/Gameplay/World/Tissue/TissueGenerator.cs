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
            List<TissueNode> primaryNodes = GeneratePrimaryNodes(worldMap, nodes);

            for (int i = 0; i < primaryNodes.Count - 1; i++)
            {
                TissueNode start = primaryNodes[i];
                TissueNode end = primaryNodes[i + 1];
                branches.Add(CreateBranch(branches.Count, start, end, isPrimary: true, thickness: 1.55f, curveAmplitude: worldMap.TileSize * 5f));
            }

            for (int i = 0; i < primaryNodes.Count; i++)
            {
                TissueNode source = primaryNodes[i];
                int branchCount = 1 + (int)MathF.Round(MathF.Abs(branchNoise.GetNoise(source.Position.X, source.Position.Y)) * 2f);
                for (int branchIndex = 0; branchIndex < branchCount; branchIndex++)
                {
                    TissueNode child = CreateSecondaryNode(worldMap, nodes, source, branchIndex);
                    branches.Add(CreateBranch(branches.Count, source, child, isPrimary: false, thickness: 0.85f, curveAmplitude: worldMap.TileSize * 3f));
                }
            }

            AddStarterNetwork(worldMap, nodes, branches);

            Rectangle worldBounds = new Rectangle(0, 0, worldMap.Width * worldMap.TileSize, worldMap.Height * worldMap.TileSize);
            return new TissueNetwork(seed, worldBounds, nodes, branches);
        }

        private List<TissueNode> GeneratePrimaryNodes(WorldMap worldMap, List<TissueNode> nodes)
        {
            List<TissueNode> primaryNodes = new();
            int margin = Math.Max(6, worldMap.Width / 24);
            int primaryCount = Math.Max(5, worldMap.Width / 36);

            for (int index = 0; index < primaryCount; index++)
            {
                float normalized = primaryCount == 1 ? 0.5f : (float)index / (primaryCount - 1);
                float baseTileX = MathHelper.Lerp(margin, worldMap.Width - margin - 1, normalized);
                float jitter = nodeNoise.GetNoise(index * 13.1f, 0f) * 8f;
                int tileX = Math.Clamp((int)MathF.Round(baseTileX + jitter), margin, worldMap.Width - margin - 1);

                int surfaceY = FindSurfaceTileY(worldMap, tileX);
                int depthOffset = 7 + (int)MathF.Round(MathF.Abs(nodeNoise.GetNoise(tileX, 11.7f)) * 8f);
                int tileY = Math.Clamp(surfaceY + depthOffset, 8, worldMap.Height - 8);

                Vector2 position = worldMap.GetTileCenter(tileX, tileY);
                TissueNode node = new TissueNode(nodes.Count, position, isPrimary: true, strength: 1f);
                nodes.Add(node);
                primaryNodes.Add(node);
            }

            return primaryNodes;
        }

        private TissueNode CreateSecondaryNode(WorldMap worldMap, List<TissueNode> nodes, TissueNode source, int branchIndex)
        {
            float directionNoise = branchNoise.GetNoise(source.Position.X * 0.5f + branchIndex * 19f, source.Position.Y * 0.5f);
            float horizontalDirection = directionNoise >= 0f ? 1f : -1f;
            float horizontalTiles = 8f + MathF.Abs(directionNoise) * 14f;
            float verticalTiles = 4f + MathF.Abs(branchNoise.GetNoise(source.Position.X, source.Position.Y + branchIndex * 23f)) * 10f;

            float targetTileX = (source.Position.X / worldMap.TileSize) + (horizontalDirection * horizontalTiles);
            float targetTileY = (source.Position.Y / worldMap.TileSize) + verticalTiles;

            int tileX = Math.Clamp((int)MathF.Round(targetTileX), 2, worldMap.Width - 3);
            int tileY = Math.Clamp((int)MathF.Round(targetTileY), 4, worldMap.Height - 5);
            int surfaceY = FindSurfaceTileY(worldMap, tileX);
            tileY = Math.Max(tileY, surfaceY + 4);

            TissueNode node = new TissueNode(
                nodes.Count,
                worldMap.GetTileCenter(tileX, tileY),
                isPrimary: false,
                strength: 0.55f);

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

        private void AddStarterNetwork(WorldMap worldMap, List<TissueNode> nodes, List<TissueBranch> branches)
        {
            int entryTileX = 24;
            int surfaceY = FindSurfaceTileY(worldMap, entryTileX);

            TissueNode entryNode = CreateNode(
                nodes,
                worldMap.GetTileCenter(entryTileX + 6, surfaceY + 8),
                isPrimary: true,
                strength: 1f);

            TissueNode chamberNode = CreateNode(
                nodes,
                worldMap.GetTileCenter(entryTileX + 32, surfaceY + 22),
                isPrimary: true,
                strength: 1f);

            TissueNode chamberDeepNode = CreateNode(
                nodes,
                worldMap.GetTileCenter(entryTileX + 44, surfaceY + 24),
                isPrimary: false,
                strength: 0.78f);

            TissueNode chamberSideNode = CreateNode(
                nodes,
                worldMap.GetTileCenter(entryTileX + 38, surfaceY + 15),
                isPrimary: false,
                strength: 0.68f);

            branches.Add(CreateBranch(branches.Count, entryNode, chamberNode, isPrimary: true, thickness: 1.95f, curveAmplitude: worldMap.TileSize * 4.25f));
            branches.Add(CreateBranch(branches.Count, chamberNode, chamberDeepNode, isPrimary: false, thickness: 1.15f, curveAmplitude: worldMap.TileSize * 2.6f));
            branches.Add(CreateBranch(branches.Count, chamberNode, chamberSideNode, isPrimary: false, thickness: 1.05f, curveAmplitude: worldMap.TileSize * 2.2f));
        }

        private static TissueNode CreateNode(List<TissueNode> nodes, Vector2 position, bool isPrimary, float strength)
        {
            TissueNode node = new TissueNode(nodes.Count, position, isPrimary, strength);
            nodes.Add(node);
            return node;
        }
    }
}
