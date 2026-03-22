using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class ElyraSkyRenderer
    {
        private readonly Texture2D pixel;

        private static readonly Color TopColor = new Color(143, 211, 255);
        private static readonly Color MidColor = new Color(154, 221, 233);
        private static readonly Color LifeColor = new Color(168, 230, 207);
        private static readonly Color HorizonColor = new Color(255, 241, 193);

        public ElyraSkyRenderer(GraphicsDevice graphicsDevice)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            spriteBatch.Draw(pixel, new Rectangle(0, 0, screenWidth, screenHeight), TopColor);

            const int gradientBands = 48;
            float horizonStart = screenHeight * 0.56f;

            for (int i = 0; i < gradientBands; i++)
            {
                float t0 = i / (float)gradientBands;
                float t1 = (i + 1) / (float)gradientBands;
                float y = screenHeight * t0;
                float bandHeight = MathHelper.Max(1f, (screenHeight * t1) - y);
                float bandCenter = (y + (bandHeight * 0.5f)) / screenHeight;

                Color bandColor = bandCenter < 0.58f
                    ? Color.Lerp(TopColor, MidColor, bandCenter / 0.58f)
                    : Color.Lerp(MidColor, HorizonColor, (bandCenter - 0.58f) / 0.42f);

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(0, (int)y, screenWidth, (int)Math.Ceiling(bandHeight)),
                    bandColor);
            }

            DrawSoftBand(spriteBatch, screenWidth, screenHeight, screenHeight * 0.22f, screenHeight * 0.28f, LifeColor * 0.14f);
            DrawSoftBand(spriteBatch, screenWidth, screenHeight, screenHeight * 0.48f, screenHeight * 0.18f, LifeColor * 0.18f);
            DrawSoftBand(spriteBatch, screenWidth, screenHeight, horizonStart, screenHeight * 0.24f, HorizonColor * 0.34f);
            DrawSoftBand(spriteBatch, screenWidth, screenHeight, screenHeight * 0.80f, screenHeight * 0.20f, new Color(255, 248, 223) * 0.18f);
        }

        private void DrawSoftBand(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float centerY, float height, Color color)
        {
            const int bandSteps = 18;

            for (int i = 0; i < bandSteps; i++)
            {
                float t = i / (float)(bandSteps - 1);
                float normalized = (t * 2f) - 1f;
                float falloff = 1f - Math.Abs(normalized);
                float segmentHeight = height / bandSteps;
                float y = centerY - (height * 0.5f) + (segmentHeight * i);

                spriteBatch.Draw(
                    pixel,
                    new Rectangle(0, (int)y, screenWidth, (int)Math.Ceiling(segmentHeight) + 1),
                    color * falloff);
            }
        }
    }
}
