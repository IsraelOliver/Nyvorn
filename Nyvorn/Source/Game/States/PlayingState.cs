using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.World;
using Nyvorn.Source.Gameplay.Combat.Weapons;

namespace Nyvorn.Source.Game.States
{
    public class PlayingState
    {
        GraphicsDevice graphicsDevice;

        private readonly WorldMap worldMap;

        private Texture2D backHandTexture;
        private Texture2D frontHandTexture;
        private Texture2D bodyTexture;
        private Texture2D legsTexture;

        private Texture2D shortStickTexture;
        private Texture2D handFront_weaponRun;

        private Texture2D attackHandbackTexture;
        private Texture2D attackHandfrontTexture;
        private Texture2D attackBodyTexture;

        private Player player;
        private ShortStick shortStick;

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

            backHandTexture = content.Load<Texture2D>("entities/player/handBackTexture_base");
            bodyTexture = content.Load<Texture2D>("entities/player/bodyTexture_base");
            legsTexture = content.Load<Texture2D>("entities/player/legsTexture_base");
            frontHandTexture = content.Load<Texture2D>("entities/player/handFrontTexture_base");

            shortStickTexture = content.Load<Texture2D>("weapons/shortStick");
            handFront_weaponRun = content.Load<Texture2D>("entities/player/handFront_weaponRun");

            attackHandbackTexture = content.Load<Texture2D>("entities/player/handBackShortSword_attack");
            attackHandfrontTexture = content.Load<Texture2D>("entities/player/handFrontShortSword_attack");
            attackBodyTexture = content.Load<Texture2D>("entities/player/bodyShortSword_attack");
            
            player = new Player(new Vector2(90, 50), bodyTexture, backHandTexture, frontHandTexture, attackHandbackTexture, attackHandfrontTexture, attackBodyTexture, legsTexture, shortStickTexture, handFront_weaponRun);
            shortStick = new ShortStick(shortStickTexture);
            camera = new Camera2D();
        }

        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            MouseState mouse = Mouse.GetState();
            Matrix inverseView = Matrix.Invert(camera.GetViewMatrix());
            Vector2 mouseWorld = Vector2.Transform(new Vector2(mouse.X, mouse.Y), inverseView);

            player.Update(dt, worldMap, screenW, screenH, mouseWorld);
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
