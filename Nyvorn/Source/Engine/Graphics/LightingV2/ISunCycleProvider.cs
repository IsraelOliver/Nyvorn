using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Minimal interface for Lighting V2 to query sun position, color, and time.
    /// Shields V2 from direct WorldDayNightCycle and WorldEnvironmentSystem coupling.
    /// Provides directional data needed for physical light calculations (DirectSunlight phase).
    /// </summary>
    public interface ISunCycleProvider
    {
        /// <summary>Current time of day, normalized [0, 1) where 0=00:00, 0.5=12:00.</summary>
        float TimeOfDay01 { get; }

        /// <summary>Sky color at current time (day blue to night dark blue).</summary>
        Color SkyColor { get; }

        /// <summary>Sun color at current time (white at noon, orange at sunrise/sunset).</summary>
        Color SunColor { get; }

        /// <summary>Ambient light color at current time.</summary>
        Color AmbientLight { get; }

        /// <summary>How dark the night currently is, [0, 1] smooth transition.</summary>
        float NightStrength { get; }

        /// <summary>Sun direction as normalized 2D vector (points from world towards sun).</summary>
        Vector2 SunDirection { get; }

        /// <summary>Sun elevation angle normalized [0, 1] where 0=horizon, 0.5=zenith, 1=opposite horizon.</summary>
        float SunElevation01 { get; }

        /// <summary>Sun light intensity multiplier at current time (0 at night, 1 at noon).</summary>
        float SunIntensity { get; }

        /// <summary>Whether the sun is above the horizon (relevant for sky lighting).</summary>
        bool IsSunAboveHorizon { get; }
    }
}
