using System;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.World.Simulation;

namespace Nyvorn.Source.Engine.Graphics.LightingV7
{
    /// <summary>
    /// Sky and sun colour for the hour, weather and moons.
    ///
    /// V7 keeps its own night curve rather than reusing SkyState.AmbientLight. That value was
    /// authored for tinting sprites and sits near (78, 95, 132) at midnight, which a multiplicative
    /// light map reads as a bright blue night. SkyState is still the source of truth for the time,
    /// rain, eclipse and the two moons - only the response curve is V7's own.
    /// </summary>
    public static class LightingV7Sky
    {
        // Cycle anchors, matching WorldDayNightCycle's phase boundaries.
        private const float DawnStart = 5f / 24f;
        private const float DawnEnd = 6.5f / 24f;
        private const float DuskStart = 17.5f / 24f;
        private const float DuskEnd = 19.5f / 24f;

        /// <summary>Ambient sky colour, already carrying weather, eclipse and moonlight.</summary>
        public static Vector3 GetSkyColor(SkyState skyState, float timeOfDay01)
        {
            Vector3 baseColor = GetBaseSkyColor(timeOfDay01);

            // Rain flattens and dims the sky.
            float rain = MathHelper.Clamp(skyState.RainIntensity, 0f, 1f);
            if (rain > 0f)
            {
                float rainFactor = MathHelper.Lerp(1f, LightingV7Config.RainSkyMultiplier, rain);
                baseColor *= rainFactor;
            }

            // An eclipse takes the sky down towards night regardless of the hour.
            float eclipse = MathHelper.Clamp(skyState.EclipseIntensity, 0f, 1f);
            if (eclipse > 0f)
            {
                Vector3 eclipsed = LightingV7Config.SkyColorNight * LightingV7Config.EclipseSkyMultiplier;
                baseColor = Vector3.Lerp(baseColor, eclipsed, eclipse);
            }

            // Two full, aligned moons brighten the night. It does nothing during the day, where the
            // sun already dominates.
            float night = GetNightAmount(timeOfDay01);
            float conjunction = MathHelper.Clamp(skyState.MoonConjunction01, 0f, 1f);
            if (night > 0f && conjunction > 0f)
            {
                float boost = 1f + conjunction * LightingV7Config.MoonConjunctionBoost * night;
                baseColor *= boost;
            }

            return baseColor;
        }

        /// <summary>Direct sun colour. Zero once the sun is down, so the shafts vanish at night.</summary>
        public static Vector3 GetSunColor(SkyState skyState, float timeOfDay01)
        {
            float above = GetSunAboveHorizon(timeOfDay01);
            if (above <= 0f)
                return Vector3.Zero;

            // Warm at the horizon, neutral overhead.
            Vector3 color = Vector3.Lerp(LightingV7Config.SunColorHorizon, LightingV7Config.SunColorNoon, above);

            // Cloud and rain kill the shafts before they dim the ambient sky.
            float rain = MathHelper.Clamp(skyState.RainIntensity, 0f, 1f);
            float eclipse = MathHelper.Clamp(skyState.EclipseIntensity, 0f, 1f);
            float clear = (1f - rain) * (1f - eclipse);

            return color * above * clear;
        }

        /// <summary>
        /// Beam slant in horizontal tiles per row: 0 overhead, growing towards either horizon.
        /// Sign flips at noon so the shafts lean the other way in the afternoon.
        /// </summary>
        public static float GetSunSlope(float timeOfDay01)
        {
            float dayProgress = GetDayProgress(timeOfDay01);
            if (dayProgress < 0f)
                return 0f;

            // +1 at sunrise, 0 at noon, -1 at sunset.
            float fromNoon = 1f - 2f * dayProgress;
            return fromNoon * LightingV7Config.SunMaxSlope;
        }

        /// <summary>0 at night, 1 with the sun overhead.</summary>
        private static float GetSunAboveHorizon(float timeOfDay01)
        {
            float dayProgress = GetDayProgress(timeOfDay01);
            if (dayProgress < 0f)
                return 0f;

            // A sine arc peaking at noon.
            return MathF.Sin(dayProgress * MathF.PI);
        }

        /// <summary>Position between sunrise and sunset, 0..1, or -1 when the sun is down.</summary>
        private static float GetDayProgress(float timeOfDay01)
        {
            float time = Normalize(timeOfDay01);
            if (time < DawnStart || time > DuskEnd)
                return -1f;

            return (time - DawnStart) / (DuskEnd - DawnStart);
        }

        /// <summary>1 in full night, 0 in full day, smooth across dawn and dusk.</summary>
        private static float GetNightAmount(float timeOfDay01)
        {
            float time = Normalize(timeOfDay01);
            if (time < DawnStart || time >= DuskEnd) return 1f;
            if (time < DawnEnd) return 1f - SmoothStep(DawnStart, DawnEnd, time);
            if (time < DuskStart) return 0f;
            return SmoothStep(DuskStart, DuskEnd, time);
        }

        private static Vector3 GetBaseSkyColor(float timeOfDay01)
        {
            float time = Normalize(timeOfDay01);

            if (time < DawnStart || time >= DuskEnd)
                return LightingV7Config.SkyColorNight;

            // Dawn: night -> dawn tint -> day.
            if (time < DawnEnd)
            {
                float t = (time - DawnStart) / (DawnEnd - DawnStart);
                return t < 0.5f
                    ? Vector3.Lerp(LightingV7Config.SkyColorNight, LightingV7Config.SkyColorDawn, SmoothStep01(t * 2f))
                    : Vector3.Lerp(LightingV7Config.SkyColorDawn, LightingV7Config.SkyColorDay, SmoothStep01((t - 0.5f) * 2f));
            }

            if (time < DuskStart)
                return LightingV7Config.SkyColorDay;

            // Dusk: day -> dusk tint -> night.
            float d = (time - DuskStart) / (DuskEnd - DuskStart);
            return d < 0.5f
                ? Vector3.Lerp(LightingV7Config.SkyColorDay, LightingV7Config.SkyColorDusk, SmoothStep01(d * 2f))
                : Vector3.Lerp(LightingV7Config.SkyColorDusk, LightingV7Config.SkyColorNight, SmoothStep01((d - 0.5f) * 2f));
        }

        private static float Normalize(float timeOfDay01)
        {
            float value = timeOfDay01 % 1f;
            return value < 0f ? value + 1f : value;
        }

        private static float SmoothStep(float start, float end, float value)
        {
            if (end <= start)
                return 0f;

            return SmoothStep01(MathHelper.Clamp((value - start) / (end - start), 0f, 1f));
        }

        private static float SmoothStep01(float t) => t * t * (3f - 2f * t);
    }
}
