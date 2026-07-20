using Microsoft.Xna.Framework;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI
{
    public interface IEnemyBrain
    {
        EnemyIntent CurrentIntent { get; }

        void NotifyHit(float knockbackX);

        EnemyBrainDecision Update(
            float dt,
            in Perception perception,
            Vector2 selfPosition,
            float worldWidth,
            int health,
            int maxHealth,
            WorldMap worldMap);
    }
}
