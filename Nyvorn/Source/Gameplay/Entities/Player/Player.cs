using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public class Player
    {
        public Vector2 Position; //é o pivot do pé
        private Vector2 Velocity;

        private bool isGrounded;
        private bool isAttacking;
        private float attackTimer;
        private const float AttackDuration = 0.3f;

        // Textura do player
        public const int SpriteW = 32;
        public const int SpriteH = 32;

        private readonly Texture2D _body;
        private readonly Texture2D _handBack;
        private readonly Texture2D _handFront;

        // Hitbox do player - fica envolta apenas do player
        public const int HitW = 10;
        public const int HitH = 23;

        private const float moveSpeed = 80f;
        private const float jumpSpeed = 280f;
        private const float gravity = 800f;

        private int moveDir;
        private bool jumpPressed;

        // Animação
        private bool facingRight = true;

        private readonly Animator anim;
        private readonly Animator animAttack;

        private AnimationState animState = AnimationState.Idle;

        private readonly Texture2D _attackHandBack;
        private readonly Texture2D _attackHandFront;
        private readonly Texture2D _attackBody;

        public Player(Vector2 startPositionPivotFoot, Texture2D sheet, Texture2D handBack, Texture2D handFront, Texture2D attackHandBack, Texture2D attackHandFront, Texture2D attackBody)
        {
            Position = startPositionPivotFoot;
            Velocity = Vector2.Zero;
            isGrounded = false;

            _body = sheet;
            _handBack = handBack;
            _handFront = handFront;

            _attackHandBack = attackHandBack;
            _attackHandFront = attackHandFront;
            _attackBody = attackBody;

            anim = new Animator(PlayerAnimations.CreateBase(), AnimationState.Idle);
            animAttack = new Animator(PlayerAnimations.CreateAttackShortSword(), AnimationState.Attack);
        }

        private float HitLeft   => Position.X - (HitW * 0.5f);
        private float HitRight  => HitLeft + HitW - 1;
        private float HitBottom => Position.Y;
        private float HitTop    => HitBottom - HitH + 1;

        public void Update(float dt, WorldMap worldMap, int screenW, int screenH)
        {
            float prevHitBottom = HitBottom;
            float prevHitTop = HitTop;
            
            ReadInput();

            Attack(dt);

            // Horizontal
            Velocity.X = moveDir * moveSpeed;
            Position.X += Velocity.X * dt;
            ResolveWorldCollisionsX(worldMap);

            // Vertical
            ApplyGravity(dt);
            Position.Y += Velocity.Y * dt;
            ResolveWorldCollisionsY(worldMap, prevHitBottom, prevHitTop);

            UpdateAnimationState();

            anim.Play(animState);
            anim.Update(dt);
            if (isAttacking)
            {
                animAttack.Update(dt);
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Rectangle src = anim.CurrentFrame;
            Rectangle attackSrc = animAttack.CurrentFrame;
            var fx = facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            const float VisualFootSink = 1f;

            var drawPos = new Vector2(
                (float)System.Math.Round(Position.X),
                (float)System.Math.Round(Position.Y + VisualFootSink)
            );

            var origin = new Vector2(16f, 32f);

            
            spriteBatch.Draw(_body, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
            
            if (!isAttacking)
            {   
                spriteBatch.Draw(_handBack, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(_handFront, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);

            } else
            {   
                spriteBatch.Draw(_attackHandBack, drawPos, attackSrc, Color.White * 0.8f, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(_attackBody, drawPos, attackSrc, Color.White * 0.8f, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(_attackHandFront, drawPos, attackSrc, Color.White * 0.8f, 0f, origin, 1f, fx, 0f);
            }
        }

        private void ReadInput()
        {
            KeyboardState teclado = Keyboard.GetState();
            MouseState mouse = Mouse.GetState();

            moveDir = 0;
            if (teclado.IsKeyDown(Keys.D)) moveDir = 1;
            else if (teclado.IsKeyDown(Keys.A)) moveDir = -1;

            jumpPressed = teclado.IsKeyDown(Keys.Space);

            if (!isAttacking && mouse.LeftButton == ButtonState.Pressed)
            {
                isAttacking = true;
                attackTimer = AttackDuration;
                
                animAttack.Reset();
                animAttack.Play(AnimationState.Attack);
            }
        }

        private void UpdateAnimationState()
        {
            if (moveDir > 0) facingRight = true;
            else if (moveDir < 0) facingRight = false;

            if (isAttacking)
            {

            }

            if (isGrounded && jumpPressed)
            {
                Velocity.Y = -jumpSpeed;
                isGrounded = false;
            }

            const float apexThreshold = 5f;

            if (!isGrounded)
            {
                if (Velocity.Y < -apexThreshold) animState = AnimationState.Jump;
                else if (Velocity.Y > apexThreshold) animState = AnimationState.Fall;
                else animState = AnimationState.Jump;
            }
            else
            {
                animState = (moveDir != 0) ? AnimationState.Walk : AnimationState.Idle;
            }
        }

        private void Attack(float dt)
        {
            if (isAttacking)
            {
                attackTimer -= dt;
                if (attackTimer <= 0f)
                {
                    isAttacking = false;
                }
            }
        }

        private void ApplyGravity(float dt)
        {
            Velocity.Y += gravity * dt;
        }

        private void ResolveWorldCollisionsX(WorldMap worldMap)
        {
            int ts = worldMap.TileSize;

            // Amostras Y dentro da hitbox
            float top = HitTop + 1;
            float bottom = HitBottom - 1;

            int tileYTop = (int)(top / ts);
            int tileYBottom = (int)(bottom / ts);

            if (Velocity.X > 0)
            {
                float right = HitRight;
                int tileX = (int)(right / ts);

                if (worldMap.IsSolidAt(tileX, tileYTop) || worldMap.IsSolidAt(tileX, tileYBottom))
                {
                    // Encosta o lado direito da hitbox na borda esquerda do tile sólido
                    float tileLeft = tileX * ts;
                    float newHitLeft = tileLeft - HitW;

                    Position.X = newHitLeft + (HitW * 0.5f);
                    Velocity.X = 0;
                }
            }
            else if (Velocity.X < 0)
            {
                float left = HitLeft;
                int tileX = (int)(left / ts);

                if (worldMap.IsSolidAt(tileX, tileYTop) || worldMap.IsSolidAt(tileX, tileYBottom))
                {
                    // Encosta o lado esquerdo da hitbox na borda direita do tile sólido
                    float tileRight = tileX * ts + ts;
                    float newHitLeft = tileRight;

                    Position.X = newHitLeft + (HitW * 0.5f);
                    Velocity.X = 0;
                }
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

            if (Velocity.Y > 0)
            {
                float bottom = HitBottom;

                int fromY = (int)(prevHitBottom / ts);
                int toY = (int)(bottom / ts);

                for (int y = fromY; y <= toY; y++)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        // Encosta o chão (HitBottom) no topo do tile
                        float tileTop = y * ts;
                        Position.Y = tileTop;

                        Velocity.Y = 0;
                        isGrounded = true;
                        return;
                    }
                }
            }
            else if (Velocity.Y < 0)
            {
                float top = HitTop;

                int fromY = (int)(prevHitTop / ts);
                int toY = (int)(top / ts);

                for (int y = fromY; y >= toY; y--)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        // Encosta o topo da hitbox no fundo do tile
                        float tileBottom = y * ts + ts;
                        float newHitTop = tileBottom;

                        // HitTop = Position.Y - HitH + 1  =>  Position.Y = HitTop + HitH - 1
                        Position.Y = newHitTop + HitH - 1;

                        Velocity.Y = 0;
                        return;
                    }
                }
            }
        }
    }
}