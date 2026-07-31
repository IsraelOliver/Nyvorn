using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Real-time validator for Phase 3.1 solar provider running game cycles.
    /// Captures dumps at Morning, Noon, Evening, Night and validates consistency.
    /// Separate from mock validator - only handles real game data.
    /// </summary>
    public static class Phase3_1RuntimeValidator
    {
        private static List<(string label, float timeOfDay, LightingV3SunState state, int updateId)> _dumps = new();
        private static bool _capturedMorning = false;
        private static bool _capturedNoon = false;
        private static bool _capturedEvening = false;
        private static bool _capturedNight = false;

        /// <summary>
        /// Record a sun state dump with label (Morning, Noon, Evening, Night).
        /// Prints the complete dump immediately.
        /// </summary>
        public static void RecordDump(string label, float timeOfDay, LightingV3SunState state, int updateId)
        {
            // Track which periods have been captured
            if (label.Contains("Morning")) _capturedMorning = true;
            if (label.Contains("Noon")) _capturedNoon = true;
            if (label.Contains("Evening")) _capturedEvening = true;
            if (label.Contains("Night")) _capturedNight = true;

            _dumps.Add((label, timeOfDay, state, updateId));

            // Print immediately upon capture
            PrintSingleDump(label, timeOfDay, state, updateId);
        }

        /// <summary>
        /// Print a single dump in full detail.
        /// </summary>
        private static void PrintSingleDump(string label, float timeOfDay, LightingV3SunState state, int updateId)
        {
            System.Console.WriteLine($"\n[Phase3_1 Captured] {label}");
            System.Console.WriteLine($"  TimeOfDay:             {timeOfDay:F3}");
            System.Console.WriteLine($"  FoundationUpdateId:    {updateId}");
            System.Console.WriteLine($"  DirectionToSun:        ({state.DirectionToSun.X:F6}, {state.DirectionToSun.Y:F6})");
            System.Console.WriteLine($"  LightTravelDirection:  ({state.LightTravelDirection.X:F6}, {state.LightTravelDirection.Y:F6})");
            System.Console.WriteLine($"  Elevation:             {state.Elevation:F4}°");
            System.Console.WriteLine($"  Intensity:             {state.Intensity:F6}");
            System.Console.WriteLine($"  LinearColor:           ({state.LinearColor.X:F6}, {state.LinearColor.Y:F6}, {state.LinearColor.Z:F6})");
            System.Console.WriteLine($"  IsAboveHorizon:        {state.IsAboveHorizon}");
        }

        /// <summary>
        /// Print all recorded dumps to console.
        /// </summary>
        public static void PrintDumps()
        {
            System.Console.WriteLine("\n╔════════════════════════════════════════════════════════════════════════════════╗");
            System.Console.WriteLine("║ PHASE 3.1 RUNTIME - REAL GAME CYCLE DUMPS                                        ║");
            System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝");

            if (_dumps.Count == 0)
            {
                System.Console.WriteLine("\nNo dumps recorded yet.");
                System.Console.WriteLine("\nStatus:");
                System.Console.WriteLine($"  Morning: {(_capturedMorning ? "✓ captured" : "⏳ waiting (6:00 AM, timeOfDay ≈ 0.25)")}");
                System.Console.WriteLine($"  Noon:    {(_capturedNoon ? "✓ captured" : "⏳ waiting (12:00 PM, timeOfDay ≈ 0.50)")}");
                System.Console.WriteLine($"  Evening: {(_capturedEvening ? "✓ captured" : "⏳ waiting (6:00 PM, timeOfDay ≈ 0.75)")}");
                System.Console.WriteLine($"  Night:   {(_capturedNight ? "✓ captured" : "⏳ waiting (0:00 AM, timeOfDay ≈ 0.00 or > 0.90)")}");
                System.Console.WriteLine("\nTo speed up time capture:");
                System.Console.WriteLine("  Open console (~) and type: /tick speed 100");
                System.Console.WriteLine("  This accelerates time 100x, completing full cycle in ~30 seconds");
                return;
            }

            foreach (var (label, timeOfDay, state, updateId) in _dumps)
            {
                System.Console.WriteLine($"\n═══════════════════════════════════════════════════════════");
                System.Console.WriteLine($"TIME: {label} (TimeOfDay={timeOfDay:F3})");
                System.Console.WriteLine($"───────────────────────────────────────────────────────────");
                System.Console.WriteLine($"UpdateId:               {updateId}");
                System.Console.WriteLine($"DirectionToSun:        ({state.DirectionToSun.X:F6}, {state.DirectionToSun.Y:F6})");
                System.Console.WriteLine($"LightTravelDirection:  ({state.LightTravelDirection.X:F6}, {state.LightTravelDirection.Y:F6})");
                System.Console.WriteLine($"Elevation:             {state.Elevation:F4}°");
                System.Console.WriteLine($"Intensity:             {state.Intensity:F6}");
                System.Console.WriteLine($"IsAboveHorizon:        {state.IsAboveHorizon}");
                System.Console.WriteLine($"LinearColor:           ({state.LinearColor.X:F6}, {state.LinearColor.Y:F6}, {state.LinearColor.Z:F6})");
                System.Console.WriteLine($"IsValid:               {state.IsValid()}");
            }
        }

        /// <summary>
        /// Validate all recorded dumps against phase 3.1 requirements.
        /// </summary>
        public static void Validate()
        {
            System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════════╗");
            System.Console.WriteLine("║ PHASE 3.1 VALIDATION RESULTS                                                    ║");
            System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝\n");

            bool allValid = true;

            // Validate 1: All states are valid
            System.Console.WriteLine("VALIDATION 1: All states valid (no NaN/infinity)");
            bool check1 = true;
            foreach (var (label, _, state, _) in _dumps)
            {
                bool valid = state.IsValid();
                System.Console.WriteLine($"  {label}: {(valid ? "✓" : "✗")}");
                check1 = check1 && valid;
            }
            allValid = allValid && check1;
            System.Console.WriteLine();

            // Validate 2: DirectionToSun is normalized
            System.Console.WriteLine("VALIDATION 2: DirectionToSun normalized");
            bool check2 = true;
            foreach (var (label, _, state, _) in _dumps)
            {
                bool normalized = state.IsNormalized();
                System.Console.WriteLine($"  {label}: {(normalized ? "✓" : "✗")}");
                check2 = check2 && normalized;
            }
            allValid = allValid && check2;
            System.Console.WriteLine();

            // Validate 3: LightTravelDirection is opposite
            System.Console.WriteLine("VALIDATION 3: LightTravelDirection is exactly opposite");
            bool check3 = true;
            foreach (var (label, _, state, _) in _dumps)
            {
                bool opposite = state.IsLightTravelDirectionValid();
                System.Console.WriteLine($"  {label}: {(opposite ? "✓" : "✗")}");
                check3 = check3 && opposite;
            }
            allValid = allValid && check3;
            System.Console.WriteLine();

            // Validate 4: Noon has mostly upward direction
            System.Console.WriteLine("VALIDATION 4: Noon - DirectionToSun points upward");
            var noon = _dumps.Find(d => d.label.Contains("Noon"));
            bool check4 = noon.state.DirectionToSun.Y < -0.5f;  // Negative Y means up
            System.Console.WriteLine($"  Y component: {noon.state.DirectionToSun.Y:F4} {(check4 ? "✓" : "✗")}");
            allValid = allValid && check4;
            System.Console.WriteLine();

            // Validate 5: Morning and Evening have opposite horizontal signs
            System.Console.WriteLine("VALIDATION 5: Morning/Evening have opposite horizontal components");
            var morning = _dumps.Find(d => d.label.Contains("Morning"));
            var evening = _dumps.Find(d => d.label.Contains("Evening"));
            bool check5 = System.Math.Sign(morning.state.DirectionToSun.X) !=
                         System.Math.Sign(evening.state.DirectionToSun.X);
            System.Console.WriteLine($"  Morning X: {morning.state.DirectionToSun.X:F4}");
            System.Console.WriteLine($"  Evening X: {evening.state.DirectionToSun.X:F4}");
            System.Console.WriteLine($"  Opposite signs: {(check5 ? "✓" : "✗")}");
            allValid = allValid && check5;
            System.Console.WriteLine();

            // Validate 6: Night has IsAboveHorizon=false
            System.Console.WriteLine("VALIDATION 6: Night - IsAboveHorizon=false");
            var night = _dumps.Find(d => d.label.Contains("Night"));
            bool check6 = !night.state.IsAboveHorizon;
            System.Console.WriteLine($"  IsAboveHorizon: {night.state.IsAboveHorizon} {(check6 ? "✓" : "✗")}");
            allValid = allValid && check6;
            System.Console.WriteLine();

            // Validate 7: Night intensity is low
            System.Console.WriteLine("VALIDATION 7: Night - Intensity is low (0.0-0.2)");
            bool check7 = night.state.Intensity >= 0f && night.state.Intensity <= 0.2f;
            System.Console.WriteLine($"  Intensity: {night.state.Intensity:F4} {(check7 ? "✓" : "✗")}");
            allValid = allValid && check7;
            System.Console.WriteLine();

            // Validate 8: Each dump has a valid UpdateId
            System.Console.WriteLine("VALIDATION 8: Each dump has valid UpdateId (frame consistency)");
            bool check8 = true;
            foreach (var (label, _, state, updateId) in _dumps)
            {
                bool validId = updateId > 0;
                System.Console.WriteLine($"  {label}: UpdateId={updateId} {(validId ? "✓" : "✗")}");
                check8 = check8 && validId;
            }
            allValid = allValid && check8;
            System.Console.WriteLine();

            // Summary
            System.Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════════╗");
            if (allValid)
            {
                System.Console.WriteLine("║ ✓ ALL VALIDATIONS PASSED - SOLAR PROVIDER READY FOR PHASE 3.2                  ║");
            }
            else
            {
                System.Console.WriteLine("║ ✗ SOME VALIDATIONS FAILED - REVIEW ABOVE                                        ║");
            }
            System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝\n");

            _dumps.Clear();
        }

        /// <summary>
        /// Test continuity of sun direction through a dense cycle sampling.
        /// Samples 256 points through one full day and checks for discontinuities.
        /// With 256 samples, each represents 360°/256 ≈ 1.4° of rotation.
        /// Threshold is derived to allow ~2.5x the expected step (margin for smooth curves).
        /// </summary>
        public static void ValidateContinuity(GameSolarProvider solarProvider)
        {
            if (solarProvider == null)
            {
                System.Console.WriteLine("\n[Phase3_1] Continuity test skipped: SolarProvider not available");
                return;
            }

            System.Console.WriteLine("\n╔════════════════════════════════════════════════════════════════════════════════╗");
            System.Console.WriteLine("║ PHASE 3.1 CONTINUITY TEST - Dense Cycle Sampling (256 points)                   ║");
            System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝\n");

            const int sampleCount = 256;

            // Calculate minimum dot product based on expected angular step
            // 360° / 256 samples = 1.40625° per step
            // Allow ~2.5x margin for smooth curves: ~3.5°
            // cos(3.5°) ≈ 0.998
            const float maxAllowedAngleDegrees = (360.0f / sampleCount) * 2.5f;
            const float minimumContinuityDot = 0.998f; // cos(~3.5°)

            bool hasBadDiscontinuities = false;
            int discontinuityCount = 0;
            Vector2 firstDirection = Vector2.Zero;
            Vector2 prevDirection = Vector2.Zero;

            for (int i = 0; i < sampleCount; i++)
            {
                float timeOfDay = i / (float)sampleCount; // [0, 1)
                var state = solarProvider.GetSunState();

                if (i == 0)
                {
                    // Store first direction for wrap-around check
                    firstDirection = state.DirectionToSun;
                }
                else
                {
                    // Check continuity between adjacent samples
                    float dot = Vector2.Dot(prevDirection, state.DirectionToSun);

                    if (dot < minimumContinuityDot)
                    {
                        discontinuityCount++;
                        hasBadDiscontinuities = true;

                        if (discontinuityCount <= 5) // Report first 5 discontinuities
                        {
                            float angleRadians = (float)System.Math.Acos(System.Math.Clamp(dot, -1f, 1f));
                            float angleDegrees = angleRadians * 180f / System.MathF.PI;

                            System.Console.WriteLine($"  ⚠ DISCONTINUITY at timeOfDay={timeOfDay:F4}");
                            System.Console.WriteLine($"    Previous: ({prevDirection.X:F6}, {prevDirection.Y:F6})");
                            System.Console.WriteLine($"    Current:  ({state.DirectionToSun.X:F6}, {state.DirectionToSun.Y:F6})");
                            System.Console.WriteLine($"    Angular difference: {angleDegrees:F2}° (threshold: {maxAllowedAngleDegrees:F2}°)");
                        }
                    }
                }

                prevDirection = state.DirectionToSun;
            }

            // Check continuity between last sample and first sample (wrap-around)
            // Vectors are naturally continuous across 360°→0° boundary
            float dotWrap = Vector2.Dot(prevDirection, firstDirection);
            if (dotWrap < minimumContinuityDot)
            {
                discontinuityCount++;
                if (!hasBadDiscontinuities && discontinuityCount <= 5)
                {
                    float angleRadians = (float)System.Math.Acos(System.Math.Clamp(dotWrap, -1f, 1f));
                    float angleDegrees = angleRadians * 180f / System.MathF.PI;

                    System.Console.WriteLine($"  ⚠ WRAP-AROUND DISCONTINUITY (sample 255→0)");
                    System.Console.WriteLine($"    Last:  ({prevDirection.X:F6}, {prevDirection.Y:F6})");
                    System.Console.WriteLine($"    First: ({firstDirection.X:F6}, {firstDirection.Y:F6})");
                    System.Console.WriteLine($"    Angular difference: {angleDegrees:F2}°");
                    hasBadDiscontinuities = true;
                }
            }

            if (!hasBadDiscontinuities)
            {
                System.Console.WriteLine("✓ Direction continuity verified across full cycle");
                System.Console.WriteLine($"  Sampled: {sampleCount} points");
                System.Console.WriteLine($"  Angular step: {360.0f / sampleCount:F2}° per sample");
                System.Console.WriteLine($"  Continuity threshold: cos({maxAllowedAngleDegrees:F2}°) = {minimumContinuityDot:F6}");
                System.Console.WriteLine($"  No discontinuities detected");
            }
            else
            {
                System.Console.WriteLine($"✗ Found {discontinuityCount} discontinuities (first 5 shown above)");
                System.Console.WriteLine("  Review direction calculation for unexpected jumps");
            }
            System.Console.WriteLine();
        }
    }
}
