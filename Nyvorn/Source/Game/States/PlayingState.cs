using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Game.States
{
    public class PlayingState : IGameState
    {
        private readonly GraphicsDevice graphicsDevice;

        private readonly WorldMap worldMap;

        private readonly Player player;
        private readonly Camera2D camera;
        private readonly InputService inputService = new();

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content)
        {
            this.graphicsDevice = graphicsDevice;

            var dirt  = content.Load<Texture2D>("blocks/dirt_block");
            var sand  = content.Load<Texture2D>("blocks/sand_block");
            var stone = content.Load<Texture2D>("blocks/stone_block");

            worldMap = new WorldMap(100, 50, 8);
            worldMap.SetTextures(dirt, sand, stone);
            worldMap.GenerateTest();

            var backHandTexture = content.Load<Texture2D>("entities/player/handBackTexture_base");
            var bodyTexture = content.Load<Texture2D>("entities/player/bodyTexture_base");
            var legsTexture = content.Load<Texture2D>("entities/player/legsTexture_base");
            var frontHandTexture = content.Load<Texture2D>("entities/player/handFrontTexture_base");

            var shortStickTexture = content.Load<Texture2D>("weapons/shortStick");
            var handFront_weaponRun = content.Load<Texture2D>("entities/player/handFront_weaponRun");

            var attackHandbackTexture = content.Load<Texture2D>("entities/player/handBackShortSword_attack");
            var attackHandfrontTexture = content.Load<Texture2D>("entities/player/handFrontShortSword_attack");
            var attackBodyTexture = content.Load<Texture2D>("entities/player/bodyShortSword_attack");
            
            player = new Player(new Vector2(90, 50), bodyTexture, backHandTexture, frontHandTexture, attackHandbackTexture, attackHandfrontTexture, attackBodyTexture, legsTexture, shortStickTexture, handFront_weaponRun);
            camera = new Camera2D();
        }

        public void OnEnter() { }

        public void OnExit() { }

        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            InputState input = inputService.Update();
            Vector2 mouseWorld = camera.ScreenToWorld(input.MouseScreenPosition);

            player.Update(dt, worldMap, input, mouseWorld);
            camera.Follow(player.Position + new Vector2(8f, 12f), screenW, screenH);

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
