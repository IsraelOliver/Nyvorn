using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Automatic capture of sun state dumps at Morning, Noon, Evening, Night.
    /// Ensures each period is captured only once per cycle.
    /// Resets when cycle restarts (detects backward jump in timeOfDay).
    /// </summary>
    public static class Phase3_1RuntimeDumpCapture
    {
        private static bool _capturedMorning = false;
        private static bool _capturedNoon = false;
        private static bool _capturedEvening = false;
        private static bool _capturedNight = false;
        private static float _lastTimeOfDay = 0f;

        public static void CheckAndCapture(
            float timeOfDay01,
            LightingV3SunState sunState,
            int updateId,
            GameSolarProvider solarProvider)
        {
            if (solarProvider == null)
                return;

            // Detect cycle restart (timeOfDay went backward)
            if (timeOfDay01 < _lastTimeOfDay - 0.1f)
            {
                // Reset for new cycle
                _capturedMorning = false;
                _capturedNoon = false;
                _capturedEvening = false;
                _capturedNight = false;
            }
            _lastTimeOfDay = timeOfDay01;

            // Morning (~6:00 AM) - timeOfDay01 ≈ 0.25
            if (!_capturedMorning && timeOfDay01 > 0.23f && timeOfDay01 < 0.27f)
            {
                _capturedMorning = true;
                Phase3_1RuntimeValidator.RecordDump("Morning", timeOfDay01, sunState, updateId);
                System.Console.WriteLine($"[Phase3_1] Captured Morning (timeOfDay={timeOfDay01:F3}, updateId={updateId})");
            }

            // Noon (~12:00 PM) - timeOfDay01 ≈ 0.5
            if (!_capturedNoon && timeOfDay01 > 0.48f && timeOfDay01 < 0.52f)
            {
                _capturedNoon = true;
                Phase3_1RuntimeValidator.RecordDump("Noon", timeOfDay01, sunState, updateId);
                System.Console.WriteLine($"[Phase3_1] Captured Noon (timeOfDay={timeOfDay01:F3}, updateId={updateId})");
            }

            // Evening (~6:00 PM) - timeOfDay01 ≈ 0.75
            if (!_capturedEvening && timeOfDay01 > 0.73f && timeOfDay01 < 0.77f)
            {
                _capturedEvening = true;
                Phase3_1RuntimeValidator.RecordDump("Evening", timeOfDay01, sunState, updateId);
                System.Console.WriteLine($"[Phase3_1] Captured Evening (timeOfDay={timeOfDay01:F3}, updateId={updateId})");
            }

            // Night (~0:00 AM) - timeOfDay01 ≈ 0.0 or 1.0
            if (!_capturedNight && (timeOfDay01 < 0.1f || timeOfDay01 > 0.9f))
            {
                _capturedNight = true;
                Phase3_1RuntimeValidator.RecordDump("Night", timeOfDay01, sunState, updateId);
                System.Console.WriteLine($"[Phase3_1] Captured Night (timeOfDay={timeOfDay01:F3}, updateId={updateId})");
            }

            // Print and validate if all 4 periods captured
            if (_capturedMorning && _capturedNoon && _capturedEvening && _capturedNight)
            {
                System.Console.WriteLine("\n[Phase3_1] All 4 periods captured! Running validation...\n");
                Phase3_1RuntimeValidator.PrintDumps();
                Phase3_1RuntimeValidator.Validate();

                // Reset for next cycle
                _capturedMorning = false;
                _capturedNoon = false;
                _capturedEvening = false;
                _capturedNight = false;
            }
        }

        public static bool AllCaptured => _capturedMorning && _capturedNoon && _capturedEvening && _capturedNight;
    }
}
