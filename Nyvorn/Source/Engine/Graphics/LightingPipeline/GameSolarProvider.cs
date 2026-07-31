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
            // Get directional data
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

            // Get intensity (0 at night, 1 at noon)
            float intensity = _sunCycleProvider.SunIntensity;

            // Convert normalized elevation [0, 1] to degrees [-90, 90]
            // 0 = horizon (-0°), 0.5 = zenith (90°), 1 = opposite horizon (180° wraps to -90°)
            float elevation01 = _sunCycleProvider.SunElevation01;
            float elevation = (elevation01 - 0.5f) * 180f;  // Maps [0, 1] to [-90, 90]

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
