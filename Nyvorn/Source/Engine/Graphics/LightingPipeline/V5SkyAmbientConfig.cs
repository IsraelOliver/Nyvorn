namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Centralized configuration for V5 Sky Ambient Prototype 0.1 - experimental mode.
    /// All magic numbers for the prototype are gathered here for easy adjustment.
    /// NOT an architectural decision - purely for testing and iteration.
    /// </summary>
    public sealed class V5SkyAmbientConfig
    {
        // Portal and field parameters
        public int MaxRangeTiles { get; set; } = 20;
        public float ForwardFalloff { get; set; } = 0.08f;
        public float LateralFalloff { get; set; } = 0.15f;
        public float DirectionChangePenalty { get; set; } = 0.25f;

        // Initial intensity when light enters from a sky portal
        public float InitialIntensity { get; set; } = 0.9f;

        // Soft ceiling: light accumulation saturates smoothly, not hard clamp
        public float SaturationSoftness { get; set; } = 1.5f;  // higher = softer curve
        public float AccumulationCeiling { get; set; } = 1.2f; // max allowed accumulated value

        // Foreground layer depth-based weights
        // Layer 1 (first solid) to Layer 5+ (deep shadow)
        public float ForegroundLayer1Weight { get; set; } = 1.00f;
        public float ForegroundLayer2Weight { get; set; } = 0.65f;
        public float ForegroundLayer3Weight { get; set; } = 0.35f;
        public float ForegroundLayer4Weight { get; set; } = 0.15f;
        public float ForegroundLayer5PlusWeight { get; set; } = 0.00f;

        // Sky color strength: how much the ambient light color tints the result
        public float SkyColorStrength { get; set; } = 0.85f;

        // Camera margin for field computation
        public int CameraMarginTiles { get; set; } = 5;

        // Debug: measure computation time separately
        public bool MeasurePerformance { get; set; } = false;
    }
}
