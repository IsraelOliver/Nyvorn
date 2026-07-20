using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI.Pathfinding
{
    public readonly struct PathWaypoint
    {
        public PathWaypoint(Vector2 worldPosition, bool requiresJump)
        {
            WorldPosition = worldPosition;
            RequiresJump = requiresJump;
        }

        public Vector2 WorldPosition { get; }

        // True when reaching this waypoint from the previous one needs a deliberate jump
        // (a validated jump-link), as opposed to a plain walk/step/fall.
        public bool RequiresJump { get; }
    }
}
