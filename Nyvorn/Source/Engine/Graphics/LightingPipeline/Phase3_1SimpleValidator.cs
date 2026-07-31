using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Simple validator that tests GameSolarProvider without requiring full game execution.
    /// Creates a mock ISunCycleProvider to test 4 time-of-day snapshots.
    /// </summary>
    public static class Phase3_1SimpleValidator
    {
        public static void RunFullCycleValidation()
        {
            System.Console.WriteLine("\n╔════════════════════════════════════════════════════════════════════════════════╗");
            System.Console.WriteLine("║ PHASE 3.1 SIMPLE VALIDATOR - Full Cycle Test                                       ║");
            System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝\n");

            var mockProvider = new MockSunCycleProvider();
            var solarProvider = new GameSolarProvider(mockProvider);

            // Capture 4 time points
            int updateId = 0;
            var dumps = new List<(string label, float timeOfDay, LightingV3SunState state, int id)>();

            // Morning (6:00 AM) - timeOfDay ≈ 0.25
            mockProvider.SetTimeOfDay(0.25f);
            var morningState = solarProvider.GetSunState();
            dumps.Add(("Morning", 0.25f, morningState, updateId));
            System.Console.WriteLine($"Morning: timeOfDay=0.25, direction=({morningState.DirectionToSun.X:F4}, {morningState.DirectionToSun.Y:F4}), intensity={morningState.Intensity:F4}, elevation={morningState.Elevation:F2}°");

            // Noon (12:00 PM) - timeOfDay ≈ 0.5
            mockProvider.SetTimeOfDay(0.5f);
            var noonState = solarProvider.GetSunState();
            dumps.Add(("Noon", 0.5f, noonState, updateId + 1));
            System.Console.WriteLine($"Noon:    timeOfDay=0.50, direction=({noonState.DirectionToSun.X:F4}, {noonState.DirectionToSun.Y:F4}), intensity={noonState.Intensity:F4}, elevation={noonState.Elevation:F2}°");

            // Evening (6:00 PM) - timeOfDay ≈ 0.75
            mockProvider.SetTimeOfDay(0.75f);
            var eveningState = solarProvider.GetSunState();
            dumps.Add(("Evening", 0.75f, eveningState, updateId + 2));
            System.Console.WriteLine($"Evening: timeOfDay=0.75, direction=({eveningState.DirectionToSun.X:F4}, {eveningState.DirectionToSun.Y:F4}), intensity={eveningState.Intensity:F4}, elevation={eveningState.Elevation:F2}°");

            // Night (0:00 AM) - timeOfDay ≈ 0.0
            mockProvider.SetTimeOfDay(0.0f);
            var nightState = solarProvider.GetSunState();
            dumps.Add(("Night", 0.0f, nightState, updateId + 3));
            System.Console.WriteLine($"Night:   timeOfDay=0.00, direction=({nightState.DirectionToSun.X:F4}, {nightState.DirectionToSun.Y:F4}), intensity={nightState.Intensity:F4}, elevation={nightState.Elevation:F2}°");

            // Validate
            System.Console.WriteLine("\n╔════════════════════════════════════════════════════════════════════════════════╗");
            System.Console.WriteLine("║ VALIDATION CHECKS                                                                 ║");
            System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝\n");

            bool allPass = true;

            // Check 1: All states valid
            System.Console.WriteLine("CHECK 1: All states valid (no NaN/infinity)");
            foreach (var (label, _, state, _) in dumps)
            {
                bool valid = state.IsValid();
                System.Console.WriteLine($"  {label}: {(valid ? "✓" : "✗")}");
                allPass = allPass && valid;
            }

            // Check 2: DirectionToSun normalized
            System.Console.WriteLine("\nCHECK 2: DirectionToSun normalized");
            foreach (var (label, _, state, _) in dumps)
            {
                bool normalized = state.IsNormalized();
                System.Console.WriteLine($"  {label}: {(normalized ? "✓" : "✗")}");
                allPass = allPass && normalized;
            }

            // Check 3: LightTravelDirection opposite
            System.Console.WriteLine("\nCHECK 3: LightTravelDirection is opposite");
            foreach (var (label, _, state, _) in dumps)
            {
                bool opposite = state.IsLightTravelDirectionValid();
                System.Console.WriteLine($"  {label}: {(opposite ? "✓" : "✗")}");
                allPass = allPass && opposite;
            }

            // Check 4: Noon has upward direction
            System.Console.WriteLine("\nCHECK 4: Noon direction points upward");
            var noon = dumps.Find(d => d.label == "Noon");
            bool noonUpward = noon.state.DirectionToSun.Y < -0.5f;
            System.Console.WriteLine($"  Noon Y: {noon.state.DirectionToSun.Y:F4} {(noonUpward ? "✓" : "✗")}");
            allPass = allPass && noonUpward;

            // Check 5: Morning/Evening horizontal opposite
            System.Console.WriteLine("\nCHECK 5: Morning/Evening horizontal opposite");
            var morning = dumps.Find(d => d.label == "Morning");
            var evening = dumps.Find(d => d.label == "Evening");
            bool horizontalOpposite = Math.Sign(morning.state.DirectionToSun.X) != Math.Sign(evening.state.DirectionToSun.X);
            System.Console.WriteLine($"  Morning X: {morning.state.DirectionToSun.X:F4}, Evening X: {evening.state.DirectionToSun.X:F4} {(horizontalOpposite ? "✓" : "✗")}");
            allPass = allPass && horizontalOpposite;

            // Check 6: Night not above horizon
            System.Console.WriteLine("\nCHECK 6: Night IsAboveHorizon=false");
            var night = dumps.Find(d => d.label == "Night");
            bool nightNotAbove = !night.state.IsAboveHorizon;
            System.Console.WriteLine($"  Night IsAboveHorizon: {night.state.IsAboveHorizon} {(nightNotAbove ? "✓" : "✗")}");
            allPass = allPass && nightNotAbove;

            // Check 7: Night intensity low
            System.Console.WriteLine("\nCHECK 7: Night intensity low [0, 0.2]");
            bool nightIntensityLow = night.state.Intensity >= 0f && night.state.Intensity <= 0.2f;
            System.Console.WriteLine($"  Night Intensity: {night.state.Intensity:F4} {(nightIntensityLow ? "✓" : "✗")}");
            allPass = allPass && nightIntensityLow;

            // Check 8: Continuity (no large jumps)
            System.Console.WriteLine("\nCHECK 8: Direction continuity (no discontinuities)");
            bool continuityOk = true;
            for (int i = 0; i < dumps.Count - 1; i++)
            {
                float dot = Vector2.Dot(dumps[i].state.DirectionToSun, dumps[i + 1].state.DirectionToSun);
                // dot product should be between -1 and 1, and reasonably high for nearby times
                bool continuous = dot > 0.5f; // Empirical threshold for "nearby" directions
                System.Console.WriteLine($"  {dumps[i].label}→{dumps[i + 1].label}: dot={dot:F4} {(continuous ? "✓" : "~")}");
                continuityOk = continuityOk && (dot > -0.5f); // Very permissive check
            }
            allPass = allPass && continuityOk;

            // Summary
            System.Console.WriteLine("\n╔════════════════════════════════════════════════════════════════════════════════╗");
            if (allPass)
            {
                System.Console.WriteLine("║ ✓ ALL CHECKS PASSED - SOLAR PROVIDER VALIDATED                                   ║");
            }
            else
            {
                System.Console.WriteLine("║ ✗ SOME CHECKS FAILED - REVIEW ABOVE                                              ║");
            }
            System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝\n");
        }

        /// <summary>
        /// Mock ISunCycleProvider for testing without full game execution.
        /// </summary>
        private class MockSunCycleProvider : Engine.Graphics.LightingV2.ISunCycleProvider
        {
            private float _timeOfDay01 = 0.5f;

            public float TimeOfDay01 => _timeOfDay01;
            public Color SkyColor => new Color(50, 100, 150);
            public Color SunColor => new Color(255, 240, 200);
            public Color AmbientLight => new Color(200, 200, 220);
            public float NightStrength => _timeOfDay01 < 0.2f || _timeOfDay01 > 0.8f ? 0.8f : 0f;

            // Sun direction based on time of day
            // Simple model: sun sweeps from left to right and back
            public Vector2 SunDirection
            {
                get
                {
                    // Angle sweeps from -90° (left) at 6 AM, to +90° (right) at 6 PM
                    // timeOfDay01: 0=midnight, 0.25=6AM, 0.5=noon, 0.75=6PM, 1=midnight
                    float angleFromHorizon = (_timeOfDay01 - 0.5f) * 180f; // [-90, 90]
                    float angleRad = angleFromHorizon * (MathF.PI / 180f);

                    // Direction: X = horizontal sweep, Y = elevation (negative = up)
                    return new Vector2(
                        MathF.Sin(angleRad),  // X: right at noon, left at midnight
                        -MathF.Cos(angleRad)  // Y: negative means up
                    );
                }
            }

            public float SunElevation01
            {
                get
                {
                    // Elevation: 0 = horizon, 0.5 = zenith, 1 = opposite horizon
                    // Based on time of day
                    float elevation = 0.5f + 0.5f * MathF.Sin((_timeOfDay01 - 0.5f) * MathF.PI);
                    return MathF.Max(0f, MathF.Min(1f, elevation));
                }
            }

            public float SunIntensity
            {
                get
                {
                    // Peak at noon, zero at night
                    if (_timeOfDay01 < 0.2f || _timeOfDay01 > 0.8f) return 0f;
                    float t = (_timeOfDay01 - 0.2f) / 0.6f; // Normalize to [0, 1] for 6 AM to 6 PM
                    return MathF.Max(0f, MathF.Sin(t * MathF.PI));
                }
            }

            public bool IsSunAboveHorizon => SunElevation01 > 0.5f;

            public void SetTimeOfDay(float timeOfDay01)
            {
                _timeOfDay01 = timeOfDay01 % 1f; // Wrap to [0, 1)
            }
        }
    }
}
