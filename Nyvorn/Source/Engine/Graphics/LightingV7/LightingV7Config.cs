using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingV7
{
    /// <summary>
    /// Tuning values for the V7 lighting system (per-tile light map, Terraria-style sweeps).
    /// All values are starting points and are meant to be adjusted by looking at the game.
    /// Decay values are per 8 px tile (square root of Terraria's per-16 px values).
    /// </summary>
    public static class LightingV7Config
    {
        // Active region = visible tiles + MarginTiles on each side. Must cover the max light reach.
        public static int MarginTiles = 48;

        // Multiplicative decay per tile, applied by the cell that RECEIVES the light.
        public static float AirDecay = 0.954f;
        public static float SolidDecay = 0.748f;
        public static float WaterDecay = 0.938f;

        // Sky channel uses its own air decay so the reach of daylight entering caves can be tuned alone.
        public static float SkyAirDecay = 0.94f;

        // Sky cap: 1 down to shallowEnd, then linear to 0 over SkyFadeTiles rows.
        public static int SkyFadeTiles = 12;

        // Sky seed intensity for air cells without a background wall.
        public static float SurfaceSkySeed = 1.0f;   // Space + Surface layers
        public static float ShallowSkySeed = 0.5f;   // ShallowUnderground layer (fissures, open pockets)

        public static int PropagationRounds = 2;
        public static float LightCutoff = 0.01f;

        // Light values are stored in the texture as value / OverbrightScale and multiplied back in the shader.
        public static float OverbrightScale = 2.0f;

        // Point sources
        public static Vector3 TorchColor = new(1.00f, 0.62f, 0.30f);
        public static float TorchIntensity = 1.2f;
        public static float FlickerAmplitude = 0.06f;   // Phase 2
        public static float FlickerHz = 8f;             // Phase 2

        // Sky colour (global uniform, does not affect propagation)
        public static Vector3 SkyColorDay = new(1.00f, 1.00f, 1.00f);
        public static Vector3 SkyColorDusk = new(1.00f, 0.72f, 0.55f);   // Phase 3
        public static Vector3 SkyColorNight = new(0.14f, 0.17f, 0.28f);  // Phase 3
        public static float RainSkyMultiplier = 0.75f;                   // Phase 3

        // Minimum light floor per layer (0 = true black)
        public static float AmbientSurface = 0.00f;
        public static float AmbientShallow = 0.02f;
        public static float AmbientCavern = 0.00f;
        public static float AmbientDeep = 0.00f;

        // --- Direct light with shadows (Phase 1.5) ---
        // When off, a source seeds the flood at full intensity (Phase 1 behaviour).
        // When on, the source is split: a hard-shadowed direct term plus a weaker flood seed.
        public static bool DirectShadowsEnabled = true;

        // Reach of the direct term, in tiles. Beyond it only the flood contributes.
        public static int DirectRadiusTiles = 13;

        // Fraction of the source that feeds the flood (the light that "bounces" into shadow).
        public static float BounceStrength = 0.35f;

        // Contact darkening in corners and under platforms. 0 = off.
        public static float AOStrength = 0.35f;

        // Halos (Phase 2)
        public static int GlowRadiusTiles = 3;
        public static float GlowAlpha = 0.35f;

        // --- Emissive sources (Phase 2) ---
        // Cave mushroom: the cyan glow from reference image 1.
        public static Vector3 MushroomEmission = new(0.30f, 0.95f, 0.90f);
        public static float MushroomIntensity = 0.55f;

        // Tissue: warm orange afterglow, scaled by the cell's own presence.
        public static Vector3 TissueEmission = new(1.00f, 0.56f, 0.23f);
        public static float TissueIntensity = 0.60f;
        public static bool TissueEmissiveEnabled = true;

        // Emissive tiles and decorations are soft ambient glows, not lamps: giving each one a hard
        // shadow reads wrong and costs a full ray pass per source. They seed the flood at full
        // strength instead. Torches keep their direct term.
        public static bool EmissivesCastDirectShadows = false;

        // Halo sprite size relative to GlowRadiusTiles for emissives (they glow smaller than torches).
        public static float EmissiveGlowScale = 0.6f;

        // --- Sky colour by hour (Phase 3) ---
        // V7 keeps its own night curve instead of reusing SkyState.AmbientLight, which is far
        // brighter at night than a multiplicative light map wants.
        public static Vector3 SkyColorDawn = new(1.00f, 0.78f, 0.62f);

        // Total darkness multiplier at full eclipse.
        public static float EclipseSkyMultiplier = 0.25f;

        // How much a full two-moon conjunction lifts the night sky (0 = no effect).
        public static float MoonConjunctionBoost = 0.60f;

        // --- Directional sun (Phase 3) ---
        public static bool SunEnabled = true;

        // How much of the sky flood survives when the sun is on. The rest of the daylight comes
        // from the direct sun term, which is what creates the shafts.
        public static float SkyBounce = 0.60f;
        public static float SunIntensity = 1.00f;

        // Horizontal tiles the beam travels per row at dawn/dusk. 0 would be straight down.
        public static float SunMaxSlope = 2.50f;

        public static Vector3 SunColorNoon = new(1.00f, 0.98f, 0.92f);
        public static Vector3 SunColorHorizon = new(1.00f, 0.58f, 0.32f);

        // --- Water (Phase 3) ---
        // Per channel: red dies first, blue carries furthest, so depth goes blue.
        public static Vector3 WaterDecayRGB = new(0.90f, 0.93f, 0.96f);

        // --- Loose sand (Phase 3) ---
        // A tile's sand fill is estimated from a 4-pixel probe, so it lands on 0, .25, .5, .75, 1.
        // Above this fraction the tile also blocks the direct ray and the sun.
        public static float SandOcclusionThreshold = 0.5f;

        // --- Debug ---
        // Hold the time key to run the day/night cycle this much faster.
        public static float DebugTimeScale = 60f;

        // 0 = off
        public static int PosterizeLevels = 0;
    }
}
