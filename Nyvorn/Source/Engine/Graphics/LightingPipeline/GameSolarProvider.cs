using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingV2;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Real solar provider that bridges V3 lighting to game's day/night cycle.
    /// Converts ISunCycleProvider (V2) data to LightingV3SunState (V3).
    /// </summary>
    public class GameSolarProvider : ISolarProvider
    {
        private readonly ISunCycleProvider _sunCycleProvider;

        public GameSolarProvider(ISunCycleProvider sunCycleProvider)
        {
            _sunCycleProvider = sunCycleProvider ?? throw new System.ArgumentNullException(nameof(sunCycleProvider));
        }

        /// <summary>
        /// Get current sun state from game's day/night cycle.
        /// </summary>
        public LightingV3SunState GetSunState()
        {
            // Get directional data (authority source)
            Vector2 directionToSun = _sunCycleProvider.SunDirection;

            // Convert color from gamma sRGB to linear
            Color sunColorGamma = _sunCycleProvider.SunColor;
            float r = sunColorGamma.R / 255f;
            float g = sunColorGamma.G / 255f;
            float b = sunColorGamma.B / 255f;

            // Approximate sRGB to linear: linear = sRGB ^ 2.2
            Vector3 linearColor = new Vector3(
                (float)System.Math.Pow(r, 2.2f),
                (float)System.Math.Pow(g, 2.2f),
                (float)System.Math.Pow(b, 2.2f)
            );

            // Derive elevation from direction (single source of truth)
            // DirectionToSun points towards sun; negative Y means up (Y-down coordinate system)
            // Elevation = asin(-Y) = asin of inverse Y component
            float elevationRadians = (float)System.Math.Asin(-directionToSun.Y);
            float elevation = elevationRadians * (180f / System.MathF.PI);  // Convert radians to degrees

            // Get intensity from cycle, but zero it if sun is below horizon
            bool isAboveHorizon = _sunCycleProvider.IsSunAboveHorizon;
            float intensity = isAboveHorizon ? _sunCycleProvider.SunIntensity : 0f;

            // Create sun state (validation happens inside constructor)
            return new LightingV3SunState(directionToSun, linearColor, intensity, elevation);
        }

        /// <summary>
        /// Get current world time for diagnostics.
        /// Time of day [0, 1) where 0=00:00, 0.5=12:00.
        /// </summary>
        public float GetTimeOfDay01()
        {
            return _sunCycleProvider.TimeOfDay01;
        }

        /// <summary>
        /// Get night strength for diagnostics.
        /// [0, 1] where 0=day, 1=night.
        /// </summary>
        public float GetNightStrength()
        {
            return _sunCycleProvider.NightStrength;
        }
    }
}
