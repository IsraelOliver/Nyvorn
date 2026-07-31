using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Automatic capture of sun state dumps at Morning, Noon, Evening, Night.
    /// Maintains two separate cycle collections:
    /// - currentCycleDumps: building up during current cycle
    /// - lastCompletedCycleDumps: validated results from previous cycle
    /// Uses mathematical marker crossing detection (no windows).
    /// MUST be called from PlayingState.Update(), not Draw().
    /// </summary>
    public static class Phase3_1RuntimeDumpCapture
    {
        // Diagnostics disabled: zero overhead (no allocations, no collections, no checks)
        // Diagnostics enabled: may allocate for capture, strings, collections, logging

        private static List<(string label, float timeOfDay, LightingV3SunState state, int updateId)> _currentCycleDumps;
        private static bool _capturedMorning = false;
        private static bool _capturedNoon = false;
        private static bool _capturedEvening = false;
        private static bool _capturedNight = false;

        private static List<(string label, float timeOfDay, LightingV3SunState state, int updateId)> _lastCompletedCycleDumps = null;

        private static bool _isConnected = false;
        private static float _lastTimeOfDay = 0f;
        private static long _lastClockLogTicks = 0;

        public static void Update(
            float timeOfDay01,
            LightingV3SunState sunState,
            int updateId,
            GameSolarProvider solarProvider)
        {
            // When diagnostics disabled: zero overhead, immediate return
            if (!LightingV3Diagnostics.EnablePhase31RuntimeValidation)
                return;

            if (solarProvider == null)
                return;

            // Lazy initialize collections (only when diagnostics enabled)
            if (_currentCycleDumps == null)
                _currentCycleDumps = new List<(string, float, LightingV3SunState, int)>();

            // Log connection once
            if (!_isConnected)
            {
                _isConnected = true;
                System.Console.WriteLine("[Phase3_1Capture] Runtime capture update connected");
            }

            // Log clock state once per second (diagnostic only)
            long nowTicks = System.DateTime.UtcNow.Ticks;
            if ((nowTicks - _lastClockLogTicks) / 10_000_000.0 >= 1.0)
            {
                _lastClockLogTicks = nowTicks;
                float delta = timeOfDay01 - _lastTimeOfDay;
                if (delta < -0.5f) delta += 1.0f;
                System.Console.WriteLine($"[Phase3_1Clock] PreviousTimeOfDay={_lastTimeOfDay:F4} CurrentTimeOfDay={timeOfDay01:F4} " +
                    $"Delta={delta:F6} UpdateId={updateId}");
            }

            // Detect period crossings using mathematical method
            CheckPeriodCrossing(timeOfDay01, sunState, updateId, "Morning", 0.25f);
            CheckPeriodCrossing(timeOfDay01, sunState, updateId, "Noon", 0.50f);
            CheckPeriodCrossing(timeOfDay01, sunState, updateId, "Evening", 0.75f);
            CheckPeriodCrossing(timeOfDay01, sunState, updateId, "Night", 0.00f);

            _lastTimeOfDay = timeOfDay01;

            // Auto-complete cycle when all 4 captured
            if (_capturedMorning && _capturedNoon && _capturedEvening && _capturedNight)
            {
                System.Console.WriteLine("\n[Phase3_1Capture] All 4 periods captured automatically during gameplay!\n");

                // Copy current dumps to completed, by value
                _lastCompletedCycleDumps = new List<(string, float, LightingV3SunState, int)>(_currentCycleDumps);

                // Validate and print completed cycle
                Phase3_1RuntimeValidator.ValidateCompletedCycle(_lastCompletedCycleDumps);

                // Reset for next cycle
                _currentCycleDumps.Clear();
                _capturedMorning = false;
                _capturedNoon = false;
                _capturedEvening = false;
                _capturedNight = false;
            }
        }

        /// <summary>
        /// Mathematically detect if we crossed a marker going from previousTime to currentTime.
        /// Handles wrapping (0.99 → 0.01) correctly.
        /// </summary>
        private static bool CrossedMarker(float previousTime, float currentTime, float marker)
        {
            // No wrap: simple check
            if (currentTime >= previousTime)
                return marker > previousTime && marker <= currentTime;

            // Wrapped: marker is crossed if before current OR after previous
            return marker > previousTime || marker <= currentTime;
        }

        /// <summary>
        /// Check if we crossed a period marker and capture it once.
        /// </summary>
        private static void CheckPeriodCrossing(float currentTime, LightingV3SunState sunState, int updateId, string label, float marker)
        {
            bool alreadyCaptured = GetCaptureFlag(label);
            if (alreadyCaptured)
                return;

            if (CrossedMarker(_lastTimeOfDay, currentTime, marker))
            {
                SetCaptureFlag(label, true);
                _currentCycleDumps.Add((label, currentTime, sunState, updateId));
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

        /// <summary>
        /// Get dumps to display: prefer lastCompleted if available, fallback to current.
        /// </summary>
        public static List<(string label, float timeOfDay, LightingV3SunState state, int updateId)> GetDumpsForDisplay()
        {
            return _lastCompletedCycleDumps ?? _currentCycleDumps;
        }

        public static bool HasCompletedCycle => _lastCompletedCycleDumps != null && _lastCompletedCycleDumps.Count == 4;

        public static int CurrentCycleProgress => _currentCycleDumps.Count;
    }
}
