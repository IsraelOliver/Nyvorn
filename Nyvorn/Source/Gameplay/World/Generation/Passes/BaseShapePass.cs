using System;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class BaseShapePass : IWorldGenPass
    {
        public string Name => "BaseShape";

        public void Apply(WorldGenContext context)
        {
            int[] surfaceHeights = BuildSurfaceProfile(context);
            bool[] sandColumns = new bool[context.WorldMap.Width];
            int[] naturalEntrances = BuildNaturalEntrances(surfaceHeights, context);

            for (int x = 0; x < context.WorldMap.Width; x++)
                sandColumns[x] = context.BiomeNoise.GetNoise(x, 0f) > context.Settings.SandBiomeThreshold;

            context.SurfaceHeights = surfaceHeights;
            context.SandColumns = sandColumns;
            context.NaturalEntrances = naturalEntrances;
            context.LayerProfile = new WorldLayerProfile(surfaceHeights, context.Settings);
        }

        private int[] BuildSurfaceProfile(WorldGenContext context)
        {
            int[] heights = new int[context.WorldMap.Width];

            for (int x = 0; x < context.WorldMap.Width; x++)
                heights[x] = GetSurfaceTileY(context, x);

            for (int pass = 0; pass < context.Settings.SurfaceSmoothingPasses; pass++)
                heights = SmoothSurfaceHeights(heights, context.WorldMap.Height);

            LimitAdjacentSurfaceSteps(heights, context.WorldMap.Height, context.Settings.MaxAdjacentSurfaceStep);
            return heights;
        }

        private int[] BuildNaturalEntrances(int[] surfaceHeights, WorldGenContext context)
        {
            int segmentCount = Math.Max(1, context.Settings.NaturalEntranceCount);
            int jitter = Math.Max(0, context.Settings.NaturalEntranceJitter);
            int[] entrances = new int[segmentCount];

            for (int i = 0; i < segmentCount; i++)
            {
                int segmentStart = (i * surfaceHeights.Length) / segmentCount;
                int segmentEnd = ((i + 1) * surfaceHeights.Length) / segmentCount - 1;
                int bestX = segmentStart;
                int bestSurfaceY = int.MaxValue;

                for (int x = segmentStart; x <= segmentEnd; x++)
                {
                    if (surfaceHeights[x] < bestSurfaceY)
                    {
                        bestSurfaceY = surfaceHeights[x];
                        bestX = x;
                    }
                }

                int offset = (int)MathF.Round(context.SurfaceDetailNoise.GetNoise(bestX * 1.7f, i * 13f) * jitter);
                entrances[i] = WrapColumn(bestX + offset, surfaceHeights.Length);
            }

            return entrances;
        }

        private int GetSurfaceTileY(WorldGenContext context, int tileX)
        {
            float warpedX = tileX + (context.SurfaceWarpNoise.GetNoise(tileX, 0f) * context.Settings.SurfaceWarpStrength);
            float macroValue = context.SurfaceNoise.GetNoise(warpedX, 0f) * context.Settings.SurfaceAmplitude;
            float detailValue = context.SurfaceDetailNoise.GetNoise(warpedX, 0f) * context.Settings.SurfaceDetailAmplitude;
            int surfaceY = context.Settings.BaseGroundLevel + (int)MathF.Round(macroValue + detailValue);
            return Math.Clamp(surfaceY, 8, context.WorldMap.Height - 10);
        }

        private int[] SmoothSurfaceHeights(int[] source, int worldHeight)
        {
            int[] smoothed = new int[source.Length];

            for (int x = 0; x < source.Length; x++)
            {
                int left = source[WrapColumn(x - 1, source.Length)];
                int center = source[x];
                int right = source[WrapColumn(x + 1, source.Length)];
                float weightedAverage = (left * 0.24f) + (center * 0.52f) + (right * 0.24f);
                smoothed[x] = ClampSurfaceHeight((int)MathF.Round(weightedAverage), worldHeight);
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

                heights[0] = ClampStep(heights[0], heights[^1], maxStep, worldHeight);
                heights[^1] = ClampStep(heights[^1], heights[0], maxStep, worldHeight);
            }
        }

        private int ClampStep(int current, int neighbor, int maxStep, int worldHeight)
        {
            int min = neighbor - maxStep;
            int max = neighbor + maxStep;
            return ClampSurfaceHeight(Math.Clamp(current, min, max), worldHeight);
        }

        private static int ClampSurfaceHeight(int surfaceY, int worldHeight)
        {
            return Math.Clamp(surfaceY, 8, worldHeight - 10);
        }

        private static int WrapColumn(int x, int width)
        {
            int wrapped = x % width;
            return wrapped < 0 ? wrapped + width : wrapped;
        }
    }
}
