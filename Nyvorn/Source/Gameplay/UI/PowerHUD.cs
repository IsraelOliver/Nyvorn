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

        public void Draw(SpriteBatch spriteBatch, PlayerPowerSystem powerSystem, int screenWidth, int screenHeight, bool constructionMode)
        {
            if (constructionMode)
            {
                DrawConstructionMode(spriteBatch, screenWidth, screenHeight);
                return;
            }

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

        private void DrawConstructionMode(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            const int size = 42;
            const int padding = 14;
            int x = padding;
            int y = screenHeight - size - padding;
            Rectangle bounds = new Rectangle(x, y, size, size);

            Color border = new Color(234, 188, 92);
            Color baseColor = new Color(24, 26, 24, 235);
            Color wallColor = new Color(92, 118, 108);
            Color wallHighlight = new Color(151, 189, 166);
            Color blueprint = new Color(66, 111, 126);

            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), Color.Black * 0.78f);
            spriteBatch.Draw(pixel, bounds, baseColor);
            spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 2), border);
            spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Bottom - 2, bounds.Width, 2), border * 0.8f);
            spriteBatch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, 2, bounds.Height), border * 0.8f);
            spriteBatch.Draw(pixel, new Rectangle(bounds.Right - 2, bounds.Y, 2, bounds.Height), border * 0.8f);

            Rectangle blueprintBounds = new Rectangle(bounds.X + 8, bounds.Y + 8, 26, 26);
            spriteBatch.Draw(pixel, blueprintBounds, blueprint * 0.32f);

            for (int i = 0; i <= 2; i++)
            {
                int lineOffset = i * 9;
                spriteBatch.Draw(pixel, new Rectangle(blueprintBounds.X + lineOffset, blueprintBounds.Y, 1, blueprintBounds.Height), blueprint * 0.65f);
                spriteBatch.Draw(pixel, new Rectangle(blueprintBounds.X, blueprintBounds.Y + lineOffset, blueprintBounds.Width, 1), blueprint * 0.65f);
            }

            DrawMiniTile(spriteBatch, bounds.X + 11, bounds.Y + 13, wallColor, wallHighlight);
            DrawMiniTile(spriteBatch, bounds.X + 21, bounds.Y + 13, wallColor, wallHighlight);
            DrawMiniTile(spriteBatch, bounds.X + 11, bounds.Y + 23, wallColor, wallHighlight);
            DrawMiniTile(spriteBatch, bounds.X + 21, bounds.Y + 23, wallColor, wallHighlight);

            spriteBatch.Draw(pixel, new Rectangle(bounds.X + 7, bounds.Y + 6, 8, 2), border * 0.9f);
            spriteBatch.Draw(pixel, new Rectangle(bounds.X + 6, bounds.Y + 7, 2, 8), border * 0.9f);

            Vector2 labelPos = new Vector2(bounds.Right + 8, bounds.Y + 3);
            spriteBatch.DrawString(font, "CONSTRUCAO", labelPos + new Vector2(1f, 1f), Color.Black);
            spriteBatch.DrawString(font, "CONSTRUCAO", labelPos, new Color(242, 211, 143));

            Vector2 subLabelPos = labelPos + new Vector2(0f, 17f);
            spriteBatch.DrawString(font, "PAREDES", subLabelPos + new Vector2(1f, 1f), Color.Black * 0.8f);
            spriteBatch.DrawString(font, "PAREDES", subLabelPos, new Color(156, 203, 183));
        }

        private void DrawMiniTile(SpriteBatch spriteBatch, int x, int y, Color fill, Color highlight)
        {
            Rectangle tile = new Rectangle(x, y, 8, 8);
            spriteBatch.Draw(pixel, tile, fill);
            spriteBatch.Draw(pixel, new Rectangle(tile.X, tile.Y, tile.Width, 1), highlight);
            spriteBatch.Draw(pixel, new Rectangle(tile.X, tile.Y, 1, tile.Height), highlight * 0.65f);
            spriteBatch.Draw(pixel, new Rectangle(tile.Right - 1, tile.Y, 1, tile.Height), Color.Black * 0.28f);
            spriteBatch.Draw(pixel, new Rectangle(tile.X, tile.Bottom - 1, tile.Width, 1), Color.Black * 0.32f);
        }
    }
}
