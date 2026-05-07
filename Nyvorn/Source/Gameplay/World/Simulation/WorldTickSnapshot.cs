namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public readonly record struct WorldTickSnapshot(
        long FastTickCount,
        long MediumTickCount,
        long SlowTickCount,
        float FastAccumulator,
        float MediumAccumulator,
        float SlowAccumulator,
        float TimeScale,
        bool IsPaused,
        WorldTickDispatch LastDispatch);
}
