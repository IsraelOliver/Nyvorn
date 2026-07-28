using System;
using Nyvorn.Source.World.Generation.Biomes;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class SurfaceBackgroundPass : IWorldGenPass
    {
        public string Name => "SurfaceBackground";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Gerando fundo de terra");

            WorldLayerDefinition shallowLayer = context.GetLayerDefinition(WorldLayerType.ShallowUnderground);

            int caveSeed = SeedHash.ToIntSeed(context.Seeds.CaveSeed);
            OpenSimplexNoise caveNoise = new OpenSimplexNoise(caveSeed + 5000);
            OpenSimplexNoise warpNoise = new OpenSimplexNoise(caveSeed + 6000);

            int caveFadeHeight = 20;
            int startY = shallowLayer.StartY;
            int endY = shallowLayer.EndY;

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    if (ShouldGenerateBackgroundTile(context, caveNoise, warpNoise, x, y, startY, endY, caveFadeHeight))
                        context.WorldMap.SetBackgroundTile(x, y, TileType.Dirt);
                }

                if ((x & 15) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Gerando fundo de terra");
            }

            context.ProgressReporter?.Complete(Name, "Fundo de terra gerado");
        }

        private static bool ShouldGenerateBackgroundTile(
            WorldGenContext context,
            OpenSimplexNoise caveNoise,
            OpenSimplexNoise warpNoise,
            int x,
            int y,
            int startY,
            int endY,
            int fadeHeight)
        {
            float frequency = 0.045f;
            float threshold = 0.18f;
            float warpFrequency = 0.040f;
            float warpStrength = 18f;

            float warpX = WorldFieldSampler.Fractal(context, warpNoise, x, y, warpFrequency, warpFrequency) * warpStrength;
            float warpY = WorldFieldSampler.Fractal(context, warpNoise, x, y, warpFrequency, warpFrequency, 1000f, 1000f) * warpStrength;

            float sample = WorldFieldSampler.SampleSeamedNoise(context, caveNoise, x, y, frequency, frequency, warpX, warpY);

            float depthT = (y - startY) / (float)Math.Max(1, endY - startY);
            depthT = Math.Clamp(depthT, 0f, 1f);

            float depthBias = Lerp(0.18f, -0.08f, depthT);
            float topFadeT = (y - startY) / (float)Math.Max(1, fadeHeight);
            topFadeT = SmoothStep01(Math.Clamp(topFadeT, 0f, 1f));
            float topFadeBias = Lerp(0.40f, 0f, topFadeT);
            float effectiveThreshold = threshold + depthBias + topFadeBias + GetBackgroundThresholdOffset(context, x);

            return sample > effectiveThreshold;
        }

        private static float GetBackgroundThresholdOffset(WorldGenContext context, int x)
        {
            BiomeSample sample = context.SampleBiome(x);
            return Lerp(
                sample.SecondaryDefinition.CaveThresholdOffset,
                sample.PrimaryDefinition.CaveThresholdOffset,
                sample.Blend);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float SmoothStep01(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
