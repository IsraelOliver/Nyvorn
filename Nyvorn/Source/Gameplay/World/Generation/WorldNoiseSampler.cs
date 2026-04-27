using System;

namespace Nyvorn.Source.World.Generation
{
    public static class WorldNoiseSampler
    {
        private const float Tau = MathF.PI * 2f;

        public static float Sample1DLooped(OpenSimplexNoise noise, int x, int worldWidth, float frequency, float seedOffset)
        {
            float t = x / (float)worldWidth;
            float angle = t * Tau;

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

        public static float Sample2DHorizontalLoop(
            OpenSimplexNoise noise,
            float x,
            float y,
            int worldWidth,
            float xFrequency,
            float yFrequency,
            float seedOffsetA,
            float seedOffsetB,
            bool wrapHorizontally)
        {
            if (!wrapHorizontally)
                return (float)noise.Evaluate((x * xFrequency) + seedOffsetA, (y * yFrequency) + seedOffsetB);

            float wrappedX = Repeat01(x / MathF.Max(1f, worldWidth));
            float angle = wrappedX * Tau;

            float loopScale = 128f;
            float radius = MathF.Max(0.01f, xFrequency * loopScale);

            float sampleX = MathF.Cos(angle) * radius + seedOffsetA;
            float sampleY = MathF.Sin(angle) * radius + seedOffsetB;
            float sampleZ = (y * yFrequency) + (seedOffsetA * 0.37f);
            float sampleW = (y * yFrequency * 0.41f) + (seedOffsetB * 0.53f);

            return (float)noise.Evaluate(sampleX, sampleY, sampleZ, sampleW);
        }

        private static float Repeat01(float value)
        {
            return value - MathF.Floor(value);
        }
    }
}
