using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Game;
using Nyvorn.Source.World.Persistence;

namespace Nyvorn.Source.Game.States
{
    public sealed class PauseMenuState : IGameState
    {
        public bool UpdateBelow => false;
        public bool DrawBelow => true;
        public bool BlockInputBelow => true;

        private readonly GraphicsDevice graphicsDevice;
        private readonly ContentManager content;
        private readonly StateMachine stateMachine;
        private readonly PlayingSession session;
        private readonly PlanetSaveService saveService = new();
        private readonly SpriteFont font;
        private readonly Texture2D pixel;

        private MouseState previousMouse;
        private KeyboardState previousKeyboard;

        public PauseMenuState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine, PlayingSession session)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
            this.stateMachine = stateMachine;
            this.session = session;
            font = content.Load<SpriteFont>("ui/UIFont");
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void OnEnter()
        {
            previousMouse = Mouse.GetState();
            previousKeyboard = Keyboard.GetState();
        }

        public void OnExit()
        {
        }

        public void Update(GameTime gameTime)
        {
            MouseState mouse = Mouse.GetState();
            KeyboardState keyboard = Keyboard.GetState();
            bool leftClickPressed = mouse.LeftButton == ButtonState.Pressed &&
                                    previousMouse.LeftButton == ButtonState.Released;
            bool escapePressed = keyboard.IsKeyDown(Keys.Escape) && !previousKeyboard.IsKeyDown(Keys.Escape);
            bool enterPressed = keyboard.IsKeyDown(Keys.Enter) && !previousKeyboard.IsKeyDown(Keys.Enter);

            if (escapePressed || (leftClickPressed && GetResumeButtonBounds().Contains(mouse.Position)) || enterPressed)
            {
                stateMachine.PopState();
            }
            else if ((leftClickPressed && GetWorldSelectButtonBounds().Contains(mouse.Position)) ||
                     (keyboard.IsKeyDown(Keys.W) && !previousKeyboard.IsKeyDown(Keys.W)))
            {
                saveService.Save(session);
                stateMachine.Clear();
                stateMachine.PushState(new WorldSelectState(graphicsDevice, content, stateMachine));
            }

            previousMouse = mouse;
            previousKeyboard = keyboard;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            Rectangle panel = GetPanelBounds();
            Rectangle resumeButton = GetResumeButtonBounds();
            Rectangle worldsButton = GetWorldSelectButtonBounds();

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            spriteBatch.Draw(pixel, new Rectangle(0, 0, screenW, screenH), Color.Black * 0.42f);
            spriteBatch.Draw(pixel, panel, new Color(23, 42, 49, 230));

            spriteBatch.DrawString(font, "Pausado", new Vector2(panel.X + 28, panel.Y + 24), new Color(255, 241, 193));
            spriteBatch.DrawString(font, session.PlanetMetadata.PlanetName, new Vector2(panel.X + 28, panel.Y + 50), new Color(168, 230, 207));

            DrawButton(spriteBatch, resumeButton, "Continuar", resumeButton.Contains(Mouse.GetState().Position));
            DrawButton(spriteBatch, worldsButton, "Voltar aos Mundos", worldsButton.Contains(Mouse.GetState().Position));

            spriteBatch.DrawString(font, "Esc fecha este menu", new Vector2(panel.X + 28, panel.Bottom - 34), new Color(143, 211, 255));
            spriteBatch.End();
        }

        private void DrawButton(SpriteBatch spriteBatch, Rectangle bounds, string text, bool hovered)
        {
            Color fill = hovered ? new Color(255, 241, 193) : new Color(168, 230, 207);
            Color textColor = new Color(16, 31, 36);

            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), new Color(143, 211, 255, 180));
            spriteBatch.Draw(pixel, bounds, fill);

            Vector2 textSize = font.MeasureString(text);
            Vector2 textPos = new Vector2(bounds.X + (bounds.Width - textSize.X) * 0.5f, bounds.Y + (bounds.Height - textSize.Y) * 0.5f);
            spriteBatch.DrawString(font, text, textPos, textColor);
        }

        private Rectangle GetPanelBounds()
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            return new Rectangle((screenW - 420) / 2, (screenH - 260) / 2, 420, 260);
        }

        private Rectangle GetResumeButtonBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.X + 28, panel.Y + 96, panel.Width - 56, 48);
        }

        private Rectangle GetWorldSelectButtonBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.X + 28, panel.Y + 156, panel.Width - 56, 48);
        }
    }
}
