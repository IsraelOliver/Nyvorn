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

    public readonly record struct SkyState(
        Color TopColor,
        Color HorizonColor,
        Color AmbientLight,
        Color FogColor,
        Color SunColor,
        Color MoonColor,
        Color NightOverlayTint,
        float SunProgress,
        float SunOpacity,
        float MoonProgress,
        float MoonOpacity,
        float StarOpacity,
        float CloudOpacity,
        float FogOpacity,
        float RainIntensity,
        float EclipseIntensity,
        float TissueCorrectionStrength,
        float Wetness,
        float VisualTimeSeconds);
}
