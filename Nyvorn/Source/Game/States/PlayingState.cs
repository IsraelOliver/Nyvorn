using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Nyvorn.Source.Game.States
{
    public class PlayingState
    {
        GraphicsDevice graphicsDevice;

        private Texture2D _red;
        public Vector2 position;
        private Vector2 velocity;

        private const float moveSpeed = 150f; //velocidade
        private float gravity = 800; // Gravidade

        private bool isGrounded;

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content)
        {
            this.graphicsDevice = graphicsDevice;

            _red = new Texture2D(graphicsDevice, 1, 1);
            _red.SetData(new[] { Color.White });

            position = new Vector2(50, 50);
        }

        public void Update(GameTime gameTime)
        {

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            const int redW = 64;
            const int redH = 64;

            // aplica a gravidade
            ApplyGravity(dt);
            position.Y += velocity.Y * dt;

            // colisão com as bordas da tela.
            ResolveFloor(screenH, redH);

            // chama o teclado
            KeyboardCheck(dt);

            position.X = MathHelper.Clamp(position.X, 0, screenW - redW);
        }

        private void ApplyGravity(float dt)
        {
            velocity.Y += gravity * dt;
        }

        private void ResolveFloor(int screenH, int redH)
        {
            isGrounded = false;
            float floorY = screenH - redH;
            if (position.Y >= floorY)
            {
                position.Y = floorY;
                velocity.Y = 0;
                isGrounded = true;
            }
        }

        private void KeyboardCheck(float deltaTime)
        {
            KeyboardState teclado = Keyboard.GetState();
            float speed = moveSpeed * deltaTime;

            if (isGrounded)
            {
                if (teclado.IsKeyDown(Keys.Space))
                {
                    velocity.Y -= 500f;
                    isGrounded = false;
                }
            }

            if (teclado.IsKeyDown(Keys.D))
            {
                position.X += speed;
            }
            else if (teclado.IsKeyDown(Keys.A))
            {
                position.X -= speed;
            }
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin();

            spriteBatch.Draw(_red, new Rectangle((int)position.X, (int)position.Y, 64, 64), Color.Red);

            spriteBatch.End();
        }
    }
}