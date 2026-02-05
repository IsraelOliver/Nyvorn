using System;
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
        private Texture2D _plataform;
        public Vector2 position;
        public Vector2 platPosition;
        private Vector2 velocity;

        private const float moveSpeed = 150f; //velocidade
        private float gravity = 800; // Gravidade

        private bool isGrounded;

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content)
        {
            this.graphicsDevice = graphicsDevice;

            _red = new Texture2D(graphicsDevice, 1, 1);
            _red.SetData(new[] { Color.White });

            _plataform = new Texture2D(graphicsDevice, 1, 1);
            _plataform.SetData(new[] { Color.White });

            position = new Vector2(50, 50);
            platPosition = new Vector2(10, 350);
        }

        public void Update(GameTime gameTime)
        {

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            const int redW = 64;
            const int redH = 64;

            float prevY = position.Y;
            float prevBottom = (int)(prevY + redH);
            

            // aplica a gravidade
            ApplyGravity(dt);
            position.Y += velocity.Y * dt;

            // colisão com as bordas da tela.
            ResolveFloor(screenH, redW, redH, prevBottom);

            // chama o teclado
            KeyboardCheck(dt);

            position.X = MathHelper.Clamp(position.X, 0, screenW - redW);
        }

        private void ApplyGravity(float dt)
        {
            velocity.Y += gravity * dt;
        }

        private void ResolveFloor(int screenH, int redW, int redH, float prevBottom)
        {
            isGrounded = false;

            const int platW = 700;
            const int platH = 20;

            Rectangle _redBounds = new Rectangle
            (
                (int)position.X,
                (int)position.Y,
                redW,
                redH
            );

            Rectangle _plataformBounds = new Rectangle
            (
                (int)platPosition.X,
                (int)platPosition.Y,
                platW,
                platH
            ); 

            if (_redBounds.Intersects(_plataformBounds) && prevBottom <= _plataformBounds.Top && velocity.Y >= 0)
            {
                position.Y = _plataformBounds.Top - redH;
                velocity.Y = 0;
                isGrounded = true;
            }

            //Impede de sair para fola da tela
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
            spriteBatch.Draw(_plataform, new Rectangle((int)platPosition.X, (int)platPosition.Y, 700, 20), Color.Green);

            spriteBatch.End();
        }
    }
}