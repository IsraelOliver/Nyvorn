using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Game;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.World.Persistence;
using System.Collections.Generic;

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
            if (input.TissueRevealPressed && session.IsPlayerOnActivatedTissueHub)
            {
                minimapVisible = true;
                minimapTissueMode = true;
            }

            if (minimapVisible)
            {
                WorldMinimapInteractionResult minimapInteraction = session.UpdateMinimapInteraction(
                    input,
                    screenW,
                    screenH,
                    minimapTissueMode);

                if (minimapInteraction.ToggleTissueMode)
                    minimapTissueMode = !minimapTissueMode;

                if (minimapInteraction.TravelHubIndex >= 0 &&
                    session.TryFastTravelToTissueHub(minimapInteraction.TravelHubIndex))
                {
                    minimapVisible = false;
                    session.Camera.CenterOn(session.Player.Position + new Vector2(8f, 12f), screenW, screenH);
                }

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
                else
                    saveService.SavePlayerOnly(session);

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
            IReadOnlyList<int> visibleLoopOffsets = GetVisibleLoopOffsets(screenW, worldWidthPixels);

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                session.PrepareTerrainRender(graphicsDevice, screenW, screenH, worldOffset);
            }

            spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
            session.DrawSky(spriteBatch, screenW, screenH);
            spriteBatch.End();

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawLoopedWorldEntities(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: session.Camera.GetViewMatrix());
            session.DrawEntities(spriteBatch);
            spriteBatch.End();

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrain(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawTissueDebug(spriteBatch);
                spriteBatch.End();
            }

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            session.DrawHud(spriteBatch, screenW, screenH);
            if (minimapVisible)
                session.DrawMinimap(spriteBatch, screenW, screenH, minimapTissueMode);
            spriteBatch.End();
        }

        private IReadOnlyList<int> GetVisibleLoopOffsets(int screenWidth, float worldWidthPixels)
        {
            if (worldWidthPixels <= 0f)
                return new[] { 0 };

            float viewWidth = screenWidth / session.Camera.Zoom;
            float left = session.Camera.Position.X;
            float right = left + viewWidth;
            int minLoop = (int)System.MathF.Floor(left / worldWidthPixels);
            int maxLoop = (int)System.MathF.Floor((right - 0.001f) / worldWidthPixels);
            int count = System.Math.Max(1, maxLoop - minLoop + 1);
            int[] offsets = new int[count];

            for (int i = 0; i < count; i++)
                offsets[i] = minLoop + i;

            return offsets;
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
