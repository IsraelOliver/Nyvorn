using System;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class SubterraneanDetailPass : IWorldGenPass
    {
        private const int CavernDetailSpacing = 8;
        private const int CavernDetailSearchRadius = 24;
        private const int CavernBranchLengthMin = 6;
        private const int CavernBranchLengthMax = 14;
        private const int CavernBranchCountMin = 3;
        private const int CavernBranchCountMax = 5;

        private const int DepthsDetailSpacing = 11;
        private const int DepthsDetailSearchRadius = 28;
        private const int DepthsBranchLengthMin = 7;
        private const int DepthsBranchLengthMax = 16;
        private const int DepthsBranchCountMin = 3;
        private const int DepthsBranchCountMax = 5;

        private const int ShallowPocketDivisor = 140;

        public string Name => "SubterraneanDetail";

        public void Apply(WorldGenContext context)
        {
            // Build dense micro-variation around the existing connected network.
            ExpandAroundExistingVoids(context, WorldDepthLayer.Cavern, spacing: CavernDetailSpacing, searchRadius: CavernDetailSearchRadius, branchLengthMin: CavernBranchLengthMin, branchLengthMax: CavernBranchLengthMax, branchCountMin: CavernBranchCountMin, branchCountMax: CavernBranchCountMax);
            ExpandAroundExistingVoids(context, WorldDepthLayer.Depths, spacing: DepthsDetailSpacing, searchRadius: DepthsDetailSearchRadius, branchLengthMin: DepthsBranchLengthMin, branchLengthMax: DepthsBranchLengthMax, branchCountMin: DepthsBranchCountMin, branchCountMax: DepthsBranchCountMax);

            // Add a few tiny shallow signals without turning the transition into a cave layer.
            AddShallowTransitionPockets(context);
        }

        private void ExpandAroundExistingVoids(
            WorldGenContext context,
            WorldDepthLayer layer,
            int spacing,
            int searchRadius,
            int branchLengthMin,
            int branchLengthMax,
            int branchCountMin,
            int branchCountMax)
        {
            Random random = new Random(context.Settings.Seed + (layer == WorldDepthLayer.Cavern ? 1501 : 1502));
            int attemptCount = Math.Max(48, context.WorldMap.Width / spacing);

            for (int attempt = 0; attempt < attemptCount; attempt++)
            {
                int x = (attempt * context.WorldMap.Width) / attemptCount;
                x = WrapColumn(x + random.Next(-spacing * 2, (spacing * 2) + 1), context.WorldMap.Width);
                int targetY = GetTargetY(context, x, layer, attempt);
                Point? anchor = FindNearestEmptyAnchor(context, x, targetY, layer, searchRadius);
                if (!anchor.HasValue)
                    continue;

                Point center = anchor.Value;
                int branchCount = random.Next(branchCountMin, branchCountMax + 1);

                // A small local pocket gives each anchor more readable mass.
                CarveEllipseBounded(context, center.X, center.Y, random.Next(2, 5), random.Next(2, 4), layer);

                for (int branchIndex = 0; branchIndex < branchCount; branchIndex++)
                {
                    float angle = GetOrganicBranchAngle(random, layer);

                    int length = random.Next(branchLengthMin, branchLengthMax + 1);
                    Point end = new Point(
                        WrapColumn(center.X + (int)MathF.Round(MathF.Cos(angle) * length), context.WorldMap.Width),
                        center.Y + (int)MathF.Round(MathF.Sin(angle) * length * 0.75f));

                    end.Y = ClampToLayer(context, end.X, end.Y, layer);
                    CarveTunnelBounded(context, center, end, layer, radiusX: 1, radiusY: 1);

                    if (random.NextDouble() < 0.78)
                        CarveEllipseBounded(context, end.X, end.Y, random.Next(2, 5), random.Next(2, 4), layer);
                }
            }
        }

        private float GetOrganicBranchAngle(Random random, WorldDepthLayer layer)
        {
            float angle;
            int attempts = 0;

            do
            {
                angle = random.NextSingle() * MathHelper.TwoPi;
                attempts++;
            }
            while (attempts < 10 && MathF.Abs(MathF.Sin(angle)) < (layer == WorldDepthLayer.Depths ? 0.22f : 0.36f));

            return angle;
        }

        private void AddShallowTransitionPockets(WorldGenContext context)
        {
            Random random = new Random(context.Settings.Seed + 1503);
            int pocketCount = Math.Max(20, context.WorldMap.Width / ShallowPocketDivisor);

            for (int i = 0; i < pocketCount; i++)
            {
                int x = (i * context.WorldMap.Width) / pocketCount;
                x = WrapColumn(x + random.Next(-24, 25), context.WorldMap.Width);
                int surfaceY = context.LayerProfile.GetSurfaceY(x);
                int y = surfaceY + context.Settings.ShallowUndergroundDepth - random.Next(2, 8);
                Point? anchor = FindNearestEmptyAnchor(context, x, y, WorldDepthLayer.ShallowUnderground, 10);
                if (!anchor.HasValue)
                    continue;

                Point start = anchor.Value;
                Point end = new Point(
                    WrapColumn(start.X + random.Next(-4, 5), context.WorldMap.Width),
                    start.Y - random.Next(2, 7));

                end.Y = ClampToLayer(context, end.X, end.Y, WorldDepthLayer.ShallowUnderground);
                CarveTunnelBounded(context, start, end, WorldDepthLayer.ShallowUnderground, radiusX: 1, radiusY: 1);

                if (random.NextDouble() < 0.35)
                    CarveEllipseBounded(context, end.X, end.Y, 1, 1, WorldDepthLayer.ShallowUnderground);
            }
        }

        private Point? FindNearestEmptyAnchor(WorldGenContext context, int centerX, int centerY, WorldDepthLayer preferredLayer, int searchRadius)
        {
            int bestScore = int.MaxValue;
            Point best = default;
            bool found = false;

            for (int offsetY = -searchRadius; offsetY <= searchRadius; offsetY++)
            {
                int y = centerY + offsetY;
                if (y < 0 || y >= context.WorldMap.Height)
                    continue;

                for (int offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
                {
                    int x = WrapColumn(centerX + offsetX, context.WorldMap.Width);
                    if (context.WorldMap.GetTile(x, y) != TileType.Empty)
                        continue;

                    WorldDepthLayer layer = context.LayerProfile.GetLayerAt(x, y);
                    if (layer != preferredLayer)
                        continue;

                    int score = Math.Abs(offsetX) + Math.Abs(offsetY);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = new Point(x, y);
                        found = true;
                    }
                }
            }

            return found ? best : null;
        }

        private int GetTargetY(WorldGenContext context, int x, WorldDepthLayer layer, int attempt)
        {
            int surfaceY = context.LayerProfile.GetSurfaceY(x);
            float noiseA = context.CaveNoise.GetNoise((x * 0.013f) + (attempt * 0.17f), attempt * 1.37f);
            float noiseB = context.CaveRoomNoise.GetNoise((x * 0.009f) - (attempt * 0.11f), attempt * 0.73f);
            float blend = ((noiseA + noiseB) * 0.5f + 1f) * 0.5f;

            if (layer == WorldDepthLayer.Cavern)
            {
                int top = surfaceY + context.Settings.CavernLayerDepth + 8;
                int bottom = surfaceY + context.Settings.DepthsLayerDepth - 28;
                int y = top + (int)MathF.Round((bottom - top) * blend);
                return Math.Clamp(y, top, Math.Max(top, bottom));
            }

            int minDepthY = surfaceY + context.Settings.DepthsLayerDepth + 18;
            int maxDepthY = context.WorldMap.Height - 22;
            int depthY = minDepthY + (int)MathF.Round((maxDepthY - minDepthY) * blend);
            return Math.Clamp(depthY, minDepthY, Math.Max(minDepthY, maxDepthY));
        }

        private int ClampToLayer(WorldGenContext context, int x, int y, WorldDepthLayer layer)
        {
            int surfaceY = context.LayerProfile.GetSurfaceY(x);

            return layer switch
            {
                WorldDepthLayer.ShallowUnderground => Math.Clamp(
                    y,
                    surfaceY + context.Settings.SurfaceTopsoilDepth + 2,
                    surfaceY + context.Settings.ShallowUndergroundDepth - 2),
                WorldDepthLayer.Cavern => Math.Clamp(
                    y,
                    surfaceY + context.Settings.CavernLayerDepth + 4,
                    surfaceY + context.Settings.DepthsLayerDepth - 8),
                _ => Math.Clamp(
                    y,
                    surfaceY + context.Settings.DepthsLayerDepth + 6,
                    context.WorldMap.Height - 12)
            };
        }

        private void CarveTunnelBounded(WorldGenContext context, Point start, Point end, WorldDepthLayer targetLayer, int radiusX, int radiusY)
        {
            Vector2 startPoint = new Vector2(start.X, start.Y);
            Vector2 endPoint = new Vector2(end.X, end.Y);
            float distance = Vector2.Distance(startPoint, endPoint);
            int steps = Math.Max(1, (int)MathF.Ceiling(distance * 1.2f));

            for (int step = 0; step <= steps; step++)
            {
                float t = step / (float)steps;
                int x = WrapColumn((int)MathF.Round(MathHelper.Lerp(start.X, end.X, t)), context.WorldMap.Width);
                int y = (int)MathF.Round(MathHelper.Lerp(start.Y, end.Y, t));
                CarveEllipseBounded(context, x, y, radiusX, radiusY, targetLayer);
            }
        }

        private void CarveEllipseBounded(WorldGenContext context, int centerX, int centerY, int radiusX, int radiusY, WorldDepthLayer targetLayer)
        {
            for (int y = centerY - radiusY; y <= centerY + radiusY; y++)
            {
                if (y < 0 || y >= context.WorldMap.Height)
                    continue;

                for (int x = centerX - radiusX; x <= centerX + radiusX; x++)
                {
                    float normalizedX = (x - centerX) / (float)Math.Max(1, radiusX);
                    float normalizedY = (y - centerY) / (float)Math.Max(1, radiusY);
                    if ((normalizedX * normalizedX) + (normalizedY * normalizedY) > 1f)
                        continue;

                    int wrappedX = WrapColumn(x, context.WorldMap.Width);
                    if (context.LayerProfile.GetLayerAt(wrappedX, y) != targetLayer)
                        continue;

                    context.WorldMap.SetTile(wrappedX, y, TileType.Empty);
                }
            }
        }

        private static int WrapColumn(int x, int width)
        {
            int wrapped = x % width;
            return wrapped < 0 ? wrapped + width : wrapped;
        }
    }
}
