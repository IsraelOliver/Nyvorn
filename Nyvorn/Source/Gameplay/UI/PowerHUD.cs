using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Powers;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class PowerHUD
    {
        private readonly Texture2D pixel;
        private readonly SpriteFont font;

        public PowerHUD(GraphicsDevice graphicsDevice, SpriteFont font)
        {
            this.font = font;
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void Draw(SpriteBatch spriteBatch, PlayerPowerSystem powerSystem, int screenWidth, int screenHeight)
        {
            Power power = powerSystem?.CurrentPower;
            if (power == null)
                return;

            const int size = 42;
            const int padding = 14;
            int x = padding;
            int y = screenHeight - size - padding;
            Rectangle bounds = new Rectangle(x, y, size, size);

            float cooldown = MathHelper.Clamp(power.CooldownProgress, 0f, 1f);
            Color border = power.IsReady ? new Color(128, 235, 191) : new Color(84, 115, 104);
            Color core = power.IsReady ? new Color(38, 154, 112) : new Color(36, 56, 50);

            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), Color.Black * 0.75f);
            spriteBatch.Draw(pixel, bounds, new Color(13, 22, 20, 230));
            spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 2), border);
            spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 2), border * 0.75f);
            spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, 2, bounds.Height), border * 0.75f);
            spriteBatch.Draw(pixel, new Rectangle(bounds.Right - 2, bounds.Y, 2, bounds.Height), border * 0.75f);

            Rectangle coreBounds = new Rectangle(bounds.X + 11, bounds.Y + 9, 20, 24);
            spriteBatch.Draw(pixel, coreBounds, core);
            spriteBatch.Draw(pixel, new Rectangle(coreBounds.X + 4, coreBounds.Y - 4, 12, 32), core * 0.45f);
            spriteBatch.Draw(pixel, new Rectangle(coreBounds.X - 4, coreBounds.Y + 6, 28, 8), core * 0.35f);

            if (cooldown > 0f)
            {
                int coverHeight = (int)(bounds.Height * cooldown);
                spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Bottom - coverHeight, bounds.Width, coverHeight), Color.Black * 0.45f);
            }

            Vector2 labelPos = new Vector2(bounds.Right + 8, bounds.Y + 4);
            spriteBatch.DrawString(font, power.Name, labelPos + new Vector2(1f, 1f), Color.Black);
            spriteBatch.DrawString(font, power.Name, labelPos, new Color(200, 238, 225));
        }
    }
}
