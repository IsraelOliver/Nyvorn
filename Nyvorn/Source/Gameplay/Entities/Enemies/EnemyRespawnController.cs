using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public sealed class EnemyRespawnController
    {
        private readonly EnemyConfig enemyConfig;
        private readonly Texture2D enemyTexture;
        private readonly Vector2 spawnPosition;
        private readonly float respawnDelay;

        private float respawnTimer = -1f;

        public EnemyRespawnController(Texture2D enemyTexture, Vector2 spawnPosition, EnemyConfig enemyConfig = null, float respawnDelay = 3f)
        {
            this.enemyConfig = enemyConfig ?? EnemyConfig.Default;
            this.enemyTexture = enemyTexture;
            this.spawnPosition = spawnPosition;
            this.respawnDelay = respawnDelay;
        }

        public void SpawnInitial(ICollection<Enemy> enemies)
        {
            if (enemies.Count > 0)
                return;

            enemies.Add(CreateEnemy());
        }

        public void Update(float dt, ICollection<Enemy> enemies)
        {
            if (enemies.Count == 0)
            {
                if (respawnTimer < 0f)
                {
                    respawnTimer = respawnDelay;
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
            return new Enemy(enemyTexture, spawnPosition, enemyConfig);
        }
    }
}
