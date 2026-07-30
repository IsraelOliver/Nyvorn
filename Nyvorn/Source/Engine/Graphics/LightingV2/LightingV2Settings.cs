namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Tunable parameters for Lighting V2.
    /// Exposed for tweaking without recompilation.
    /// </summary>
    public sealed class LightingV2Settings
    {
        /// <summary>Margin in tiles around the visible region to compute lighting.</summary>
        public int PropagationMarginTiles { get; set; } = 12;

        /// <summary>Enable ambient sky light calculations.</summary>
        public bool EnableAmbientSkyLight { get; set; } = true;

        /// <summary>Enable direct sunlight calculations.</summary>
        public bool EnableDirectSunlight { get; set; } = true;

        /// <summary>Enable local light (torch) calculations.</summary>
        public bool EnableLocalLights { get; set; } = true;

        /// <summary>Enable surface hit lighting (lighting on faces struck by sun).</summary>
        public bool EnableSurfaceHitLight { get; set; } = false;

        /// <summary>Enable visible sun shafts effect.</summary>
        public bool EnableSunShafts { get; set; } = false;

        /// <summary>Enable bounce/reflection lighting (experimental).</summary>
        public bool EnableBounceLight { get; set; } = false;

        /// <summary>Dirty flag: recalculate only when camera moves this many tiles.</summary>
        public float CameraMovementThresholdTiles { get; set; } = 0.5f;

        /// <summary>Dirty flag: recalculate when sun rotates beyond this threshold (radians).</summary>
        public float SunRotationThresholdRadians { get; set; } = 0.05f;

        /// <summary>Base color temperature for point lights (default torch color).</summary>
        public float TorchColorTemperatureKelvin { get; set; } = 2000f;
    }
}
