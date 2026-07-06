using Nyvorn.Source.Engine.Physics.Liquids;

namespace Nyvorn.Source.World.Generation
{
    public readonly record struct WorldGenLiquidPlacement(int X, int Y, LiquidType Type, int Amount);
}
