using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Entities.Enemies.EnemyAnimations;
using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public class Enemy
    {
        private readonly Texture2D texture;
        private readonly Texture2D debugPixel;
        private readonly EnemyAnimator animator;

        private const int FrameW = 32;
        private const int FrameH = 32;
        private Vector2 position;
        private float velocityY;
        private float knockbackVelocityX;
        private float attackTimer;
        private float hurtTimer;

        private readonly int maxHealth;
        private int health;
        private int lastDamageAttackSequence = -1;

        public Vector2 Position => position;
        public bool IsAlive => health > 0;
        public int Health => health;
        public int MaxHealth => maxHealth;

        private float HitLeft => position.X - 8f;
        private float HitRight => HitLeft + 16f - 1f;
        private float HitBottom => position.Y;
        private float HitTop => HitBottom - 24f + 1f;
        public Rectangle Hurtbox => new Rectangle((int)HitLeft, (int)HitTop, 16, 24);

        public Enemy(Texture2D texture, Vector2 position, int maxHealth = 100)
        {
            this.texture = texture;
            this.position = position;
            this.maxHealth = maxHealth;
            health = maxHealth;
            velocityY = 0f;
            knockbackVelocityX = 0f;
            attackTimer = 0f;
            hurtTimer = 0f;

            animator = new EnemyAnimator(EnemyTestAnimations.Create(), EnemyAnimState.Idle);

            debugPixel = new Texture2D(texture.GraphicsDevice, 1, 1);
            debugPixel.SetData(new[] { Color.Lime });
        }

        public void Update(float dt, WorldMap worldMap)
        {
            if (attackTimer > 0f) attackTimer -= dt;
            if (hurtTimer > 0f) hurtTimer -= dt;

            float prevHitBottom = HitBottom;
            float prevHitTop = HitTop;

            position.X += knockbackVelocityX * dt;
            knockbackVelocityX = MathHelper.Lerp(knockbackVelocityX, 0f, MathHelper.Clamp(dt * 10f, 0f, 1f));

            velocityY += 800f * dt;
            position.Y += velocityY * dt;
            ResolveWorldCollisionsY(worldMap, prevHitBottom, prevHitTop);

            EnemyAnimState state = ResolveAnimState();
            animator.Play(state);
            animator.Update(dt);
        }

        public bool TryReceiveDamage(Rectangle attackHitbox, int attackSequence, int damage)
        {
            if (!IsAlive)
                return false;

            if (attackSequence == lastDamageAttackSequence)
                return false;

            if (!attackHitbox.Intersects(Hurtbox))
                return false;

            health = Math.Max(0, health - damage);
            lastDamageAttackSequence = attackSequence;
            hurtTimer = 0.15f;
            return true;
        }

        public void ApplyKnockback(float forceX, float forceY = -55f)
        {
            knockbackVelocityX = forceX;
            if (forceY < velocityY)
                velocityY = forceY;
        }

        public void TriggerAttackVisual(float duration = 0.12f)
        {
            if (!IsAlive)
                return;

            if (duration > attackTimer)
                attackTimer = duration;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!IsAlive)
                return;

            Rectangle src = animator.CurrentFrame;
            if (src == Rectangle.Empty)
                src = new Rectangle(0, 1 * FrameH, FrameW, FrameH);

            Vector2 origin = new Vector2(16f, 32f);
            spriteBatch.Draw(texture, Position, src, Color.White, 0f, origin, 1f, SpriteEffects.None, 0f);
            spriteBatch.Draw(debugPixel, Hurtbox, Color.Lime * 0.5f);
        }

        private void ResolveWorldCollisionsY(WorldMap worldMap, float prevHitBottom, float prevHitTop)
        {
            int ts = worldMap.TileSize;

            float left = HitLeft + 1;
            float right = HitRight - 1;
            int tileXLeft = (int)(left / ts);
            int tileXRight = (int)(right / ts);

            if (velocityY > 0)
            {
                int fromY = (int)(prevHitBottom / ts);
                int toY = (int)(HitBottom / ts);

                for (int y = fromY; y <= toY; y++)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        position.Y = y * ts;
                        velocityY = 0f;
                        return;
                    }
                }
            }
            else if (velocityY < 0)
            {
                int fromY = (int)(prevHitTop / ts);
                int toY = (int)(HitTop / ts);

                for (int y = fromY; y >= toY; y--)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        float tileBottom = y * ts + ts;
                        position.Y = tileBottom + 24f - 1f;
                        velocityY = 0f;
                        return;
                    }
                }
            }
        }

        private EnemyAnimState ResolveAnimState()
        {
            if (!IsAlive)
                return EnemyAnimState.Dead;

            if (hurtTimer > 0f)
                return EnemyAnimState.Hurt;

            if (attackTimer > 0f)
                return EnemyAnimState.Attack;

            bool isMoving = Math.Abs(knockbackVelocityX) > 8f;
            if (isMoving)
                return EnemyAnimState.Move;

            return EnemyAnimState.Idle;
        }

    }
}
