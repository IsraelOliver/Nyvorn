using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public sealed class WorldEnvironmentSystem
    {
        private const float EclipseTransitionSeconds = 30f;
        private const float EclipseActiveSeconds = 220f;
        private const float EclipseDissipatingSeconds = 45f;
        private const float EclipseResidueSeconds = 180f;

        private const int RainChanceMorningSalt = 101;
        private const int RainChanceAfternoonSalt = 102;
        private const int RainActiveDurationSalt = 103;
        private const int EclipseChanceSalt = 201;

        private static readonly SkyKeyframe[] SkyKeyframes =
        {
            new(0f / 24f, new Color(8, 18, 43), new Color(15, 34, 77), new Color(78, 95, 132), new Color(27, 38, 68)),
            new(4f / 24f, new Color(17, 37, 79), new Color(66, 72, 104), new Color(95, 104, 134), new Color(46, 50, 83)),
            new(5f / 24f, new Color(47, 82, 126), new Color(223, 128, 86), new Color(170, 134, 118), new Color(124, 93, 94)),
            new(6.5f / 24f, new Color(102, 190, 255), new Color(156, 220, 255), new Color(222, 236, 245), new Color(164, 205, 228)),
            new(12f / 24f, new Color(87, 179, 255), new Color(137, 216, 255), new Color(245, 250, 255), new Color(176, 215, 234)),
            new(14.5f / 24f, new Color(99, 184, 247), new Color(150, 214, 245), new Color(238, 244, 250), new Color(171, 205, 224)),
            new(17.5f / 24f, new Color(74, 129, 204), new Color(246, 146, 82), new Color(212, 164, 126), new Color(132, 85, 94)),
            new(19.5f / 24f, new Color(13, 31, 73), new Color(30, 47, 86), new Color(82, 95, 123), new Color(32, 43, 73)),
            new(1f, new Color(8, 18, 43), new Color(15, 34, 77), new Color(78, 95, 132), new Color(27, 38, 68))
        };

        private readonly int seed;
        // Named generically (not "rain") because its Kind can become Storm/Hail/Snow/Sandstorm later;
        // the WorldEventDefinition it's evaluated against is looked up by Kind, not hardcoded here.
        private readonly WorldEventRuntimeSaveData weatherEvent;
        private readonly WorldEventRuntimeSaveData eclipse;

        private WorldTimeSnapshot previousTime;
        private bool hasPreviousTime;
        private int revision;
        private int persistedRevision;
        private float visualTimeSeconds;
        private float humidity = 0.35f;
        private float cloudCover;
        private float wind;
        private float wetness;
        private int rainCooldownCycles;
        private int eclipseCooldownCycles;

        public WorldEnvironmentSystem(int seed, WorldEnvironmentSaveData saveData, WorldTimeSnapshot initialTime)
        {
            this.seed = seed;
            saveData ??= new WorldEnvironmentSaveData { CycleIndex = initialTime.CycleIndex };
            weatherEvent = CloneEvent(saveData.Rain);
            eclipse = CloneEvent(saveData.Eclipse);
            rainCooldownCycles = Math.Max(0, saveData.RainCooldownCycles);
            eclipseCooldownCycles = Math.Max(0, saveData.EclipseCooldownCycles);
            humidity = MathHelper.Clamp(saveData.Humidity, 0f, 1f);
            cloudCover = MathHelper.Clamp(saveData.CloudCover, 0f, 1f);
            wind = MathHelper.Clamp(saveData.Wind, 0f, 1f);
            wetness = MathHelper.Clamp(saveData.Wetness, 0f, 1f);
            previousTime = initialTime;
            hasPreviousTime = true;
            RefreshSnapshot(initialTime);
            MarkPersisted();
        }

        public SkyState SkyState { get; private set; }
        public WeatherState WeatherState { get; private set; }
        public WorldEventState EventState { get; private set; }
        public TissueCycleState TissueCycleState { get; private set; }
        public bool HasUnsavedChanges => revision != persistedRevision;
        public float EnemyRespawnDelayMultiplier { get; private set; } = 1f;
        public float GrassGrowthChanceMultiplier { get; private set; } = 1f;

        public void Update(float dt, float timeScale, bool isPaused, WorldTimeSnapshot time)
        {
            if (!hasPreviousTime)
            {
                previousTime = time;
                hasPreviousTime = true;
            }

            float scaledDt = isPaused ? 0f : MathF.Max(0f, dt) * MathF.Max(0f, timeScale);
            if (time.CycleIndex > previousTime.CycleIndex)
            {
                rainCooldownCycles = Math.Max(0, rainCooldownCycles - (time.CycleIndex - previousTime.CycleIndex));
                eclipseCooldownCycles = Math.Max(0, eclipseCooldownCycles - (time.CycleIndex - previousTime.CycleIndex));
                revision++;
            }

            if (scaledDt > 0f)
            {
                visualTimeSeconds += scaledDt;
                HandleAutomaticTriggers(previousTime, time);
                AdvanceEvent(weatherEvent, scaledDt, GetWeatherStageDuration);
                AdvanceEvent(eclipse, scaledDt, GetEclipseDuration);
                UpdateWeatherMeters(scaledDt);
            }

            RefreshSnapshot(time);
            previousTime = time;
        }

        public WorldEnvironmentSaveData CreateSaveData(int cycleIndex)
        {
            return new WorldEnvironmentSaveData
            {
                CycleIndex = cycleIndex,
                Rain = CloneEvent(weatherEvent),
                Eclipse = CloneEvent(eclipse),
                RainCooldownCycles = rainCooldownCycles,
                EclipseCooldownCycles = eclipseCooldownCycles,
                Humidity = humidity,
                CloudCover = cloudCover,
                Wind = wind,
                Wetness = wetness
            };
        }

        public void MarkPersisted()
        {
            persistedRevision = revision;
        }

        public void ForceRain()
        {
            SetEventStage(weatherEvent, WorldEventKind.Rain, WorldEventChannel.Weather, WorldEventStage.Omen, 8f, true, previousTime.CycleIndex);
            rainCooldownCycles = 1;
            revision++;
        }

        public void StopRain()
        {
            if (!IsRunning(weatherEvent))
                return;

            SetEventStage(weatherEvent, WorldEventKind.Rain, WorldEventChannel.Weather, WorldEventStage.Dissipating, 12f, weatherEvent.IsForced, weatherEvent.StartedCycleIndex);
            revision++;
        }

        public void ForceEclipse()
        {
            SetEventStage(eclipse, WorldEventKind.SolarEclipse, WorldEventChannel.Astronomical, WorldEventStage.Transition, 10f, true, previousTime.CycleIndex);
            eclipseCooldownCycles = 3;
            revision++;
        }

        public void StopEclipse()
        {
            if (!IsRunning(eclipse))
                return;

            SetEventStage(eclipse, WorldEventKind.SolarEclipse, WorldEventChannel.Astronomical, WorldEventStage.Dissipating, 18f, eclipse.IsForced, eclipse.StartedCycleIndex);
            revision++;
        }

        public void ClearEvents()
        {
            ClearEvent(weatherEvent);
            ClearEvent(eclipse);
            rainCooldownCycles = 0;
            eclipseCooldownCycles = 0;
            cloudCover = 0f;
            wind = 0f;
            wetness = 0f;
            revision++;
        }

        public string GetStatusText()
        {
            return $"Rain:{FormatEvent(weatherEvent)} Eclipse:{FormatEvent(eclipse)} " +
                   $"cloud:{cloudCover:0.00} wind:{wind:0.00} wet:{wetness:0.00} " +
                   $"cd R/E:{rainCooldownCycles}/{eclipseCooldownCycles}";
        }

        private void HandleAutomaticTriggers(WorldTimeSnapshot previous, WorldTimeSnapshot current)
        {
            if (Crossed(previous, current, WorldDayNightCycle.PreDawnStartTimeOfDay01))
                TryQueueEclipseOmen(current);

            if (Crossed(previous, current, WorldDayNightCycle.DayCommandTimeOfDay01))
            {
                if (eclipse.Stage == WorldEventStage.Omen)
                    SetEventStage(eclipse, WorldEventKind.SolarEclipse, WorldEventChannel.Astronomical, WorldEventStage.Transition, EclipseTransitionSeconds, false, current.CycleIndex);

                TryStartRain(current, RainChanceMorningSalt);
            }

            if (Crossed(previous, current, WorldDayNightCycle.AfternoonStartTimeOfDay01))
                TryStartRain(current, RainChanceAfternoonSalt);
        }

        private void TryQueueEclipseOmen(WorldTimeSnapshot time)
        {
            if (IsRunning(eclipse) || eclipseCooldownCycles > 0)
                return;

            if (Deterministic01(time.CycleIndex, EclipseChanceSalt) > 0.08f)
                return;

            SetEventStage(eclipse, WorldEventKind.SolarEclipse, WorldEventChannel.Astronomical, WorldEventStage.Omen, 99999f, false, time.CycleIndex);
            eclipseCooldownCycles = 3;
            revision++;
        }

        private void TryStartRain(WorldTimeSnapshot time, int salt)
        {
            if (IsRunning(weatherEvent) || rainCooldownCycles > 0 || IsEclipseThreatening())
                return;

            float chance = 0.18f + (humidity * 0.18f) + (cloudCover * 0.10f) - (wetness * 0.14f);
            if (Deterministic01(time.CycleIndex, salt) > MathHelper.Clamp(chance, 0.05f, 0.42f))
                return;

            SetEventStage(weatherEvent, WorldEventKind.Rain, WorldEventChannel.Weather, WorldEventStage.Omen, WorldEventDefinition.Rain.OmenSeconds, false, time.CycleIndex);
            rainCooldownCycles = 1;
            revision++;
        }

        private void AdvanceEvent(
            WorldEventRuntimeSaveData data,
            float dt,
            Func<WorldEventStage, bool, float> durationProvider)
        {
            if (!IsRunning(data))
                return;

            data.StageElapsedSeconds += dt;
            if (data.Stage == WorldEventStage.Active && data.Channel == WorldEventChannel.Weather)
            {
                float instabilityRate = WorldEventDefinition.Get(data.Kind).InstabilityGainRate;
                data.Instability = MathHelper.Clamp(data.Instability + (dt * instabilityRate), 0f, 1f);
            }

            if (data.StageDurationSeconds > 9000f)
            {
                revision++;
                return;
            }

            while (IsRunning(data) && data.StageDurationSeconds > 0f && data.StageElapsedSeconds >= data.StageDurationSeconds)
            {
                data.StageElapsedSeconds -= data.StageDurationSeconds;
                WorldEventStage nextStage = data.Stage switch
                {
                    WorldEventStage.Omen => WorldEventStage.Transition,
                    WorldEventStage.Transition => WorldEventStage.Active,
                    WorldEventStage.Active => WorldEventStage.Dissipating,
                    WorldEventStage.Dissipating => WorldEventStage.Residue,
                    WorldEventStage.Residue => WorldEventStage.Inactive,
                    _ => WorldEventStage.Inactive
                };

                if (nextStage == WorldEventStage.Inactive)
                {
                    ClearEvent(data);
                    break;
                }

                data.Stage = nextStage;
                data.StageDurationSeconds = durationProvider(nextStage, data.IsForced);
            }

            revision++;
        }

        private void UpdateWeatherMeters(float dt)
        {
            float rainIntensity = GetWeatherIntensity();
            float eclipseIntensity = GetEclipseIntensity();
            WorldEventDefinition def = WorldEventDefinition.Get(weatherEvent.Kind);
            float targetCloud = weatherEvent.Stage switch
            {
                WorldEventStage.Omen => def.CloudOmen,
                WorldEventStage.Transition => def.CloudTransition,
                WorldEventStage.Active => def.CloudActive,
                WorldEventStage.Dissipating => def.CloudDissipating,
                WorldEventStage.Residue => def.CloudResidue,
                _ => 0f
            };
            targetCloud = MathF.Max(targetCloud, eclipseIntensity * 0.28f);

            float targetWind = weatherEvent.Stage switch
            {
                WorldEventStage.Omen => def.WindOmen,
                WorldEventStage.Transition => def.WindTransition,
                WorldEventStage.Active => def.WindActive,
                WorldEventStage.Dissipating => def.WindDissipating,
                _ => def.WindResidue
            };

            cloudCover = Approach(cloudCover, targetCloud, dt * 0.22f);
            wind = Approach(wind, targetWind, dt * 0.28f);
            humidity = Approach(humidity, rainIntensity > 0f ? 0.85f : 0.35f, dt * 0.025f);

            if (rainIntensity > 0.05f)
                wetness = MathHelper.Clamp(wetness + (dt * rainIntensity * def.WetnessGainRate), 0f, 1f);
            else
                wetness = Approach(wetness, 0f, dt * 0.010f);
        }

        private void RefreshSnapshot(WorldTimeSnapshot time)
        {
            float rainIntensity = GetWeatherIntensity();
            float eclipseIntensity = GetEclipseIntensity();
            TissueCycleState = CreateTissueCycleState(time);
            WeatherState = new WeatherState(humidity, cloudCover, wind, wetness, rainIntensity);
            EventState = new WorldEventState(CloneEvent(weatherEvent), CloneEvent(eclipse), rainCooldownCycles, eclipseCooldownCycles);
            EnemyRespawnDelayMultiplier = MathHelper.Clamp(
                MathHelper.Lerp(1f, 0.48f, MathF.Max(eclipseIntensity * 0.85f, TissueCycleState.CorrectionStrength * 0.45f)),
                0.35f,
                1.15f);
            GrassGrowthChanceMultiplier = 1f + (wetness * 0.75f) + (rainIntensity * 0.35f);
            SkyState = CreateSkyState(time, rainIntensity, eclipseIntensity, TissueCycleState);
        }

        private SkyState CreateSkyState(
            WorldTimeSnapshot time,
            float rainIntensity,
            float eclipseIntensity,
            TissueCycleState tissueState)
        {
            SkyKeyframe keyframe = InterpolateSkyKeyframe(time.TimeOfDay01);
            float sunProgress = MathHelper.Clamp(
                (time.TimeOfDay01 - WorldDayNightCycle.SunriseStartTimeOfDay01) /
                (WorldDayNightCycle.NightCommandTimeOfDay01 - WorldDayNightCycle.SunriseStartTimeOfDay01),
                0f,
                1f);
            float moonProgress = time.TimeOfDay01 >= WorldDayNightCycle.NightCommandTimeOfDay01
                ? (time.TimeOfDay01 - WorldDayNightCycle.NightCommandTimeOfDay01) /
                  (1f - WorldDayNightCycle.NightCommandTimeOfDay01 + WorldDayNightCycle.SunriseStartTimeOfDay01)
                : (time.TimeOfDay01 + (1f - WorldDayNightCycle.NightCommandTimeOfDay01)) /
                  (1f - WorldDayNightCycle.NightCommandTimeOfDay01 + WorldDayNightCycle.SunriseStartTimeOfDay01);
            moonProgress = MathHelper.Clamp(moonProgress, 0f, 1f);

            Color top = keyframe.TopColor;
            Color horizon = keyframe.HorizonColor;
            Color ambient = keyframe.AmbientLight;
            Color fog = keyframe.FogColor;
            float rainVisual = MathF.Max(rainIntensity, cloudCover * 0.65f);
            if (rainVisual > 0f)
            {
                top = Color.Lerp(top, new Color(55, 73, 91), rainVisual * 0.74f);
                horizon = Color.Lerp(horizon, new Color(102, 117, 130), rainVisual * 0.72f);
                ambient = Color.Lerp(ambient, new Color(126, 138, 148), rainVisual * 0.45f);
                fog = Color.Lerp(fog, new Color(118, 132, 142), rainVisual * 0.70f);
            }

            if (eclipseIntensity > 0f)
            {
                top = Color.Lerp(top, new Color(7, 12, 28), eclipseIntensity * 0.92f);
                horizon = Color.Lerp(horizon, new Color(58, 63, 78), eclipseIntensity * 0.86f);
                ambient = Color.Lerp(ambient, new Color(72, 83, 104), eclipseIntensity * 0.72f);
                fog = Color.Lerp(fog, new Color(46, 48, 64), eclipseIntensity * 0.55f);
            }

            if (tissueState.CorrectionStrength > 0.05f)
            {
                float tissueTint = tissueState.CorrectionStrength * 0.18f;
                top = Color.Lerp(top, new Color(30, 19, 58), tissueTint);
                horizon = Color.Lerp(horizon, new Color(65, 35, 82), tissueTint);
            }

            float sunOpacity = MathHelper.Clamp((1f - time.NightStrength) * (1f - eclipseIntensity * 0.45f), 0f, 1f);
            float moonOpacity = MathHelper.Clamp(time.NightStrength * (1f - eclipseIntensity * 0.20f), 0f, 1f);
            float starOpacity = MathF.Pow(time.NightStrength, 1.25f) * (1f - rainVisual * 0.72f) * (1f - eclipseIntensity * 0.35f);
            float overlayAlpha = MathHelper.Clamp(
                (time.NightStrength * 0.50f) +
                (rainVisual * 0.14f) +
                (eclipseIntensity * 0.52f),
                0f,
                0.68f);

            // Near moon reuses the exact progress formula the old single moon always used
            // (ArcSpeedMultiplier 1.0 = ties to the same night window). Far moon drifts relative to
            // it via a slightly slower ArcSpeedMultiplier - a per-night "beat" offset accumulates
            // from that speed mismatch, so the two only line up again every several nights.
            float farMoonProgress = ComputeDriftedMoonProgress(moonProgress, time.CycleIndex, MoonDefinition.FarMoon.ArcSpeedMultiplier);
            float nearMoonPhase01 = ComputeMoonPhase01(time, MoonDefinition.NearMoon.PhasePeriodDays);
            float farMoonPhase01 = ComputeMoonPhase01(time, MoonDefinition.FarMoon.PhasePeriodDays);
            float moonConjunction01 = ComputeMoonConjunction01(moonProgress, farMoonProgress, nearMoonPhase01, farMoonPhase01);

            return new SkyState(
                top,
                horizon,
                ambient,
                fog,
                Color.Lerp(new Color(255, 239, 165), new Color(154, 176, 210), eclipseIntensity),
                new Color(6, 12, 32) * overlayAlpha,
                sunProgress,
                sunOpacity,
                new MoonState(moonProgress, moonOpacity, nearMoonPhase01),
                new MoonState(farMoonProgress, moonOpacity, farMoonPhase01),
                moonConjunction01,
                MathHelper.Clamp(starOpacity, 0f, 1f),
                MathHelper.Clamp(cloudCover, 0f, 1f),
                MathHelper.Clamp((rainVisual * 0.24f) + (wetness * 0.14f) + (tissueState.ResidueStrength * 0.12f), 0f, 1f),
                rainIntensity,
                eclipseIntensity,
                tissueState.CorrectionStrength,
                wetness,
                wind,
                visualTimeSeconds);
        }

        // Deterministic day-count drift instead of an explicit offset table: with ArcSpeedMultiplier
        // slightly below 1, the far moon falls a little further behind the near moon's arc each
        // night; that lag wraps modulo 1, so it periodically comes back around to near-zero (close
        // alignment) without ever being scripted to "happen on day N".
        private static float ComputeDriftedMoonProgress(float referenceProgress, int cycleIndex, float arcSpeedMultiplier)
        {
            float nightStartOffset = Frac(cycleIndex * (1f - arcSpeedMultiplier));
            return Frac((referenceProgress * arcSpeedMultiplier) + nightStartOffset);
        }

        private static float ComputeMoonPhase01(WorldTimeSnapshot time, float phasePeriodDays)
        {
            float daysElapsed = time.CycleIndex + time.TimeOfDay01;
            return Frac(daysElapsed / MathF.Max(phasePeriodDays, 0.0001f));
        }

        // 0 at new moon, 1 at full moon, 0 again at the next new moon.
        private static float ComputeMoonFullness(float phase01)
        {
            return 0.5f - (0.5f * MathF.Cos(MathHelper.TwoPi * phase01));
        }

        // How close the two moons' arc positions currently are, as a 0..1 falloff - only pixels
        // within MoonConjunctionPositionWindow of each other register any alignment at all.
        private const float MoonConjunctionPositionWindow = 0.06f;

        // Exposed on SkyState as MoonConjunction01 - product of position-alignment and
        // fullness-alignment, so it's close to 0 almost always (both terms independently rare
        // given the moons' different PhasePeriodDays/ArcSpeedMultiplier) and only spikes when both
        // conditions land on the same night. Nothing subscribes to this yet (see SkyState comment).
        private static float ComputeMoonConjunction01(float nearProgress, float farProgress, float nearPhase01, float farPhase01)
        {
            float progressDelta = MathF.Abs(nearProgress - farProgress);
            float positionAlignment01 = MathHelper.Clamp(1f - (progressDelta / MoonConjunctionPositionWindow), 0f, 1f);
            float fullnessAlignment01 = ComputeMoonFullness(nearPhase01) * ComputeMoonFullness(farPhase01);
            return MathHelper.Clamp(positionAlignment01 * fullnessAlignment01, 0f, 1f);
        }

        private static float Frac(float value) => value - MathF.Floor(value);

        private TissueCycleState CreateTissueCycleState(WorldTimeSnapshot time)
        {
            float value = time.TimeOfDay01;
            WorldEventStage stage = WorldEventStage.Inactive;
            float correction = 0f;
            float residue = 0f;

            if (time.Phase == WorldTimePhase.Sunset)
            {
                stage = WorldEventStage.Omen;
                correction = MathHelper.SmoothStep(0f, 0.25f, PhaseProgress(value, WorldDayNightCycle.SunsetStartTimeOfDay01, WorldDayNightCycle.NightCommandTimeOfDay01));
            }
            else if (time.Phase == WorldTimePhase.Night)
            {
                stage = WorldEventStage.Transition;
                correction = MathHelper.Lerp(0.35f, 0.76f, PhaseProgress(value, WorldDayNightCycle.NightCommandTimeOfDay01, 1f));
            }
            else if (time.Phase == WorldTimePhase.DeepNight)
            {
                stage = WorldEventStage.Active;
                correction = MathHelper.Lerp(1f, 0.72f, PhaseProgress(value, 0f, WorldDayNightCycle.PreDawnStartTimeOfDay01));
            }
            else if (time.Phase == WorldTimePhase.PreDawn)
            {
                stage = WorldEventStage.Dissipating;
                correction = MathHelper.Lerp(0.65f, 0.22f, PhaseProgress(value, WorldDayNightCycle.PreDawnStartTimeOfDay01, WorldDayNightCycle.SunriseStartTimeOfDay01));
            }
            else if (time.Phase == WorldTimePhase.Sunrise)
            {
                stage = WorldEventStage.Residue;
                residue = 1f - PhaseProgress(value, WorldDayNightCycle.SunriseStartTimeOfDay01, 6.5f / 24f);
                correction = residue * 0.16f;
            }

            return new TissueCycleState(
                stage,
                MathHelper.Clamp(correction, 0f, 1f),
                MathHelper.Clamp((correction * 0.65f) + (residue * 0.15f), 0f, 1f),
                MathHelper.Clamp(residue, 0f, 1f));
        }

        private static SkyKeyframe InterpolateSkyKeyframe(float timeOfDay01)
        {
            float time = MathHelper.Clamp(timeOfDay01, 0f, 1f);
            for (int i = 0; i < SkyKeyframes.Length - 1; i++)
            {
                SkyKeyframe current = SkyKeyframes[i];
                SkyKeyframe next = SkyKeyframes[i + 1];
                if (time < current.Time || time > next.Time)
                    continue;

                float amount = MathHelper.SmoothStep(0f, 1f, (time - current.Time) / (next.Time - current.Time));
                return new SkyKeyframe(
                    time,
                    Color.Lerp(current.TopColor, next.TopColor, amount),
                    Color.Lerp(current.HorizonColor, next.HorizonColor, amount),
                    Color.Lerp(current.AmbientLight, next.AmbientLight, amount),
                    Color.Lerp(current.FogColor, next.FogColor, amount));
            }

            return SkyKeyframes[0];
        }

        private float GetWeatherIntensity()
        {
            WorldEventDefinition def = WorldEventDefinition.Get(weatherEvent.Kind);
            return weatherEvent.Stage switch
            {
                WorldEventStage.Omen => 0f,
                WorldEventStage.Transition => MathHelper.SmoothStep(0f, def.IntensityTransitionPeak, StageProgress(weatherEvent)),
                WorldEventStage.Active => def.IntensityActive,
                WorldEventStage.Dissipating => MathHelper.Lerp(def.IntensityDissipatingStart, def.IntensityDissipatingEnd, StageProgress(weatherEvent)),
                WorldEventStage.Residue => MathHelper.Lerp(def.IntensityResidueStart, 0f, StageProgress(weatherEvent)),
                _ => 0f
            };
        }

        private float GetEclipseIntensity()
        {
            return eclipse.Stage switch
            {
                WorldEventStage.Omen => 0.12f,
                WorldEventStage.Transition => MathHelper.SmoothStep(0f, 1f, StageProgress(eclipse)),
                WorldEventStage.Active => 1f,
                WorldEventStage.Dissipating => MathHelper.Lerp(1f, 0.25f, StageProgress(eclipse)),
                WorldEventStage.Residue => MathHelper.Lerp(0.25f, 0f, StageProgress(eclipse)),
                _ => 0f
            };
        }

        private float GetWeatherStageDuration(WorldEventStage stage, bool forced)
        {
            WorldEventDefinition def = WorldEventDefinition.Get(weatherEvent.Kind);
            float scale = forced ? def.ForcedDurationScale : 1f;
            return stage switch
            {
                WorldEventStage.Omen => def.OmenSeconds * scale,
                WorldEventStage.Transition => def.TransitionSeconds * scale,
                WorldEventStage.Active => MathHelper.Lerp(
                    def.ActiveSecondsMin,
                    def.ActiveSecondsMax,
                    Deterministic01(weatherEvent.StartedCycleIndex, RainActiveDurationSalt)) * scale,
                WorldEventStage.Dissipating => def.DissipatingSeconds * scale,
                WorldEventStage.Residue => def.ResidueSeconds * scale,
                _ => 0f
            };
        }

        private static float GetEclipseDuration(WorldEventStage stage, bool forced)
        {
            float scale = forced ? 0.65f : 1f;
            return stage switch
            {
                WorldEventStage.Omen => 99999f,
                WorldEventStage.Transition => EclipseTransitionSeconds * scale,
                WorldEventStage.Active => EclipseActiveSeconds * scale,
                WorldEventStage.Dissipating => EclipseDissipatingSeconds * scale,
                WorldEventStage.Residue => EclipseResidueSeconds * scale,
                _ => 0f
            };
        }

        private static void SetEventStage(
            WorldEventRuntimeSaveData data,
            WorldEventKind kind,
            WorldEventChannel channel,
            WorldEventStage stage,
            float duration,
            bool forced,
            int cycleIndex)
        {
            data.Kind = kind;
            data.Channel = channel;
            data.Stage = stage;
            data.StageElapsedSeconds = 0f;
            data.StageDurationSeconds = MathF.Max(0f, duration);
            data.StartedCycleIndex = Math.Max(0, cycleIndex);
            data.IsForced = forced;
            data.Instability = 0f;
        }

        // Escalates an already-running weather event to a new Kind (e.g. Rain -> Storm, Phase 2)
        // in place - deliberately does NOT reset Stage/StageElapsedSeconds/StageDurationSeconds the
        // way SetEventStage does, so a storm doesn't snap back to Omen when the rain that grew into
        // it was already Active. Not called yet: no Kind besides Rain exists for the Weather channel.
        private static void PromoteEventKind(WorldEventRuntimeSaveData data, WorldEventKind newKind)
        {
            data.Kind = newKind;
            data.Instability = 0f;
        }

        private static void ClearEvent(WorldEventRuntimeSaveData data)
        {
            data.Kind = WorldEventKind.None;
            data.Stage = WorldEventStage.Inactive;
            data.StageElapsedSeconds = 0f;
            data.StageDurationSeconds = 0f;
            data.StartedCycleIndex = 0;
            data.IsForced = false;
            data.Instability = 0f;
        }

        private static WorldEventRuntimeSaveData CloneEvent(WorldEventRuntimeSaveData source)
        {
            if (source == null)
                return new WorldEventRuntimeSaveData();

            return new WorldEventRuntimeSaveData
            {
                Kind = source.Kind,
                Channel = source.Channel,
                Stage = source.Stage,
                StageElapsedSeconds = source.StageElapsedSeconds,
                StageDurationSeconds = source.StageDurationSeconds,
                StartedCycleIndex = source.StartedCycleIndex,
                IsForced = source.IsForced,
                Instability = source.Instability
            };
        }

        private bool IsEclipseThreatening()
        {
            return eclipse.Stage is WorldEventStage.Omen or WorldEventStage.Transition or WorldEventStage.Active;
        }

        private static bool IsRunning(WorldEventRuntimeSaveData data)
        {
            return data != null && data.Stage != WorldEventStage.Inactive && data.Kind != WorldEventKind.None;
        }

        private static float StageProgress(WorldEventRuntimeSaveData data)
        {
            if (data == null || data.StageDurationSeconds <= 0f || data.StageDurationSeconds > 9000f)
                return 0f;

            return MathHelper.Clamp(data.StageElapsedSeconds / data.StageDurationSeconds, 0f, 1f);
        }

        private static float PhaseProgress(float time, float start, float end)
        {
            if (end <= start)
                return 0f;

            return MathHelper.Clamp((time - start) / (end - start), 0f, 1f);
        }

        private static bool Crossed(WorldTimeSnapshot previous, WorldTimeSnapshot current, float threshold)
        {
            if (current.CycleIndex > previous.CycleIndex)
                return previous.TimeOfDay01 < threshold || current.TimeOfDay01 >= threshold;

            return previous.TimeOfDay01 < threshold && current.TimeOfDay01 >= threshold;
        }

        private float Deterministic01(int cycleIndex, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)seed) * 16777619u;
                hash = (hash ^ (uint)cycleIndex) * 16777619u;
                hash = (hash ^ (uint)salt) * 16777619u;
                hash ^= hash >> 13;
                hash *= 1274126177u;
                return (hash & 0x00FFFFFF) / 16777215f;
            }
        }

        private static float Approach(float current, float target, float amount)
        {
            if (current < target)
                return MathF.Min(target, current + amount);

            return MathF.Max(target, current - amount);
        }

        private static string FormatEvent(WorldEventRuntimeSaveData data)
        {
            if (!IsRunning(data))
                return "off";

            return $"{data.Stage} {StageProgress(data) * 100f:0}%";
        }

        private readonly record struct SkyKeyframe(
            float Time,
            Color TopColor,
            Color HorizonColor,
            Color AmbientLight,
            Color FogColor);
    }
}
