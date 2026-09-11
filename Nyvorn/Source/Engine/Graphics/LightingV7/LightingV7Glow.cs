using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Engine.Graphics.LightingV7
{
    /// <summary>
    /// The additive halo around a light source, plus the per-source flicker both the halo and the
    /// light map use.
    ///
    /// The gradient is generated once at load as a 64x64 texture. It is a stand-in for hand-drawn
    /// art: swap the texture and nothing else changes.
    /// </summary>
    public sealed class LightingV7Glow
    {
        private const int TextureSize = 64;

        private readonly Texture2D gradient;

        public LightingV7Glow(GraphicsDevice graphicsDevice)
        {
            if (graphicsDevice == null)
                throw new ArgumentNullException(nameof(graphicsDevice));

            gradient = new Texture2D(graphicsDevice, TextureSize, TextureSize, false, SurfaceFormat.Color);

            Color[] pixels = new Color[TextureSize * TextureSize];
            float centre = (TextureSize - 1) * 0.5f;
            float maxRadius = centre;

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float distance = MathF.Sqrt(dx * dx + dy * dy) / maxRadius;

                    // Squared falloff, clipped at the edge so the quad has no visible seam.
                    float alpha = MathHelper.Clamp(1f - distance, 0f, 1f);
                    alpha *= alpha;

                    // Premultiplied: SpriteBatch and Additive both expect it that way.
                    byte value = (byte)MathHelper.Clamp(alpha * 255f, 0f, 255f);
                    pixels[y * TextureSize + x] = new Color(value, value, value, value);
                }
            }

            gradient.SetData(pixels);
        }

        public Texture2D Texture => gradient;

        /// <summary>
        /// Smooth per-source flicker around 1.0. The phase comes from the source position, so two
        /// torches side by side do not pulse in lockstep, and it is stable frame to frame because
        /// it is a function of position and time rather than a random draw.
        /// </summary>
        public static float GetFlicker(Vector2 worldPixels, float timeSeconds)
        {
            float amplitude = LightingV7Config.FlickerAmplitude;
            if (amplitude <= 0f)
                return 1f;

            float phase = worldPixels.X * 0.013f + worldPixels.Y * 0.017f;
            float hz = LightingV7Config.FlickerHz;

            // Three octaves so it wavers rather than pulsing on one note.
            float wave = MathF.Sin(timeSeconds * hz + phase) * 0.5f
                       + MathF.Sin(timeSeconds * hz * 1.7f + phase * 1.37f) * 0.3f
                       + MathF.Sin(timeSeconds * hz * 2.9f + phase * 0.73f) * 0.2f;

            return 1f + wave * amplitude;
        }

        /// <summary>Draws one halo. Call inside an Additive, world-space SpriteBatch block.</summary>
        public void Draw(SpriteBatch spriteBatch, Vector2 worldPixels, Vector3 color, float radiusTiles, float alpha, int tileSize)
        {
            float radiusPixels = radiusTiles * tileSize;
            if (radiusPixels <= 0f || alpha <= 0f)
                return;

            var destination = new Rectangle(
                (int)MathF.Round(worldPixels.X - radiusPixels),
                (int)MathF.Round(worldPixels.Y - radiusPixels),
                (int)MathF.Round(radiusPixels * 2f),
                (int)MathF.Round(radiusPixels * 2f));

            var tint = new Color(
                MathHelper.Clamp(color.X, 0f, 1f) * alpha,
                MathHelper.Clamp(color.Y, 0f, 1f) * alpha,
                MathHelper.Clamp(color.Z, 0f, 1f) * alpha,
                alpha);

            spriteBatch.Draw(gradient, destination, tint);
        }

        public void Dispose()
        {
            gradient?.Dispose();
        }
    }
}
