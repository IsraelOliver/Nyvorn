using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    public sealed class WorldCreationState : IGameState
    {
        private enum FocusField
        {
            PlanetName,
            Seed,
            SizePreset,
            CreateButton
        }

        public bool UpdateBelow => false;
        public bool DrawBelow => false;
        public bool BlockInputBelow => true;

        private const int PlanetNameMaxLength = 18;
        private const int SeedTextMaxLength = 32;
        private const double KeyRepeatInitialDelaySeconds = 0.34;
        private const double KeyRepeatIntervalSeconds = 0.045;
        private const float CaretBlinkIntervalSeconds = 0.48f;

        private readonly GraphicsDevice graphicsDevice;
        private readonly ContentManager content;
        private readonly StateMachine stateMachine;
        private readonly SpriteFont font;
        private readonly Texture2D pixel;
        private readonly PlanetSaveService saveService = new();
        private readonly Dictionary<Keys, double> nextRepeatByKey = new();

        private KeyboardState previousKeyboard;
        private MouseState previousMouse;
        private FocusField focusField = FocusField.PlanetName;

        private string planetName = "Elyra";
        private string seedText = SeedHash.CreateRandomSeedText();
        private WorldSizePreset selectedPreset = WorldSizePreset.Medium;
        private int planetNameCaretIndex;
        private int seedCaretIndex;
        private double textEditClockSeconds;
        private float caretBlinkTimer;

        public WorldCreationState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
            this.stateMachine = stateMachine;
            font = content.Load<SpriteFont>("ui/UIFont");
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
            planetNameCaretIndex = planetName.Length;
            seedCaretIndex = seedText.Length;
        }

        public void OnEnter()
        {
            previousKeyboard = Keyboard.GetState();
            previousMouse = Mouse.GetState();
        }

        public void OnExit()
        {
        }

        public void Update(GameTime gameTime)
        {
            KeyboardState keyboard = Keyboard.GetState();
            MouseState mouse = Mouse.GetState();
            bool leftClickPressed = mouse.LeftButton == ButtonState.Pressed &&
                                    previousMouse.LeftButton == ButtonState.Released;
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            textEditClockSeconds += gameTime.ElapsedGameTime.TotalSeconds;
            caretBlinkTimer += dt;
            RemoveReleasedRepeatKeys(keyboard);

            if ((keyboard.IsKeyDown(Keys.Tab) && !previousKeyboard.IsKeyDown(Keys.Tab)) ||
                (keyboard.IsKeyDown(Keys.Down) && !previousKeyboard.IsKeyDown(Keys.Down)))
            {
                SetFocus(NextFocus(focusField));
            }
            else if (keyboard.IsKeyDown(Keys.Up) && !previousKeyboard.IsKeyDown(Keys.Up))
            {
                SetFocus(PreviousFocus(focusField));
            }
            else if (keyboard.IsKeyDown(Keys.Escape) && !previousKeyboard.IsKeyDown(Keys.Escape))
            {
                stateMachine.ReplaceState(new WorldSelectState(graphicsDevice, content, stateMachine));
                previousKeyboard = keyboard;
                previousMouse = mouse;
                return;
            }

            if (focusField == FocusField.SizePreset)
            {
                if (keyboard.IsKeyDown(Keys.Left) && !previousKeyboard.IsKeyDown(Keys.Left))
                    selectedPreset = CyclePreset(-1);
                else if (keyboard.IsKeyDown(Keys.Right) && !previousKeyboard.IsKeyDown(Keys.Right))
                    selectedPreset = CyclePreset(1);
            }

            if (leftClickPressed)
                HandleMouseClick(mouse.Position);

            HandleTextInput(keyboard);

            bool createPressed = (keyboard.IsKeyDown(Keys.Enter) && !previousKeyboard.IsKeyDown(Keys.Enter)) ||
                                 (leftClickPressed && GetCreateButtonBounds().Contains(mouse.Position));
            if (createPressed)
                CreateWorld();

            previousKeyboard = keyboard;
            previousMouse = mouse;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            Rectangle panel = GetPanelBounds();

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            spriteBatch.Draw(pixel, new Rectangle(0, 0, screenW, screenH), new Color(10, 22, 26, 180));
            spriteBatch.Draw(pixel, panel, new Color(23, 42, 49, 220));
            spriteBatch.Draw(pixel, new Rectangle(panel.X - 2, panel.Y - 2, panel.Width + 4, panel.Height + 4), new Color(255, 241, 193, 40));
            spriteBatch.Draw(pixel, panel, new Color(23, 42, 49, 220));

            Vector2 titlePos = new Vector2(panel.X + 28, panel.Y + 24);
            spriteBatch.DrawString(font, "Novo Mundo", titlePos, new Color(255, 241, 193));
            string wrappedSubtitle = TextLayout.WrapText(font, "Crie um planeta e entre em Elyra.", panel.Width - 56);
            spriteBatch.DrawString(font, wrappedSubtitle, titlePos + new Vector2(0f, 26f), new Color(168, 230, 207));

            DrawLabeledField(spriteBatch, "Planeta", GetPlanetNameBounds(), planetName, focusField == FocusField.PlanetName);
            DrawLabeledField(
                spriteBatch,
                "Seed",
                GetSeedBounds(),
                seedText,
                focusField == FocusField.Seed,
                placeholder: "Aleatoria");
            DrawPresetSelector(spriteBatch);
            DrawCreateButton(spriteBatch);
            DrawPresetDescription(spriteBatch);

            spriteBatch.End();
        }

        private void HandleMouseClick(Point mousePosition)
        {
            if (GetPlanetNameBounds().Contains(mousePosition))
            {
                SetFocus(FocusField.PlanetName);
                planetNameCaretIndex = GetCaretIndexFromMouse(planetName, GetPlanetNameBounds(), mousePosition);
                ResetCaretBlink();
                return;
            }

            if (GetSeedBounds().Contains(mousePosition))
            {
                SetFocus(FocusField.Seed);
                seedCaretIndex = GetCaretIndexFromMouse(seedText, GetSeedBounds(), mousePosition);
                ResetCaretBlink();
                return;
            }

            IReadOnlyList<(WorldSizePreset Preset, Rectangle Bounds)> presetButtons = GetPresetButtons();
            foreach ((WorldSizePreset preset, Rectangle bounds) in presetButtons)
            {
                if (!bounds.Contains(mousePosition))
                    continue;

                selectedPreset = preset;
                SetFocus(FocusField.SizePreset);
                return;
            }

            if (GetCreateButtonBounds().Contains(mousePosition))
                SetFocus(FocusField.CreateButton);
        }

        private void HandleTextInput(KeyboardState keyboard)
        {
            if (focusField != FocusField.PlanetName && focusField != FocusField.Seed)
                return;

            foreach (Keys key in keyboard.GetPressedKeys())
            {
                if (!ShouldTriggerKey(key, keyboard))
                    continue;

                if (HandleTextCommand(key))
                    continue;

                if (focusField == FocusField.PlanetName && TryGetPlanetCharacter(keyboard, key, out char planetChar))
                {
                    InsertActiveTextCharacter(planetChar, PlanetNameMaxLength);
                }
                else if (focusField == FocusField.Seed && TryGetSeedCharacter(keyboard, key, out char seedChar))
                {
                    InsertActiveTextCharacter(seedChar, SeedTextMaxLength);
                }
            }
        }

        private void DrawLabeledField(
            SpriteBatch spriteBatch,
            string label,
            Rectangle bounds,
            string value,
            bool isFocused,
            string placeholder = null)
        {
            Color border = isFocused ? new Color(255, 241, 193) : new Color(143, 211, 255);
            Color fill = isFocused ? new Color(34, 61, 69) : new Color(28, 50, 58);
            Vector2 labelPos = new Vector2(bounds.X, bounds.Y - 22);

            spriteBatch.DrawString(font, label, labelPos, border);
            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), border * 0.75f);
            spriteBatch.Draw(pixel, bounds, fill);

            bool isPlaceholder = string.IsNullOrEmpty(value) && !string.IsNullOrWhiteSpace(placeholder);
            string displayValue = isPlaceholder ? placeholder : value;
            Color textColor = isPlaceholder ? new Color(143, 211, 255, 120) : Color.White;
            Vector2 textPos = new Vector2(bounds.X + 12, bounds.Y + 10);

            spriteBatch.DrawString(font, displayValue, textPos, textColor);

            if (isFocused && IsCaretVisible())
                DrawTextCaret(spriteBatch, bounds, textPos, value, GetActiveCaretIndex());
        }

        private void DrawTextCaret(SpriteBatch spriteBatch, Rectangle bounds, Vector2 textPos, string value, int caretIndex)
        {
            int clampedCaret = Math.Clamp(caretIndex, 0, value.Length);
            string beforeCaret = clampedCaret <= 0 ? string.Empty : value[..clampedCaret];
            float caretX = textPos.X + font.MeasureString(beforeCaret).X;
            int x = Math.Clamp((int)MathF.Round(caretX), bounds.X + 8, bounds.Right - 10);
            int y = (int)MathF.Round(textPos.Y + 2f);
            int height = Math.Min(bounds.Height - 18, Math.Max(12, font.LineSpacing - 6));

            spriteBatch.Draw(pixel, new Rectangle(x, y, 2, height), Color.White);
        }

        private bool HandleTextCommand(Keys key)
        {
            switch (key)
            {
                case Keys.Back:
                    RemoveActiveTextBeforeCaret();
                    return true;

                case Keys.Delete:
                    RemoveActiveTextAtCaret();
                    return true;

                case Keys.Left:
                    MoveActiveCaret(-1);
                    return true;

                case Keys.Right:
                    MoveActiveCaret(1);
                    return true;

                case Keys.Home:
                    SetActiveCaretIndex(0);
                    ResetCaretBlink();
                    return true;

                case Keys.End:
                    SetActiveCaretIndex(GetActiveText().Length);
                    ResetCaretBlink();
                    return true;

                default:
                    return false;
            }
        }

        private void InsertActiveTextCharacter(char character, int maxLength)
        {
            string value = GetActiveText();
            if (value.Length >= maxLength)
                return;

            int caretIndex = Math.Clamp(GetActiveCaretIndex(), 0, value.Length);
            string nextValue = value.Insert(caretIndex, character.ToString());
            SetActiveText(nextValue);
            SetActiveCaretIndex(caretIndex + 1);
            ResetCaretBlink();
        }

        private void RemoveActiveTextBeforeCaret()
        {
            string value = GetActiveText();
            int caretIndex = Math.Clamp(GetActiveCaretIndex(), 0, value.Length);
            if (caretIndex <= 0)
                return;

            string nextValue = value.Remove(caretIndex - 1, 1);
            SetActiveText(nextValue);
            SetActiveCaretIndex(caretIndex - 1);
            ResetCaretBlink();
        }

        private void RemoveActiveTextAtCaret()
        {
            string value = GetActiveText();
            int caretIndex = Math.Clamp(GetActiveCaretIndex(), 0, value.Length);
            if (caretIndex >= value.Length)
                return;

            string nextValue = value.Remove(caretIndex, 1);
            SetActiveText(nextValue);
            SetActiveCaretIndex(caretIndex);
            ResetCaretBlink();
        }

        private void MoveActiveCaret(int direction)
        {
            SetActiveCaretIndex(GetActiveCaretIndex() + direction);
            ResetCaretBlink();
        }

        private string GetActiveText()
        {
            return focusField == FocusField.Seed ? seedText : planetName;
        }

        private void SetActiveText(string value)
        {
            if (focusField == FocusField.Seed)
                seedText = value;
            else
                planetName = value;
        }

        private int GetActiveCaretIndex()
        {
            return focusField == FocusField.Seed ? seedCaretIndex : planetNameCaretIndex;
        }

        private void SetActiveCaretIndex(int index)
        {
            if (focusField == FocusField.Seed)
                seedCaretIndex = Math.Clamp(index, 0, seedText.Length);
            else
                planetNameCaretIndex = Math.Clamp(index, 0, planetName.Length);
        }

        private int GetCaretIndexFromMouse(string value, Rectangle bounds, Point mousePosition)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            float localX = Math.Max(0f, mousePosition.X - (bounds.X + 12f));
            int bestIndex = 0;
            float bestDistance = Math.Abs(localX);

            for (int i = 1; i <= value.Length; i++)
            {
                float width = font.MeasureString(value[..i]).X;
                float distance = Math.Abs(localX - width);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                bestIndex = i;
            }

            return bestIndex;
        }

        private bool ShouldTriggerKey(Keys key, KeyboardState keyboard)
        {
            if (!keyboard.IsKeyDown(key))
                return false;

            if (!previousKeyboard.IsKeyDown(key))
            {
                nextRepeatByKey[key] = textEditClockSeconds + KeyRepeatInitialDelaySeconds;
                return true;
            }

            if (!nextRepeatByKey.TryGetValue(key, out double nextRepeatAt))
            {
                nextRepeatByKey[key] = textEditClockSeconds + KeyRepeatInitialDelaySeconds;
                return false;
            }

            if (textEditClockSeconds < nextRepeatAt)
                return false;

            nextRepeatByKey[key] = textEditClockSeconds + KeyRepeatIntervalSeconds;
            return true;
        }

        private void RemoveReleasedRepeatKeys(KeyboardState keyboard)
        {
            if (nextRepeatByKey.Count == 0)
                return;

            List<Keys> releasedKeys = null;
            foreach (KeyValuePair<Keys, double> entry in nextRepeatByKey)
            {
                if (keyboard.IsKeyDown(entry.Key))
                    continue;

                releasedKeys ??= new List<Keys>();
                releasedKeys.Add(entry.Key);
            }

            if (releasedKeys == null)
                return;

            for (int i = 0; i < releasedKeys.Count; i++)
                nextRepeatByKey.Remove(releasedKeys[i]);
        }

        private void SetFocus(FocusField nextFocus)
        {
            if (focusField == nextFocus)
                return;

            focusField = nextFocus;
            nextRepeatByKey.Clear();
            ClampTextCarets();
            ResetCaretBlink();
        }

        private void ClampTextCarets()
        {
            planetNameCaretIndex = Math.Clamp(planetNameCaretIndex, 0, planetName.Length);
            seedCaretIndex = Math.Clamp(seedCaretIndex, 0, seedText.Length);
        }

        private bool IsCaretVisible()
        {
            return (caretBlinkTimer % (CaretBlinkIntervalSeconds * 2f)) < CaretBlinkIntervalSeconds;
        }

        private void ResetCaretBlink()
        {
            caretBlinkTimer = 0f;
        }

        private void DrawPresetSelector(SpriteBatch spriteBatch)
        {
            Vector2 labelPos = new Vector2(GetPlanetNameBounds().X, GetPresetAreaBounds().Y - 22);
            spriteBatch.DrawString(font, "Tamanho", labelPos, focusField == FocusField.SizePreset ? new Color(255, 241, 193) : new Color(143, 211, 255));

            foreach ((WorldSizePreset preset, Rectangle bounds) in GetPresetButtons())
            {
                bool isSelected = preset == selectedPreset;
                Color fill = isSelected ? new Color(66, 118, 127) : new Color(28, 50, 58);
                Color border = isSelected ? new Color(255, 241, 193) : new Color(143, 211, 255);

                spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), border * 0.8f);
                spriteBatch.Draw(pixel, bounds, fill);

                string label = GetPresetLabel(preset);
                Vector2 labelSize = font.MeasureString(label);
                Vector2 textPos = new Vector2(bounds.X + (bounds.Width - labelSize.X) * 0.5f, bounds.Y + (bounds.Height - labelSize.Y) * 0.5f);
                spriteBatch.DrawString(font, label, textPos, Color.White);
            }
        }

        private void DrawPresetDescription(SpriteBatch spriteBatch)
        {
            WorldGenConfig settings = WorldGenConfig.CreatePreset(selectedPreset, WorldGenConfig.DefaultSeed);
            Rectangle area = GetDescriptionBounds();
            spriteBatch.Draw(pixel, area, new Color(18, 34, 40, 210));

            string line1 = $"{settings.WorldWidth}x{settings.WorldHeight} tiles";
            string line2 = selectedPreset switch
            {
                WorldSizePreset.Small => "Versao mais compacta do planeta.",
                WorldSizePreset.Medium => "30% menor que o grande.",
                _ => "Escala base inspirada no medio do Terraria."
            };
            string line3 = "Esquerda/Direita mudam o preset.";

            float textWidth = area.Width - 24;
            string wrappedLine1 = TextLayout.WrapText(font, line1, textWidth);
            string wrappedLine2 = TextLayout.WrapText(font, line2, textWidth);
            string wrappedLine3 = TextLayout.WrapText(font, line3, textWidth);
            Vector2 textPos = new Vector2(area.X + 12, area.Y + 10);

            spriteBatch.DrawString(font, wrappedLine1, textPos, new Color(255, 241, 193));
            textPos.Y += TextLayout.GetWrappedHeight(font, wrappedLine1);
            spriteBatch.DrawString(font, wrappedLine2, textPos, new Color(168, 230, 207));
            textPos.Y += TextLayout.GetWrappedHeight(font, wrappedLine2);
            spriteBatch.DrawString(font, wrappedLine3, textPos, new Color(143, 211, 255));
        }

        private void DrawCreateButton(SpriteBatch spriteBatch)
        {
            Rectangle bounds = GetCreateButtonBounds();
            bool hovered = bounds.Contains(Mouse.GetState().Position);
            bool focused = focusField == FocusField.CreateButton;
            Color fill = focused || hovered ? new Color(255, 241, 193) : new Color(168, 230, 207);
            Color textColor = new Color(16, 31, 36);

            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), new Color(143, 211, 255, 180));
            spriteBatch.Draw(pixel, bounds, fill);

            string label = "Criar Mundo";
            Vector2 labelSize = font.MeasureString(label);
            Vector2 labelPos = new Vector2(bounds.X + (bounds.Width - labelSize.X) * 0.5f, bounds.Y + (bounds.Height - labelSize.Y) * 0.5f);
            spriteBatch.DrawString(font, label, labelPos, textColor);
        }

        private void CreateWorld()
        {
            string seed = ResolveSeedText();
            string finalPlanetName = string.IsNullOrWhiteSpace(planetName) ? "Elyra" : planetName.Trim();
            PlayingSessionFactory factory = new PlayingSessionFactory(graphicsDevice, content);
            stateMachine.ReplaceState(new LoadingWorldState(
                graphicsDevice,
                content,
                stateMachine,
                factory.CreateBuildOperation(finalPlanetName, selectedPreset, seed),
                "Gerando Planeta",
                session => saveService.Save(session)));
        }

        private string ResolveSeedText()
        {
            if (string.IsNullOrWhiteSpace(seedText))
                seedText = SeedHash.CreateRandomSeedText();

            return seedText.Trim();
        }

        private FocusField NextFocus(FocusField current)
        {
            return current switch
            {
                FocusField.PlanetName => FocusField.Seed,
                FocusField.Seed => FocusField.SizePreset,
                FocusField.SizePreset => FocusField.CreateButton,
                _ => FocusField.PlanetName
            };
        }

        private FocusField PreviousFocus(FocusField current)
        {
            return current switch
            {
                FocusField.CreateButton => FocusField.SizePreset,
                FocusField.SizePreset => FocusField.Seed,
                FocusField.Seed => FocusField.PlanetName,
                _ => FocusField.CreateButton
            };
        }

        private WorldSizePreset CyclePreset(int direction)
        {
            WorldSizePreset[] presets = (WorldSizePreset[])Enum.GetValues(typeof(WorldSizePreset));
            int currentIndex = Array.IndexOf(presets, selectedPreset);
            int nextIndex = (currentIndex + direction + presets.Length) % presets.Length;
            return presets[nextIndex];
        }

        private bool TryGetPlanetCharacter(KeyboardState keyboard, Keys key, out char result)
        {
            result = '\0';
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

            if (key >= Keys.A && key <= Keys.Z)
            {
                char baseChar = (char)('a' + (key - Keys.A));
                result = shift ? char.ToUpperInvariant(baseChar) : baseChar;
                return true;
            }

            if (TryGetSeedCharacter(keyboard, key, out result))
                return true;

            if (key == Keys.Space)
            {
                result = ' ';
                return true;
            }

            if (key == Keys.OemMinus || key == Keys.Subtract)
            {
                result = '-';
                return true;
            }

            return false;
        }

        private bool TryGetSeedCharacter(KeyboardState keyboard, Keys key, out char result)
        {
            result = '\0';
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

            if (key >= Keys.A && key <= Keys.Z)
            {
                char baseChar = (char)('a' + (key - Keys.A));
                result = shift ? char.ToUpperInvariant(baseChar) : baseChar;
                return true;
            }

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                result = (char)('0' + (key - Keys.D0));
                return true;
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                result = (char)('0' + (key - Keys.NumPad0));
                return true;
            }

            if (key == Keys.Space)
            {
                result = ' ';
                return true;
            }

            if (key == Keys.OemMinus && shift)
            {
                result = '_';
                return true;
            }

            if (key == Keys.OemMinus || key == Keys.Subtract)
            {
                result = '-';
                return true;
            }

            return false;
        }

        private string GetPresetLabel(WorldSizePreset preset)
        {
            return preset switch
            {
                WorldSizePreset.Small => "Pequeno",
                WorldSizePreset.Medium => "Medio",
                _ => "Grande"
            };
        }

        private IReadOnlyList<(WorldSizePreset Preset, Rectangle Bounds)> GetPresetButtons()
        {
            Rectangle area = GetPresetAreaBounds();
            int buttonGap = 10;
            int buttonWidth = (area.Width - (buttonGap * 2)) / 3;
            List<(WorldSizePreset, Rectangle)> buttons = new(3);
            WorldSizePreset[] presets = (WorldSizePreset[])Enum.GetValues(typeof(WorldSizePreset));

            for (int i = 0; i < presets.Length; i++)
            {
                Rectangle bounds = new Rectangle(area.X + (i * (buttonWidth + buttonGap)), area.Y, buttonWidth, area.Height);
                buttons.Add((presets[i], bounds));
            }

            return buttons;
        }

        private Rectangle GetPanelBounds()
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            return new Rectangle((screenW - 520) / 2, (screenH - 520) / 2, 520, 520);
        }

        private Rectangle GetPlanetNameBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.X + 28, panel.Y + 96, panel.Width - 56, 56);
        }

        private Rectangle GetSeedBounds()
        {
            Rectangle planetBounds = GetPlanetNameBounds();
            return new Rectangle(planetBounds.X, planetBounds.Bottom + 40, planetBounds.Width, 56);
        }

        private Rectangle GetPresetAreaBounds()
        {
            Rectangle seedBounds = GetSeedBounds();
            return new Rectangle(seedBounds.X, seedBounds.Bottom + 46, seedBounds.Width, 42);
        }

        private Rectangle GetDescriptionBounds()
        {
            Rectangle presetBounds = GetPresetAreaBounds();
            return new Rectangle(presetBounds.X, presetBounds.Bottom + 16, presetBounds.Width, 88);
        }

        private Rectangle GetCreateButtonBounds()
        {
            Rectangle descriptionBounds = GetDescriptionBounds();
            return new Rectangle(descriptionBounds.X, descriptionBounds.Bottom + 22, descriptionBounds.Width, 48);
        }
    }
}
