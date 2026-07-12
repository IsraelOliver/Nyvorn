using System;

namespace Nyvorn.Source.Gameplay.Crafting
{
    // Flags: each crafting station is an independent bit, so being near one doesn't imply
    // access to recipes gated behind another (e.g. Furnace doesn't unlock Workbench recipes).
    [Flags]
    public enum CraftTier
    {
        Basic = 0,
        Workbench = 1 << 0,
        Furnace = 1 << 1
    }

    public static class CraftTierExtensions
    {
        public static bool Satisfies(this CraftTier available, CraftTier required)
        {
            return (required & available) == required;
        }
    }
}
