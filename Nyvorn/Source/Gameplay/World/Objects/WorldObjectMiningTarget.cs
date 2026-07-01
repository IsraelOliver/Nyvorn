using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public readonly record struct WorldObjectMiningTarget(WorldObjectMiningDefinition MiningDefinition, Rectangle Bounds)
    {
        public Vector2 Center => Bounds.Center.ToVector2();
    }
}
