using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class ElyraSkyRenderer
    {
        private readonly Texture2D pixel;

        private static readonly Color DefaultSkyColor = new(102, 190, 255);

        public ElyraSkyRenderer(GraphicsDevice graphicsDevice)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            Draw(spriteBatch, screenWidth, screenHeight, DefaultSkyColor);
        }

        public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight, Color skyColor)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            spriteBatch.Draw(pixel, new Rectangle(0, 0, screenWidth, screenHeight), skyColor);
        }
    }
}
