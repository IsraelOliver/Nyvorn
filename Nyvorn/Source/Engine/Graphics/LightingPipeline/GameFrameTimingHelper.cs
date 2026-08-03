using System;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Measures wall-clock frame intervals at Game level.
    /// Must be called at the very start of each frame (before PlayingState.Update).
    /// This ensures measurement includes Update, Draw, Present, VSync, GPU waits, scheduler waits.
    /// </summary>
    public class GameFrameTimingHelper
    {
        private long _lastFrameStartTicks = 0;
        private double _lastFrameIntervalMs = 0.0;
        private bool _isFirstCall = true;

        /// <summary>
        /// Call this at the very start of each game frame (before Update).
        /// Returns the interval since the previous call (0.0 for first call).
        /// </summary>
        public double MeasureFrameIntervalAndStart()
        {
            long now = Stopwatch.GetTimestamp();

            if (_isFirstCall)
            {
                _isFirstCall = false;
                _lastFrameStartTicks = now;
                return 0.0;  // First frame has no valid interval
            }

            double interval = TicksToMs(now - _lastFrameStartTicks);
            _lastFrameIntervalMs = interval;
            _lastFrameStartTicks = now;
            return interval;
        }

        private static double TicksToMs(long ticks)
        {
            return (ticks / (double)Stopwatch.Frequency) * 1000.0;
        }

        public double LastFrameIntervalMs => _lastFrameIntervalMs;
    }
}
