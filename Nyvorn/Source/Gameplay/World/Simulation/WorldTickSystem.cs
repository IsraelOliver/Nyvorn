using System;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public sealed class WorldTickSystem
    {
        private readonly WorldTickConfig config;

        private float fastAccumulator;
        private float mediumAccumulator;
        private float slowAccumulator;

        public WorldTickSystem(WorldTickConfig config = null)
        {
            this.config = config ?? WorldTickConfig.Default;
        }

        public WorldTickConfig Config => config;
        public float TimeScale { get; private set; } = 1f;
        public bool IsPaused { get; private set; }
        public long FastTickCount { get; private set; }
        public long MediumTickCount { get; private set; }
        public long SlowTickCount { get; private set; }

        public void SetTimeScale(float timeScale)
        {
            if (float.IsNaN(timeScale) || float.IsInfinity(timeScale) || timeScale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(timeScale), "Time scale precisa ser maior que zero.");

            TimeScale = timeScale;
        }

        public void SetPaused(bool isPaused)
        {
            IsPaused = isPaused;
        }

        public void RecordManualDispatch(WorldTickDispatch dispatch)
        {
            FastTickCount += dispatch.FastTicks;
            MediumTickCount += dispatch.MediumTicks;
            SlowTickCount += dispatch.SlowTicks;
        }

        public WorldTickDispatch Advance(float dt)
        {
            if (dt <= 0f || IsPaused)
                return default;

            dt *= TimeScale;
            fastAccumulator += dt;
            mediumAccumulator += dt;
            slowAccumulator += dt;

            WorldTickDispatch dispatch = new(
                FastTicks: ConsumeTicks(ref fastAccumulator, config.FastTickInterval, config.MaxFastTicksPerFrame, out bool fastOverflowed),
                MediumTicks: ConsumeTicks(ref mediumAccumulator, config.MediumTickInterval, config.MaxMediumTicksPerFrame, out bool mediumOverflowed),
                SlowTicks: ConsumeTicks(ref slowAccumulator, config.SlowTickInterval, config.MaxSlowTicksPerFrame, out bool slowOverflowed),
                FastOverflowed: fastOverflowed,
                MediumOverflowed: mediumOverflowed,
                SlowOverflowed: slowOverflowed);

            FastTickCount += dispatch.FastTicks;
            MediumTickCount += dispatch.MediumTicks;
            SlowTickCount += dispatch.SlowTicks;
            return dispatch;
        }

        private static int ConsumeTicks(ref float accumulator, float interval, int maxTicks, out bool overflowed)
        {
            int ticks = 0;
            while (accumulator >= interval && ticks < maxTicks)
            {
                accumulator -= interval;
                ticks++;
            }

            overflowed = accumulator >= interval;
            if (overflowed)
                accumulator %= interval;

            return ticks;
        }
    }
}
