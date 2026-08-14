using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Configuration for V6 Terraria-inspired lighting system.
    ///
    /// Central point for tuning behavior without changing core propagation logic.
    /// </summary>
    public static class V6LightingConfig
    {
        // Active buffer region (camera visible + margin)
        public static int LightMarginTiles { get; set; } = 8;

        // Medium transmission rates (0..1, applied per-tile during propagation)
        // Higher = light travels farther through that medium
        public static float BackgroundTransmission { get; set; } = 0.85f;  // Background: good light diffusion
        public static float ForegroundTransmission { get; set; } = 0.40f;  // Foreground: strong attenuation
        public static float WaterTransmission { get; set; } = 0.70f;      // Water: similar to background for now

        // Dark ambient color (used when no light source reaches a cell)
        public static Color DarkAmbientColor { get; set; } = new Color(20, 20, 30);  // Dark blue-ish

        // Propagation iterations per frame
        // Higher = smoother gradients, higher cost
        // Sweeps: each pass goes in one direction (left->right, right->left, etc.)
        public static int PropagationPasses { get; set; } = 4;

        // Debug rendering
        public static bool ShowSourcesDebug { get; set; } = false;  // Shift+L cycles through debug modes
        public static bool ShowLightDebug { get; set; } = false;

        // Background lighting (ETAPA 2)
        public static float BackgroundSeedIntensity { get; set; } = 0.90f;     // Initial intensity when adjacent to SkyOpen
        public static float BackgroundInitialStrength { get; set; } = 1.00f;   // Multiplier on seed intensity
        public static float BackgroundFalloff { get; set; } = 0.10f;           // Intensity loss per tile
        public static float BackgroundSoftCap { get; set; } = 1.00f;           // Clamp for normalization
        public static int BackgroundMaxDistance { get; set; } = 10;            // Max tiles to propagate (safety limit)

        // Foreground indirect from background (ETAPA 2.5)
        public static float ForegroundIndirectFromBackgroundStrength { get; set; } = 0.20f;  // Very weak coupling

        // Artificial lighting (ETAPA 7 — Torch Point Light)
        public static Color TorchLightColor { get; set; } = new Color(1.0f, 0.60f, 0.20f);  // Outer/base color: warm orange
        public static float TorchLightIntensity { get; set; } = 1.0f;
        public static int TorchLightRadiusTiles { get; set; } = 9;

        // P2-B1: Torch light falloff curve exponent
        public static float TorchLightFalloffExponent { get; set; } = 1.4f;  // 1.4=balanced (default), 1.0/1.6=debug A/B

        // P2-B2: Torch light color shaping (temperature gradient)
        public static Color TorchLightCoreColor { get; set; } = new Color(1.0f, 0.82f, 0.45f);  // Core color: warm yellow
        public static float TorchLightColorCoreExponent { get; set; } = 2.0f;  // Exponent for core blend concentration
        public static bool TorchLightColorShapingEnabled { get; set; } = true;  // Default: shaped color (approved visual)

        // P2-B3: Torch light flicker (subtle brightness oscillation)
        public static bool TorchLightFlickerEnabled { get; set; } = false;  // Default: off (baseline stable)
        public static float TorchLightFlickerAmount { get; set; } = 0.08f;  // Oscillation intensity (±8% by default)

        // Point Light Attenuation (Starbound-style energy loss)
        public static float PointLightAirAttenuationPerTile { get; set; } = 0.08f;      // Linear per distance
        public static float PointLightForegroundObstacleAttenuation { get; set; } = 0.70f;  // Per Foreground cell traversed
        public static float PointLightDoorObstacleAttenuation { get; set; } = 0.95f;   // Closed door: nearly blocks
    }
}
