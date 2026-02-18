using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.World;

// esse arquivo é a classe do player, onde implementamos a lógica de movimento, física, colisões e animação do personagem.
// Ele usa o Animator.cs pra controlar qual frame desenhar baseado no estado atual do player (Idle, Walk, Jump, Fall), e o PlayerAnimations.cs é o "banco de dados" das animações do player, onde cada estado tem um array de frames (Rectangles) associados a ele.

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public class Player
    {
        public Vector2 Position;
        private Vector2 Velocity;

        private bool isGrounded;

        // sprite parte visual
        public const int SpriteW = 17;
        public const int SpriteH = 24;

        // hitbox do sprite
        public const int HitW = 14;
        public const int HitH = 23;

        // centaliza a hitbox exatamente em volta da textura do sprite
        public const int HitOffX = 1;
        public const int HitOffY = 1;

        // deixa o pe dentro do chao 1 pixel - puramentente visual kk
        private const int FootSinkPx = 1;

        private const float moveSpeed = 150f;
        private const float jumpSpeed = 2000f;
        private float gravity = 8000f;

        private int moveDir;
        private bool jumpPressed;

        // Animação
        private readonly Texture2D _sheet;
        private bool facingRight = true;

        private Animator anim;
        private AnimationState animState = AnimationState.Idle;

        public Player(Vector2 startPosition, Texture2D sheet)
        {
            Position = startPosition;
            Velocity = Vector2.Zero;
            isGrounded = false;
            _sheet = sheet;

            anim = new Animator(PlayerAnimations.Create(), AnimationState.Idle);
        }

        // Helpers da hitbox em coordenadas de mundo (pixels)
        private float HitLeft   => Position.X + HitOffX;
        private float HitRight  => Position.X + HitOffX + HitW - 1;
        private float HitTop    => Position.Y + HitOffY;
        private float HitBottom => Position.Y + HitOffY + HitH - 1;

        public void Update(float dt, WorldMap worldMap, int screenW, int screenH)
        {
            float prevHitBottom = HitBottom;
            float prevHitTop    = HitTop;
         
            ReadInput();

            Velocity.X = moveDir * moveSpeed;
            Position.X += Velocity.X * dt;
            ResolveWorldCollisionsX(worldMap);

            ApplyGravity(dt);
            Position.Y += Velocity.Y * dt;
            ResolveWorldCollisionsY(worldMap, prevHitBottom, prevHitTop);

            UpdateAnimationState();

            anim.Play(animState);
            anim.Update(dt);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Rectangle src = anim.CurrentFrame;
            var fx = facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            var drawPos = new Vector2((float)System.Math.Round(Position.X), (float)System.Math.Round(Position.Y));
            spriteBatch.Draw(_sheet, drawPos, src, Color.White, 0f, Vector2.Zero, 1f, fx, 0f);
        }

        private void ReadInput()
        {
            KeyboardState teclado = Keyboard.GetState();

            moveDir = 0;
            if (teclado.IsKeyDown(Keys.D))
                moveDir = 1;
            else if (teclado.IsKeyDown(Keys.A))
                moveDir = -1;

            jumpPressed = teclado.IsKeyDown(Keys.Space);
        }

        private void UpdateAnimationState()
        {
            if (moveDir > 0) facingRight = true;
            else if (moveDir < 0) facingRight = false;

            if (isGrounded && jumpPressed)
            {
                Velocity.Y = -jumpSpeed;
                isGrounded = false;
            }

            const float apexThreshold = 5f;

            if (!isGrounded)
            {
                if(Velocity.Y < -apexThreshold)
                    animState = AnimationState.Jump;
                else if (Velocity.Y > apexThreshold)
                    animState = AnimationState.Fall;
                else
                    animState = AnimationState.Jump;
            } else
            {
                if (moveDir != 0)
                    animState = AnimationState.Walk;
                else
                    animState = AnimationState.Idle;
            }
        }

        private void ApplyGravity(float dt)
        {
            Velocity.Y += gravity * dt;
        }

        private void ResolveWorldCollisionsX(WorldMap worldMap)
        {
            int ts = worldMap.TileSize;

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
                    float tileLeft = tileX * ts;
                    Position.X = (tileLeft - HitW) - HitOffX;
                    Velocity.X = 0;
                }
            }
            else if (Velocity.X < 0)
            {
                float left = HitLeft;
                int tileX = (int)(left / ts);

                if (worldMap.IsSolidAt(tileX, tileYTop) || worldMap.IsSolidAt(tileX, tileYBottom))
                {
                    float tileRight = tileX * ts + ts;
                    Position.X = tileRight - HitOffX;
                    Velocity.X = 0;
                }
            }
        }

        private void ResolveWorldCollisionsY(WorldMap worldMap, float prevHitBottom, float prevHitTop)
        {
            isGrounded = false;

            int ts = worldMap.TileSize;

            float left = HitLeft;
            float right = HitRight;

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
                        float tileTop = y * ts;
                        float newHitTop = (tileTop - HitH) + FootSinkPx;

                        Position.Y = newHitTop - HitOffY;

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
                        float tileBottom = y * ts + ts;

                        float newHitTop = tileBottom;

                        Position.Y = newHitTop - HitOffY;
                        Velocity.Y = 0;
                        return;
                    }
                }
            }
        }

        private void KeyboardCheck(float dt)
        {
            KeyboardState teclado = Keyboard.GetState();

            if (isGrounded && teclado.IsKeyDown(Keys.Space))
            {
                Velocity.Y = -260f;
                animState = AnimationState.Jump;
                isGrounded = false;
            }

            Velocity.X = 0f;

            if (teclado.IsKeyDown(Keys.D))
            {
                Velocity.X = moveSpeed;
                animState = AnimationState.Walk;
            } else if (teclado.IsKeyDown(Keys.A))
            {
                Velocity.X = -moveSpeed;
                animState = AnimationState.Walk;
            } else if (isGrounded)
            {
                animState = AnimationState.Idle;
            } else if (Velocity.Y > 0)
            {
                animState = AnimationState.Fall;
            }
        }
    }
}
