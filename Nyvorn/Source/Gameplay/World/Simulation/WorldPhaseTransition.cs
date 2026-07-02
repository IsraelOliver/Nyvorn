namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public enum WorldPhaseTransitionKind
    {
        None,
        DeepNightStarted,
        PreDawnStarted,
        SunriseStarted,
        DayStarted,
        NoonReached,
        AfternoonStarted,
        SunsetStarted,
        NightStarted
    }

    public readonly record struct WorldPhaseTransition(
        WorldPhaseTransitionKind Kind,
        WorldTimePhase PreviousPhase,
        WorldTimePhase CurrentPhase);
}
