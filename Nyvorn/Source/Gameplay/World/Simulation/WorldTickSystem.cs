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
            LastDispatch = default;
        }

        public WorldTickConfig Config => config;
        public float TimeScale { get; private set; } = 1f;
        public bool IsPaused { get; private set; }
        public long FastTickCount { get; private set; }
        public long MediumTickCount { get; private set; }
        public long SlowTickCount { get; private set; }
        public WorldTickDispatch LastDispatch { get; private set; }

        public void SetTimeScale(float timeScale)
        {
            if (float.IsNaN(timeScale) || float.IsInfinity(timeScale) || timeScale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(timeScale), "Time scale precisa ser maior que zero.");

            TimeScale = timeScale;
        }

        public void SetPaused(bool isPaused)
        {
            IsPaused = isPaused;
            if (isPaused)
                LastDispatch = default;
        }

        public void RecordManualDispatch(WorldTickDispatch dispatch)
        {
            FastTickCount += dispatch.FastTicks;
            MediumTickCount += dispatch.MediumTicks;
            SlowTickCount += dispatch.SlowTicks;
            LastDispatch = dispatch;
        }

        public WorldTickDispatch Advance(float dt)
        {
            if (dt <= 0f || IsPaused)
            {
                LastDispatch = default;
                return LastDispatch;
            }

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
            LastDispatch = dispatch;
            return dispatch;
        }

        public void Reset()
        {
            fastAccumulator = 0f;
            mediumAccumulator = 0f;
            slowAccumulator = 0f;
            FastTickCount = 0;
            MediumTickCount = 0;
            SlowTickCount = 0;
            LastDispatch = default;
        }

        public WorldTickSnapshot CreateSnapshot()
        {
            return new WorldTickSnapshot(
                FastTickCount,
                MediumTickCount,
                SlowTickCount,
                fastAccumulator,
                mediumAccumulator,
                slowAccumulator,
                TimeScale,
                IsPaused,
                LastDispatch);
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
