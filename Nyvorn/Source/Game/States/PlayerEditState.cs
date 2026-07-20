using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.World.Persistence;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayerEditState : IGameState
    {
        public bool UpdateBelow => false;
        public bool DrawBelow => false;
        public bool BlockInputBelow => true;

        private readonly GraphicsDevice graphicsDevice;
        private readonly ContentManager content;
        private readonly StateMachine stateMachine;
        private readonly PlayerSaveSummary playerSummary;
        private readonly PlayerSaveService saveService = new();
        private readonly SpriteFont font;
        private readonly Texture2D pixel;

        private KeyboardState previousKeyboard;
        private MouseState previousMouse;
        private string playerName;

        public PlayerEditState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine, PlayerSaveSummary playerSummary)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
            this.stateMachine = stateMachine;
            this.playerSummary = playerSummary;
            playerName = playerSummary?.Name ?? "Jogador";
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

            if (keyboard.IsKeyDown(Keys.Escape) && !previousKeyboard.IsKeyDown(Keys.Escape))
            {
                ReturnToPlayerSelect();
                previousKeyboard = keyboard;
                previousMouse = mouse;
                return;
            }

            if (leftClickPressed && GetCancelButtonBounds().Contains(mouse.Position))
            {
                ReturnToPlayerSelect();
                previousKeyboard = keyboard;
                previousMouse = mouse;
                return;
            }

            HandleTextInput(keyboard);

            bool confirmPressed = (keyboard.IsKeyDown(Keys.Enter) && !previousKeyboard.IsKeyDown(Keys.Enter)) ||
                                  (leftClickPressed && GetSaveButtonBounds().Contains(mouse.Position));
            if (confirmPressed)
            {
                saveService.Rename(playerSummary.PlayerId, playerName);
                ReturnToPlayerSelect();
                previousKeyboard = keyboard;
                previousMouse = mouse;
                return;
            }

            previousKeyboard = keyboard;
            previousMouse = mouse;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            Rectangle panel = GetPanelBounds();
            Rectangle nameBounds = GetPlayerNameBounds();
            Rectangle saveBounds = GetSaveButtonBounds();
            Rectangle cancelBounds = GetCancelButtonBounds();

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            spriteBatch.Draw(pixel, new Rectangle(0, 0, screenW, screenH), new Color(10, 22, 26, 190));
            spriteBatch.Draw(pixel, panel, new Color(23, 42, 49, 235));
            spriteBatch.Draw(pixel, new Rectangle(panel.X - 2, panel.Y - 2, panel.Width + 4, panel.Height + 4), new Color(255, 241, 193, 40));

            Vector2 titlePos = new Vector2(panel.X + 28, panel.Y + 24);
            spriteBatch.DrawString(font, "Editar Jogador", titlePos, new Color(255, 241, 193));
            string wrappedSubtitle = TextLayout.WrapText(font, "Renomeie o personagem salvo.", panel.Width - 56);
            spriteBatch.DrawString(font, wrappedSubtitle, titlePos + new Vector2(0f, 26f), new Color(168, 230, 207));

            DrawLabeledField(spriteBatch, "Nome", nameBounds, string.IsNullOrWhiteSpace(playerName) ? "Jogador" : playerName);

            DrawButton(spriteBatch, saveBounds, "Salvar", new Color(255, 241, 193), new Color(16, 31, 36));
            DrawButton(spriteBatch, cancelBounds, "Cancelar", new Color(28, 50, 58), Color.White);
            spriteBatch.End();
        }

        private void HandleTextInput(KeyboardState keyboard)
        {
            foreach (Keys key in keyboard.GetPressedKeys())
            {
                if (previousKeyboard.IsKeyDown(key))
                    continue;

                if (key == Keys.Back)
                {
                    if (playerName.Length > 0)
                        playerName = playerName[..^1];
                    continue;
                }

                if (TryGetNameCharacter(keyboard, key, out char nameChar) && playerName.Length < 18)
                    playerName += nameChar;
            }
        }

        private void DrawLabeledField(SpriteBatch spriteBatch, string label, Rectangle bounds, string value)
        {
            Color border = new Color(255, 241, 193);
            Color fill = new Color(34, 61, 69);
            Vector2 labelPos = new Vector2(bounds.X, bounds.Y - 22);

            spriteBatch.DrawString(font, label, labelPos, border);
            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), border * 0.75f);
            spriteBatch.Draw(pixel, bounds, fill);
            string wrappedValue = TextLayout.WrapText(font, value, bounds.Width - 24);
            spriteBatch.DrawString(font, wrappedValue, new Vector2(bounds.X + 12, bounds.Y + 10), Color.White);
        }

        private void DrawButton(SpriteBatch spriteBatch, Rectangle bounds, string label, Color fill, Color textColor)
        {
            bool hovered = bounds.Contains(Mouse.GetState().Position);
            Color actualFill = hovered ? Color.Lerp(fill, Color.White, 0.12f) : fill;
            Color border = bounds == GetSaveButtonBounds() ? new Color(143, 211, 255, 180) : new Color(143, 211, 255, 120);

            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), border);
            spriteBatch.Draw(pixel, bounds, actualFill);

            Vector2 labelSize = font.MeasureString(label);
            Vector2 labelPos = new Vector2(bounds.X + (bounds.Width - labelSize.X) * 0.5f, bounds.Y + (bounds.Height - labelSize.Y) * 0.5f);
            spriteBatch.DrawString(font, label, labelPos, textColor);
        }

        private bool TryGetNameCharacter(KeyboardState keyboard, Keys key, out char result)
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

            if (key == Keys.OemMinus || key == Keys.Subtract)
            {
                result = '-';
                return true;
            }

            return false;
        }

        private void ReturnToPlayerSelect()
        {
            stateMachine.ReplaceState(new PlayerSelectState(graphicsDevice, content, stateMachine));
        }

        private Rectangle GetPanelBounds()
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            return new Rectangle((screenW - 520) / 2, (screenH - 280) / 2, 520, 280);
        }

        private Rectangle GetPlayerNameBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.X + 28, panel.Y + 96, panel.Width - 56, 56);
        }

        private Rectangle GetSaveButtonBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.X + 28, panel.Bottom - 74, 220, 42);
        }

        private Rectangle GetCancelButtonBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.Right - 248, panel.Bottom - 74, 220, 42);
        }
    }
}
