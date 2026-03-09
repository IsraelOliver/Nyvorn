using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    public class Enemy
    {
        private readonly Texture2D texture;

        private const int FrameW = 32;
        private const int FrameH = 32;
        private const int WalkRow = 0;
        private const int IdleJumpFallRow = 1;
        private const int WalkFrameStart = 0;
        private const int WalkFrameEnd = 5;
        private const int IdleCol = 0;
        private const int JumpCol = 1;
        private const int FallCol = 2;

        private int animRow = IdleJumpFallRow;
        private int animCol = IdleCol;
        private float animTimer;
        private Vector2 position;
        private float velocityY;
        private float knockbackVelocityX;
        private bool isGrounded;

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
            animRow = IdleJumpFallRow;
            animCol = IdleCol;
            animTimer = 0f;
            velocityY = 0f;
            knockbackVelocityX = 0f;
            isGrounded = false;
        }

        public void Update(float dt, WorldMap worldMap)
        {
            float prevHitBottom = HitBottom;
            float prevHitTop = HitTop;

            position.X += knockbackVelocityX * dt;
            knockbackVelocityX = MathHelper.Lerp(knockbackVelocityX, 0f, MathHelper.Clamp(dt * 10f, 0f, 1f));

            velocityY += 800f * dt;
            position.Y += velocityY * dt;
            ResolveWorldCollisionsY(worldMap, prevHitBottom, prevHitTop);
            UpdateAnimation(dt);
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
            return true;
        }

        public void ApplyKnockback(float forceX, float forceY = -55f)
        {
            knockbackVelocityX = forceX;
            if (forceY < velocityY)
                velocityY = forceY;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!IsAlive)
                return;

            Rectangle src = new Rectangle(animCol * FrameW, animRow * FrameH, FrameW, FrameH);
            Vector2 origin = new Vector2(16f, 32f);
            spriteBatch.Draw(texture, Position, src, Color.White, 0f, origin, 1f, SpriteEffects.None, 0f);
        }

        private void UpdateAnimation(float dt)
        {
            const float apexThreshold = 6f;

            if (!isGrounded)
            {
                animRow = IdleJumpFallRow;
                animCol = velocityY < -apexThreshold ? JumpCol : FallCol;
                animTimer = 0f;
                return;
            }

            bool isWalking = Math.Abs(knockbackVelocityX) > 8f;

            if (!isWalking)
            {
                animRow = IdleJumpFallRow;
                animCol = IdleCol;
                animTimer = 0f;
                return;
            }

            animRow = WalkRow;
            animTimer += dt;

            const float walkFrameTime = 0.1f;
            while (animTimer >= walkFrameTime)
            {
                animTimer -= walkFrameTime;
                animCol++;
                if (animCol > WalkFrameEnd || animCol < WalkFrameStart)
                    animCol = WalkFrameStart;
            }
        }

        private void ResolveWorldCollisionsY(WorldMap worldMap, float prevHitBottom, float prevHitTop)
        {
            isGrounded = false;
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
                        isGrounded = true;
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

    }
}
