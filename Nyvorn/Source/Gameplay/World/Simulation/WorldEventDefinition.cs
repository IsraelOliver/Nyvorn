namespace Nyvorn.Source.Gameplay.World.Simulation
{
    // Per-kind timing/curve data for weather-channel events (Rain today; Storm/Hail/Snow/Sandstorm
    // later become new entries here, not new code paths in WorldEnvironmentSystem).
    public readonly record struct WorldEventDefinition(
        WorldEventKind Kind,
        WorldEventChannel Channel,
        float OmenSeconds,
        float TransitionSeconds,
        float ActiveSecondsMin,
        float ActiveSecondsMax,
        float DissipatingSeconds,
        float ResidueSeconds,
        float ForcedDurationScale,
        float WindOmen,
        float WindTransition,
        float WindActive,
        float WindDissipating,
        float WindResidue,
        float CloudOmen,
        float CloudTransition,
        float CloudActive,
        float CloudDissipating,
        float CloudResidue,
        float IntensityTransitionPeak,
        float IntensityActive,
        float IntensityDissipatingStart,
        float IntensityDissipatingEnd,
        float IntensityResidueStart,
        float WetnessGainRate,
        float InstabilityGainRate)
    {
        public static WorldEventDefinition Rain => new(
            Kind: WorldEventKind.Rain,
            Channel: WorldEventChannel.Weather,
            OmenSeconds: 25f,
            TransitionSeconds: 30f,
            // Widened from a fixed 160s so back-to-back rain events don't all feel identical -
            // rolled once per event instance in GetWeatherStageDuration (~5-23% of a 1800s day).
            ActiveSecondsMin: 90f,
            ActiveSecondsMax: 420f,
            DissipatingSeconds: 40f,
            ResidueSeconds: 120f,
            ForcedDurationScale: 0.50f,
            WindOmen: 0.30f,
            WindTransition: 0.55f,
            WindActive: 0.75f,
            WindDissipating: 0.35f,
            WindResidue: 0.05f,
            CloudOmen: 0.35f,
            CloudTransition: 0.65f,
            CloudActive: 0.95f,
            CloudDissipating: 0.55f,
            CloudResidue: 0.20f,
            IntensityTransitionPeak: 0.42f,
            IntensityActive: 0.85f,
            IntensityDissipatingStart: 0.65f,
            IntensityDissipatingEnd: 0.18f,
            IntensityResidueStart: 0.16f,
            WetnessGainRate: 0.035f,
            InstabilityGainRate: 0.05f);

        public static WorldEventDefinition Get(WorldEventKind kind)
        {
            return kind switch
            {
                WorldEventKind.Rain => Rain,
                _ => Rain
            };
        }
    }
}
