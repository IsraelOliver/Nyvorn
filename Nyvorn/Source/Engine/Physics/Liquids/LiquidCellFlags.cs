using System;

namespace Nyvorn.Source.Engine.Physics.Liquids
{
    [Flags]
    public enum LiquidCellFlags : byte
    {
        None = 0,
        Dirty = 1,
        Active = 2
    }
}
