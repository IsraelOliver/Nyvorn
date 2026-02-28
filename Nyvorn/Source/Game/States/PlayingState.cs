using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Game.States
{
    public class PlayingState
    {
        GraphicsDevice graphicsDevice;

        private readonly WorldMap worldMap;

        private Texture2D playerTexture;
        private Player player;

        private Camera2D camera;

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content)
        {
            this.graphicsDevice = graphicsDevice;

            var dirt  = content.Load<Texture2D>("blocks/dirt_block");
            var sand  = content.Load<Texture2D>("blocks/sand_block");
            var stone = content.Load<Texture2D>("blocks/stone_block");

            worldMap = new WorldMap(100, 50, 8);
            worldMap.SetTextures(dirt, sand, stone);
            worldMap.GenerateTest();

            playerTexture = content.Load<Texture2D>("entities/player_body");
            player = new Player(new Vector2(90, 50), playerTexture);
            camera = new Camera2D();
        }

        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
          
            player.Update(dt, worldMap, screenW, screenH);
            camera.Follow(player.Position + new Vector2(8f, 12f), screenW, screenH);;

        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: camera.GetViewMatrix());

            worldMap.Draw(spriteBatch);

            player.Draw(spriteBatch);

            spriteBatch.End();
        }
    }
}