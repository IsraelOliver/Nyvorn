using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Combines sky ambient field (background) + foreground lighting and applies sky color tint.
    /// Produces a final light color to overlay on the scene.
    /// </summary>
    public sealed class V5SkyAmbientCompositor
    {
        private readonly V5SkyAmbientConfig config;
        private readonly V5SkyAmbientField field;
        private readonly V5ForegroundLighting foreground;

        public V5SkyAmbientCompositor(V5SkyAmbientConfig config, V5SkyAmbientField field, V5ForegroundLighting foreground)
        {
            this.config = config;
            this.field = field;
            this.foreground = foreground;
        }

        /// <summary>
        /// Sample combined intensity at a tile: background field + foreground contribution.
        /// </summary>
        public float SampleCombinedIntensity(int tileX, int tileY, bool isForegroundTile)
        {
            if (isForegroundTile)
            {
                return foreground.GetForegroundLightIntensity(tileX, tileY);
            }
            else
            {
                // Background: use field directly
                return field.SampleField(tileX, tileY);
            }
        }

        /// <summary>
        /// Get the final light color for a tile, tinted by sky color.
        /// </summary>
        public Color GetSkyAmbientColor(int tileX, int tileY, Color skyAmbientColor, bool isForegroundTile)
        {
            float intensity = SampleCombinedIntensity(tileX, tileY, isForegroundTile);

            // Convert sky color to 0-1 range
            float skyR = skyAmbientColor.R / 255f;
            float skyG = skyAmbientColor.G / 255f;
            float skyB = skyAmbientColor.B / 255f;

            // Apply intensity with sky color strength
            float strengthFactor = config.SkyColorStrength;
            float r = skyR * intensity * strengthFactor + (1f - strengthFactor) * intensity;
            float g = skyG * intensity * strengthFactor + (1f - strengthFactor) * intensity;
            float b = skyB * intensity * strengthFactor + (1f - strengthFactor) * intensity;

            // Clamp and convert back
            byte rByte = (byte)(System.Math.Clamp(r, 0f, 1f) * 255f);
            byte gByte = (byte)(System.Math.Clamp(g, 0f, 1f) * 255f);
            byte bByte = (byte)(System.Math.Clamp(b, 0f, 1f) * 255f);
            return new Color(rByte, gByte, bByte, (byte)255);
        }
    }
}
