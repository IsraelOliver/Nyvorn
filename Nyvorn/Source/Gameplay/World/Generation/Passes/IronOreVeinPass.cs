using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class IronOreVeinPass : IWorldGenPass
    {
        private const int MinDepthBlocks = 5;

        // Same depth breakpoints DirtToStoneTransitionPass uses for its own threshold curve,
        // so iron ore's rarity follows the same "shape" that already reads well in-game.
        private const float SurfaceQuietPercent = 0.12f;
        private const float InversionDepthPercent = 0.20f;

        private const float NearSurfaceThreshold = 0.90f;
        private const float SurfaceQuietEndThreshold = 0.75f;
        private const float PeakThreshold = 0.55f;
        private const float BottomThreshold = 0.85f;

        public string Name => "IronOreVein";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Semeando veios de minerio de ferro");

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = Math.Clamp(context.SurfaceHeights[x], 0, context.WorldMap.Height - 1);
                int undergroundEndY = context.WorldMap.Height - 1;
                int undergroundDepth = Math.Max(1, undergroundEndY - surfaceY);
                int minOreY = surfaceY + MinDepthBlocks;

                for (int y = minOreY; y <= undergroundEndY; y++)
                {
                    if (context.WorldMap.GetTile(x, y) != TileType.Stone)
                        continue;

                    float depth01 = (y - surfaceY) / (float)undergroundDepth;
                    float threshold = GetOreThreshold(depth01);
                    float field = WorldFieldSampler.SampleIronOreVeinField(context, x, y);

                    if (field > threshold)
                        context.WorldMap.SetTile(x, y, TileType.IronOre);
                }

                if ((x & 31) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Semeando veios de minerio de ferro");
            }

            context.ProgressReporter?.Complete(Name, "Veios de minerio de ferro prontos");
        }

        private static float GetOreThreshold(float depth01)
        {
            if (depth01 < SurfaceQuietPercent)
            {
                float t = SmoothStep01(InverseLerp(0f, SurfaceQuietPercent, depth01));
                return Lerp(NearSurfaceThreshold, SurfaceQuietEndThreshold, t);
            }

            if (depth01 < InversionDepthPercent)
            {
                float t = SmoothStep01(InverseLerp(SurfaceQuietPercent, InversionDepthPercent, depth01));
                return Lerp(SurfaceQuietEndThreshold, PeakThreshold, t);
            }

            float fallT = SmoothStep01(InverseLerp(InversionDepthPercent, 1f, depth01));
            return Lerp(PeakThreshold, BottomThreshold, fallT);
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
