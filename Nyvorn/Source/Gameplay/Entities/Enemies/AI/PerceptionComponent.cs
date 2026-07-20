using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Enemies;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI
{
    // Sensor layer: turns raw world/physics state into a Perception snapshot the brain reasons
    // over. Kept separate from the brain so decision logic never touches raw positions/collision
    // flags directly - swapping in a smarter brain later only means consuming more of this struct.
    public sealed class PerceptionComponent
    {
        public Perception Scan(
            Vector2 selfPosition,
            Vector2 targetPosition,
            float worldWidth,
            bool onGround,
            bool blockedHorizontally,
            EnemyConfig config)
        {
            Vector2 offset = LoopAwareMath.GetOffset(selfPosition, targetPosition, worldWidth);
            bool seesTarget = System.MathF.Abs(offset.X) <= config.PlayerAwarenessRange
                && System.MathF.Abs(offset.Y) <= config.PlayerVerticalAwarenessRange;

            return new Perception
            {
                SeesTarget = seesTarget,
                TargetOffset = offset,
                OnGround = onGround,
                BlockedHorizontally = blockedHorizontally
            };
        }
    }
}
