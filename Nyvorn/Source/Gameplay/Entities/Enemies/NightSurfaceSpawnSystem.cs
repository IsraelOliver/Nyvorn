using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Gameplay.Entities.Enemies.AI;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    // Terraria-style hostile spawning for surface enemies, gated to night: every SpawnAttemptInterval
    // seconds, if the surface population is under MaxSurfaceEnemies, tries to spawn one enemy just
    // off-screen on each side (left and right), independently. A candidate tile is valid if it has
    // solid ground and enough clear headroom above for the enemy's body - no distance-to-other-
    // enemies check, same as Terraria (the population cap alone controls density). Doesn't retry a
    // failed side within the same cycle; the next cycle (or a different camera position) tries again.
    //
    // Also owns despawning: any surface enemy that's been off-camera for DespawnGraceSeconds gets
    // removed, whether that's because it wandered off during the day or got left behind at night.
    public sealed class NightSurfaceSpawnSystem
    {
        private const float SpawnAttemptInterval = 3f;
        private const int MaxSurfaceEnemies = 8;
        private const float SpawnEdgeMargin = 16f;
        private const float DespawnGraceSeconds = 6f;

        private readonly Texture2D enemyTexture;
        private readonly EnemyConfig enemyConfig;
        private readonly Dictionary<Enemy, float> offScreenTimers = new();

        private float spawnAttemptTimer;

        public NightSurfaceSpawnSystem(Texture2D enemyTexture, EnemyConfig enemyConfig = null)
        {
            this.enemyTexture = enemyTexture;
            this.enemyConfig = enemyConfig ?? EnemyConfig.Default;
        }

        public void Update(
            float dt,
            bool isNight,
            WorldMap worldMap,
            Camera2D camera,
            int screenWidth,
            int screenHeight,
            List<Enemy> enemies)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            float viewWidth = screenWidth / camera.Zoom;
            float viewHeight = screenHeight / camera.Zoom;
            Vector2 cameraCenter = camera.Position + new Vector2(viewWidth * 0.5f, viewHeight * 0.5f);

            UpdateDespawns(dt, cameraCenter, viewWidth, viewHeight, worldMap.PixelWidth, enemies);

            if (!isNight)
            {
                spawnAttemptTimer = 0f;
                return;
            }

            spawnAttemptTimer -= dt;
            if (spawnAttemptTimer > 0f)
                return;

            spawnAttemptTimer = SpawnAttemptInterval;

            if (CountSurfaceEnemies(enemies) >= MaxSurfaceEnemies)
                return;

            TrySpawnAtSide(worldMap, cameraCenter, viewWidth, atRightSide: false, enemies);
            if (CountSurfaceEnemies(enemies) < MaxSurfaceEnemies)
                TrySpawnAtSide(worldMap, cameraCenter, viewWidth, atRightSide: true, enemies);
        }

        private static int CountSurfaceEnemies(List<Enemy> enemies)
        {
            int count = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i].IsAlive && enemies[i].Config.Habitat == EnemyHabitat.Surface)
                    count++;
            }

            return count;
        }

        private void TrySpawnAtSide(WorldMap worldMap, Vector2 cameraCenter, float viewWidth, bool atRightSide, List<Enemy> enemies)
        {
            int tileSize = worldMap.TileSize;
            float edgeWorldX = atRightSide
                ? cameraCenter.X + (viewWidth * 0.5f) + SpawnEdgeMargin
                : cameraCenter.X - (viewWidth * 0.5f) - SpawnEdgeMargin;

            int tileX = worldMap.WrapTileX((int)MathF.Floor(edgeWorldX / tileSize));
            if (!TryFindGroundTile(worldMap, tileX, out int groundTileY))
                return;

            Vector2 spawnPosition = new Vector2((tileX + 0.5f) * tileSize, groundTileY * tileSize);
            Enemy enemy = new Enemy(enemyTexture, spawnPosition, enemyConfig);

            // Spawned off-screen on purpose, which puts it outside PlayerAwarenessRange too - without
            // this it would stand frozen until the player wandered close enough to notice it organically.
            enemy.NoticePlayerImmediately();

            enemies.Add(enemy);
        }

        // Solid ground + clear headroom for the enemy's body, Terraria-style - no check against
        // other enemies, the population cap already controls density. Only checks the column
        // straight down from the sky, so it fails (rather than retrying elsewhere) if the surface
        // right at this tile is blocked - the next attempt a few seconds later tries again.
        private bool TryFindGroundTile(WorldMap worldMap, int tileX, out int groundTileY)
        {
            int heightTiles = Math.Max(1, (int)MathF.Ceiling(enemyConfig.HurtboxSize.Y / (float)worldMap.TileSize));

            for (int y = 0; y < worldMap.Height; y++)
            {
                if (!worldMap.IsSolidAt(tileX, y))
                    continue;

                for (int h = 1; h <= heightTiles; h++)
                {
                    if (worldMap.IsSolidAt(tileX, y - h))
                    {
                        groundTileY = 0;
                        return false;
                    }
                }

                groundTileY = y;
                return true;
            }

            groundTileY = 0;
            return false;
        }

        private void UpdateDespawns(float dt, Vector2 cameraCenter, float viewWidth, float viewHeight, float worldPixelWidth, List<Enemy> enemies)
        {
            float halfWidth = viewWidth * 0.5f;
            float halfHeight = viewHeight * 0.5f;

            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                Enemy enemy = enemies[i];
                if (!enemy.IsAlive || enemy.Config.Habitat != EnemyHabitat.Surface)
                {
                    offScreenTimers.Remove(enemy);
                    continue;
                }

                Vector2 offset = LoopAwareMath.GetOffset(cameraCenter, enemy.Position, worldPixelWidth);
                bool onScreen = MathF.Abs(offset.X) <= halfWidth && MathF.Abs(offset.Y) <= halfHeight;
                if (onScreen)
                {
                    offScreenTimers.Remove(enemy);
                    continue;
                }

                float timer = (offScreenTimers.TryGetValue(enemy, out float existing) ? existing : 0f) + dt;
                if (timer >= DespawnGraceSeconds)
                {
                    offScreenTimers.Remove(enemy);
                    enemies.RemoveAt(i);
                }
                else
                {
                    offScreenTimers[enemy] = timer;
                }
            }
        }
    }
}
