using System.Collections.Generic;

namespace Nyvorn.Source.World.Generation
{
    public static class WorldFieldSampler
    {
        private const float ShallowStonePocketFrequencyScale = 3.5f;
        private const float ShallowStonePocketWarpStrengthScale = 0.15f;
        private const float DeepDirtPocketBaseFrequency = 0.08f;
        private const float DeepDirtPocketWarpFrequency = 0.035f;
        private const float DeepDirtPocketWarpStrength = 8f;

        private sealed class NoiseSet
        {
            public NoiseSet(int seed)
            {
                CaveNoise = new OpenSimplexNoise(seed + 1000);
                WarpNoise = new OpenSimplexNoise(seed + 2000);
                DeepNoise = new OpenSimplexNoise(seed + 3000);
            }

            public OpenSimplexNoise CaveNoise { get; }
            public OpenSimplexNoise WarpNoise { get; }
            public OpenSimplexNoise DeepNoise { get; }
        }

        private static readonly Dictionary<int, NoiseSet> CachedNoiseSets = new();
        private static readonly object NoiseLock = new();

        public static float Sample(WorldGenContext context, int x, int y)
        {
            NoiseSet noiseSet = GetNoiseSet(context.Config.Seed);
            WorldLayerDefinition cavernLayer = context.GetLayerDefinition(WorldLayerType.Cavern);
            WorldLayerDefinition deepLayer = context.GetLayerDefinition(WorldLayerType.DeepCavern);

            int transitionHeight = System.Math.Max(20, cavernLayer.Height / 6);
            int transitionStartY = cavernLayer.EndY - (transitionHeight / 2);
            int transitionEndY = deepLayer.StartY + (transitionHeight / 2);

            if (y < transitionStartY)
                return SampleCavernField(context, noiseSet.CaveNoise, noiseSet.WarpNoise, x, y);

            if (y > transitionEndY)
                return SampleDeepField(context, noiseSet.CaveNoise, noiseSet.WarpNoise, noiseSet.DeepNoise, x, y);

            float blendT = (y - transitionStartY) / (float)System.Math.Max(1, transitionEndY - transitionStartY);
            blendT = SmoothStep01(System.Math.Clamp(blendT, 0f, 1f));
            float selector = SampleSeamedNoise(context, noiseSet.DeepNoise, x, y, 0.05f, 0.05f);
            float bias = Lerp(-0.35f, 0.35f, blendT);

            return (selector + bias) > 0f
                ? SampleDeepField(context, noiseSet.CaveNoise, noiseSet.WarpNoise, noiseSet.DeepNoise, x, y)
                : SampleCavernField(context, noiseSet.CaveNoise, noiseSet.WarpNoise, x, y);
        }

        public static float SampleShallowStonePocketField(WorldGenContext context, int x, int y)
        {
            NoiseSet noiseSet = GetNoiseSet(context.Config.Seed);
            return SampleScaledCavernField(
                context,
                noiseSet.CaveNoise,
                noiseSet.WarpNoise,
                x,
                y,
                ShallowStonePocketFrequencyScale,
                ShallowStonePocketWarpStrengthScale);
        }

        public static float SampleDeepDirtPocketField(WorldGenContext context, int x, int y)
        {
            NoiseSet noiseSet = GetNoiseSet(context.Config.Seed);

            float warpX = Fractal(context, noiseSet.WarpNoise, x, y, DeepDirtPocketWarpFrequency, DeepDirtPocketWarpFrequency, 2200f, 400f) * DeepDirtPocketWarpStrength;
            float warpY = Fractal(context, noiseSet.WarpNoise, x, y, DeepDirtPocketWarpFrequency, DeepDirtPocketWarpFrequency, 3200f, 1400f) * DeepDirtPocketWarpStrength;

            float pocketBase = SampleSeamedNoise(
                context,
                noiseSet.DeepNoise,
                x,
                y,
                DeepDirtPocketBaseFrequency,
                DeepDirtPocketBaseFrequency,
                warpX,
                warpY);
            float broadPocket = Fractal(context, noiseSet.DeepNoise, x, y, 0.010f, 0.015f, 1100f, 2600f);
            float verticalPocket = SampleSeamedNoise(context, noiseSet.CaveNoise, x, y, 0.008f, 0.040f, 1700f, 800f);

            return (pocketBase * 0.55f) +
                   (broadPocket * 0.30f) +
                   (verticalPocket * 0.15f);
        }

        public static bool UsesDeepThreshold(WorldGenContext context, int x, int y)
        {
            NoiseSet noiseSet = GetNoiseSet(context.Config.Seed);
            WorldLayerDefinition cavernLayer = context.GetLayerDefinition(WorldLayerType.Cavern);
            WorldLayerDefinition deepLayer = context.GetLayerDefinition(WorldLayerType.DeepCavern);

            int transitionHeight = System.Math.Max(20, cavernLayer.Height / 6);
            int transitionStartY = cavernLayer.EndY - (transitionHeight / 2);
            int transitionEndY = deepLayer.StartY + (transitionHeight / 2);

            if (y < transitionStartY)
                return false;

            if (y > transitionEndY)
                return true;

            float blendT = (y - transitionStartY) / (float)System.Math.Max(1, transitionEndY - transitionStartY);
            blendT = SmoothStep01(System.Math.Clamp(blendT, 0f, 1f));
            float selector = SampleSeamedNoise(context, noiseSet.DeepNoise, x, y, 0.05f, 0.05f);
            float bias = Lerp(-0.35f, 0.35f, blendT);
            return (selector + bias) > 0f;
        }

