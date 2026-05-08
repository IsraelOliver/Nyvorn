using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Enemies;
using Nyvorn.Source.Gameplay.Items;
using System;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionEntityRuntimeSystem
    {
        private const float EntitySimulationRangeInTiles = 56f;

        public required SessionRuntimeContext RuntimeContext { get; init; }
        public required WorldItemRuntimeSystem WorldItemRuntimeSystem { get; init; }
        public required EnemyRespawnController EnemyRespawnController { get; init; }

        public void Update(float dt)
        {
            for (int i = RuntimeContext.Enemies.Count - 1; i >= 0; i--)
            {
                if (IsWithinSimulationRange(RuntimeContext.Enemies[i].Position))
                    RuntimeContext.Enemies[i].Update(dt, RuntimeContext.WorldMap);
            }

            WorldItemRuntimeSystem.Update(dt, IsWithinSimulationRange);
            EnemyRespawnController.Update(dt, RuntimeContext.Enemies);
        }

        private bool IsWithinSimulationRange(Vector2 worldPosition)
        {
            float maxDistance = RuntimeContext.WorldMap.TileSize * EntitySimulationRangeInTiles;
            return GetLoopAwareDistance(worldPosition, RuntimeContext.Player.Position) <= maxDistance;
        }

        private float GetLoopAwareDistance(Vector2 a, Vector2 b)
        {
            float worldWidth = RuntimeContext.WorldMap.PixelWidth;
            float deltaX = a.X - b.X;

            if (deltaX > worldWidth * 0.5f)
                deltaX -= worldWidth;
            else if (deltaX < -worldWidth * 0.5f)
                deltaX += worldWidth;

            float deltaY = a.Y - b.Y;
            return MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        }
    }
}
