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

        // Halos (Phase 2)
        public static int GlowRadiusTiles = 3;
        public static float GlowAlpha = 0.35f;

        // 0 = off
        public static int PosterizeLevels = 0;
    }
}
