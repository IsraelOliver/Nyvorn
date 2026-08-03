using System;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Profiles RENDERED FRAMES, not Update calls.
    ///
    /// MonoGame fixed timestep allows multiple Update() without Draw() when IsRunningSlowly.
    /// This profiler measures at BeginDraw/Draw/EndDraw level.
    ///
    /// Key distinction:
    /// - Update calls can occur multiple times per rendered frame
    /// - Only BeginDraw->EndDraw represents a rendered frame
    /// - WallClockFrameInterval is between successive BeginDraw calls
    /// </summary>
    public class RenderedFrameProfiler
    {
        public enum ProfileState
        {
            Inactive,
            ValidationPass,    // 10 warmup + 30 rendered frames per mode
            SmokeTest,         // 30 warmup + 120 rendered frames per mode
            BaselineWarmup,    // 300 rendered frames discarded (system stabilization)
            BaselineCapture    // 600 rendered frames measured
        }

        public enum LightingMode
        {
            Legacy,
            V3Mode_None,
            V3Mode_SunVisibility
        }

        // Rendered frame counts (NOT update counts)
        private const int ValidationWarmupFrames = 10;
        private const int ValidationMeasuredFrames = 30;

        private const int SmokeTestWarmupFrames = 30;
        private const int SmokeTestMeasuredFrames = 120;

        private const int BaselineWarmupFrames = 300;
        private const int MaxFramesPerRun = 600;

        // Watchdog timeout (2 seconds without a rendered frame = failure)
        private const double WatchdogTimeoutSeconds = 2.0;

        // State
        private ProfileState _state = ProfileState.Inactive;
        private LightingMode _currentMode = LightingMode.Legacy;
        private long _lastBeginDrawTicks = 0;
        private int _warmupFramesRemaining = 0;
        private int _measuredFramesRemaining = 0;
        private bool _isFirstFrame = true;

        // Per-rendered-frame accumulation (since last BeginDraw)
        private long _frameStartTicksBeginDraw;
        private long _updateStartTicks;
        private long _drawStartTicks;
        private int _updateCallsThisFrame = 0;
        private double _updateCpuMsThisFrame = 0.0;
        private bool _isRunningSlowlyThisFrame = false;
        private int _sunVisibilityBuildCallsThisFrame = 0;

        // Validation
        private int _configuredResolutionWidth = 0;
        private int _configuredResolutionHeight = 0;
        private float _configuredZoom = 0;
        private int _configuredActiveRegionWidth = 0;
        private int _configuredActiveRegionHeight = 0;
        private int _configuredSampleCount = 0;
        private bool _configurationLocked = false;

        public bool MeasurementValid { get; private set; } = true;
        public InvalidReasonEnum InvalidReason { get; private set; } = InvalidReasonEnum.None;

        public enum InvalidReasonEnum
        {
            None = 0,
            ModeChanged = 1,
            ResolutionChanged = 2,
            ZoomChanged = 3,
            ActiveRegionSizeChanged = 4,
            SampleCountChanged = 5,
            NoRenderedFrameTimeout = 6,
            DrawSkipped = 7,
            UnknownFailure = 99
        }

        // Circular buffers
        private RenderedFrameMetrics[] _frameMetrics = new RenderedFrameMetrics[MaxFramesPerRun];
        private RenderedFrameCallCounts[] _frameCalls = new RenderedFrameCallCounts[MaxFramesPerRun];

        // Aggregate watchdog
        private long _lastDrawTicks = 0;
        private int _drawSkippedCount = 0;
        private int _isRunningSlowlyCount = 0;

        public RenderedFrameProfiler()
        {
            for (int i = 0; i < MaxFramesPerRun; i++)
            {
                _frameMetrics[i] = new RenderedFrameMetrics();
                _frameCalls[i] = new RenderedFrameCallCounts();
            }
        }

        public void StartValidationPass(
            LightingMode initialMode,
            int resolutionWidth,
            int resolutionHeight,
            float zoom,
            int activeRegionWidth,
            int activeRegionHeight,
            int sampleCount)
        {
            _state = ProfileState.ValidationPass;
            _currentMode = initialMode;
            _warmupFramesRemaining = ValidationWarmupFrames;
            _measuredFramesRemaining = ValidationMeasuredFrames;
            _isFirstFrame = true;
            _drawSkippedCount = 0;
            _isRunningSlowlyCount = 0;
            MeasurementValid = true;
            InvalidReason = InvalidReasonEnum.None;

            LockConfiguration(resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
        }

        public void StartSmokeTest(
            LightingMode initialMode,
            int resolutionWidth,
            int resolutionHeight,
            float zoom,
            int activeRegionWidth,
            int activeRegionHeight,
            int sampleCount)
        {
            _state = ProfileState.SmokeTest;
            _currentMode = initialMode;
            _warmupFramesRemaining = SmokeTestWarmupFrames;
            _measuredFramesRemaining = SmokeTestMeasuredFrames;
            _isFirstFrame = true;
            _drawSkippedCount = 0;
            _isRunningSlowlyCount = 0;
            MeasurementValid = true;
            InvalidReason = InvalidReasonEnum.None;

            LockConfiguration(resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
        }

        public void StartBaseline(
            LightingMode mode,
            int resolutionWidth,
            int resolutionHeight,
            float zoom,
            int activeRegionWidth,
            int activeRegionHeight,
            int sampleCount)
        {
            _state = ProfileState.BaselineWarmup;
            _currentMode = mode;
            _warmupFramesRemaining = BaselineWarmupFrames;
            _measuredFramesRemaining = MaxFramesPerRun;
            _isFirstFrame = true;
            _drawSkippedCount = 0;
            _isRunningSlowlyCount = 0;
            MeasurementValid = true;
            InvalidReason = InvalidReasonEnum.None;

            LockConfiguration(resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
        }

        private void LockConfiguration(int resolutionWidth, int resolutionHeight, float zoom,
            int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            _configuredResolutionWidth = resolutionWidth;
            _configuredResolutionHeight = resolutionHeight;
            _configuredZoom = zoom;
            _configuredActiveRegionWidth = activeRegionWidth;
            _configuredActiveRegionHeight = activeRegionHeight;
            _configuredSampleCount = sampleCount;
            _configurationLocked = true;
        }

        public void ValidateConfiguration(int resolutionWidth, int resolutionHeight, float zoom,
            int activeRegionWidth, int activeRegionHeight, int sampleCount, LightingMode currentMode)
        {
            if (!_configurationLocked || _state == ProfileState.Inactive)
                return;

            if (currentMode != _currentMode)
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.ModeChanged;
                _state = ProfileState.Inactive;
                return;
            }

            if (resolutionWidth != _configuredResolutionWidth || resolutionHeight != _configuredResolutionHeight)
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.ResolutionChanged;
                _state = ProfileState.Inactive;
                return;
            }

            if (Math.Abs(zoom - _configuredZoom) > 0.0001f)
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.ZoomChanged;
                _state = ProfileState.Inactive;
                return;
            }

            if (activeRegionWidth != _configuredActiveRegionWidth || activeRegionHeight != _configuredActiveRegionHeight)
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.ActiveRegionSizeChanged;
                _state = ProfileState.Inactive;
                return;
            }

            if (sampleCount != _configuredSampleCount)
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.SampleCountChanged;
                _state = ProfileState.Inactive;
                return;
            }
        }

        // Called from Game.Update() - accumulates update calls and time
        public void OnUpdateStart()
        {
            if (_state == ProfileState.Inactive)
                return;

            _updateStartTicks = Stopwatch.GetTimestamp();
            _updateCallsThisFrame++;
        }

        public void OnUpdateEnd()
        {
            if (_state == ProfileState.Inactive)
                return;

            long ticks = Stopwatch.GetTimestamp();
            _updateCpuMsThisFrame += TicksToMs(ticks - _updateStartTicks);
        }

        public void OnUpdateSetIsRunningSlowly(bool isRunningSlowly)
        {
            if (_state == ProfileState.Inactive)
                return;

            if (isRunningSlowly)
                _isRunningSlowlyThisFrame = true;
        }

        // Called from LightingV3Foundation when SunVisibility is rebuilt
        public void OnSunVisibilityBuild()
        {
            if (_state == ProfileState.Inactive)
                return;

            _sunVisibilityBuildCallsThisFrame++;
        }

        // Called from Game.BeginDraw() - marks rendered frame boundary
        public bool OnBeginDraw()
        {
            if (_state == ProfileState.Inactive)
                return true;

            long now = Stopwatch.GetTimestamp();

            // First frame: skip interval calculation
            if (_isFirstFrame)
            {
                _isFirstFrame = false;
                _lastBeginDrawTicks = now;
                _frameStartTicksBeginDraw = now;
                _updateCallsThisFrame = 0;
                _updateCpuMsThisFrame = 0.0;
                _isRunningSlowlyThisFrame = false;
                _sunVisibilityBuildCallsThisFrame = 0;
                return true;
            }

            // Check watchdog: timeout if no draw for 2+ seconds
            if (now - _lastDrawTicks > (long)(WatchdogTimeoutSeconds * Stopwatch.Frequency))
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.NoRenderedFrameTimeout;
                _state = ProfileState.Inactive;
                return true;
            }

            // Calculate wall-clock interval since last BeginDraw
            double wallClockIntervalMs = TicksToMs(now - _lastBeginDrawTicks);
            _lastBeginDrawTicks = now;
            _frameStartTicksBeginDraw = now;

            // Start accumulating for this new rendered frame
            RenderedFrameMetrics metrics = new RenderedFrameMetrics
            {
                WallClockFrameIntervalMs = wallClockIntervalMs,
                UpdateCallsSinceLastDraw = _updateCallsThisFrame,
                TotalUpdateCpuMs = _updateCpuMsThisFrame,
                IsRunningSlowly = _isRunningSlowlyThisFrame,
                SunVisibilityBuildCallsThisFrame = _sunVisibilityBuildCallsThisFrame
            };

            RenderedFrameCallCounts calls = new RenderedFrameCallCounts
            {
                UpdateCallsCount = _updateCallsThisFrame,
                SunVisibilityBuildCalls = _sunVisibilityBuildCallsThisFrame
            };

            // Reset for next frame's updates
            _updateCallsThisFrame = 0;
            _updateCpuMsThisFrame = 0.0;
            _isRunningSlowlyThisFrame = false;
            _sunVisibilityBuildCallsThisFrame = 0;

            // Determine if this frame is warmup or measured
            bool isWarmupFrame = (_warmupFramesRemaining > 0);

            if (isWarmupFrame)
            {
                _warmupFramesRemaining--;
                return true;  // Continue, don't record
            }

            // Measured frame
            if (_measuredFramesRemaining > 0)
            {
                int bufferIndex = MaxFramesPerRun - _measuredFramesRemaining;
                if (bufferIndex < MaxFramesPerRun)
                {
                    _frameMetrics[bufferIndex] = metrics;
                    _frameCalls[bufferIndex] = calls;
                }
                _measuredFramesRemaining--;
            }

            // Check for completion
            if (_measuredFramesRemaining == 0)
            {
                if (_state == ProfileState.ValidationPass || _state == ProfileState.SmokeTest)
                {
                    _state = ProfileState.Inactive;
                }
                else if (_state == ProfileState.BaselineWarmup)
                {
                    _state = ProfileState.BaselineCapture;
                    _warmupFramesRemaining = 0;
                    _measuredFramesRemaining = MaxFramesPerRun;
                }
                else if (_state == ProfileState.BaselineCapture)
                {
                    _state = ProfileState.Inactive;
                }
            }

            return true;
        }

        public void OnDrawStart()
        {
            if (_state == ProfileState.Inactive)
                return;

            _lastDrawTicks = Stopwatch.GetTimestamp();
            _drawStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnDrawEnd()
        {
            if (_state == ProfileState.Inactive)
                return;

            long ticks = Stopwatch.GetTimestamp();
            double drawCpuMs = TicksToMs(ticks - _drawStartTicks);

            // Update last measured frame with draw time
            if (_measuredFramesRemaining >= 0 && _measuredFramesRemaining < MaxFramesPerRun)
            {
                int bufferIndex = MaxFramesPerRun - _measuredFramesRemaining - 1;
                if (bufferIndex >= 0 && bufferIndex < MaxFramesPerRun)
                {
                    _frameMetrics[bufferIndex].DrawCpuMs = drawCpuMs;
                }
            }
        }

        public void OnPresentMeasured(double presentMs)
        {
            if (_state == ProfileState.Inactive)
                return;

            if (_measuredFramesRemaining >= 0 && _measuredFramesRemaining < MaxFramesPerRun)
            {
                int bufferIndex = MaxFramesPerRun - _measuredFramesRemaining - 1;
                if (bufferIndex >= 0 && bufferIndex < MaxFramesPerRun)
                {
                    _frameMetrics[bufferIndex].PresentMs = presentMs;
                }
            }
        }

        public void OnDrawSkipped()
        {
            if (_state == ProfileState.Inactive)
                return;

            _drawSkippedCount++;
            MeasurementValid = false;
            InvalidReason = InvalidReasonEnum.DrawSkipped;
            _state = ProfileState.Inactive;
        }

        public ProfileResult EndProfile()
        {
            int framesRecorded = MaxFramesPerRun - (_measuredFramesRemaining > 0 ? _measuredFramesRemaining : 0);

            ProfileResult result = new ProfileResult
            {
                IsValid = MeasurementValid,
                InvalidReason = InvalidReason,
                Mode = _currentMode,
                ConfiguredResolutionWidth = _configuredResolutionWidth,
                ConfiguredResolutionHeight = _configuredResolutionHeight,
                ConfiguredZoom = _configuredZoom,
                ConfiguredActiveRegionWidth = _configuredActiveRegionWidth,
                ConfiguredActiveRegionHeight = _configuredActiveRegionHeight,
                ConfiguredSampleCount = _configuredSampleCount,
                FramesRecorded = framesRecorded,
                DrawSkippedCount = _drawSkippedCount,
                IsRunningSlowlyCount = _isRunningSlowlyCount,
                Metrics = new RenderedFrameMetrics[framesRecorded],
                Calls = new RenderedFrameCallCounts[framesRecorded]
            };

            Array.Copy(_frameMetrics, result.Metrics, framesRecorded);
            Array.Copy(_frameCalls, result.Calls, framesRecorded);

            _state = ProfileState.Inactive;
            _configurationLocked = false;
            return result;
        }

        private static double TicksToMs(long ticks)
        {
            return (ticks / (double)Stopwatch.Frequency) * 1000.0;
        }

        public bool IsActive => _state != ProfileState.Inactive;
        public ProfileState CurrentState => _state;
        public LightingMode CurrentMode => _currentMode;

        // Data structures for RENDERED FRAMES
        public struct RenderedFrameMetrics
        {
            // Wall-clock timing (includes GPU, Present, VSync, scheduler)
            public double WallClockFrameIntervalMs;

            // CPU timing components
            public double TotalUpdateCpuMs;  // Sum of all Update() calls this frame
            public double DrawCpuMs;
            public double PresentMs;

            // Derived
            public double TotalCpuMs => TotalUpdateCpuMs + DrawCpuMs;
            public double UnaccountedWallTimeMs => WallClockFrameIntervalMs - TotalCpuMs;
            public double FPS => WallClockFrameIntervalMs > 0 ? 1000.0 / WallClockFrameIntervalMs : 0;

            // Breakdown
            public int UpdateCallsSinceLastDraw;  // How many Update calls occurred this frame
            public bool IsRunningSlowly;
            public int SunVisibilityBuildCallsThisFrame;  // How many times SunVisibility was rebuilt
        }

        public struct RenderedFrameCallCounts
        {
            public int UpdateCallsCount;
            public int SunVisibilityBuildCalls;
        }

        public struct ProfileResult
        {
            public bool IsValid;
            public InvalidReasonEnum InvalidReason;

            public LightingMode Mode;
            public int ConfiguredResolutionWidth;
            public int ConfiguredResolutionHeight;
            public float ConfiguredZoom;
            public int ConfiguredActiveRegionWidth;
            public int ConfiguredActiveRegionHeight;
            public int ConfiguredSampleCount;

            public int FramesRecorded;
            public int DrawSkippedCount;
            public int IsRunningSlowlyCount;

            public RenderedFrameMetrics[] Metrics;
            public RenderedFrameCallCounts[] Calls;
        }
    }
}
