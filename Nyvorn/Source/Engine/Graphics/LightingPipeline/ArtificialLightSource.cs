using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    public struct ArtificialLightSource
    {
        public Vector2 PositionPixels;       // World pixel coordinates
        public Color ColorRGB;                // Outer/base light color in [0..1] range
        public Color CoreColorRGB;            // P2-B2: Core color for high-energy regions
        public float ColorCoreExponent;       // P2-B2: Exponent for core color blending
        public bool UseColorShaping;          // P2-B2: Enable color temperature shaping
        public float Intensity;               // Multiplier (typically 0..2)
        public int RadiusTiles;               // Reach in tiles
        public float OutputMultiplier;        // P2-B3: Brightness multiplier for flicker/pulsing (default 1.0)
    }
}
