using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Gameplay.Entities.Enemies;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.World.Simulation;
using System;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionEntityRuntimeSystem
    {
        // Buffer beyond the visible screen, in tiles - keeps entities simulated a bit past the edge
        // of view, not just barely inside it.
        private const float SimulationRangeMarginTiles = 20f;

        public required SessionRuntimeContext RuntimeContext { get; init; }
        public required WorldItemRuntimeSystem WorldItemRuntimeSystem { get; init; }
        public required EnemyRespawnController EnemyRespawnController { get; init; }
        public WorldEnvironmentSystem EnvironmentSystem { get; set; }
        public WorldDayNightCycle DayNightCycle { get; set; }
        public NightSurfaceSpawnSystem NightSurfaceSpawnSystem { get; set; }
        public SandSystem SandSystem { get; set; }

        public void Update(float dt, int screenWidth, int screenHeight)
        {
            // Full night strength only (not dawn/dusk twilight) - surface enemies are otherwise
            // passive/wandering, see GroundChaserBrain. Cave enemies (once they exist) are always
            // "hostile time" regardless of the clock, since this rule is surface-only.
            bool isNight = (DayNightCycle?.NightStrength ?? 0f) >= 1f;

            // Derived from the actual screen/zoom/tile size instead of a fixed tile count: with an
            // 8px tile size, a flat "56 tiles" (448px) can be smaller than half the screen at higher
            // resolutions or lower zoom, so an enemy spawned just off-screen (NightSurfaceSpawnSystem)
            // fell outside the old fixed range and never got Update() called at all - it sat frozen
            // until the player closed the distance to inside 448px, which on those screens meant
            // "until it scrolled into view."
            float simulationRange = ComputeSimulationRange(screenWidth, screenHeight);

            for (int i = RuntimeContext.Enemies.Count - 1; i >= 0; i--)
            {
                Enemy enemy = RuntimeContext.Enemies[i];
                if (!IsWithinSimulationRange(enemy.Position, simulationRange))
                    continue;

                bool isHostileTime = isNight || enemy.Config.Habitat != EnemyHabitat.Surface;
                enemy.Update(dt, RuntimeContext.WorldMap, SandSystem, RuntimeContext.Player.Position, isHostileTime);
            }

            WorldItemRuntimeSystem.Update(dt, worldPosition => IsWithinSimulationRange(worldPosition, simulationRange));
            float respawnDelayMultiplier = EnvironmentSystem?.EnemyRespawnDelayMultiplier ?? 1f;
            EnemyRespawnController.Update(dt, RuntimeContext.Enemies, respawnDelayMultiplier);

            NightSurfaceSpawnSystem?.Update(
                dt, isNight, RuntimeContext.WorldMap, RuntimeContext.Camera, screenWidth, screenHeight, RuntimeContext.Enemies);
        }

        private float ComputeSimulationRange(int screenWidth, int screenHeight)
        {
            float zoom = RuntimeContext.Camera.Zoom > 0f ? RuntimeContext.Camera.Zoom : 1f;
            float halfViewWidth = (screenWidth / zoom) * 0.5f;
            float halfViewHeight = (screenHeight / zoom) * 0.5f;
            float halfViewExtent = Math.Max(halfViewWidth, halfViewHeight);
            return halfViewExtent + (RuntimeContext.WorldMap.TileSize * SimulationRangeMarginTiles);
        }

        private bool IsWithinSimulationRange(Vector2 worldPosition, float maxDistance)
        {
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
