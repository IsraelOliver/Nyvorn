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
using System.Globalization;

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
        private readonly List<string> consoleHistory = new();
        private KeyboardState previousConsoleKeyboard;
        private float autoSaveTimer;
        private const float AutoSaveInterval = 60f;
        private const int MaxConsoleInputLength = 96;
        private const int MaxConsoleHistoryLines = 6;

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
            bool handledConsoleThisFrame = false;

            if (!consoleOpen && IsNewConsoleKeyPress(keyboard, Keys.T))
            {
                consoleOpen = true;
                consoleInput = string.Empty;
                consoleMessage = string.Empty;
                previousConsoleKeyboard = keyboard;
                handledConsoleThisFrame = true;
            }

            if (consoleOpen)
            {
                HandleConsoleInput(keyboard);
                previousConsoleKeyboard = keyboard;
                input = CreateConsoleGameplayInput(input);
                handledConsoleThisFrame = true;
            }

            session.EnsureCurrentTissueHubActivated();

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

            if (!handledConsoleThisFrame && IsNewConsoleKeyPress(keyboard, Keys.Escape))
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
            session.UpdateSimulationViewport(screenW, screenH);
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

            session.FollowCamera(screenW, screenH);
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

            AddConsoleHistory("> " + command);
            string normalized = command.ToLowerInvariant();
            if (normalized == "/help" || normalized == "help")
            {
                SetConsoleMessage("Comandos: /help, spawn pickaxe, tick status/speed/pause/resume/reset/step, grass grow, debug ticks, world save");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "spawn pickaxe" || normalized == "spawn picareta")
            {
                SetConsoleMessage(session.TryDropItem(ItemId.Pickaxe)
                    ? "Spawned: pickaxe"
                    : "Falha ao spawnar pickaxe");
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteTickCommand(command))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteGrassCommand(command))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteDebugCommand(command))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteWorldCommand(command))
            {
                consoleInput = string.Empty;
                return;
            }

            SetConsoleMessage($"Comando desconhecido: {command}");
            consoleInput = string.Empty;
        }

        private void SetConsoleMessage(string message)
        {
            consoleMessage = message;
            if (!string.IsNullOrWhiteSpace(message))
                AddConsoleHistory(message);
        }

        private void AddConsoleHistory(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            consoleHistory.Add(line);
            while (consoleHistory.Count > MaxConsoleHistoryLines)
                consoleHistory.RemoveAt(0);
        }

        private static InputState CreateConsoleGameplayInput(InputState input)
        {
            return new InputState(
                0,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                -1,
                false,
                0,
                input.MouseScreenPosition,
                0);
        }

        private bool TryExecuteTickCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("tick", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1 || parts[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
            {
                SetConsoleMessage(session.WorldTicksPaused
                    ? $"Tick speed: {session.WorldTickTimeScale:0.##}x (paused)"
                    : $"Tick speed: {session.WorldTickTimeScale:0.##}x");
                return true;
            }

            if (parts[1].Equals("reset", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTickTimeScale(1f);
                session.SetWorldTicksPaused(false);
                SetConsoleMessage("Tick speed: 1x");
                return true;
            }

            if (parts[1].Equals("pause", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTicksPaused(true);
                SetConsoleMessage("World ticks pausados");
                return true;
            }

            if (parts[1].Equals("resume", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTicksPaused(false);
                SetConsoleMessage($"Tick speed: {session.WorldTickTimeScale:0.##}x");
                return true;
            }

            if (parts[1].Equals("step", System.StringComparison.OrdinalIgnoreCase))
            {
                int cycles = 1;
                if (parts.Length >= 3 && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out cycles))
                {
                    SetConsoleMessage("Uso: tick step [1..600]");
                    return true;
                }

                cycles = System.Math.Clamp(cycles, 1, 600);
                session.StepWorldTicks(cycles);
                SetConsoleMessage($"Ticks manuais: {cycles}");
                return true;
            }

            if (parts[1].Equals("speed", System.StringComparison.OrdinalIgnoreCase) &&
                parts.Length >= 3 &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float speed))
            {
                session.SetWorldTickTimeScale(speed);
                session.SetWorldTicksPaused(false);
                SetConsoleMessage($"Tick speed: {session.WorldTickTimeScale:0.##}x");
                return true;
            }

            SetConsoleMessage("Uso: tick speed 1..16, tick step [n], tick pause, tick resume, tick reset, tick status");
            return true;
        }

        private bool TryExecuteGrassCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("grass", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length >= 2 && parts[1].Equals("grow", System.StringComparison.OrdinalIgnoreCase))
            {
                int samples = 256;
                if (parts.Length >= 3 && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out samples))
                {
                    SetConsoleMessage("Uso: grass grow [samples]");
                    return true;
                }

                samples = System.Math.Clamp(samples, 1, 10000);
                int grown = session.ForceGrassGrowthSamples(samples);
                SetConsoleMessage($"Grass grow: {grown}/{samples}");
                return true;
            }

            SetConsoleMessage("Uso: grass grow [samples]");
            return true;
        }

        private bool TryExecuteDebugCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("debug", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length >= 2 && parts[1].Equals("ticks", System.StringComparison.OrdinalIgnoreCase))
            {
                string paused = session.WorldTicksPaused ? " paused" : string.Empty;
                SetConsoleMessage(
                    $"F:{session.FastTickCount} M:{session.MediumTickCount} S:{session.SlowTickCount} " +
                    $"samples:{session.LastRandomTileSampleCount} grass:{session.LastGrassGrowthCount} " +
                    $"chunks:{session.ActiveSimulationChunks.Count} speed:{session.WorldTickTimeScale:0.##}x{paused}");
                return true;
            }

            SetConsoleMessage("Uso: debug ticks");
            return true;
        }

        private bool TryExecuteWorldCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("world", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length >= 2 && parts[1].Equals("save", System.StringComparison.OrdinalIgnoreCase))
            {
                saveService.Save(session);
                autoSaveTimer = AutoSaveInterval;
                SetConsoleMessage("World saved");
                return true;
            }

            SetConsoleMessage("Uso: world save");
            return true;
        }

        private void DrawConsole(SpriteBatch spriteBatch, int screenWidth)
        {
            int lineHeight = (int)System.MathF.Ceiling(consoleFont.LineSpacing * 0.9f);
            int inputHeight = lineHeight + 10;
            int inputY = graphicsDevice.PresentationParameters.BackBufferHeight - inputHeight;
            Rectangle inputBounds = new Rectangle(0, inputY, screenWidth, inputHeight);

            int historyCount = consoleHistory.Count;
            if (historyCount > 0)
            {
                int historyHeight = (lineHeight * historyCount) + 8;
                Rectangle historyBounds = new Rectangle(0, System.Math.Max(0, inputY - historyHeight), screenWidth, historyHeight);
                spriteBatch.Draw(consolePixel, historyBounds, Color.Black * 0.58f);

                int firstLineY = historyBounds.Y + 4;
                for (int i = 0; i < historyCount; i++)
                {
                    string line = consoleHistory[i];
                    Color color = line.StartsWith("> ", System.StringComparison.Ordinal) ? new Color(180, 220, 255) : new Color(220, 235, 238);
                    spriteBatch.DrawString(consoleFont, line, new Vector2(10, firstLineY + (i * lineHeight)), color);
                }
            }

            spriteBatch.Draw(consolePixel, inputBounds, Color.Black * 0.82f);
            spriteBatch.Draw(consolePixel, new Rectangle(0, inputBounds.Y, screenWidth, 2), new Color(143, 211, 255));

            string prompt = "> " + consoleInput + "_";
            spriteBatch.DrawString(consoleFont, prompt, new Vector2(10, inputBounds.Y + 5), Color.White);
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
