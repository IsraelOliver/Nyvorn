using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Automatic capture of sun state dumps at Morning, Noon, Evening, Night.
    /// Ensures each period is captured only once per cycle.
    /// Detects period crossing with proper wrapping (0.99 → 0.01).
    /// MUST be called from PlayingState.Update(), not Draw().
    /// </summary>
    public static class Phase3_1RuntimeDumpCapture
    {
        private static bool _isConnected = false;
        private static bool _capturedMorning = false;
        private static bool _capturedNoon = false;
        private static bool _capturedEvening = false;
        private static bool _capturedNight = false;
        private static float _lastTimeOfDay = 0f;
        private static long _lastClockLogTicks = 0;

        public static void Update(
            float timeOfDay01,
            LightingV3SunState sunState,
            int updateId,
            GameSolarProvider solarProvider)
        {
            if (solarProvider == null)
                return;

            // Log connection once
            if (!_isConnected)
            {
                _isConnected = true;
                System.Console.WriteLine("[Phase3_1Capture] Runtime capture update connected");
            }

            // Log clock state once per second (not every frame)
            long nowTicks = System.DateTime.UtcNow.Ticks;
            if ((nowTicks - _lastClockLogTicks) / 10_000_000.0 >= 1.0) // Ticks to seconds
            {
                _lastClockLogTicks = nowTicks;
                float delta = timeOfDay01 - _lastTimeOfDay;
                if (delta < -0.5f) delta += 1.0f; // Wrap correction
                System.Console.WriteLine($"[Phase3_1Clock] PreviousTimeOfDay={_lastTimeOfDay:F4} CurrentTimeOfDay={timeOfDay01:F4} " +
                    $"Delta={delta:F6} UpdateId={updateId}");
            }

            // Detect period crossings with wrapping support
            CheckPeriodCrossing(timeOfDay01, sunState, updateId, "Morning", 0.25f);
            CheckPeriodCrossing(timeOfDay01, sunState, updateId, "Noon", 0.50f);
            CheckPeriodCrossing(timeOfDay01, sunState, updateId, "Evening", 0.75f);
            CheckPeriodCrossing(timeOfDay01, sunState, updateId, "Night", 0.00f);

            _lastTimeOfDay = timeOfDay01;

            // Auto-validate when all 4 captured
            if (_capturedMorning && _capturedNoon && _capturedEvening && _capturedNight)
            {
                System.Console.WriteLine("\n[Phase3_1Capture] All 4 periods captured automatically during gameplay!\n");
                Phase3_1RuntimeValidator.PrintDumps();
                Phase3_1RuntimeValidator.Validate();

                // Reset for next cycle
                _capturedMorning = false;
                _capturedNoon = false;
                _capturedEvening = false;
                _capturedNight = false;
            }
        }

        /// <summary>
        /// Check if we crossed a period marker (Morning, Noon, Evening, Night).
        /// Handles wrapping: 0.99 → 0.01 crosses 0.00 (Night).
        /// </summary>
        private static void CheckPeriodCrossing(float currentTime, LightingV3SunState sunState, int updateId, string label, float targetTime)
        {
            bool shouldCapture = false;
            bool alreadyCaptured = GetCaptureFlag(label);

            if (alreadyCaptured)
                return;

            // Window around target (±0.02)
            float windowStart = targetTime - 0.02f;
            float windowEnd = targetTime + 0.02f;

            // Check for crossing with wrapping
            if (targetTime == 0.00f)
            {
                // Night: crosses at 0.00, wrapping from 0.99 to 0.01
                shouldCapture = ((_lastTimeOfDay > 0.98f && currentTime < 0.05f) ||  // Wrapped
                                (currentTime >= windowStart && currentTime <= windowEnd));  // Direct
            }
            else
            {
                // Other periods: direct check
                shouldCapture = (currentTime >= windowStart && currentTime <= windowEnd);
            }

            if (shouldCapture)
            {
                SetCaptureFlag(label, true);
                Phase3_1RuntimeValidator.RecordDump(label, currentTime, sunState, updateId);
                System.Console.WriteLine($"[Phase3_1Capture] Captured {label} (timeOfDay={currentTime:F4}, updateId={updateId})");
            }
        }

        private static bool GetCaptureFlag(string label)
        {
            return label switch
            {
                "Morning" => _capturedMorning,
                "Noon" => _capturedNoon,
                "Evening" => _capturedEvening,
                "Night" => _capturedNight,
                _ => false
            };
        }

        private static void SetCaptureFlag(string label, bool value)
        {
            switch (label)
            {
                case "Morning": _capturedMorning = value; break;
                case "Noon": _capturedNoon = value; break;
                case "Evening": _capturedEvening = value; break;
                case "Night": _capturedNight = value; break;
            }
        }

        public static bool AllCaptured => _capturedMorning && _capturedNoon && _capturedEvening && _capturedNight;
    }
}
