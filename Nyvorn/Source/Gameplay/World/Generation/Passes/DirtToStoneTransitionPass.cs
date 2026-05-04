using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class DirtToStoneTransitionPass : IWorldGenPass
    {
        private const float InversionDepthPercent = 0.35f;
        private const float SurfaceQuietPercent = 0.12f;
        private const float TopStoneThresholdNearSurface = 0.75f;
        private const float TopStoneThresholdEndOfSurfaceQuiet = 0.55f;
        private const float MidStoneThresholdStart = 0.50f;
        private const float MidStoneThresholdAtInversion = 0.20f;
        private const float DeepDirtThresholdAtInversion = 0.30f;
        private const float DeepDirtThresholdAtBottom = 0.38f;

        public string Name => "DirtToStoneTransition";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Misturando terra e pedra em bolsões");

            int dirtCount = 0;
            int stoneCount = 0;

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = context.SurfaceHeights[x];
                int undergroundStartY = Math.Clamp(surfaceY, 0, context.WorldMap.Height - 1);
                int undergroundEndY = context.WorldMap.Height - 1;
                int undergroundDepth = Math.Max(1, undergroundEndY - undergroundStartY);

                for (int y = undergroundStartY; y <= undergroundEndY; y++)
                {
                    TileType currentTile = context.WorldMap.GetTile(x, y);
                    if (currentTile != TileType.Dirt && currentTile != TileType.Stone)
                        continue;

                    float depth01 = (y - undergroundStartY) / (float)undergroundDepth;

                    float stoneThreshold = GetTopStoneThreshold(depth01);
                    float dirtThreshold = GetBottomDirtThreshold(depth01);

                    TileType nextTile;
                    if (depth01 < InversionDepthPercent)
                    {
                        float field = WorldFieldSampler.SampleShallowStonePocketField(context, x, y);
                        nextTile = field > stoneThreshold ? TileType.Stone : TileType.Dirt;
                    }
                    else
                    {
                        float field = WorldFieldSampler.SampleDeepDirtPocketField(context, x, y);
                        nextTile = field > dirtThreshold ? TileType.Dirt : TileType.Stone;
                    }

                    if (nextTile != currentTile)
                        context.WorldMap.SetTile(x, y, nextTile);

                    if (nextTile == TileType.Stone)
                        stoneCount++;
                    else
                        dirtCount++;
                }

                if ((x & 31) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Misturando terra e pedra em bolsões");
            }

            context.DebugStats["DirtToStoneTransition.DirtTiles"] = dirtCount.ToString();
            context.DebugStats["DirtToStoneTransition.StoneTiles"] = stoneCount.ToString();
            context.DebugStats["DirtToStoneTransition.InversionDepthPercent"] = InversionDepthPercent.ToString("0.00");
            context.ProgressReporter?.Complete(Name, "Transição terra-pedra pronta");
        }

        internal static float GetMaterialThreshold(float depth01)
        {
            return depth01 < InversionDepthPercent
                ? GetTopStoneThreshold(depth01)
                : GetBottomDirtThreshold(depth01);
        }

        internal static float GetTopStoneThreshold(float depth01)
        {
            if (depth01 < SurfaceQuietPercent)
            {
                float topT = InverseLerp(0f, SurfaceQuietPercent, depth01);
                topT = SmoothStep01(topT);
                return Lerp(TopStoneThresholdNearSurface, TopStoneThresholdEndOfSurfaceQuiet, topT);
            }

            float middleT = InverseLerp(SurfaceQuietPercent, InversionDepthPercent, depth01);
            middleT = SmoothStep01(middleT);
            return Lerp(MidStoneThresholdStart, MidStoneThresholdAtInversion, middleT);
        }

        internal static float GetBottomDirtThreshold(float depth01)
        {
            float bottomDepthT = InverseLerp(InversionDepthPercent, 1f, depth01);
            bottomDepthT = SmoothStep01(bottomDepthT);
            return Lerp(DeepDirtThresholdAtInversion, DeepDirtThresholdAtBottom, bottomDepthT);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * Math.Clamp(t, 0f, 1f));
        }

        private static float InverseLerp(float a, float b, float value)
        {
            if (MathF.Abs(b - a) < 0.0001f)
                return 0f;

            return Math.Clamp((value - a) / (b - a), 0f, 1f);
        }

        private static float SmoothStep01(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - (2f * t));
        }
    }
}