        internal static float SampleCavernField(WorldGenContext context, OpenSimplexNoise caveNoise, OpenSimplexNoise warpNoise, int x, int y)
        {
            const float frequency = 0.045f;
            const float warpFrequency = 0.040f;
            const float warpStrength = 18f;

            float warpX = Fractal(context, warpNoise, x, y, warpFrequency, warpFrequency) * warpStrength;
            float warpY = Fractal(context, warpNoise, x, y, warpFrequency, warpFrequency, 1000f, 1000f) * warpStrength;

            return SampleSeamedNoise(context, caveNoise, x, y, frequency, frequency, warpX, warpY);
        }

        internal static float SampleScaledCavernField(
            WorldGenContext context,
            OpenSimplexNoise caveNoise,
            OpenSimplexNoise warpNoise,
            int x,
            int y,
            float frequencyScale,
            float warpStrengthScale)
        {
            float frequency = 0.045f * frequencyScale;
            float warpFrequency = 0.040f * frequencyScale;
            float warpStrength = 18f * warpStrengthScale;

            float warpX = Fractal(context, warpNoise, x, y, warpFrequency, warpFrequency) * warpStrength;
            float warpY = Fractal(context, warpNoise, x, y, warpFrequency, warpFrequency, 1000f, 1000f) * warpStrength;

            return SampleSeamedNoise(context, caveNoise, x, y, frequency, frequency, warpX, warpY);
        }

        internal static float SampleDeepField(WorldGenContext context, OpenSimplexNoise caveNoise, OpenSimplexNoise warpNoise, OpenSimplexNoise deepNoise, int x, int y)
        {
            const float baseFrequency = 0.030f;
            const float warpFrequency = 0.045f;
            const float warpStrength = 22f;

            float warpX = Fractal(context, warpNoise, x, y, warpFrequency, warpFrequency) * warpStrength;
            float warpY = Fractal(context, warpNoise, x, y, warpFrequency, warpFrequency, 1400f, 1400f) * warpStrength;

            float baseSample = SampleSeamedNoise(context, caveNoise, x, y, baseFrequency, baseFrequency, warpX, warpY);
            float largeVoid = Fractal(context, deepNoise, x, y, 0.018f, 0.018f);
            float macroVoid = SampleSeamedNoise(context, deepNoise, x, y, 0.006f, 0.006f);
            float verticalBias = System.MathF.Abs(SampleSeamedNoise(context, deepNoise, x, y, 0.004f, 0.090f));

            return baseSample +
                   (largeVoid * 0.90f) +
                   (macroVoid * 0.8f) +
                   (verticalBias * 0.6f);
        }

        internal static float Fractal(
            WorldGenContext context,
            OpenSimplexNoise noise,
            float x,
            float y,
            float xFrequency,
            float yFrequency,
            float sampleOffsetX = 0f,
            float sampleOffsetY = 0f)
        {
            float value = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float amplitudeSum = 0f;

            for (int i = 0; i < 3; i++)
            {
                value += SampleSeamedNoise(
                    context,
                    noise,
                    x,
                    y,
                    xFrequency * frequency,
                    yFrequency * frequency,
                    sampleOffsetX,
                    sampleOffsetY) * amplitude;
                amplitudeSum += amplitude;
                frequency *= 2f;
                amplitude *= 0.5f;
            }

            return amplitudeSum <= 0f ? 0f : value / amplitudeSum;
        }

        internal static float SampleSeamedNoise(
            WorldGenContext context,
            OpenSimplexNoise noise,
            float x,
            float y,
            float xFrequency,
            float yFrequency,
            float sampleOffsetX = 0f,
            float sampleOffsetY = 0f)
        {
            int seamWidth = GetSeamWidth(context);
            float baseSample = SampleNoise(noise, x + sampleOffsetX, y + sampleOffsetY, xFrequency, yFrequency);

            if (seamWidth <= 0 || x >= seamWidth)
                return baseSample;

            float edgeSample = SampleNoise(
                noise,
                x + context.WorldMap.Width + sampleOffsetX,
                y + sampleOffsetY,
                xFrequency,
                yFrequency);
            float seamT = SmoothStep01(System.Math.Clamp(x / seamWidth, 0f, 1f));

            return Lerp(edgeSample, baseSample, seamT);
        }

        private static float SampleNoise(OpenSimplexNoise noise, float x, float y, float xFrequency, float yFrequency)
        {
            return (float)noise.Evaluate(x * xFrequency, y * yFrequency);
        }

        private static int GetSeamWidth(WorldGenContext context)
        {
            if (!context.Config.WrapHorizontally || context.WorldMap.Width <= 0)
                return 0;

            int width = System.Math.Clamp(context.WorldMap.Width / 64, 32, 96);
            return System.Math.Min(width, System.Math.Max(1, context.WorldMap.Width / 2));
        }

        private static NoiseSet GetNoiseSet(int seed)
        {
            lock (NoiseLock)
            {
                if (!CachedNoiseSets.TryGetValue(seed, out NoiseSet noiseSet))
                {
                    noiseSet = new NoiseSet(seed);
                    CachedNoiseSets[seed] = noiseSet;
                }

                return noiseSet;
            }
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * t);
        }

        private static float SmoothStep01(float t)
        {
            t = System.Math.Clamp(t, 0f, 1f);
            return t * t * (3f - (2f * t));
        }
    }
}
