using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Nyvorn.Source.Game.States
{
    public class PlayingState
    {
        private Texture2D _red;

        public Vector2 Position;
        private float speed = 150f;
        //private float gravity = 500f; // Gravidade

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content)
        {
            
            _red = new Texture2D(graphicsDevice, 1, 1);
            _red.SetData(new[] { Color.White });

            Position = new Vector2(50, 50);
        }

        public void Update(GameTime gameTime)
        {   
            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
            KeyboardState teclado = Keyboard.GetState();

            float updateSpeed = speed * deltaTime;
            
            //Position.Y += updateSpeed; TENHO QUE FAZER UMA GRAVIDADE VALIDA

            if(teclado.IsKeyDown(Keys.D))
            {
                Position.X += updateSpeed;
            } 
            else if (teclado.IsKeyDown(Keys.A))
            {
                Position.X -= updateSpeed;
            }
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin();

            spriteBatch.Draw(_red, new Rectangle((int)Position.X, (int)Position.Y, 64, 64), Color.Red);

            spriteBatch.End();
        }
    }
}