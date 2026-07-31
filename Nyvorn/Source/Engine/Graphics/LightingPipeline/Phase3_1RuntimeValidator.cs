using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Runtime validator for Phase 3.1 solar provider.
    /// Captures dumps at specific times of day and validates consistency.
    /// </summary>
    public static class Phase3_1RuntimeValidator
    {
        private static List<(string label, float timeOfDay, LightingV3SunState state, int updateId)> _dumps = new();

        /// <summary>
        /// Record a sun state dump with label (Morning, Noon, Evening, Night).
        /// </summary>
        public static void RecordDump(string label, float timeOfDay, LightingV3SunState state, int updateId)
        {
            _dumps.Add((label, timeOfDay, state, updateId));
        }

        /// <summary>
        /// Print all recorded dumps to console in validation format.
        /// </summary>
        public static void PrintDumps()
        {
            System.Console.WriteLine("\n╔════════════════════════════════════════════════════════════════════════════════╗");
            System.Console.WriteLine("║ PHASE 3.1 RUNTIME VALIDATION - SOLAR PROVIDER STATE DUMPS                        ║");
            System.Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝\n");

            foreach (var (label, timeOfDay, state, updateId) in _dumps)
            {
                System.Console.WriteLine($"═══════════════════════════════════════════════════════════");
                System.Console.WriteLine($"TIME: {label} (TimeOfDay={timeOfDay:F3})");
                System.Console.WriteLine($"───────────────────────────────────────────────────────────");
                System.Console.WriteLine($"UpdateId:               {updateId}");
                System.Console.WriteLine($"DirectionToSun:        ({state.DirectionToSun.X:F4}, {state.DirectionToSun.Y:F4})");
                System.Console.WriteLine($"LightTravelDirection:  ({state.LightTravelDirection.X:F4}, {state.LightTravelDirection.Y:F4})");
                System.Console.WriteLine($"Elevation:             {state.Elevation:F2}°");
                System.Console.WriteLine($"Intensity:             {state.Intensity:F4}");
                System.Console.WriteLine($"IsAboveHorizon:        {state.IsAboveHorizon}");
                System.Console.WriteLine($"LinearColor:           ({state.LinearColor.X:F4}, {state.LinearColor.Y:F4}, {state.LinearColor.Z:F4})");
                System.Console.WriteLine($"IsValid:               {state.IsValid()}");
                System.Console.WriteLine();
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
    }
}
