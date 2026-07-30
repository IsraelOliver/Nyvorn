using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.World.Simulation;
using System;

namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Adapter that bridges Lighting V2 to the existing day/night cycle and environment system.
    /// Combines WorldDayNightCycle and WorldEnvironmentSystem into a single provider interface.
    /// Provides both basic sky data and directional sun data for physical lighting calculations.
    /// </summary>
    public sealed class SunCycleAdapter : ISunCycleProvider
    {
        private readonly WorldDayNightCycle dayNightCycle;
        private readonly WorldEnvironmentSystem environmentSystem;

        public SunCycleAdapter(
            WorldDayNightCycle dayNightCycle,
            WorldEnvironmentSystem environmentSystem)
        {
            this.dayNightCycle = dayNightCycle ?? throw new System.ArgumentNullException(nameof(dayNightCycle));
            this.environmentSystem = environmentSystem ?? throw new System.ArgumentNullException(nameof(environmentSystem));
        }

        public float TimeOfDay01 => dayNightCycle.TimeOfDay01;

        public Color SkyColor => dayNightCycle.SkyColor;

        public Color SunColor => environmentSystem.SkyState.SunColor;

        public Color AmbientLight => environmentSystem.SkyState.AmbientLight;

        public float NightStrength => dayNightCycle.NightStrength;

        // Directional sun data derived from SkyState
        public Vector2 SunDirection
        {
            get
            {
                float progress = environmentSystem.SkyState.SunProgress;
                // Sun arcs from 10% (left) to 90% (right) of screen horizontally
                // Arc formula: sin(progress * PI) gives elevation, uses symmetric arc
                float t = MathHelper.Clamp(progress, 0f, 1f);
                // Direction: normalized vector from world center towards sun position
                // Horizontal: left (-1) to right (+1), vertical: down (-1) to up (+1)
                float dirX = MathHelper.Lerp(-0.8f, 0.8f, t);
                float dirY = -MathF.Sin(t * MathF.PI); // Positive sin = sun goes up, then down
                Vector2 direction = new Vector2(dirX, dirY);
                return Vector2.Normalize(direction);
            }
        }

        public float SunElevation01
        {
            get
            {
                float progress = environmentSystem.SkyState.SunProgress;
                float t = MathHelper.Clamp(progress, 0f, 1f);
                // sin(progress * PI) ranges [0, 1, 0] - peaks at 0.5
                return MathF.Sin(t * MathF.PI);
            }
        }

        public float SunIntensity
        {
            get
            {
                // Sun intensity is inverse of night strength: bright at day, dark at night
                return MathHelper.Clamp(1f - NightStrength, 0f, 1f);
            }
        }

        public bool IsSunAboveHorizon
        {
            get
            {
                // Sun is above horizon when SunOpacity is significant and not in deep night
                float sunOpacity = environmentSystem.SkyState.SunOpacity;
                return sunOpacity > 0.05f && NightStrength < 0.95f;
            }
        }
    }
}
