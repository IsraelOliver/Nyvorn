using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Nyvorn.Source.Game.States
{
    public sealed class LoadingWorldState : IGameState
    {
        public bool UpdateBelow => false;
        public bool DrawBelow => false;
        public bool BlockInputBelow => true;

        private readonly GraphicsDevice graphicsDevice;
        private readonly ContentManager content;
        private readonly StateMachine stateMachine;
        private readonly PlayingSessionFactory.BuildOperation buildOperation;
        private readonly Action<PlayingSession> onCompleted;
        private readonly SpriteFont font;
        private readonly Texture2D pixel;

        private bool hasDrawnFirstFrame;
        private bool completionHandled;
        private readonly string titleText;
        private float displayedProgress;
        private float busyPulse;

        public LoadingWorldState(
            GraphicsDevice graphicsDevice,
            ContentManager content,
            StateMachine stateMachine,
            PlayingSessionFactory.BuildOperation buildOperation,
            string titleText = "Carregando Mundo",
            Action<PlayingSession> onCompleted = null)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
            this.stateMachine = stateMachine;
            this.buildOperation = buildOperation;
            this.onCompleted = onCompleted;
            this.titleText = titleText;

            font = content.Load<SpriteFont>("ui/UIFont");
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void OnEnter()
        {
            hasDrawnFirstFrame = false;
            completionHandled = false;
            displayedProgress = 0f;
            busyPulse = 0f;
        }

        public void OnExit()
        {
        }

        public void Update(GameTime gameTime)
        {
            if (!hasDrawnFirstFrame || completionHandled)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            float targetProgress = MathHelper.Clamp(buildOperation.Progress, 0f, 1f);
            displayedProgress = MathHelper.Lerp(displayedProgress, targetProgress, 1f - MathF.Exp(-dt * 8f));
            if (buildOperation.IsBusy)
                busyPulse += dt * 1.75f;

            if (!buildOperation.IsCompleted)
                buildOperation.Advance();

            if (!buildOperation.IsCompleted)
                return;

            completionHandled = true;
            PlayingSession session = buildOperation.Result;
            onCompleted?.Invoke(session);
            stateMachine.ReplaceState(new PlayingState(graphicsDevice, content, stateMachine, session));
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            Rectangle panel = new Rectangle((screenW - 540) / 2, (screenH - 220) / 2, 540, 220);
            float t = (float)gameTime.TotalGameTime.TotalSeconds;
            string dots = ((int)(t * 2.5f) % 4) switch
            {
                0 => "",
                1 => ".",
                2 => "..",
                _ => "..."
            };

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            spriteBatch.Draw(pixel, new Rectangle(0, 0, screenW, screenH), new Color(10, 22, 26, 200));
            spriteBatch.Draw(pixel, new Rectangle(panel.X - 3, panel.Y - 3, panel.Width + 6, panel.Height + 6), new Color(143, 211, 255, 64));
            spriteBatch.Draw(pixel, panel, new Color(23, 42, 49, 230));

            Vector2 titlePos = new Vector2(panel.X + 28, panel.Y + 32);
            spriteBatch.DrawString(font, titleText, titlePos, new Color(255, 241, 193));
            string phaseText = buildOperation.IsCompleted ? "Concluido" : buildOperation.CurrentPhaseLabel;
            string statusText = buildOperation.IsCompleted ? "Concluido" : buildOperation.StatusText;
            bool showDetail = !string.Equals(phaseText, statusText, StringComparison.Ordinal);
            spriteBatch.DrawString(font, phaseText + dots, titlePos + new Vector2(0f, 42f), Color.White);
            if (showDetail)
                spriteBatch.DrawString(font, statusText, titlePos + new Vector2(0f, 68f), new Color(168, 230, 207));

            float helperYOffset = showDetail ? 110f : 84f;
            spriteBatch.DrawString(font, "Isso pode levar um pouco mais em mundos grandes.", titlePos + new Vector2(0f, helperYOffset), new Color(168, 230, 207));

            Rectangle barOuter = new Rectangle(panel.X + 28, panel.Bottom - 56, panel.Width - 56, 16);
            spriteBatch.Draw(pixel, barOuter, new Color(18, 34, 40, 220));
            float targetProgress = MathHelper.Clamp(buildOperation.Progress, 0f, 1f);
            float visibleProgress = MathHelper.Clamp(displayedProgress, 0f, 1f);
            if (buildOperation.IsBusy)
            {
                float pulse = (MathF.Sin(busyPulse * MathF.PI * 2f) * 0.5f) + 0.5f;
                visibleProgress = Math.Max(visibleProgress, Math.Min(1f, targetProgress + (pulse * 0.015f)));
            }

            int progressWidth = (int)MathF.Round(barOuter.Width * visibleProgress);
            if (progressWidth > 0)
                spriteBatch.Draw(pixel, new Rectangle(barOuter.X, barOuter.Y, progressWidth, barOuter.Height), new Color(143, 211, 255));

            string percentText = $"{(int)MathF.Round(visibleProgress * 100f)}%";
            Vector2 percentSize = font.MeasureString(percentText);
            spriteBatch.DrawString(
                font,
                percentText,
                new Vector2(barOuter.Right - percentSize.X, barOuter.Y - 28),
                new Color(255, 241, 193));
            spriteBatch.End();

            hasDrawnFirstFrame = true;
        }
    }
}
