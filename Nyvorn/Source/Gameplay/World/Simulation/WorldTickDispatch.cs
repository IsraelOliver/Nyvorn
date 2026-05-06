namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public readonly record struct WorldTickDispatch(
        int FastTicks,
        int MediumTicks,
        int SlowTicks,
        bool FastOverflowed,
        bool MediumOverflowed,
        bool SlowOverflowed)
    {
        public bool AnyTicks => FastTicks > 0 || MediumTicks > 0 || SlowTicks > 0;
        public bool AnyOverflow => FastOverflowed || MediumOverflowed || SlowOverflowed;
    }
}
