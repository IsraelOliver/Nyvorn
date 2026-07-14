using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    // Per-moon config - everything about how a moon looks and moves lives here, not scattered as
    // magic numbers in the renderer. NearMoon/FarMoon are placeholder tuning (and placeholder
    // disc sprites, drawn procedurally - swap in real art later without touching the math).
    public readonly record struct MoonDefinition(
        float DiscRadius,             // base (unzoomed) pixel radius
        Color Color,                  // base tint, also used for the bloom pass
        float ParallaxFactor,         // fraction of camera pan applied to this moon's screen position
        float PhasePeriodDays,        // game-days per full new-to-full-to-new phase cycle
        float ArcSpeedMultiplier,     // relative to the reference night-arc speed (1.0 = same as the sun's night window)
        float ResidualGlow,           // dark-side minimum brightness, 0..1 (never pure black)
        float BloomRadiusMultiplier,  // gaussian sigma for the additive bloom, in DiscRadius units
        float BloomIntensity)         // additive bloom strength at the moon's center
    {
        public static MoonDefinition NearMoon => new(
            DiscRadius: 20f,
            Color: new Color(228, 221, 203),
            ParallaxFactor: 0.15f,
            PhasePeriodDays: 4f,
            ArcSpeedMultiplier: 1f,
            ResidualGlow: 0.08f,
            BloomRadiusMultiplier: 6f,
            BloomIntensity: 0.35f);

        public static MoonDefinition FarMoon => new(
            DiscRadius: 8f,
            Color: new Color(150, 168, 198),
            ParallaxFactor: 0.03f,
            PhasePeriodDays: 9f,
            ArcSpeedMultiplier: 0.92f,
            ResidualGlow: 0.22f,
            BloomRadiusMultiplier: 5f,
            BloomIntensity: 0.20f);
    }
}
