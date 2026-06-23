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
using Nyvorn.Source.World.Decorations;
using Nyvorn.Source.World.Persistence;
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
        private KeyboardState previousConsoleKeyboard;
        private float autoSaveTimer;
        private const float AutoSaveInterval = 60f;
        private const int MaxConsoleInputLength = 96;
        private const int MaxConsoleHistoryLines = 20;

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

            CraftTier craftTier = session.WorkbenchRuntimeSystem.GetNearbyCraftTier();
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
                    craftTier = interactionResult.CraftTier;
                }
            }

            if (!handledConsoleThisFrame && input.CyclePowerPressed)
                session.PowerSystem.CycleNextPower();

            if (!handledConsoleThisFrame && !session.IsConstructionMode && input.ActivePowerJustPressed)
                session.PowerSystem.TryActivateCurrentPower();

            session.UpdateSimulationViewport(screenW, screenH);
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
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Back);
                session.DrawLoopedWorldEntities(spriteBatch, screenW, screenH, worldOffset);
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

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrain(spriteBatch, screenW, screenH, worldOffset);
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

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            session.DrawHud(spriteBatch, screenW, screenH);
            if (minimapVisible)
                session.DrawMinimap(spriteBatch, screenW, screenH, minimapTissueMode);
            playerHubUI.Draw(spriteBatch, session.WorkbenchRuntimeSystem.GetNearbyCraftTier());
            if (consoleOpen)
                DrawConsole(spriteBatch, screenW);
            spriteBatch.End();
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
            if (normalized == "help")
            {
                ShowConsoleHelp();
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

            if (normalized == "spawn pickaxe" || normalized == "spawn picareta" || normalized == "spawn wood pickaxe")
            {
                SetConsoleMessage(session.TryDropItem(ItemId.WoodPickaxe)
                    ? "Spawned: wood pickaxe"
                    : "Falha ao spawnar wood pickaxe");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "spawn stone pickaxe")
            {
                SetConsoleMessage(session.TryDropItem(ItemId.StonePickaxe)
                    ? "Spawned: stone pickaxe"
                    : "Falha ao spawnar stone pickaxe");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "spawn iron pickaxe")
            {
                SetConsoleMessage(session.TryDropItem(ItemId.IronPickaxe)
                    ? "Spawned: iron pickaxe"
                    : "Falha ao spawnar iron pickaxe");
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteTickCommand(commandBody))
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

        private void ShowConsoleHelp()
        {
            consoleHistory.Clear();
            consoleMessage = "Comandos disponiveis";
            string[] commands =
            {
                "Comandos disponiveis:",
                "/help",
                "/debugfly [on|off]",
                "/tissuevisual on|off",
                "/tissuefield on|off",
                "/spawn pickaxe",
                "/spawn picareta",
                "/spawn wood pickaxe",
                "/spawn stone pickaxe",
                "/spawn iron pickaxe",
                "/tick status",
                "/tick",
                "/tick speed <1..16>",
                "/tick pause",
                "/tick resume",
                "/tick reset",
                "/tick step [1..600]",
                "/grass grow [1..10000]",
                "/debug ticks",
                "/world save"
            };

            for (int i = 0; i < commands.Length; i++)
                AddConsoleHistory(commands[i]);
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
