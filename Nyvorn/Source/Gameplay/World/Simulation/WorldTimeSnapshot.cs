namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public readonly record struct WorldTimeSnapshot(
        float TimeOfDay01,
        int CycleIndex,
        WorldTimePhase Phase,
        string ClockText24h,
        float NightStrength);
}
