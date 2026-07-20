using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Game;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.Interaction;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.Gameplay.World.Simulation;
using Nyvorn.Source.World.Decorations;
using Nyvorn.Source.World.Persistence;
using Nyvorn.Source.World.Tissue;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nyvorn.Source.Game.States
{
    public class PlayingState : IGameState
    {
        public bool UpdateBelow => false;
        public bool DrawBelow => false;
        public bool BlockInputBelow => true;

        // Darkens whatever's already drawn (destination *= source) instead of alpha-compositing
        // over it, so the wetness overlay tints the tile actually on screen rather than needing to
        // duplicate/replace its draw.
        private static readonly BlendState MultiplyBlend = new()
        {
            ColorSourceBlend = Blend.DestinationColor,
            ColorDestinationBlend = Blend.Zero,
            AlphaSourceBlend = Blend.DestinationAlpha,
            AlphaDestinationBlend = Blend.Zero
        };

        private readonly GraphicsDevice graphicsDevice;
        private readonly StateMachine stateMachine;
        private readonly ContentManager content;
        private readonly PlayingSession session;
        private readonly PlanetSaveService saveService = new();
        private readonly InputService inputService = new();
        private readonly SpriteFont consoleFont;
        private readonly Texture2D consolePixel;
        private readonly PlayerHubUI playerHubUI;
        private bool deathStatePushed;
        private bool minimapVisible;
        private bool minimapTissueMode;
        private bool consoleOpen;
        private string consoleInput = string.Empty;
        private string consoleMessage = string.Empty;
        private readonly List<string> consoleHistory = new();
        private int consoleCursor;
        private int consoleSelectionAnchor = -1;
        private int commandHistoryIndex;
        private string commandHistoryDraft = string.Empty;
        private float consoleCursorBlinkTimer;
        private Vector2 consoleTargetWorld;
        private KeyboardState previousConsoleKeyboard;
        private float autoSaveTimer;
        private const float AutoSaveInterval = 60f;
        private const int MaxConsoleInputLength = 96;
        private const int MaxConsoleHistoryLines = 40;
        private const int HelpCommandsPerPage = 10;
        private bool showFps;
        private float fpsSmoothed;
        private readonly System.Diagnostics.Stopwatch fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();

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
            commandHistoryIndex = session.ConsoleCommandHistory.Count;
            previousConsoleKeyboard = Keyboard.GetState();
            consoleFont = content.Load<SpriteFont>("ui/UIFont");
            consolePixel = new Texture2D(graphicsDevice, 1, 1);
            consolePixel.SetData(new[] { Color.White });
            playerHubUI = new PlayerHubUI(graphicsDevice, session);
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

            if (consoleOpen)
                consoleCursorBlinkTimer += dt;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            InputState input = inputService.Update();
            consoleTargetWorld = session.Camera.ScreenToWorld(input.MouseScreenPosition);
            KeyboardState keyboard = Keyboard.GetState();
            bool handledConsoleThisFrame = false;

            if (input.ToggleDebugPressed)
            {
                consoleOpen = !consoleOpen;
                if (consoleOpen)
                {
                    ResetConsoleEditor();
                    consoleMessage = string.Empty;
                }

                previousConsoleKeyboard = keyboard;
                input = input.ConsumeGameplayInput();
                handledConsoleThisFrame = true;
            }

            if (consoleOpen)
            {
                HandleConsoleInput(keyboard);
                previousConsoleKeyboard = keyboard;
                input = input.ConsumeGameplayInput();
                handledConsoleThisFrame = true;
            }

            session.EnsureCurrentTissueHubActivated();

            if (!handledConsoleThisFrame && input.CancelPressed)
            {
                if (playerHubUI.IsOpen)
                {
                    playerHubUI.Close();
                    previousConsoleKeyboard = keyboard;
                    input = input.ConsumeGameplayInput();
                    handledConsoleThisFrame = true;
                }
                else if (minimapVisible)
                {
                    minimapVisible = false;
                    previousConsoleKeyboard = keyboard;
                    input = input.ConsumeGameplayInput();
                    handledConsoleThisFrame = true;
                }
                else
                {
                    previousConsoleKeyboard = keyboard;
                    stateMachine.PushState(new PauseMenuState(graphicsDevice, content, stateMachine, session));
                    return;
                }
            }

            if (!handledConsoleThisFrame && input.TogglePlayerHubPressed)
                playerHubUI.Toggle();

            if (!handledConsoleThisFrame && input.ToggleMapPressed)
                minimapVisible = !minimapVisible;

            if (!handledConsoleThisFrame && input.ToggleConstructionModePressed)
                session.ToggleConstructionMode();

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

            Vector2 mouseWorld = session.Camera.ScreenToWorld(input.MouseScreenPosition);
            session.WorkbenchRuntimeSystem.UpdateHover(mouseWorld);
            session.FurnaceRuntimeSystem.UpdateHover(mouseWorld);

            CraftTier craftTier = session.WorkbenchRuntimeSystem.GetNearbyCraftTier() | session.FurnaceRuntimeSystem.GetNearbyCraftTier();
            playerHubUI.Update(input, craftTier);
            if (playerHubUI.IsOpen && playerHubUI.ContainsMouse(input.MouseScreenPosition.ToPoint(), craftTier))
                input = input.ConsumeWorldMouseInput();

            if (!handledConsoleThisFrame && input.InteractPressed)
            {
                bool interactedWithDoor = session.DoorRuntimeSystem.TryInteract(session.Player);
                if (!interactedWithDoor &&
                    session.WorkbenchRuntimeSystem.TryInteract(session.Player, out InteractionResult interactionResult) &&
                    interactionResult.OpenPlayerHub)
                {
                    playerHubUI.Open();
                    craftTier |= interactionResult.CraftTier;
                }
                else if (!interactedWithDoor &&
                    session.FurnaceRuntimeSystem.TryInteract(session.Player, out InteractionResult furnaceInteractionResult) &&
                    furnaceInteractionResult.OpenPlayerHub)
                {
                    playerHubUI.Open();
                    craftTier |= furnaceInteractionResult.CraftTier;
                }
            }

            if (!handledConsoleThisFrame && input.CyclePowerPressed)
                session.PowerSystem.CycleNextPower();

            session.UpdateSimulationViewport(screenW, screenH);

            if (!handledConsoleThisFrame && !session.IsConstructionMode && input.ActivePowerJustPressed)
                session.PowerSystem.TryActivateCurrentPower();

            session.Update(dt, input, mouseWorld);
            autoSaveTimer -= dt;
            if (autoSaveTimer <= 0f)
            {
                if (session.HasUnsavedWorldChanges)
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

            session.FollowCamera(dt, screenW, screenH);
            previousConsoleKeyboard = keyboard;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            // Real wall-clock time between Draw calls, not GameTime - MonoGame's default fixed
            // timestep can report a near-constant ElapsedGameTime from both Update and Draw
            // regardless of actual rendering performance, which would mask real slowdowns.
            float drawDt = (float)fpsStopwatch.Elapsed.TotalSeconds;
            fpsStopwatch.Restart();
            if (drawDt > 0f)
            {
                float instantFps = 1f / drawDt;
                fpsSmoothed = fpsSmoothed <= 0f ? instantFps : MathHelper.Lerp(fpsSmoothed, instantFps, 0.1f);
            }

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            float worldWidthPixels = session.WorldMap.PixelWidth;
            IReadOnlyList<int> visibleLoopOffsets = GetVisibleLoopOffsets(screenW, worldWidthPixels);

            // Set once per frame, before any terrain/decoration/entity draws below read it, so
            // sky-exposed tiles/trees/entities pick up the sun's current color (warm at sunset,
            // cool at night) instead of always rendering at flat Color.White.
            session.WorldMap.SetAmbientLight(session.EnvironmentSystem.SkyState.AmbientLight);

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                session.PrepareTerrainRender(graphicsDevice, screenW, screenH, worldOffset);
            }

            spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
            session.DrawSky(spriteBatch, screenW, screenH);
            spriteBatch.End();

            // Its own Begin/End pass because it uses the SunRays pixel shader instead of the
            // default sprite effect - SpriteBatch only supports one Effect per Begin/End pair.
            session.DrawSunGlow(spriteBatch, screenW, screenH);
            session.DrawMoons(spriteBatch, screenW, screenH);

            // Drawn after the sun/moons (not merged into DrawSky above) so the mountains, which
            // sit closer than the sky, occlude the sun/moons instead of the sun rendering on top.
            // PointClamp (not LinearClamp) so the pixel-art background stays crisp when scaled.
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            session.DrawParallaxMountains(spriteBatch, screenW, screenH);
            spriteBatch.End();

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Back);
                session.DrawBackgroundWalls(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Front);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                // Water draws before the terrain pass so tiles composite on top of it, the same
                // way sand is layered behind tiles in DrawTerrainBase: any undrawn pixel in a
                // tile's own art reveals the water behind it instead of a gap, while opaque tile
                // pixels still fully occlude the water as expected.
                spriteBatch.Begin(
                    samplerState: SamplerState.PointClamp,
                    blendState: BlendState.AlphaBlend,
                    transformMatrix: transform);
                session.DrawWater(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainBase(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                // Recomputed every frame instead of baked into the terrain, so it always reflects
                // current wetness - see WorldMap.DrawWetnessOverlay.
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: MultiplyBlend, transformMatrix: transform);
                session.DrawWetnessOverlay(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(
                    samplerState: SamplerState.PointClamp,
                    blendState: BlendState.AlphaBlend,
                    transformMatrix: transform);
                session.DrawSkylightShadows(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainOverlay(spriteBatch);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                // Fires after the foreground terrain pass above (not bundled with the background
                // walls earlier) so solid ground tiles no longer paint over enemies/items/placed
                // objects standing in front of/on top of them.
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawLoopedWorldEntities(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive, transformMatrix: transform);
                session.DrawTissueHalo(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawTissueCore(spriteBatch, screenW, screenH, worldOffset);
                session.DrawTissueFieldOverlay(spriteBatch, screenW, screenH, worldOffset);
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

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawInteriorFocusOverlay(spriteBatch, screenW, screenH, worldOffset);
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

            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
            session.DrawRainFront(spriteBatch, screenW, screenH);
            session.DrawNightOverlay(spriteBatch, screenW, screenH);
            spriteBatch.End();

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            session.DrawHud(spriteBatch, screenW, screenH);
            if (minimapVisible)
                session.DrawMinimap(spriteBatch, screenW, screenH, minimapTissueMode);
            playerHubUI.Draw(spriteBatch, session.WorkbenchRuntimeSystem.GetNearbyCraftTier() | session.FurnaceRuntimeSystem.GetNearbyCraftTier());
            if (showFps)
                DrawFpsCounter(spriteBatch);
            if (consoleOpen)
                DrawConsole(spriteBatch, screenW);
            spriteBatch.End();
        }

        private void DrawFpsCounter(SpriteBatch spriteBatch)
        {
            string text = $"FPS: {fpsSmoothed:0}";
            Vector2 position = new Vector2(8f, 8f);
            spriteBatch.DrawString(consoleFont, text, position + Vector2.One, Color.Black);
            spriteBatch.DrawString(consoleFont, text, position, Color.White);
        }

        private void HandleConsoleInput(KeyboardState keyboard)
        {
            consoleCursor = System.Math.Clamp(consoleCursor, 0, consoleInput.Length);
            bool control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

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

                if (control && key == Keys.A)
                {
                    consoleSelectionAnchor = 0;
                    consoleCursor = consoleInput.Length;
                    ResetConsoleCursorBlink();
                    continue;
                }

                if (control && key == Keys.C)
                {
                    CopyConsoleSelection();
                    continue;
                }

                if (control && key == Keys.X)
                {
                    CutConsoleSelection();
                    continue;
                }

                if (control && key == Keys.V)
                {
                    PasteConsoleClipboard();
                    continue;
                }

                if (key == Keys.Up)
                {
                    NavigateCommandHistory(-1);
                    continue;
                }

                if (key == Keys.Down)
                {
                    NavigateCommandHistory(1);
                    continue;
                }

                if (key == Keys.Left)
                {
                    MoveConsoleCursor(consoleCursor - 1, shift);
                    continue;
                }

                if (key == Keys.Right)
                {
                    MoveConsoleCursor(consoleCursor + 1, shift);
                    continue;
                }

                if (key == Keys.Home)
                {
                    MoveConsoleCursor(0, shift);
                    continue;
                }

                if (key == Keys.End)
                {
                    MoveConsoleCursor(consoleInput.Length, shift);
                    continue;
                }

                if (key == Keys.Back)
                {
                    BackspaceConsoleInput();
                    continue;
                }

                if (key == Keys.Delete)
                {
                    DeleteConsoleInput();
                    continue;
                }

                if (control)
                    continue;

                if (TryGetConsoleCharacter(keyboard, key, out char character))
                    InsertConsoleText(character.ToString());
            }
        }

        private bool HasConsoleSelection => consoleSelectionAnchor >= 0 && consoleSelectionAnchor != consoleCursor;

        private void ResetConsoleEditor()
        {
            consoleInput = string.Empty;
            consoleCursor = 0;
            consoleSelectionAnchor = -1;
            commandHistoryIndex = session.ConsoleCommandHistory.Count;
            commandHistoryDraft = string.Empty;
            ResetConsoleCursorBlink();
        }

        private void ResetConsoleCursorBlink()
        {
            consoleCursorBlinkTimer = 0f;
        }

        private void MoveConsoleCursor(int target, bool selecting)
        {
            if (selecting)
            {
                if (consoleSelectionAnchor < 0)
                    consoleSelectionAnchor = consoleCursor;
            }
            else
            {
                consoleSelectionAnchor = -1;
            }

            consoleCursor = System.Math.Clamp(target, 0, consoleInput.Length);
            if (consoleSelectionAnchor == consoleCursor)
                consoleSelectionAnchor = -1;
            ResetConsoleCursorBlink();
        }

        private void NavigateCommandHistory(int direction)
        {
            IReadOnlyList<string> history = session.ConsoleCommandHistory;
            if (history.Count == 0)
                return;

            if (commandHistoryIndex < 0 || commandHistoryIndex > history.Count)
                commandHistoryIndex = history.Count;
            if (commandHistoryIndex == history.Count && direction < 0)
                commandHistoryDraft = consoleInput;

            commandHistoryIndex = System.Math.Clamp(commandHistoryIndex + direction, 0, history.Count);
            consoleInput = commandHistoryIndex == history.Count
                ? commandHistoryDraft
                : history[commandHistoryIndex];
            consoleCursor = consoleInput.Length;
            consoleSelectionAnchor = -1;
            ResetConsoleCursorBlink();
        }

        private void CopyConsoleSelection()
        {
            string text = GetSelectedConsoleText();
            ClipboardService.TrySetText(text);
            ResetConsoleCursorBlink();
        }

        private void CutConsoleSelection()
        {
            CopyConsoleSelection();
            if (HasConsoleSelection)
                DeleteConsoleSelection();
            else if (consoleInput.Length > 0)
                ReplaceConsoleRange(0, consoleInput.Length, string.Empty);
        }

        private void PasteConsoleClipboard()
        {
            ClipboardService.TryGetText(out string clipboardText);
            InsertConsoleText(SanitizeConsolePaste(clipboardText));
        }

        private string GetSelectedConsoleText()
        {
            if (!HasConsoleSelection)
                return consoleInput;

            GetConsoleSelectionRange(out int start, out int length);
            return consoleInput.Substring(start, length);
        }

        private void BackspaceConsoleInput()
        {
            if (DeleteConsoleSelection())
                return;
            if (consoleCursor <= 0)
                return;

            ReplaceConsoleRange(consoleCursor - 1, 1, string.Empty);
        }

        private void DeleteConsoleInput()
        {
            if (DeleteConsoleSelection())
                return;
            if (consoleCursor >= consoleInput.Length)
                return;

            ReplaceConsoleRange(consoleCursor, 1, string.Empty);
        }

        private bool DeleteConsoleSelection()
        {
            if (!HasConsoleSelection)
                return false;

            GetConsoleSelectionRange(out int start, out int length);
            ReplaceConsoleRange(start, length, string.Empty);
            return true;
        }

        private void InsertConsoleText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            int start = consoleCursor;
            int removeLength = 0;
            if (HasConsoleSelection)
                GetConsoleSelectionRange(out start, out removeLength);

            int available = MaxConsoleInputLength - (consoleInput.Length - removeLength);
            if (available <= 0)
                return;
            if (text.Length > available)
                text = text[..available];

            ReplaceConsoleRange(start, removeLength, text);
        }

        private void ReplaceConsoleRange(int start, int length, string replacement)
        {
            consoleInput = consoleInput.Remove(start, length).Insert(start, replacement);
            consoleCursor = start + replacement.Length;
            consoleSelectionAnchor = -1;
            commandHistoryIndex = session.ConsoleCommandHistory.Count;
            commandHistoryDraft = string.Empty;
            ResetConsoleCursorBlink();
        }

        private void GetConsoleSelectionRange(out int start, out int length)
        {
            start = System.Math.Min(consoleCursor, consoleSelectionAnchor);
            int end = System.Math.Max(consoleCursor, consoleSelectionAnchor);
            length = end - start;
        }

        private static string SanitizeConsolePaste(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            StringBuilder sanitized = new(text.Length);
            bool previousWasSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '\r' || character == '\n' || character == '\t')
                    character = ' ';
                if (character < ' ' || character > '~')
                    continue;
                if (character == ' ' && previousWasSpace)
                    continue;

                sanitized.Append(character);
                previousWasSpace = character == ' ';
            }

            return sanitized.ToString();
        }

        private void ExecuteConsoleCommand()
        {
            string command = consoleInput.Trim();
            if (command.Length == 0)
                return;

            AddConsoleHistory("> " + command);
            ResetConsoleEditor();
            if (!command.StartsWith("/", System.StringComparison.Ordinal))
            {
                SetConsoleMessage("Comandos devem comecar com /. Digite /help");
                return;
            }

            session.AddConsoleCommand(command);
            string commandBody = command[1..].Trim();
            string normalized = commandBody.ToLowerInvariant();
            if (normalized == "help" || normalized.StartsWith("help ", System.StringComparison.Ordinal))
            {
                int page = 1;
                string[] helpParts = commandBody.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (helpParts.Length > 1 && int.TryParse(helpParts[1], out int parsedPage))
                    page = parsedPage;

                ShowConsoleHelp(page);
                return;
            }

            if (normalized == "debugfly")
            {
                bool enabled = session.ToggleDebugFly();
                SetConsoleMessage(enabled
                    ? "Debug fly ativado: W/A/S/D para voar e atravessar blocos"
                    : "Debug fly desativado: movimento normal restaurado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "debugfly on")
            {
                session.SetDebugFly(true);
                SetConsoleMessage("Debug fly ativado: W/A/S/D para voar e atravessar blocos");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "debugfly off")
            {
                session.SetDebugFly(false);
                SetConsoleMessage("Debug fly desativado: movimento normal restaurado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuevisual on")
            {
                session.SetTissueVisualEnabled(true);
                SetConsoleMessage("Tissue cosmic web ativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuevisual off")
            {
                session.SetTissueVisualEnabled(false);
                SetConsoleMessage("Tissue cosmic web desativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuevisual")
            {
                SetConsoleMessage("Uso: /tissuevisual on ou /tissuevisual off");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuefield on")
            {
                session.SetTissueFieldVisualEnabled(true);
                SetConsoleMessage("TissueField real ativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuefield off")
            {
                session.SetTissueFieldVisualEnabled(false);
                SetConsoleMessage("TissueField real desativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuefield")
            {
                SetConsoleMessage("Uso: /tissuefield on ou /tissuefield off");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "skylight on")
            {
                session.SetSkylightShadowsEnabled(true);
                SetConsoleMessage("Sombras de skylight ativadas");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "skylight off")
            {
                session.SetSkylightShadowsEnabled(false);
                SetConsoleMessage("Sombras de skylight desativadas");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "skylight")
            {
                SetConsoleMessage("Uso: /skylight on ou /skylight off");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "fps on")
            {
                showFps = true;
                SetConsoleMessage("Contador de FPS ativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "fps off")
            {
                showFps = false;
                SetConsoleMessage("Contador de FPS desativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "fps")
            {
                SetConsoleMessage("Uso: /fps on ou /fps off");
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteGetCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteSpawnCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteTickCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteTimeCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteEventCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteWaterCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteTissuePulseCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteTissueMutationCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteGrassCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteDebugCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteWorldCommand(commandBody))
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

        private static readonly string[] ConsoleCommands =
        {
            "/help [pagina]",
            "/debugfly [on|off]",
            "/tissuevisual on|off",
            "/tissuefield on|off",
            "/skylight on|off",
            "/fps on|off",
            "/tissuepulse",
            "/tissuepulse <speed|trail|fade|memory|curve|intensity|node> <valor>",
            "/tissuepulse reset",
            "/tissuedamage <valor> [raio]",
            "/tissueheal <valor> [raio]",
            "/tissuecorrupt <valor> [raio]",
            "/tissuememory <valor> [raio]",
            "/tissueflow <valor> [raio]",
            "/tissueremove [raio]",
            "/tissuereset [raio]",
            "/get <item> [quantidade]",
            "/get list",
            "/spawn <entidade> (reservado)",
            "/tick status",
            "/tick",
            "/tick speed <1..16>",
            "/tick pause",
            "/tick resume",
            "/tick reset",
            "/tick step [1..600]",
            "/time",
            "/time status",
            "/time day",
            "/time night",
            "/time dawn",
            "/time sunrise",
            "/time noon",
            "/time sunset",
            "/time midnight",
            "/event status",
            "/event rain start|stop",
            "/event eclipse start|stop",
            "/event clear",
            "/water status",
            "/water tune",
            "/water tune slow|balanced|fast",
            "/water tune <tps|fall|side|search|cells> <valor>",
            "/water place [raio]",
            "/water drain [raio]",
            "/water clear",
            "/grass grow [1..10000]",
            "/debug ticks",
            "/world save"
        };

        private void ShowConsoleHelp(int page)
        {
            consoleHistory.Clear();

            int totalPages = System.Math.Max(1, (int)System.Math.Ceiling(ConsoleCommands.Length / (float)HelpCommandsPerPage));
            page = System.Math.Clamp(page, 1, totalPages);

            consoleMessage = $"Comandos disponiveis (pagina {page}/{totalPages})";
            AddConsoleHistory($"Comandos disponiveis (pagina {page}/{totalPages}):");

            int startIndex = (page - 1) * HelpCommandsPerPage;
            int endIndex = System.Math.Min(startIndex + HelpCommandsPerPage, ConsoleCommands.Length);
            for (int i = startIndex; i < endIndex; i++)
                AddConsoleHistory(ConsoleCommands[i]);

            if (page < totalPages)
                AddConsoleHistory($"/help {page + 1} para mais comandos");
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
                    SetConsoleMessage("Uso: /tick step [1..600]");
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

            SetConsoleMessage("Uso: /tick speed 1..16, /tick step [n], /tick pause, /tick resume, /tick reset, /tick status");
            return true;
        }

        private bool TryExecuteTimeCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("time", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1 || parts[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("day", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.DayCommandTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("night", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.NightCommandTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("dawn", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.PreDawnStartTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("sunrise", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.SunriseStartTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("noon", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.NoonStartTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("sunset", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.SunsetStartTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("midnight", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.DeepNightStartTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            SetConsoleMessage("Uso: /time status|day|night|dawn|sunrise|noon|sunset|midnight");
            return true;
        }

        private void ShowTimeStatus()
        {
            string paused = session.WorldTicksPaused ? " paused" : string.Empty;
            SetConsoleMessage(
                $"Horario:{session.WorldClockText24h} fase:{session.WorldTimePhase} ciclo:{session.WorldCycleIndex} " +
                $"noite:{session.WorldNightStrength:0.00} speed:{session.WorldTickTimeScale:0.##}x{paused}");
            AddConsoleHistory(session.WorldEnvironmentStatusText);
        }

        private bool TryExecuteEventCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("event", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1 || parts[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowEventStatus();
                return true;
            }

            if (parts[1].Equals("clear", System.StringComparison.OrdinalIgnoreCase))
            {
                session.EnvironmentSystem.ClearEvents();
                SetConsoleMessage("Eventos ambientais limpos");
                ShowEventStatus();
                return true;
            }

            if (parts.Length < 3)
            {
                ShowEventUsage();
                return true;
            }

            string target = parts[1].ToLowerInvariant();
            string action = parts[2].ToLowerInvariant();
            if (target == "rain")
            {
                if (action == "start")
                {
                    session.EnvironmentSystem.ForceRain();
                    SetConsoleMessage("Chuva forcada: pressagio iniciado");
                    ShowEventStatus();
                    return true;
                }

                if (action == "stop")
                {
                    session.EnvironmentSystem.StopRain();
                    SetConsoleMessage("Chuva dissipando");
                    ShowEventStatus();
                    return true;
                }
            }

            if (target == "eclipse")
            {
                if (action == "start")
                {
                    session.EnvironmentSystem.ForceEclipse();
                    SetConsoleMessage("Eclipse forcado: transicao iniciada");
                    ShowEventStatus();
                    return true;
                }

                if (action == "stop")
                {
                    session.EnvironmentSystem.StopEclipse();
                    SetConsoleMessage("Eclipse dissipando");
                    ShowEventStatus();
                    return true;
                }
            }

            ShowEventUsage();
            return true;
        }

        private void ShowEventStatus()
        {
            AddConsoleHistory("Eventos: " + session.WorldEnvironmentStatusText);
            AddConsoleHistory(
                $"Weather rain:{session.WeatherState.RainIntensity:0.00} cloud:{session.WeatherState.CloudCover:0.00} " +
                $"wind:{session.WeatherState.Wind:0.00} wet:{session.WeatherState.Wetness:0.00}");
            AddConsoleHistory(
                $"Tissue correction:{session.TissueCycleState.CorrectionStrength:0.00} " +
                $"stage:{session.TissueCycleState.Stage}");
        }

        private void ShowEventUsage()
        {
            SetConsoleMessage("Uso: /event status, /event rain start|stop, /event eclipse start|stop, /event clear");
        }

        private bool TryExecuteWaterCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("water", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1 || parts[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
            {
                SetConsoleMessage(session.GetWaterStatusText());
                return true;
            }

            if (parts[1].Equals("tune", System.StringComparison.OrdinalIgnoreCase))
            {
                ExecuteWaterTuneCommand(parts);
                return true;
            }

            if (parts[1].Equals("clear", System.StringComparison.OrdinalIgnoreCase))
            {
                int removed = session.ClearWater();
                SetConsoleMessage($"Water clear: {removed} tiles removidos");
                return true;
            }

            if (parts[1].Equals("place", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseWaterRadius(parts, defaultRadius: 1, out int radiusTiles))
                {
                    SetConsoleMessage("Uso: /water place [raio 0..16]");
                    return true;
                }

                int placed = session.PlaceWaterAtMouse(consoleTargetWorld, radiusTiles);
                SetConsoleMessage($"Water place: {placed} tiles");
                return true;
            }

            if (parts[1].Equals("drain", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseWaterRadius(parts, defaultRadius: 2, out int radiusTiles))
                {
                    SetConsoleMessage("Uso: /water drain [raio 0..16]");
                    return true;
                }

                int removed = session.DrainWaterAtMouse(consoleTargetWorld, radiusTiles);
                SetConsoleMessage($"Water drain: {removed} tiles");
                return true;
            }

            SetConsoleMessage("Uso: /water status, /water tune, /water place [raio], /water drain [raio], /water clear");
            return true;
        }

        private void ExecuteWaterTuneCommand(string[] parts)
        {
            if (parts.Length == 2 ||
                (parts.Length == 3 && parts[2].Equals("status", System.StringComparison.OrdinalIgnoreCase)))
            {
                SetConsoleMessage(session.GetWaterTuningText());
                return;
            }

            if (parts.Length == 3)
            {
                session.TryApplyWaterTuningPreset(parts[2], out string presetMessage);
                SetConsoleMessage(presetMessage);
                return;
            }

            if (parts.Length == 4 &&
                int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                session.TrySetWaterTuningValue(parts[2], value, out string tuneMessage);
                SetConsoleMessage(tuneMessage);
                return;
            }

            SetConsoleMessage("Uso: /water tune, /water tune slow|balanced|fast, /water tune <tps|fall|side|search|cells> <valor>");
        }

        private static bool TryParseWaterRadius(string[] parts, int defaultRadius, out int radiusTiles)
        {
            radiusTiles = defaultRadius;
            if (parts.Length <= 2)
                return true;

            if (parts.Length > 3 ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out radiusTiles))
            {
                return false;
            }

            radiusTiles = System.Math.Clamp(radiusTiles, 0, 16);
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
                    SetConsoleMessage("Uso: /grass grow [samples]");
                    return true;
                }

                samples = System.Math.Clamp(samples, 1, 10000);
                int grown = session.ForceGrassGrowthSamples(samples);
                SetConsoleMessage($"Grass grow: {grown}/{samples}");
                return true;
            }

            SetConsoleMessage("Uso: /grass grow [samples]");
            return true;
        }

        private bool TryExecuteTissuePulseCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("tissuepulse", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1 ||
                parts[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowTissuePulseStatus();
                return true;
            }

            if (parts[1].Equals("reset", System.StringComparison.OrdinalIgnoreCase))
            {
                TissueConfig.Resonance.ResetTuning();
                SetConsoleMessage("Tissue pulse: parametros restaurados");
                ShowTissuePulseStatus();
                return true;
            }

            if (parts[1].Equals("help", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowTissuePulseUsage();
                return true;
            }

            if (parts.Length < 3 ||
                !float.TryParse(parts[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                ShowTissuePulseUsage();
                return true;
            }

            string parameter = parts[1].ToLowerInvariant();
            float appliedValue;
            switch (parameter)
            {
                case "speed":
                    appliedValue = TissueConfig.Resonance.SetPulseSpeed(value);
                    break;
                case "trail":
                    appliedValue = TissueConfig.Resonance.SetPulseTrailLength(value);
                    break;
                case "fade":
                    appliedValue = TissueConfig.Resonance.SetPulseFadePower(value);
                    break;
                case "memory":
                    appliedValue = TissueConfig.Resonance.SetMemoryLifetime(value);
                    break;
                case "curve":
                    appliedValue = TissueConfig.Resonance.SetMemoryFadeCurve(value);
                    break;
                case "intensity":
                    appliedValue = TissueConfig.Resonance.SetMemoryIntensity(value);
                    break;
                case "node":
                case "nodeafterglow":
                case "nodelifetime":
                    parameter = "node";
                    appliedValue = TissueConfig.Resonance.SetNodeAfterglowLifetime(value);
                    break;
                default:
                    ShowTissuePulseUsage();
                    return true;
            }

            SetConsoleMessage($"Tissue pulse {parameter}: {appliedValue:0.###}");
            return true;
        }

        private bool TryExecuteTissueMutationCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            string operation = parts[0].ToLowerInvariant();
            bool hasValue = operation is
                "tissuedamage" or
                "tissueheal" or
                "tissuecorrupt" or
                "tissuememory" or
                "tissueflow";
            bool isSimpleOperation = operation is "tissueremove" or "tissuereset";
            if (!hasValue && !isSimpleOperation)
                return false;

            float value = 0f;
            int radiusPartIndex;
            if (hasValue)
            {
                if (parts.Length < 2 || parts.Length > 3 ||
                    !float.TryParse(
                        parts[1].Replace(',', '.'),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value) ||
                    float.IsNaN(value) ||
                    float.IsInfinity(value) ||
                    value < 0f ||
                    value > 1f ||
                    (operation != "tissueflow" && value <= 0f))
                {
                    ShowTissueMutationUsage(operation);
                    return true;
                }

                radiusPartIndex = 2;
            }
            else
            {
                if (parts.Length > 2)
                {
                    ShowTissueMutationUsage(operation);
                    return true;
                }

                radiusPartIndex = 1;
            }

            int radius = 0;
            if (parts.Length > radiusPartIndex &&
                (!int.TryParse(
                    parts[radiusPartIndex],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out radius) ||
                 radius < 0 || radius > 16))
            {
                ShowTissueMutationUsage(operation);
                return true;
            }

            Point target = session.WorldMap.WorldToTile(consoleTargetWorld);
            int centerX = session.WorldMap.WrapTileX(target.X);
            int changedCount = 0;
            int radiusSquared = radius * radius;
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                int tileY = target.Y + offsetY;
                if (tileY < 0 || tileY >= session.WorldMap.Height)
                    continue;

                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (radius > 0 &&
                        ((offsetX * offsetX) + (offsetY * offsetY)) > radiusSquared)
                    {
                        continue;
                    }

                    int tileX = centerX + offsetX;
                    bool changed = operation switch
                    {
                        "tissuedamage" => session.TissueMutations.DamageTile(tileX, tileY, value),
                        "tissueheal" => session.TissueMutations.RestoreTile(tileX, tileY, value),
                        "tissuecorrupt" => session.TissueMutations.AddCorruption(tileX, tileY, value),
                        "tissuememory" => session.TissueMutations.AddMemory(tileX, tileY, value),
                        "tissueflow" => session.TissueMutations.SetFlow(tileX, tileY, value),
                        "tissueremove" => session.TissueMutations.RemoveTissue(tileX, tileY),
                        "tissuereset" => session.TissueMutations.ResetTile(tileX, tileY),
                        _ => false
                    };
                    if (changed)
                        changedCount++;
                }
            }

            SetConsoleMessage(
                $"{operation}: {changedCount} tile(s) alterado(s) em ({centerX}, {target.Y}), raio {radius}");
            return true;
        }

        private void ShowTissueMutationUsage(string operation)
        {
            if (operation is "tissueremove" or "tissuereset")
            {
                SetConsoleMessage($"Uso: /{operation} [raio 0..16]");
                return;
            }

            SetConsoleMessage($"Uso: /{operation} <valor 0..1> [raio 0..16]");
        }

        private bool TryExecuteGetCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("get", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1)
            {
                ShowGetUsage();
                return true;
            }

            if (parts.Length == 2 && parts[1].Equals("list", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowGetItemList();
                return true;
            }

            int identifierPartCount = parts.Length - 1;
            int quantity = 1;
            if (parts.Length >= 3 &&
                int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedQuantity))
            {
                quantity = parsedQuantity;
                identifierPartCount--;
            }

            if (quantity < 1 || quantity > 9999 || identifierPartCount <= 0)
            {
                ShowGetUsage();
                return true;
            }

            string itemIdentifier = string.Join(' ', parts, 1, identifierPartCount);
            if (!ItemDefinitions.TryResolveCommandId(itemIdentifier, out ItemDefinition definition))
            {
                SetConsoleMessage($"Item desconhecido: {itemIdentifier}. Use /get list");
                return true;
            }

            int added = session.StoreItem(definition.Id, quantity, preferInventory: true);
            string commandId = ItemDefinitions.GetCommandId(definition.Id);
            if (added == quantity)
            {
                SetConsoleMessage($"Adicionado: {added}x {definition.Name} [{commandId}]");
            }
            else if (added > 0)
            {
                SetConsoleMessage($"Inventario cheio: adicionado {added}/{quantity}x {definition.Name} [{commandId}]");
            }
            else
            {
                SetConsoleMessage($"Inventario cheio: nenhum {definition.Name} foi adicionado");
            }

            return true;
        }

        private void ShowGetUsage()
        {
            SetConsoleMessage("Uso: /get <item> [quantidade 1..9999]");
            AddConsoleHistory("Use /get list para ver todos os IDs disponíveis");
        }

        private void ShowGetItemList()
        {
            SetConsoleMessage("Itens disponiveis para /get:");
            foreach (ItemDefinition definition in ItemDefinitions.GetAll())
            {
                string stack = definition.Stackable
                    ? $"stack {definition.MaxStack}"
                    : "nao empilhavel";
                AddConsoleHistory(
                    $"{ItemDefinitions.GetCommandId(definition.Id)} (#{(byte)definition.Id}) - {definition.Name}, {stack}");
            }
        }

        private bool TryExecuteSpawnCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("spawn", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1)
            {
                SetConsoleMessage("Uso: /spawn <entidade>. Nenhuma entidade debug registrada ainda");
                return true;
            }

            string identifier = string.Join(' ', parts, 1, parts.Length - 1).ToLowerInvariant();
            if (identifier == "enemy" || identifier == "enemy fsm")
            {
                SpawnDebugEnemy(Nyvorn.Source.Gameplay.Entities.Enemies.EnemyConfig.Default);
                SetConsoleMessage("Inimigo (FSM) spawnado perto do jogador");
                return true;
            }

            if (identifier == "enemy signature" || identifier == "enemy utility")
            {
                SpawnDebugEnemy(Nyvorn.Source.Gameplay.Entities.Enemies.EnemyConfig.Signature);
                SetConsoleMessage("Inimigo assinatura (UtilityBrain + pathfinding) spawnado perto do jogador");
                return true;
            }

            if (ItemDefinitions.TryResolveCommandId(identifier, out ItemDefinition item))
            {
                SetConsoleMessage(
                    $"{item.Name} e item. Use /get {ItemDefinitions.GetCommandId(item.Id)} [quantidade]");
                return true;
            }

            SetConsoleMessage($"Entidade desconhecida: {identifier}. Use /spawn enemy ou /spawn enemy signature");
            return true;
        }

        private void SpawnDebugEnemy(Nyvorn.Source.Gameplay.Entities.Enemies.EnemyConfig enemyConfig)
        {
            Vector2 spawnPosition = session.Player.Position + new Vector2(48f, 0f);
            Nyvorn.Source.Gameplay.Entities.Enemies.Enemy enemy = session.EntityRuntimeSystem.EnemyRespawnController.SpawnAt(spawnPosition, enemyConfig);
            session.Enemies.Add(enemy);
        }

        private void ShowTissuePulseStatus()
        {
            SetConsoleMessage(
                $"Pulse speed:{TissueConfig.Resonance.PulseSpeed:0.##} " +
                $"trail:{TissueConfig.Resonance.PulseTrailLength:0.##} " +
                $"fade:{TissueConfig.Resonance.PulseFadePower:0.##}");
            AddConsoleHistory(
                $"Memory lifetime:{TissueConfig.Resonance.MemoryLifetime:0.##} " +
                $"curve:{TissueConfig.Resonance.MemoryFadeCurve:0.##} " +
                $"intensity:{TissueConfig.Resonance.MemoryIntensity:0.##} " +
                $"node:{TissueConfig.Resonance.NodeAfterglowLifetime:0.##}");
        }

        private void ShowTissuePulseUsage()
        {
            SetConsoleMessage("Uso: /tissuepulse <speed|trail|fade|memory|curve|intensity|node> <valor>");
            AddConsoleHistory("Use /tissuepulse para status ou /tissuepulse reset para restaurar");
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

            SetConsoleMessage("Uso: /debug ticks");
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

            SetConsoleMessage("Uso: /world save");
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

            const string promptPrefix = "> ";
            Vector2 promptPosition = new(10, inputBounds.Y + 5);
            if (HasConsoleSelection)
            {
                GetConsoleSelectionRange(out int selectionStart, out int selectionLength);
                float selectionX = promptPosition.X + consoleFont.MeasureString(promptPrefix + consoleInput[..selectionStart]).X;
                float selectionWidth = consoleFont.MeasureString(consoleInput.Substring(selectionStart, selectionLength)).X;
                spriteBatch.Draw(
                    consolePixel,
                    new Rectangle(
                        (int)System.MathF.Floor(selectionX),
                        inputBounds.Y + 4,
                        System.Math.Max(2, (int)System.MathF.Ceiling(selectionWidth)),
                        lineHeight),
                    new Color(55, 115, 170, 190));
            }

            spriteBatch.DrawString(consoleFont, promptPrefix + consoleInput, promptPosition, Color.White);
            if ((consoleCursorBlinkTimer % 1f) < 0.58f)
            {
                float cursorX = promptPosition.X + consoleFont.MeasureString(promptPrefix + consoleInput[..consoleCursor]).X;
                spriteBatch.Draw(
                    consolePixel,
                    new Rectangle((int)System.MathF.Round(cursorX), inputBounds.Y + 6, 2, lineHeight - 4),
                    new Color(190, 235, 255));
            }

            const string shortcutHint = "Ctrl+A/C/X/V  Shift+Left/Right  Up/Down: history";
            float hintWidth = consoleFont.MeasureString(shortcutHint).X;
            float hintX = screenWidth - hintWidth - 10f;
            if (consoleInput.Length == 0 && hintX > 420f)
            {
                spriteBatch.DrawString(
                    consoleFont,
                    shortcutHint,
                    new Vector2(hintX, inputBounds.Y + 5),
                    new Color(135, 165, 178));
            }
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
            session.RespawnPlayerAtWorldCenter();
            deathStatePushed = false;
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            session.Camera.CenterOn(session.Player.Position + new Vector2(8f, 12f), screenW, screenH);
            saveService.SavePlayerOnly(session);
            autoSaveTimer = AutoSaveInterval;
            stateMachine.PopState();
        }
    }
}
