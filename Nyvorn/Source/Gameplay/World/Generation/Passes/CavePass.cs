using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class CavePass : IWorldGenPass
    {
        public string Name => "Cave";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Cavando cavernas");

            WorldLayerDefinition shallowLayer = context.GetLayerDefinition(WorldLayerType.ShallowUnderground);
            WorldLayerDefinition cavernLayer = context.GetLayerDefinition(WorldLayerType.Cavern);
            WorldLayerDefinition deepLayer = context.GetLayerDefinition(WorldLayerType.DeepCavern);

            int startY = shallowLayer.StartY;
            int endY = deepLayer.EndY;

            OpenSimplexNoise caveNoise = new OpenSimplexNoise(context.Config.Seed + 1000);
            OpenSimplexNoise warpNoise = new OpenSimplexNoise(context.Config.Seed + 2000);
            OpenSimplexNoise deepNoise = new OpenSimplexNoise(context.Config.Seed + 3000);

            int caveFadeHeight = 30;
            int transitionHeight = Math.Max(20, cavernLayer.Height / 6);
            int transitionStartY = cavernLayer.EndY - (transitionHeight / 2);
            int transitionEndY = deepLayer.StartY + (transitionHeight / 2);

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    if (!context.WorldMap.IsSolidAt(x, y))
                        continue;

                    if (y < transitionStartY)
                    {
                        if (ShouldCarveCavern(context, caveNoise, warpNoise, x, y, startY, cavernLayer.EndY, caveFadeHeight))
                            context.WorldMap.SetTile(x, y, TileType.Empty);
                    }
                    else if (y > transitionEndY)
                    {
                        if (ShouldCarveDeepCavern(context, caveNoise, warpNoise, deepNoise, x, y, deepLayer))
                            context.WorldMap.SetTile(x, y, TileType.Empty);
                    }
                    else
                    {
                        float blendT = (y - transitionStartY) / (float)Math.Max(1, transitionEndY - transitionStartY);
                        blendT = SmoothStep01(Math.Clamp(blendT, 0f, 1f));

                        bool cavernCarve = ShouldCarveCavern(context, caveNoise, warpNoise, x, y, startY, cavernLayer.EndY, caveFadeHeight);
                        bool deepCarve = ShouldCarveDeepCavern(context, caveNoise, warpNoise, deepNoise, x, y, deepLayer);

                        float selector = (float)deepNoise.Evaluate(x * 0.05f, y * 0.05f);
                        float bias = Lerp(-0.35f, 0.35f, blendT);
                        bool finalCarve = (selector + bias) > 0f ? deepCarve : cavernCarve;

                        if (finalCarve)
                            context.WorldMap.SetTile(x, y, TileType.Empty);
                    }
                }

                if ((x & 15) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Cavando cavernas");
            }

            context.ProgressReporter?.Complete(Name, "Cavernas esculpidas");
        }

        private static bool ShouldCarveCavern(
            WorldGenContext context,
            OpenSimplexNoise caveNoise,
            OpenSimplexNoise warpNoise,
            int x,
            int y,
            int startY,
            int cavernEndY,
            int fadeHeight)
        {
            float frequency = 0.045f;
            float threshold = 0.18f;
            float warpFrequency = 0.040f;
            float warpStrength = 18f;

            float warpX = Fractal(warpNoise, x * warpFrequency, y * warpFrequency) * warpStrength;
            float warpY = Fractal(warpNoise, (x + 1000f) * warpFrequency, (y + 1000f) * warpFrequency) * warpStrength;

            float sample = (float)caveNoise.Evaluate(
                (x + warpX) * frequency,
                (y + warpY) * frequency);

            float depthT = (y - startY) / (float)Math.Max(1, cavernEndY - startY);
            depthT = Math.Clamp(depthT, 0f, 1f);

            float depthBias = Lerp(0.18f, -0.08f, depthT);
            float topFadeT = (y - startY) / (float)Math.Max(1, fadeHeight);
            topFadeT = SmoothStep01(Math.Clamp(topFadeT, 0f, 1f));
            float topFadeBias = Lerp(0.40f, 0f, topFadeT);
            float effectiveThreshold = threshold + depthBias + topFadeBias;

            return sample > effectiveThreshold;
        }

        private static bool ShouldCarveDeepCavern(
            WorldGenContext context,
            OpenSimplexNoise caveNoise,
            OpenSimplexNoise warpNoise,
            OpenSimplexNoise deepNoise,
            int x,
            int y,
            WorldLayerDefinition deepLayer)
        {
            float depthT = Math.Clamp(deepLayer.GetNormalizedDepth(y), 0f, 1f);
            float baseFrequency = 0.030f;
            float warpFrequency = 0.045f;
            float warpStrength = 22f;

            float warpX = Fractal(warpNoise, x * warpFrequency, y * warpFrequency) * warpStrength;
            float warpY = Fractal(warpNoise, (x + 1400f) * warpFrequency, (y + 1400f) * warpFrequency) * warpStrength;

            float baseSample = (float)caveNoise.Evaluate(
                (x + warpX) * baseFrequency,
                (y + warpY) * baseFrequency);

            float largeVoid = Fractal(deepNoise, x * 0.018f, y * 0.018f);
            float macroVoid = (float)deepNoise.Evaluate(x * 0.006f, y * 0.006f);
            float verticalBias = MathF.Abs((float)deepNoise.Evaluate(x * 0.004f, y * 0.090f));
            float deepAggression = Lerp(0.08f, 0.24f, depthT);

            float combined =
                baseSample +
                (largeVoid * 0.90f) +
                (macroVoid * 0.8f) +
                (verticalBias * 0.6f);

            float effectiveThreshold = 0.18f + deepAggression;
            return combined > effectiveThreshold;
        }

        private static float Fractal(OpenSimplexNoise noise, float x, float y)
        {
            float value = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float amplitudeSum = 0f;

            for (int i = 0; i < 3; i++)
            {
                value += (float)noise.Evaluate(x * frequency, y * frequency) * amplitude;
                amplitudeSum += amplitude;
                frequency *= 2f;
                amplitude *= 0.5f;
            }

            if (amplitudeSum <= 0f)
                return 0f;

            return value / amplitudeSum;
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * t);
        }

        private static float SmoothStep01(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - (2f * t));
        }
    }
}
