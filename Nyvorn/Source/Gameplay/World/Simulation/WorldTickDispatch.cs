namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public readonly record struct WorldTickDispatch(
        int FastTicks,
        int MediumTicks,
        int SlowTicks,
        bool FastOverflowed,
        bool MediumOverflowed,
        bool SlowOverflowed);
}
