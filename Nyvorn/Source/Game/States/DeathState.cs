using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace Nyvorn.Source.Game.States
{
    public class DeathState : IGameState
    {
        public bool UpdateBelow => false;
        public bool DrawBelow => true;
        public bool BlockInputBelow => true;

        private readonly GraphicsDevice graphicsDevice;
        private readonly Action retryAction;
        private readonly SpriteFont font;
        private readonly Texture2D pixel;

        private MouseState previousMouse;

        public DeathState(GraphicsDevice graphicsDevice, ContentManager content, Action retryAction)
        {
            this.graphicsDevice = graphicsDevice;
            this.retryAction = retryAction;
            font = content.Load<SpriteFont>("ui/UIFont");

            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void OnEnter()
        {
            previousMouse = Mouse.GetState();
        }

        public void OnExit() { }

        public void Update(GameTime gameTime)
        {
            MouseState mouse = Mouse.GetState();
            KeyboardState keyboard = Keyboard.GetState();

            Rectangle retryButton = GetRetryButtonBounds();
            Point mousePoint = new Point(mouse.X, mouse.Y);
            bool clicked = mouse.LeftButton == ButtonState.Pressed &&
                           previousMouse.LeftButton == ButtonState.Released &&
                           retryButton.Contains(mousePoint);

            if (clicked || keyboard.IsKeyDown(Keys.Enter))
                retryAction?.Invoke();

            previousMouse = mouse;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            spriteBatch.Draw(pixel, new Rectangle(0, 0, screenW, screenH), Color.Black * 0.6f);

            string title = "Voce morreu";
            Vector2 titleSize = font.MeasureString(title);
            Vector2 titlePos = new Vector2((screenW - titleSize.X) * 0.5f, (screenH * 0.5f) - 60f);
            spriteBatch.DrawString(font, title, titlePos, Color.White);

            Rectangle retryButton = GetRetryButtonBounds();
            Color buttonColor = retryButton.Contains(Mouse.GetState().Position) ? new Color(200, 200, 200) : Color.White;
            spriteBatch.Draw(pixel, retryButton, buttonColor);
            spriteBatch.Draw(pixel, new Rectangle(retryButton.X - 2, retryButton.Y - 2, retryButton.Width + 4, retryButton.Height + 4), Color.Black * 0.85f);
            spriteBatch.Draw(pixel, retryButton, buttonColor);

            string retryText = "Retry";
            Vector2 retrySize = font.MeasureString(retryText);
            Vector2 retryPos = new Vector2(
                retryButton.X + (retryButton.Width - retrySize.X) * 0.5f,
                retryButton.Y + (retryButton.Height - retrySize.Y) * 0.5f);
            spriteBatch.DrawString(font, retryText, retryPos, Color.Black);

            spriteBatch.End();
        }

        private Rectangle GetRetryButtonBounds()
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            return new Rectangle((screenW / 2) - 70, (screenH / 2), 140, 40);
        }
    }
}
