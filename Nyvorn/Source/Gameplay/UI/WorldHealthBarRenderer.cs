using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class WorldHealthBarRenderer
    {
        private readonly Texture2D pixel;

        public WorldHealthBarRenderer(GraphicsDevice graphicsDevice)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 anchor, int current, int max, int width, int height)
        {
            if (max <= 0)
                return;

            float ratio = MathHelper.Clamp((float)current / max, 0f, 1f);
            int fill = (int)(width * ratio);
            int x = (int)anchor.X - (width / 2);
            int y = (int)anchor.Y;

            spriteBatch.Draw(pixel, new Rectangle(x - 1, y - 1, width + 2, height + 2), Color.Black * 0.9f);
            spriteBatch.Draw(pixel, new Rectangle(x, y, width, height), new Color(48, 48, 48));
            if (fill > 0)
                spriteBatch.Draw(pixel, new Rectangle(x, y, fill, height), Color.LimeGreen);
        }
    }
}
