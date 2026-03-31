using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Game;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.World.Persistence;

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
        private readonly PlanetSaveService saveService = new();
        private readonly InputService inputService = new();
        private bool deathStatePushed;
        private bool minimapVisible;
        private bool minimapTissueMode;
        private float autoSaveTimer;
        private const float AutoSaveInterval = 60f;

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine)
            : this(graphicsDevice, content, stateMachine, new PlayingSessionFactory(graphicsDevice, content).Create())
        {
        }

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine, PlayingSession session)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
            this.stateMachine = stateMachine;
            this.session = session;
            deathStatePushed = false;
            minimapVisible = false;
            minimapTissueMode = false;
            autoSaveTimer = AutoSaveInterval;
        }

        public void OnEnter() { }

        public void OnExit()
        {
            saveService.Save(session);
        }

        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            InputState input = inputService.Update();
            if (input.ToggleMinimapPressed)
                minimapVisible = !minimapVisible;
            if (minimapVisible)
            {
                WorldMinimapInteractionResult minimapInteraction = session.UpdateMinimapInteraction(
                    input,
                    screenW,
                    screenH,
                    minimapTissueMode);

                if (minimapInteraction.ToggleTissueMode)
                    minimapTissueMode = !minimapTissueMode;

                if (minimapInteraction.ConsumedMouse)
                    input = input.ConsumeWorldMouseInput();
            }

            KeyboardState keyboard = Keyboard.GetState();
            if (keyboard.IsKeyDown(Keys.Escape))
            {
                stateMachine.PushState(new PauseMenuState(graphicsDevice, content, stateMachine, session));
                return;
            }

            if (input.OpenInventoryPressed && stateMachine.CurrentState is not InventoryState)
                stateMachine.PushState(new InventoryState(graphicsDevice, stateMachine, session));

            if (stateMachine.CurrentState is InventoryState inventoryState &&
                inventoryState.ContainsMouse(Mouse.GetState().Position))
            {
                input = input.ConsumeWorldMouseInput();
            }

            Vector2 mouseWorld = session.Camera.ScreenToWorld(input.MouseScreenPosition);
            session.Update(dt, input, mouseWorld);
            autoSaveTimer -= dt;
            if (autoSaveTimer <= 0f)
            {
                if (session.WorldMap.HasUnsavedChanges)
                    saveService.Save(session);

                autoSaveTimer = AutoSaveInterval;
            }

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
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            float worldWidthPixels = session.WorldMap.PixelWidth;
            int centerLoop = (int)System.MathF.Floor(session.Camera.Position.X / worldWidthPixels);

            session.RenderTissueMask(graphicsDevice, spriteBatch);

            spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
            session.DrawSky(spriteBatch, screenW, screenH);
            spriteBatch.End();

            for (int loopOffset = -1; loopOffset <= 1; loopOffset++)
            {
                float worldOffset = (centerLoop + loopOffset) * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrain(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            for (int loopOffset = -1; loopOffset <= 1; loopOffset++)
            {
                float worldOffset = (centerLoop + loopOffset) * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawLoopedWorldEntities(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: session.Camera.GetViewMatrix());
            session.DrawEntities(spriteBatch);
            spriteBatch.End();

            spriteBatch.Begin(
                sortMode: SpriteSortMode.Deferred,
                samplerState: SamplerState.PointClamp,
                blendState: BlendState.Additive,
                effect: session.TissueCompositeEffect);
            session.DrawTissueOverlay(spriteBatch);
            spriteBatch.End();

            for (int loopOffset = -1; loopOffset <= 1; loopOffset++)
            {
                float worldOffset = (centerLoop + loopOffset) * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawTissueDebug(spriteBatch);
                spriteBatch.End();
            }

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            session.DrawHud(spriteBatch, screenW);
            if (minimapVisible)
                session.DrawMinimap(spriteBatch, screenW, screenH, minimapTissueMode);
            spriteBatch.End();
        }

        private void RetryFromDeath()
        {
            stateMachine.Clear();
            PlanetWorldMetadata metadata = session.PlanetMetadata;
            PlayingSessionFactory factory = new PlayingSessionFactory(graphicsDevice, content);
            stateMachine.PushState(new LoadingWorldState(
                graphicsDevice,
                content,
                stateMachine,
                factory.CreateBuildOperation(metadata.PlanetName, metadata.SizePreset, metadata.Seed),
                "Regenerando Planeta"));
        }
    }
}
