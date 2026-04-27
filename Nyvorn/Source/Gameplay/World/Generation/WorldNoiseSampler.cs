using System;

namespace Nyvorn.Source.World.Generation
{
    public static class WorldNoiseSampler
    {
        public static float Sample1DLooped(OpenSimplexNoise noise, int x, int worldWidth, float frequency, float seedOffset)
        {
            float t = x / (float)worldWidth;
            float angle = t * MathF.PI * 2f;

            float loopScale = 128f;
            float radius = MathF.Max(0.01f, frequency * loopScale);

            float sampleX = MathF.Cos(angle) * radius + seedOffset;
            float sampleY = MathF.Sin(angle) * radius + seedOffset * 0.5f;

            return (float)noise.Evaluate(sampleX, sampleY);
        }

        public static float SampleTerrain1D(
        OpenSimplexNoise noise,
        int x,
        int worldWidth,
        float frequency,
        float seedOffset,
        bool wrapHorizontally)
        {
            if (wrapHorizontally)
                return Sample1DLooped(noise, x, worldWidth, frequency, seedOffset);

            return Sample1D(noise, x, frequency, seedOffset);
        }

        public static float Sample1D(OpenSimplexNoise noise, int x, float frequency, float seedOffset)
        {
            return (float)noise.Evaluate(x * frequency, seedOffset);
        }
    }
}