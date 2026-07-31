using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Immutable directional sun state for Phase 3 directional lighting.
    /// Readonly struct - returned by value, no allocations.
    /// </summary>
    public readonly struct LightingV3SunState
    {
        // Direction from world toward sun (normalized)
        public readonly Vector2 DirectionToSun;

        // Direction light travels (-DirectionToSun, normalized)
        public readonly Vector2 LightTravelDirection;

        // Sun color in linear color space (not gamma-corrected)
        public readonly Vector3 LinearColor;

        // Sun intensity (0-1 scale, modulates color)
        public readonly float Intensity;

        // Sun elevation in degrees (-90 to +90, where 0 = horizon, 90 = zenith)
        public readonly float Elevation;

        // True if sun is above horizon (Elevation > 0)
        public readonly bool IsAboveHorizon;

        // Tolerance for vector normalization validation
        private const float NormalizationTolerance = 0.01f;

        /// <summary>
        /// Create sun state with automatic validation.
        /// </summary>
        public LightingV3SunState(Vector2 directionToSun, Vector3 linearColor, float intensity, float elevation)
        {
            // Validate and normalize direction vector
            float lengthSq = directionToSun.LengthSquared();

            if (float.IsNaN(lengthSq) || float.IsInfinity(lengthSq) || lengthSq < 0.001f)
            {
                // Invalid vector - use safe default (sun overhead)
                DirectionToSun = Vector2.UnitY * -1f;  // Points upward from ground
            }
            else
            {
                DirectionToSun = Vector2.Normalize(directionToSun);
            }

            // Light travels opposite to direction toward sun
            LightTravelDirection = -DirectionToSun;

            // Clamp intensity to valid range
            Intensity = MathHelper.Clamp(intensity, 0f, 1f);

            // Store color (validation done at call site)
            LinearColor = linearColor;

            // Clamp elevation to valid range
            Elevation = MathHelper.Clamp(elevation, -90f, 90f);

            // Sun is above horizon if elevation > 0
            IsAboveHorizon = elevation > 0f;
        }

        /// <summary>
        /// Validate that DirectionToSun is properly normalized.
        /// </summary>
        public bool IsNormalized()
        {
            float lengthSq = DirectionToSun.LengthSquared();
            return !float.IsNaN(lengthSq) && !float.IsInfinity(lengthSq) &&
                   System.Math.Abs(lengthSq - 1f) <= NormalizationTolerance;
        }

        /// <summary>
        /// Validate that LightTravelDirection is exactly opposite of DirectionToSun.
        /// </summary>
        public bool IsLightTravelDirectionValid()
        {
            Vector2 expected = -DirectionToSun;
            float dx = LightTravelDirection.X - expected.X;
            float dy = LightTravelDirection.Y - expected.Y;
            float diffSq = dx * dx + dy * dy;
            return diffSq < 0.0001f;  // Very tight tolerance
        }

        /// <summary>
        /// Validate entire state for correctness.
        /// </summary>
        public bool IsValid()
        {
            return IsNormalized() &&
                   IsLightTravelDirectionValid() &&
                   !float.IsNaN(Intensity) &&
                   !float.IsInfinity(Intensity) &&
                   !float.IsNaN(Elevation) &&
                   !float.IsInfinity(Elevation) &&
                   !float.IsNaN(LinearColor.X) &&
                   !float.IsNaN(LinearColor.Y) &&
                   !float.IsNaN(LinearColor.Z);
        }

        /// <summary>
        /// Get diagnostic string for debug output.
        /// </summary>
        public override string ToString()
        {
            return $"SunState[Direction=({DirectionToSun.X:F3},{DirectionToSun.Y:F3}) " +
                   $"Elev={Elevation:F1}° Intensity={Intensity:F3} Above={IsAboveHorizon}]";
        }

        /// <summary>
        /// Create sun state with zero intensity (night).
        /// </summary>
        public static LightingV3SunState Night()
        {
            return new LightingV3SunState(Vector2.UnitY * -1f, Vector3.Zero, 0f, -45f);
        }

        /// <summary>
        /// Create sun state for high noon (straight down).
        /// </summary>
        public static LightingV3SunState Noon()
        {
            return new LightingV3SunState(Vector2.UnitY, new Vector3(1f, 0.95f, 0.9f), 1f, 90f);
        }

        /// <summary>
        /// Create sun state for morning (upper right).
        /// </summary>
        public static LightingV3SunState Morning()
        {
            Vector2 dir = Vector2.Normalize(new Vector2(1f, -1f));
            return new LightingV3SunState(dir, new Vector3(1f, 0.8f, 0.6f), 0.8f, 45f);
        }

        /// <summary>
        /// Create sun state for evening (upper left).
        /// </summary>
        public static LightingV3SunState Evening()
        {
            Vector2 dir = Vector2.Normalize(new Vector2(-1f, -1f));
            return new LightingV3SunState(dir, new Vector3(1f, 0.6f, 0.3f), 0.7f, 30f);
        }
    }
}
