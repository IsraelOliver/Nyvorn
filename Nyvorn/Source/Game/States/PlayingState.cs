using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Gameplay.Entities.Player;

namespace Nyvorn.Source.Game.States
{
    public class PlayingState
    {
        GraphicsDevice graphicsDevice;

        private Texture2D _red;
        private Player player;

        private const int platW = 250;
        private const int platH = 20;

        private readonly List<Rectangle> platforms = new List<Rectangle>();

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content)
        {
            this.graphicsDevice = graphicsDevice;

            _red = new Texture2D(graphicsDevice, 1, 1);
            _red.SetData(new[] { Color.White });

            player = new Player(new Vector2(50, 50));

            platforms.Add(new Rectangle(30, 380, 700, platH));
            platforms.Add(new Rectangle(250, 300, platW, platH));
            platforms.Add(new Rectangle(520, 220, platW, platH));
        }


        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
          
            player.Update(dt, platforms, screenW, screenH);

        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin();

            spriteBatch.Draw(_red, new Rectangle((int)player.Position.X, (int)player.Position.Y, Player.Width, Player.Height), Color.Red);
            
            foreach (var plat in platforms)
            {
                spriteBatch.Draw(_red, plat, Color.Green);
            }

            spriteBatch.End();
        }
    }
}