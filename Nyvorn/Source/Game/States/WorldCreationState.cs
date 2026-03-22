using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
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

        private readonly GraphicsDevice graphicsDevice;
        private readonly ContentManager content;
        private readonly StateMachine stateMachine;
        private readonly SpriteFont font;
        private readonly Texture2D pixel;
        private readonly PlanetSaveService saveService = new();

        private KeyboardState previousKeyboard;
        private MouseState previousMouse;
        private FocusField focusField = FocusField.PlanetName;

        private string planetName = "Elyra";
        private string seedText = "1337";
        private WorldSizePreset selectedPreset = WorldSizePreset.Medium;

        public WorldCreationState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
            this.stateMachine = stateMachine;
            font = content.Load<SpriteFont>("ui/UIFont");
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
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

            if ((keyboard.IsKeyDown(Keys.Tab) && !previousKeyboard.IsKeyDown(Keys.Tab)) ||
                (keyboard.IsKeyDown(Keys.Down) && !previousKeyboard.IsKeyDown(Keys.Down)))
            {
                focusField = NextFocus(focusField);
            }
            else if (keyboard.IsKeyDown(Keys.Up) && !previousKeyboard.IsKeyDown(Keys.Up))
            {
                focusField = PreviousFocus(focusField);
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
            spriteBatch.DrawString(font, "Crie um planeta e entre em Elyra.", titlePos + new Vector2(0f, 26f), new Color(168, 230, 207));

            DrawLabeledField(spriteBatch, "Planeta", GetPlanetNameBounds(), planetName, focusField == FocusField.PlanetName);
            DrawLabeledField(spriteBatch, "Seed", GetSeedBounds(), string.IsNullOrWhiteSpace(seedText) ? "Aleatoria" : seedText, focusField == FocusField.Seed);
            DrawPresetSelector(spriteBatch);
            DrawCreateButton(spriteBatch);
            DrawPresetDescription(spriteBatch);

            spriteBatch.End();
        }

        private void HandleMouseClick(Point mousePosition)
        {
            if (GetPlanetNameBounds().Contains(mousePosition))
            {
                focusField = FocusField.PlanetName;
                return;
            }

            if (GetSeedBounds().Contains(mousePosition))
            {
                focusField = FocusField.Seed;
                return;
            }

            IReadOnlyList<(WorldSizePreset Preset, Rectangle Bounds)> presetButtons = GetPresetButtons();
            foreach ((WorldSizePreset preset, Rectangle bounds) in presetButtons)
            {
                if (!bounds.Contains(mousePosition))
                    continue;

                selectedPreset = preset;
                focusField = FocusField.SizePreset;
                return;
            }

            if (GetCreateButtonBounds().Contains(mousePosition))
                focusField = FocusField.CreateButton;
        }

        private void HandleTextInput(KeyboardState keyboard)
        {
            if (focusField != FocusField.PlanetName && focusField != FocusField.Seed)
                return;

            foreach (Keys key in keyboard.GetPressedKeys())
            {
                if (previousKeyboard.IsKeyDown(key))
                    continue;

                if (key == Keys.Back)
                {
                    if (focusField == FocusField.PlanetName && planetName.Length > 0)
                        planetName = planetName[..^1];
                    else if (focusField == FocusField.Seed && seedText.Length > 0)
                        seedText = seedText[..^1];

                    continue;
                }

                if (focusField == FocusField.PlanetName && TryGetPlanetCharacter(keyboard, key, out char planetChar))
                {
                    if (planetName.Length < 18)
                        planetName += planetChar;
                }
                else if (focusField == FocusField.Seed && TryGetSeedCharacter(key, out char seedChar))
                {
                    if (seedText.Length < 10)
                        seedText += seedChar;
                }
            }
        }

        private void DrawLabeledField(SpriteBatch spriteBatch, string label, Rectangle bounds, string value, bool isFocused)
        {
            Color border = isFocused ? new Color(255, 241, 193) : new Color(143, 211, 255);
            Color fill = isFocused ? new Color(34, 61, 69) : new Color(28, 50, 58);
            Vector2 labelPos = new Vector2(bounds.X, bounds.Y - 22);

            spriteBatch.DrawString(font, label, labelPos, border);
            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), border * 0.75f);
            spriteBatch.Draw(pixel, bounds, fill);
            spriteBatch.DrawString(font, value, new Vector2(bounds.X + 12, bounds.Y + 10), Color.White);
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
            WorldGenSettings settings = WorldGenSettings.CreatePreset(selectedPreset, 1337);
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

            spriteBatch.DrawString(font, line1, new Vector2(area.X + 12, area.Y + 10), new Color(255, 241, 193));
            spriteBatch.DrawString(font, line2, new Vector2(area.X + 12, area.Y + 34), new Color(168, 230, 207));
            spriteBatch.DrawString(font, line3, new Vector2(area.X + 12, area.Y + 58), new Color(143, 211, 255));
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
            int seed = ParseSeed();
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

        private int ParseSeed()
        {
            if (int.TryParse(seedText, out int seed))
                return seed;

            return Random.Shared.Next(1, int.MaxValue);
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

            if (TryGetSeedCharacter(key, out result))
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

        private bool TryGetSeedCharacter(Keys key, out char result)
        {
            result = '\0';

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
            return new Rectangle((screenW - 520) / 2, (screenH - 420) / 2, 520, 420);
        }

        private Rectangle GetPlanetNameBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.X + 28, panel.Y + 96, panel.Width - 56, 42);
        }

        private Rectangle GetSeedBounds()
        {
            Rectangle planetBounds = GetPlanetNameBounds();
            return new Rectangle(planetBounds.X, planetBounds.Bottom + 40, planetBounds.Width, 42);
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
