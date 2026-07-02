using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public sealed class WorldDayNightCycle
    {
        public const float DefaultDayLengthSeconds = 1800f;
        public const float DefaultStartTimeOfDay01 = 0.25f;
        public const float DayCommandTimeOfDay01 = 6f / 24f;
        public const float NightCommandTimeOfDay01 = 19.5f / 24f;
        public const float DeepNightStartTimeOfDay01 = 0f / 24f;
        public const float PreDawnStartTimeOfDay01 = 4f / 24f;
        public const float SunriseStartTimeOfDay01 = 5f / 24f;
        public const float NoonStartTimeOfDay01 = 12f / 24f;
        public const float AfternoonStartTimeOfDay01 = 14.5f / 24f;
        public const float SunsetStartTimeOfDay01 = 17.5f / 24f;

        private const float DawnStart = SunriseStartTimeOfDay01;
        private const float DawnEnd = 6.5f / 24f;
        private const float DuskStart = SunsetStartTimeOfDay01;
        private const float DuskEnd = NightCommandTimeOfDay01;
        private const float MaxNightOverlayAlpha = 0.58f;

        private static readonly Color DaySkyColor = new(102, 190, 255);
        private static readonly Color NightSkyColor = new(16, 38, 88);
        private static readonly Color NightOverlayBaseColor = new(6, 12, 32);

        private readonly float dayLengthSeconds;
        private float persistedTimeOfDay01;
        private int persistedCycleIndex;

        public WorldDayNightCycle(
            float initialTimeOfDay01 = DefaultStartTimeOfDay01,
            float dayLengthSeconds = DefaultDayLengthSeconds,
            int cycleIndex = 0)
        {
            this.dayLengthSeconds = Math.Max(1f, dayLengthSeconds);
            TimeOfDay01 = NormalizeTime(initialTimeOfDay01);
            CycleIndex = Math.Max(0, cycleIndex);
            persistedTimeOfDay01 = TimeOfDay01;
            persistedCycleIndex = CycleIndex;
        }

        public float DayLengthSeconds => dayLengthSeconds;
        public float TimeOfDay01 { get; private set; }
        public int CycleIndex { get; private set; }
        public WorldTimePhase CurrentPhase => GetPhase(TimeOfDay01);
        public float CyclePercent => TimeOfDay01 * 100f;
        public float NightStrength => CalculateNightStrength(TimeOfDay01);
        public float NightOverlayAlpha => NightStrength * MaxNightOverlayAlpha;
        public Color SkyColor => Color.Lerp(DaySkyColor, NightSkyColor, NightStrength);
        public Color NightOverlayTint => NightOverlayBaseColor * NightOverlayAlpha;
        public string ClockText24h => FormatClock24h(TimeOfDay01);

        public bool HasUnsavedChanges =>
            MathF.Abs(TimeOfDay01 - persistedTimeOfDay01) > 0.0001f ||
            CycleIndex != persistedCycleIndex;

        public void Advance(float dt, float timeScale, bool isPaused)
        {
            if (isPaused || dt <= 0f || timeScale <= 0f ||
                float.IsNaN(dt) || float.IsInfinity(dt) ||
                float.IsNaN(timeScale) || float.IsInfinity(timeScale))
            {
                return;
            }

            float delta = (dt * timeScale) / dayLengthSeconds;
            float rawTime = TimeOfDay01 + delta;
            if (rawTime >= 1f)
                CycleIndex += Math.Max(1, (int)MathF.Floor(rawTime));

            TimeOfDay01 = NormalizeTime(rawTime);
        }

        public void SetTimeOfDay01(float timeOfDay01)
        {
            TimeOfDay01 = NormalizeTime(timeOfDay01);
        }

        public void MarkPersisted()
        {
            persistedTimeOfDay01 = TimeOfDay01;
            persistedCycleIndex = CycleIndex;
        }

        public WorldTimeSnapshot CreateSnapshot()
        {
            return new WorldTimeSnapshot(
                TimeOfDay01,
                CycleIndex,
                CurrentPhase,
                ClockText24h,
                NightStrength);
        }

        public static WorldTimePhase GetPhase(float timeOfDay01)
        {
            float time = NormalizeTime(timeOfDay01);
            if (time >= NightCommandTimeOfDay01)
                return WorldTimePhase.Night;
            if (time >= SunsetStartTimeOfDay01)
                return WorldTimePhase.Sunset;
            if (time >= AfternoonStartTimeOfDay01)
                return WorldTimePhase.Afternoon;
            if (time >= NoonStartTimeOfDay01)
                return WorldTimePhase.Noon;
            if (time >= DawnEnd)
                return WorldTimePhase.Morning;
            if (time >= SunriseStartTimeOfDay01)
                return WorldTimePhase.Sunrise;
            if (time >= PreDawnStartTimeOfDay01)
                return WorldTimePhase.PreDawn;

            return WorldTimePhase.DeepNight;
        }

        public static string FormatClock24h(float timeOfDay01)
        {
            int totalMinutes = (int)MathF.Floor(NormalizeTime(timeOfDay01) * 24f * 60f);
            totalMinutes %= 24 * 60;

            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return $"{hours:00}:{minutes:00}";
        }

        private static float CalculateNightStrength(float timeOfDay01)
        {
            float time = NormalizeTime(timeOfDay01);
            if (time < DawnStart || time >= DuskEnd)
                return 1f;

            if (time < DawnEnd)
                return 1f - SmoothStep(DawnStart, DawnEnd, time);

            if (time < DuskStart)
                return 0f;

            return SmoothStep(DuskStart, DuskEnd, time);
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            float amount = MathHelper.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
            return amount * amount * (3f - (2f * amount));
        }

        private static float NormalizeTime(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return DefaultStartTimeOfDay01;

            value %= 1f;
            if (value < 0f)
                value += 1f;

            return value;
        }
    }
}
