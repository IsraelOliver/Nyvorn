using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public class Player
    {
        public Vector2 Position;     // posição do SPRITE (canto superior esquerdo do frame 17x24)
        private Vector2 Velocity;

        private bool isGrounded;

        // FRAME do sprite (visual)
        public const int SpriteW = 17;
        public const int SpriteH = 24;

        // HITBOX real (colisão)
        public const int HitW = 14;
        public const int HitH = 23;

        // Offsets da hitbox dentro do frame (começa no 2 e vai até 15/24 => offset 1)
        public const int HitOffX = 1;
        public const int HitOffY = 1;

        // Se você quer 1px do pé "dentro" do chão:
        private const int FootSinkPx = 1; // coloque 0 se não quiser

        private const float moveSpeed = 150f;
        private float gravity = 800f;

        // Animação
        private readonly Texture2D _sheet;
        private int frameW = 17, frameH = 24;
        private int frameX = 0, frameY = 1;
        private float animTimer = 0f;
        private float frameTime = 0.08f;
        private bool facingRight = true;

        public Player(Vector2 startPosition, Texture2D sheet)
        {
            Position = startPosition;
            Velocity = Vector2.Zero;
            isGrounded = false;
            _sheet = sheet;
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

            KeyboardCheck(dt);

            Position.X += Velocity.X * dt;
            ResolveWorldCollisionsX(worldMap);

            ApplyGravity(dt);
            Position.Y += Velocity.Y * dt;
            ResolveWorldCollisionsY(worldMap, prevHitBottom, prevHitTop);

            UpdateAnimation(dt);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Rectangle src = new Rectangle(frameX * frameW, frameY * frameH, frameW, frameH);
            var fx = facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            var drawPos = new Vector2((float)System.Math.Round(Position.X), (float)System.Math.Round(Position.Y));
            spriteBatch.Draw(_sheet, drawPos, src, Color.White, 0f, Vector2.Zero, 1f, fx, 0f);
        }

        private void UpdateAnimation(float dt)
        {
            bool isMoving = Velocity.X != 0;

            if (isMoving)
            {
                frameY = 0; // correndo
                facingRight = Velocity.X > 0;

                animTimer += dt;
                if (animTimer >= frameTime)
                {
                    animTimer -= frameTime;
                    frameX = (frameX + 1) % 8;
                }
            }
            else
            {
                frameY = 1; // parado
                frameX = 0;
                animTimer = 0f;
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
                isGrounded = false;
            }

            Velocity.X = 0f;

            if (teclado.IsKeyDown(Keys.D))
                Velocity.X = moveSpeed;
            else if (teclado.IsKeyDown(Keys.A))
                Velocity.X = -moveSpeed;
        }
    }
}
