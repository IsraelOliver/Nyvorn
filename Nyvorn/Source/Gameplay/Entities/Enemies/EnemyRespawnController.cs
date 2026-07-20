using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public sealed class EnemyRespawnController
    {
        private readonly EnemyConfig enemyConfig;
        private readonly Texture2D enemyTexture;
        private readonly Func<Vector2> spawnPositionProvider;
        private readonly float respawnDelay;
        private readonly bool spawningEnabled;

        private float respawnTimer = -1f;

        public EnemyRespawnController(
            Texture2D enemyTexture,
            Func<Vector2> spawnPositionProvider,
            EnemyConfig enemyConfig = null,
            float respawnDelay = 3f,
            bool spawningEnabled = true)
        {
            this.enemyConfig = enemyConfig ?? EnemyConfig.Default;
            this.enemyTexture = enemyTexture;
            this.spawnPositionProvider = spawnPositionProvider ?? throw new ArgumentNullException(nameof(spawnPositionProvider));
            this.respawnDelay = respawnDelay;
            this.spawningEnabled = spawningEnabled;
        }

        public void Update(float dt, ICollection<Enemy> enemies, float delayMultiplier = 1f)
        {
            if (!spawningEnabled)
            {
                respawnTimer = -1f;
                return;
            }

            if (enemies.Count == 0)
            {
                if (respawnTimer < 0f)
                {
                    respawnTimer = respawnDelay * Math.Clamp(delayMultiplier, 0.25f, 2f);
                    return;
                }

                respawnTimer -= dt;
                if (respawnTimer <= 0f)
                {
                    enemies.Add(CreateEnemy());
                    respawnTimer = -1f;
                }

                return;
            }

            respawnTimer = -1f;
        }

        private Enemy CreateEnemy()
        {
            return new Enemy(enemyTexture, spawnPositionProvider(), enemyConfig);
        }

        // Debug/manual spawn entry point (e.g. the /spawn enemy console command) - reuses the same
        // texture as the normal respawn cycle but lets the caller pick position and config, so a
        // "signature" enemy can be dropped in for testing without wiring a whole spawn-rate rule.
        public Enemy SpawnAt(Vector2 position, EnemyConfig overrideConfig = null)
        {
            return new Enemy(enemyTexture, position, overrideConfig ?? enemyConfig);
        }
    }
}
