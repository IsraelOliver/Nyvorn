using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class SurfaceProfilePass : IWorldGenPass
    {
        public string Name => "SurfaceProfile";

        public void Apply(WorldGenContext context)
        {
            int[] heights = new int[context.WorldMap.Width];
            WorldLayerDefinition surfaceLayer = context.GetLayerDefinition(WorldLayerType.Surface);

            for (int x = 0; x < context.WorldMap.Width; x++)
                heights[x] = GetSurfaceTileY(context, surfaceLayer, x);

            LimitAdjacentSurfaceSteps(heights, context.WorldMap.Height, context.Config.MaxSurfaceStepPerColumn);

            for (int pass = 0; pass < context.Config.SurfaceSmoothingPasses; pass++)
                heights = SmoothSurfaceHeights(heights, surfaceLayer, context.WorldMap.Height);

            FlattenSpawnRegion(context, heights, surfaceLayer);
            LimitAdjacentSurfaceSteps(heights, context.WorldMap.Height, context.Config.MaxSurfaceStepPerColumn);

            context.SurfaceHeights = heights;
            context.DebugStats["Surface.MinY"] = GetMinHeight(heights).ToString();
            context.DebugStats["Surface.MaxY"] = GetMaxHeight(heights).ToString();
            context.DebugStats["Surface.CenterY"] = heights[context.WorldMap.Width / 2].ToString();
        }

        private int GetSurfaceTileY(WorldGenContext context, WorldLayerDefinition surfaceLayer, int tileX)
        {
            float warpedX = tileX + (context.SurfaceWarpNoise.GetNoise(tileX, 0f) * context.Config.SurfaceWarpStrength);
            float macroValue = context.SurfaceNoise.GetNoise(warpedX, 0f) * context.Config.SurfaceAmplitude;
            float detailValue = context.SurfaceDetailNoise.GetNoise(warpedX, 0f) * context.Config.SurfaceDetailAmplitude;
            int surfaceY = context.Config.SurfaceBaseHeight + (int)MathF.Round(macroValue + detailValue);
            return ClampSurfaceHeight(surfaceY, surfaceLayer, context.WorldMap.Height);
        }

        private int[] SmoothSurfaceHeights(int[] source, WorldLayerDefinition surfaceLayer, int worldHeight)
        {
            int[] smoothed = new int[source.Length];

            for (int x = 0; x < source.Length; x++)
            {
                int left = source[WrapColumn(x - 1, source.Length)];
                int center = source[x];
                int right = source[WrapColumn(x + 1, source.Length)];
                float weightedAverage = (left * 0.24f) + (center * 0.52f) + (right * 0.24f);
                smoothed[x] = ClampSurfaceHeight((int)MathF.Round(weightedAverage), surfaceLayer, worldHeight);
            }

            return smoothed;
        }

        private void LimitAdjacentSurfaceSteps(int[] heights, int worldHeight, int maxAdjacentSurfaceStep)
        {
            int maxStep = Math.Max(1, maxAdjacentSurfaceStep);

            for (int pass = 0; pass < 3; pass++)
            {
                for (int x = 1; x < heights.Length; x++)
                    heights[x] = ClampStep(heights[x], heights[x - 1], maxStep, worldHeight);

                for (int x = heights.Length - 2; x >= 0; x--)
                    heights[x] = ClampStep(heights[x], heights[x + 1], maxStep, worldHeight);
            }
        }

        private int ClampStep(int current, int neighbor, int maxStep, int worldHeight)
        {
            int min = neighbor - maxStep;
            int max = neighbor + maxStep;
            return ClampSurfaceHeight(Math.Clamp(current, min, max), null, worldHeight);
        }

        private void FlattenSpawnRegion(WorldGenContext context, int[] heights, WorldLayerDefinition surfaceLayer)
        {
            int centerX = Math.Clamp(context.Config.SpawnApproximateTileX, 0, heights.Length - 1);
            int flatHalfWidth = Math.Max(1, context.Config.SurfaceSpawnFlattenHalfWidth);
            int blendWidth = Math.Max(0, context.Config.SurfaceSpawnFlattenBlendWidth);

            int flatStart = Math.Max(0, centerX - flatHalfWidth);
            int flatEnd = Math.Min(heights.Length - 1, centerX + flatHalfWidth);
            int targetHeight = GetAverageHeight(heights, flatStart, flatEnd);

            for (int x = 0; x < heights.Length; x++)
            {
                int distanceFromFlat = GetDistanceFromRange(x, flatStart, flatEnd);
                if (distanceFromFlat > blendWidth)
                    continue;

                float blend = blendWidth <= 0
                    ? 1f
                    : 1f - (distanceFromFlat / (float)(blendWidth + 1));

                float flattened = heights[x] + ((targetHeight - heights[x]) * blend);
                heights[x] = ClampSurfaceHeight((int)MathF.Round(flattened), surfaceLayer, context.WorldMap.Height);
            }
        }

        private static int ClampSurfaceHeight(int surfaceY, WorldLayerDefinition? surfaceLayer, int worldHeight)
        {
            if (surfaceLayer.HasValue)
            {
                int min = surfaceLayer.Value.StartY;
                int max = Math.Max(min, surfaceLayer.Value.EndY);
                return Math.Clamp(surfaceY, min, max);
            }

            return Math.Clamp(surfaceY, 8, worldHeight - 10);
        }

        private static int WrapColumn(int x, int width)
        {
            int wrapped = x % width;
            return wrapped < 0 ? wrapped + width : wrapped;
        }

        private static int GetDistanceFromRange(int x, int start, int end)
        {
            if (x < start)
                return start - x;
            if (x > end)
                return x - end;

            return 0;
        }

        private static int GetAverageHeight(int[] heights, int start, int end)
        {
            int sum = 0;
            int count = 0;

            for (int x = start; x <= end; x++)
            {
                sum += heights[x];
                count++;
            }

            return count > 0 ? (int)MathF.Round(sum / (float)count) : heights[Math.Clamp(start, 0, heights.Length - 1)];
        }

        private static int GetMinHeight(int[] heights)
        {
            int min = int.MaxValue;
            for (int i = 0; i < heights.Length; i++)
                min = Math.Min(min, heights[i]);

            return min;
        }

        private static int GetMaxHeight(int[] heights)
        {
            int max = int.MinValue;
            for (int i = 0; i < heights.Length; i++)
                max = Math.Max(max, heights[i]);

            return max;
        }
    }
}
