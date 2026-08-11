using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    public struct ArtificialLightSource
    {
        public Vector2 PositionPixels;   // World pixel coordinates
        public Color ColorRGB;            // Light color in [0..1] range
        public float Intensity;           // Multiplier (typically 0..2)
        public int RadiusTiles;           // Reach in tiles
    }
}
