using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Game;
using Nyvorn.Source.Gameplay.Items;
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
        private readonly SpriteFont consoleFont;
        private readonly Texture2D consolePixel;
        private bool deathStatePushed;
        private bool minimapVisible;
        private bool minimapTissueMode;
        private bool consoleOpen;
        private string consoleInput = string.Empty;
        private string consoleMessage = string.Empty;
        private KeyboardState previousConsoleKeyboard;
        private float autoSaveTimer;
        private const float AutoSaveInterval = 60f;
        private const int MaxConsoleInputLength = 96;

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
            consoleOpen = false;
            previousConsoleKeyboard = Keyboard.GetState();
            consoleFont = content.Load<SpriteFont>("ui/UIFont");
            consolePixel = new Texture2D(graphicsDevice, 1, 1);
            consolePixel.SetData(new[] { Color.White });
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
            KeyboardState keyboard = Keyboard.GetState();

            if (!consoleOpen && IsNewConsoleKeyPress(keyboard, Keys.T))
            {
                consoleOpen = true;
                consoleInput = string.Empty;
                consoleMessage = string.Empty;
                previousConsoleKeyboard = keyboard;
                return;
            }

            if (consoleOpen)
            {
                HandleConsoleInput(keyboard);
                previousConsoleKeyboard = keyboard;
                return;
            }

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

            if (IsNewConsoleKeyPress(keyboard, Keys.Escape))
            {
                previousConsoleKeyboard = keyboard;
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
                previousConsoleKeyboard = keyboard;
                stateMachine.PushState(new DeathState(graphicsDevice, content, RetryFromDeath));
                return;
            }

            session.Camera.Follow(session.Player.Position + new Vector2(8f, 12f), screenW, screenH);
            previousConsoleKeyboard = keyboard;
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
            if (consoleOpen)
                DrawConsole(spriteBatch, screenW);
            spriteBatch.End();
        }

        private void HandleConsoleInput(KeyboardState keyboard)
        {
            foreach (Keys key in keyboard.GetPressedKeys())
            {
                if (previousConsoleKeyboard.IsKeyDown(key))
                    continue;

                if (key == Keys.Escape)
                {
                    consoleOpen = false;
                    return;
                }

                if (key == Keys.Enter)
                {
                    ExecuteConsoleCommand();
                    return;
                }

                if (key == Keys.Back)
                {
                    if (consoleInput.Length > 0)
                        consoleInput = consoleInput[..^1];

                    continue;
                }

                if (consoleInput.Length >= MaxConsoleInputLength)
                    continue;

                if (TryGetConsoleCharacter(keyboard, key, out char character))
                    consoleInput += character;
            }
        }

        private void ExecuteConsoleCommand()
        {
            string command = consoleInput.Trim();
            if (command.Length == 0)
                return;

            string normalized = command.ToLowerInvariant();
            if (normalized == "spawn pickaxe" || normalized == "spawn picareta")
            {
                consoleMessage = session.TryDropItem(ItemId.Pickaxe)
                    ? "Spawned: pickaxe"
                    : "Falha ao spawnar pickaxe";
                consoleInput = string.Empty;
                return;
            }

            consoleMessage = $"Comando desconhecido: {command}";
            consoleInput = string.Empty;
        }

        private void DrawConsole(SpriteBatch spriteBatch, int screenWidth)
        {
            Rectangle panelBounds = new Rectangle(12, 12, System.Math.Min(screenWidth - 24, 720), 72);
            spriteBatch.Draw(consolePixel, panelBounds, Color.Black * 0.78f);
            spriteBatch.Draw(consolePixel, new Rectangle(panelBounds.X, panelBounds.Bottom - 2, panelBounds.Width, 2), new Color(143, 211, 255));

            if (!string.IsNullOrWhiteSpace(consoleMessage))
                spriteBatch.DrawString(consoleFont, consoleMessage, new Vector2(panelBounds.X + 10, panelBounds.Y + 8), new Color(180, 220, 255));

            string prompt = "> " + consoleInput + "_";
            spriteBatch.DrawString(consoleFont, prompt, new Vector2(panelBounds.X + 10, panelBounds.Y + 38), Color.White);
        }

        private bool IsNewConsoleKeyPress(KeyboardState keyboard, Keys key)
        {
            return keyboard.IsKeyDown(key) && !previousConsoleKeyboard.IsKeyDown(key);
        }

        private static bool TryGetConsoleCharacter(KeyboardState keyboard, Keys key, out char character)
        {
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

            if (key >= Keys.A && key <= Keys.Z)
            {
                char baseChar = (char)('a' + (key - Keys.A));
                character = shift ? char.ToUpperInvariant(baseChar) : baseChar;
                return true;
            }

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                character = (char)('0' + (key - Keys.D0));
                return true;
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                character = (char)('0' + (key - Keys.NumPad0));
                return true;
            }

            character = key switch
            {
                Keys.Space => ' ',
                Keys.OemMinus => shift ? '_' : '-',
                Keys.OemPeriod => '.',
                Keys.OemComma => ',',
                Keys.OemQuestion => '/',
                _ => '\0'
            };

            return character != '\0';
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
