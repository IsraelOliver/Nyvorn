using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public enum WorldEventChannel
    {
        Astronomical,
        Weather,
        Ecological,
        Tissue,
        Smiley
    }

    public enum WorldEventKind
    {
        None,
        Rain,
        SolarEclipse,
        CorrectiveNight
    }

    public enum WorldEventStage
    {
        Inactive,
        Omen,
        Transition,
        Active,
        Dissipating,
        Residue
    }

    public sealed class WorldEventRuntimeSaveData
    {
        public WorldEventKind Kind { get; set; } = WorldEventKind.None;
        public WorldEventChannel Channel { get; set; }
        public WorldEventStage Stage { get; set; } = WorldEventStage.Inactive;
        public float StageElapsedSeconds { get; set; }
        public float StageDurationSeconds { get; set; }
        public int StartedCycleIndex { get; set; }
        public bool IsForced { get; set; }
        // Rises while Active (weather-channel events only); a future escalation (e.g. Rain -> Storm,
        // Phase 2) crosses a threshold and promotes Kind in place via PromoteEventKind, without
        // resetting Stage/StageElapsedSeconds the way SetEventStage does. Unused by Rain today -
        // added now so Phase 2 doesn't need a save-schema change to introduce it.
        public float Instability { get; set; }
    }

    public sealed class WorldEnvironmentSaveData
    {
        public int CycleIndex { get; set; }
        public WorldEventRuntimeSaveData Rain { get; set; } = new();
        public WorldEventRuntimeSaveData Eclipse { get; set; } = new();
        public int RainCooldownCycles { get; set; }
        public int EclipseCooldownCycles { get; set; }
        public float Humidity { get; set; } = 0.35f;
        public float CloudCover { get; set; }
        public float Wind { get; set; }
        public float Wetness { get; set; }
    }

    public readonly record struct WeatherState(
        float Humidity,
        float CloudCover,
        float Wind,
        float Wetness,
        float RainIntensity);

    public readonly record struct WorldEventState(
        WorldEventRuntimeSaveData Rain,
        WorldEventRuntimeSaveData Eclipse,
        int RainCooldownCycles,
        int EclipseCooldownCycles);

    public readonly record struct TissueCycleState(
        WorldEventStage Stage,
        float CorrectionStrength,
        float PulseBoost,
        float ResidueStrength);

    // Per-moon runtime values for a given frame - MoonDefinition (Simulation/MoonDefinition.cs)
    // holds the config each of these plays against (radius, color, parallax, bloom, ...).
    public readonly record struct MoonState(
        float Progress,   // arc position within tonight's pass, 0..1 (same semantics as the old single MoonProgress)
        float Opacity,    // tied to NightStrength - 0 during the day, same as before
        float Phase01);   // this moon's own waxing/waning cycle position, 0/1 = new, 0.5 = full

    public readonly record struct SkyState(
        Color TopColor,
        Color HorizonColor,
        Color AmbientLight,
        Color FogColor,
        Color SunColor,
        Color NightOverlayTint,
        float SunProgress,
        float SunOpacity,
        MoonState NearMoon,
        MoonState FarMoon,
        // 0..1 hook: how full AND aligned the two moons currently are (product of a fullness term
        // and a position-alignment term - see WorldEnvironmentSystem.ComputeMoonConjunction01).
        // Exposed as data only for now; no reactions (tissue tint, ambient cooling, discrete
        // begin/peak/end events) are wired to it yet - that's the next pass, plugging in here.
        float MoonConjunction01,
        float StarOpacity,
        float CloudOpacity,
        float FogOpacity,
        float RainIntensity,
        float EclipseIntensity,
        float TissueCorrectionStrength,
        float Wetness,
        float Wind,
        float VisualTimeSeconds);
}
