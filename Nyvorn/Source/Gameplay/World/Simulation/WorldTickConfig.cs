namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public sealed class WorldTickConfig
    {
        public static WorldTickConfig Default { get; } = new();

        public WorldTickConfig(
            float fastTickRate = 60f,
            float mediumTickRate = 4f,
            float slowTickRate = 1f,
            int maxFastTicksPerFrame = 8,
            int maxMediumTicksPerFrame = 2,
            int maxSlowTicksPerFrame = 1)
        {
            FastTickRate = ValidateTickRate(fastTickRate, nameof(fastTickRate));
            MediumTickRate = ValidateTickRate(mediumTickRate, nameof(mediumTickRate));
            SlowTickRate = ValidateTickRate(slowTickRate, nameof(slowTickRate));
            MaxFastTicksPerFrame = ValidateMaxTicks(maxFastTicksPerFrame, nameof(maxFastTicksPerFrame));
            MaxMediumTicksPerFrame = ValidateMaxTicks(maxMediumTicksPerFrame, nameof(maxMediumTicksPerFrame));
            MaxSlowTicksPerFrame = ValidateMaxTicks(maxSlowTicksPerFrame, nameof(maxSlowTicksPerFrame));
        }

        public float FastTickRate { get; }
        public float MediumTickRate { get; }
        public float SlowTickRate { get; }

        public float FastTickInterval => 1f / FastTickRate;
        public float MediumTickInterval => 1f / MediumTickRate;
        public float SlowTickInterval => 1f / SlowTickRate;

        public int MaxFastTicksPerFrame { get; }
        public int MaxMediumTicksPerFrame { get; }
        public int MaxSlowTicksPerFrame { get; }

        private static float ValidateTickRate(float value, string paramName)
        {
            if (value <= 0f)
                throw new System.ArgumentOutOfRangeException(paramName, "Tick rate precisa ser maior que zero.");

            return value;
        }

        private static int ValidateMaxTicks(int value, string paramName)
        {
            if (value <= 0)
                throw new System.ArgumentOutOfRangeException(paramName, "Max ticks por frame precisa ser maior que zero.");

            return value;
        }
    }
}
