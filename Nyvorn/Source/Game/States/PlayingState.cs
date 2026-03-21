using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Game;

namespace Nyvorn.Source.Game.States
{
    public class PlayingState : IGameState
    {
        public bool UpdateBelow => false;
        public bool DrawBelow => false;
        public bool BlockInputBelow => true;

        private readonly GraphicsDevice graphicsDevice;
        private readonly StateMachine stateMachine;
        private readonly ContentManager content;
        private readonly PlayingSession session;
        private readonly InputService inputService = new();
        private bool deathStatePushed;

<<<<<<< HEAD
        private Texture2D backHandTexture;
        private Texture2D frontHandTexture;
        private Texture2D bodyTexture;
        private Texture2D legsTexture;

        private Texture2D attackHandbackTexture;
        private Texture2D attackHandfrontTexture;
        private Texture2D attackBodyTexture;

        private Player player;
=======
        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine)
            : this(graphicsDevice, content, stateMachine, new PlayingSessionFactory(graphicsDevice, content).Create())
        {
        }
>>>>>>> 06a0242ea9d5e0753e26f589eb466b0d3ef40484

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine, PlayingSession session)
        {
            this.graphicsDevice = graphicsDevice;
<<<<<<< HEAD

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

            attackHandbackTexture = content.Load<Texture2D>("entities/player/handBackShortSword_attack");
            attackHandfrontTexture = content.Load<Texture2D>("entities/player/handFrontShortSword_attack");
            attackBodyTexture = content.Load<Texture2D>("entities/player/bodyShortSword_attack");
            
            player = new Player(new Vector2(90, 50), bodyTexture, backHandTexture, frontHandTexture, attackHandbackTexture, attackHandfrontTexture, attackBodyTexture, legsTexture);
            camera = new Camera2D();
=======
            this.content = content;
            this.stateMachine = stateMachine;
            this.session = session;
            deathStatePushed = false;
>>>>>>> 06a0242ea9d5e0753e26f589eb466b0d3ef40484
        }

        public void OnEnter() { }

        public void OnExit() { }

        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            InputState input = inputService.Update();
            if (input.OpenInventoryPressed && stateMachine.CurrentState is not InventoryState)
                stateMachine.PushState(new InventoryState(graphicsDevice, stateMachine, session));

            if (stateMachine.CurrentState is InventoryState inventoryState &&
                inventoryState.ContainsMouse(Mouse.GetState().Position))
            {
                input = input.ConsumeWorldMouseInput();
            }

            Vector2 mouseWorld = session.Camera.ScreenToWorld(input.MouseScreenPosition);
            session.Update(dt, input, mouseWorld);

            if (!session.Player.IsAlive && !deathStatePushed)
            {
                deathStatePushed = true;
                stateMachine.PushState(new DeathState(graphicsDevice, content, RetryFromDeath));
                return;
            }

            session.Camera.Follow(session.Player.Position + new Vector2(8f, 12f), screenW, screenH);
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: session.Camera.GetViewMatrix());
            session.DrawWorld(spriteBatch);
            spriteBatch.End();

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            session.DrawHud(spriteBatch, screenW);
            spriteBatch.End();
        }

        private void RetryFromDeath()
        {
            stateMachine.Clear();
            stateMachine.PushState(new PlayingState(graphicsDevice, content, stateMachine));
        }
    }
}
