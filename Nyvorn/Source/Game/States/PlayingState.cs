using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Gameplay.Entities.Enemies;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Game.States
{
    public class PlayingState : IGameState
    {
        private readonly GraphicsDevice graphicsDevice;

        private readonly WorldMap worldMap;

        private readonly Player player;
        private readonly List<Enemy> enemies = new();
        private readonly Texture2D enemyTexture;
        private readonly Vector2 enemySpawnPosition = new Vector2(118, 50);
        private readonly Camera2D camera;
        private readonly InputService inputService = new();
        private const float EnemyRespawnDelay = 3f;
        private float enemyRespawnTimer = -1f;
        private int lastLoggedPlayerHealth = -1;
        private int lastLoggedEnemyHealth = -1;

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content)
        {
            this.graphicsDevice = graphicsDevice;

            var dirt = content.Load<Texture2D>("blocks/dirt_block");
            var sand = content.Load<Texture2D>("blocks/sand_block");
            var stone = content.Load<Texture2D>("blocks/stone_block");

            worldMap = new WorldMap(100, 50, 8);
            worldMap.SetTextures(dirt, sand, stone);
            worldMap.GenerateTest();

            var backHandTexture = content.Load<Texture2D>("entities/player/handBackTexture_base");
            var bodyTexture = content.Load<Texture2D>("entities/player/bodyTexture_base");
            var legsTexture = content.Load<Texture2D>("entities/player/legsTexture_base");
            var frontHandTexture = content.Load<Texture2D>("entities/player/handFrontTexture_base");

            var shortStickTexture = content.Load<Texture2D>("weapons/shortStick");
            var handFront_weaponRun = content.Load<Texture2D>("entities/player/handFront_weaponRun");

            var attackHandbackTexture = content.Load<Texture2D>("entities/player/handBackShortSword_attack");
            var attackHandfrontTexture = content.Load<Texture2D>("entities/player/handFrontShortSword_attack");
            var attackBodyTexture = content.Load<Texture2D>("entities/player/bodyShortSword_attack");
            enemyTexture = content.Load<Texture2D>("entities/enemy/enemy_test");

            player = new Player(new Vector2(90, 50), bodyTexture, backHandTexture, frontHandTexture, attackHandbackTexture, attackHandfrontTexture, attackBodyTexture, legsTexture, shortStickTexture, handFront_weaponRun);
            SpawnEnemy();
            camera = new Camera2D();
        }

        public void OnEnter() { }

        public void OnExit() { }

        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            InputState input = inputService.Update();
            Vector2 mouseWorld = camera.ScreenToWorld(input.MouseScreenPosition);

            player.Update(dt, worldMap, input, mouseWorld);

            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                Enemy enemy = enemies[i];
                enemy.Update(dt, worldMap);

                if (player.HasActiveAttackHitbox)
                {
                    bool tookDamage = enemy.TryReceiveDamage(player.AttackHitbox, player.AttackSequence, damage: 20);
                    if (tookDamage)
                    {
                        float dir = enemy.Position.X >= player.Position.X ? 1f : -1f;
                        enemy.ApplyKnockback(150f * dir, -65f);
                    }
                }

                if (enemy.IsAlive && player.IsAlive && player.Hurtbox.Intersects(enemy.Hurtbox))
                {
                    bool tookDamage = player.TryReceiveDamage(damage: 10);
                    if (tookDamage)
                    {
                        enemy.TriggerAttackVisual();
                        float dir = player.Position.X >= enemy.Position.X ? 1f : -1f;
                        player.ApplyKnockback(180f * dir, -75f);
                    }
                }

                if (!enemy.IsAlive)
                    enemies.RemoveAt(i);
            }

            LogHealthToConsole();

            if (enemies.Count == 0)
            {
                if (enemyRespawnTimer < 0f)
                    enemyRespawnTimer = EnemyRespawnDelay;
                else
                {
                    enemyRespawnTimer -= dt;
                    if (enemyRespawnTimer <= 0f)
                    {
                        SpawnEnemy();
                        enemyRespawnTimer = -1f;
                    }
                }
            }
            else
            {
                enemyRespawnTimer = -1f;
            }

            camera.Follow(player.Position + new Vector2(8f, 12f), screenW, screenH);

        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: camera.GetViewMatrix());

            worldMap.Draw(spriteBatch);

            player.Draw(spriteBatch);
            foreach (var enemy in enemies)
            {
                enemy.Draw(spriteBatch);
            }

            spriteBatch.End();
        }

        private void SpawnEnemy()
        {
            enemies.Add(new Enemy(enemyTexture, enemySpawnPosition, maxHealth: 100));
            lastLoggedEnemyHealth = -1;
        }

        private void LogHealthToConsole()
        {
            if (player.Health != lastLoggedPlayerHealth)
            {
                Console.WriteLine($"Player HP: {player.Health}/{player.MaxHealth}");
                lastLoggedPlayerHealth = player.Health;
            }

            int enemyHealth = enemies.Count > 0 ? enemies[0].Health : 0;
            int enemyMax = enemies.Count > 0 ? enemies[0].MaxHealth : 100;

            if (enemyHealth != lastLoggedEnemyHealth)
            {
                Console.WriteLine($"Enemy HP: {enemyHealth}/{enemyMax}");
                lastLoggedEnemyHealth = enemyHealth;
            }
        }
    }
}
