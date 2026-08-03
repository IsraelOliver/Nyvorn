using System;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Central frame profiler for Phase A0.0 runtime sanity baseline.
    /// Zero allocations in hot path - uses pre-allocated circular buffers.
    /// Measures Legacy, V3 Mode=None, and V3 Mode=SunVisibility timing.
    ///
    /// Important: Frame interval is measured externally (Game level).
    /// This class measures CPU time only (Update + Draw phases).
    /// </summary>
    public class FrameProfiler
    {
        // Profiler states
        public enum ProfileState
        {
            Inactive,
            ValidationPass,    // 10 warmup + 30 measured frames per mode
            SmokeTest,         // 30 warmup + 120 measured frames per mode
            BaselineWarmup,    // 300 frames discarded (system stabilization)
            BaselineCapture    // 600 frames measured
        }

        public enum LightingMode
        {
            Legacy,
            V3Mode_None,
            V3Mode_SunVisibility
        }

        // Configuration
        private const int MaxFramesPerRun = 600;

        // Validation/Smoke warmup frames (discarded, not measured)
        private const int ValidationWarmupFrames = 10;
        private const int ValidationMeasuredFrames = 30;

        private const int SmokeTestWarmupFrames = 30;
        private const int SmokeTestMeasuredFrames = 120;

        // Baseline warmup frames (discarded)
        private const int BaselineWarmupFrames = 300;

        // State
        private ProfileState _state = ProfileState.Inactive;
        private LightingMode _currentMode = LightingMode.Legacy;
        private int _frameCount = 0;
        private int _warmupFramesRemaining = 0;
        private int _measuredFramesRemaining = 0;
        private bool _isFirstFrame = true;

        // Configuration validation (must not change during run)
        private int _configuredResolutionWidth = 0;
        private int _configuredResolutionHeight = 0;
        private float _configuredZoom = 0;
        private int _configuredActiveRegionWidth = 0;
        private int _configuredActiveRegionHeight = 0;
        private int _configuredSampleCount = 0;
        private bool _configurationLocked = false;

        // Measurement validity
        public bool MeasurementValid { get; private set; } = true;
        public string InvalidReason { get; private set; } = "";

        // Circular buffers (pre-allocated)
        private FrameMetrics[] _frameMetrics = new FrameMetrics[MaxFramesPerRun];
        private CallCounts[] _frameCalls = new CallCounts[MaxFramesPerRun];

        // Current frame data (filled during frame, added to buffer at end)
        private FrameMetrics _currentFrameMetrics;
        private CallCounts _currentFrameCalls;

        // Timing snapshots (per-frame, not wall-clock)
        private long _updateStartTicks;
        private long _drawStartTicks;

        // Note: WallClockFrameInterval is NOT measured here.
        // It must be measured externally (Game level) by:
        // 1. Recording tick at start of frame (before Update)
        // 2. Recording tick at start of next frame
        // 3. Computing interval
        // This ensures inclusion of Present, VSync, GPU waits, scheduler waits.

        public FrameProfiler()
        {
            // Pre-allocate all buffers
            for (int i = 0; i < MaxFramesPerRun; i++)
            {
                _frameMetrics[i] = new FrameMetrics();
                _frameCalls[i] = new CallCounts();
            }
        }

        /// <summary>
        /// Start validation pass: 10 warmup + 30 measured frames per mode.
        /// </summary>
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
            _frameCount = 0;
            _warmupFramesRemaining = ValidationWarmupFrames;
            _measuredFramesRemaining = ValidationMeasuredFrames;
            _isFirstFrame = true;
            MeasurementValid = true;
            InvalidReason = "";

            LockConfiguration(resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
        }

        /// <summary>
        /// Start smoke test: 30 warmup + 120 measured frames per mode.
        /// Measured frames collected independently for Legacy, V3 None, V3 SunVisibility.
        /// </summary>
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
            _frameCount = 0;
            _warmupFramesRemaining = SmokeTestWarmupFrames;
            _measuredFramesRemaining = SmokeTestMeasuredFrames;
            _isFirstFrame = true;
            MeasurementValid = true;
            InvalidReason = "";

            LockConfiguration(resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
        }

        /// <summary>
        /// Start baseline: 300 frames warmup, then 600 frames measured.
        /// </summary>
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
            _frameCount = 0;
            _warmupFramesRemaining = BaselineWarmupFrames;
            _measuredFramesRemaining = MaxFramesPerRun;
            _isFirstFrame = true;
            MeasurementValid = true;
            InvalidReason = "";

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

        /// <summary>
        /// Validate that configuration hasn't changed during measurement.
        /// Call this each frame. Invalidates measurement if mismatch detected.
        /// </summary>
        public void ValidateConfiguration(int resolutionWidth, int resolutionHeight, float zoom,
            int activeRegionWidth, int activeRegionHeight, int sampleCount, LightingMode currentMode)
        {
            if (!_configurationLocked || _state == ProfileState.Inactive)
                return;

            if (currentMode != _currentMode)
            {
                MeasurementValid = false;
                InvalidReason = "LightingMode changed during measurement";
                _state = ProfileState.Inactive;
                return;
            }

            if (resolutionWidth != _configuredResolutionWidth ||
                resolutionHeight != _configuredResolutionHeight)
            {
                MeasurementValid = false;
                InvalidReason = "Resolution changed during measurement";
                _state = ProfileState.Inactive;
                return;
            }

            if (Math.Abs(zoom - _configuredZoom) > 0.0001f)
            {
                MeasurementValid = false;
                InvalidReason = "Camera.Zoom changed during measurement";
                _state = ProfileState.Inactive;
                return;
            }

            if (activeRegionWidth != _configuredActiveRegionWidth ||
                activeRegionHeight != _configuredActiveRegionHeight)
            {
                MeasurementValid = false;
                InvalidReason = "ActiveRegion size changed during measurement";
                _state = ProfileState.Inactive;
                return;
            }

            if (sampleCount != _configuredSampleCount)
            {
                MeasurementValid = false;
                InvalidReason = "SampleCount changed during measurement";
                _state = ProfileState.Inactive;
                return;
            }
        }

        /// <summary>
        /// Stop profiling and return data for analysis.
        /// Call only after measurement is complete (state transitioned to Inactive).
        /// </summary>
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
                Metrics = new FrameMetrics[framesRecorded],
                Calls = new CallCounts[framesRecorded]
            };

            Array.Copy(_frameMetrics, result.Metrics, framesRecorded);
            Array.Copy(_frameCalls, result.Calls, framesRecorded);

            _state = ProfileState.Inactive;
            _configurationLocked = false;
            return result;
        }

        /// <summary>
        /// Called at very start of frame (before Update).
        /// wallClockFrameIntervalMs: interval since start of previous frame
        /// (must be computed externally at Game level, includes GPU/Present/VSync)
        /// Pass 0.0 for first frame (will be skipped).
        /// </summary>
        public void OnGlobalFrameStart(double wallClockFrameIntervalMs)
        {
            if (_state == ProfileState.Inactive)
                return;

            // Skip first frame (no valid interval)
            if (_isFirstFrame)
            {
                _isFirstFrame = false;
                _currentFrameMetrics = new FrameMetrics();
                _currentFrameCalls = new CallCounts();
                return;
            }

            _currentFrameMetrics = new FrameMetrics();
            _currentFrameMetrics.WallClockFrameIntervalMs = wallClockFrameIntervalMs;
            _currentFrameCalls = new CallCounts();
        }

        /// <summary>
        /// Called at start of PlayingState.Update.
        /// </summary>
        public void OnUpdateStart()
        {
            if (_state == ProfileState.Inactive)
                return;

            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Called at start of Foundation.Update.
        /// </summary>
        public void OnFoundationUpdateStart()
        {
            if (_state == ProfileState.Inactive)
                return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Called at end of Foundation.Update.
        /// </summary>
        public void OnFoundationUpdateEnd()
        {
            if (_state == ProfileState.Inactive)
                return;
            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.FoundationUpdateMs = TicksToMs(ticks - _updateStartTicks);
            _currentFrameCalls.FoundationUpdateCalls++;
        }

        /// <summary>
        /// Called at start of ClassifyRegionToSlot.
        /// </summary>
        public void OnClassificationStart()
        {
            if (_state == ProfileState.Inactive)
                return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnClassificationEnd()
        {
            if (_state == ProfileState.Inactive)
                return;
            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.ClassificationMs = TicksToMs(ticks - _updateStartTicks);
        }

        /// <summary>
        /// Called at start of BuildOpacityFieldsToSlot.
        /// </summary>
        public void OnOccluderBuildStart()
        {
            if (_state == ProfileState.Inactive)
                return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnOccluderBuildEnd()
        {
            if (_state == ProfileState.Inactive)
                return;
            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.OccluderBuildMs = TicksToMs(ticks - _updateStartTicks);
        }

        /// <summary>
        /// Called at start of BuildSunVisibilityFieldToSlot.
        /// </summary>
        public void OnSunVisibilityBuildStart()
        {
            if (_state == ProfileState.Inactive)
                return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnSunVisibilityBuildEnd()
        {
            if (_state == ProfileState.Inactive)
                return;
            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.SunVisibilityBuildMs = TicksToMs(ticks - _updateStartTicks);
            _currentFrameCalls.BuildSunVisibilityCalls++;
        }

        /// <summary>
        /// Called at end of PlayingState.Update (after all components).
        /// </summary>
        public void OnUpdateEnd()
        {
            if (_state == ProfileState.Inactive)
                return;

            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.TotalUpdateMs = TicksToMs(ticks - _updateStartTicks);
        }

        /// <summary>
        /// Called when world rendering starts (before sprite batch draws).
        /// </summary>
        public void OnWorldRenderStart()
        {
            if (_state == ProfileState.Inactive)
                return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnWorldRenderEnd()
        {
            if (_state == ProfileState.Inactive)
                return;
            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.WorldRenderMs = TicksToMs(ticks - _updateStartTicks);
        }

        /// <summary>
        /// Called at start of PlayingState.Draw.
        /// </summary>
        public void OnDrawStart()
        {
            if (_state == ProfileState.Inactive)
                return;

            _drawStartTicks = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Called when LightingV3DebugRenderer.Render() is called.
        /// </summary>
        public void OnDebugRendererStart()
        {
            if (_state == ProfileState.Inactive)
                return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnDebugRendererEnd()
        {
            if (_state == ProfileState.Inactive)
                return;
            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.DebugRendererMs = TicksToMs(ticks - _updateStartTicks);
            _currentFrameCalls.DebugRendererCalls++;
        }

        /// <summary>
        /// Called when RenderSunVisibility is called within debug renderer.
        /// </summary>
        public void OnRenderSunVisibilityStart()
        {
            if (_state == ProfileState.Inactive)
                return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnRenderSunVisibilityEnd()
        {
            if (_state == ProfileState.Inactive)
                return;
            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.RenderSunVisibilityMs = TicksToMs(ticks - _updateStartTicks);
            _currentFrameCalls.RenderSunVisibilityCalls++;
        }

        /// <summary>
        /// Called when debug visualization is composited to final RT.
        /// </summary>
        public void OnDebugCompositeStart()
        {
            if (_state == ProfileState.Inactive)
                return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnDebugCompositeEnd()
        {
            if (_state == ProfileState.Inactive)
                return;
            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.DebugCompositeMs = TicksToMs(ticks - _updateStartTicks);
            _currentFrameCalls.DebugCompositeCalls++;
        }

        /// <summary>
        /// Called when HUD is drawn.
        /// </summary>
        public void OnHudDrawStart()
        {
            if (_state == ProfileState.Inactive)
                return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnHudDrawEnd()
        {
            if (_state == ProfileState.Inactive)
                return;
            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.HudDrawMs = TicksToMs(ticks - _updateStartTicks);
            _currentFrameCalls.HudDrawCalls++;
        }

        /// <summary>
        /// Record that DebugRenderer was called but returned early (mode=None).
        /// </summary>
        public void OnDebugRendererEarlyReturn()
        {
            if (_state == ProfileState.Inactive)
                return;
            _currentFrameCalls.DebugRendererEarlyReturns++;
        }

        /// <summary>
        /// Called at end of PlayingState.Draw.
        /// </summary>
        public void OnDrawEnd()
        {
            if (_state == ProfileState.Inactive)
                return;

            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.TotalDrawMs = TicksToMs(ticks - _drawStartTicks);
        }

        /// <summary>
        /// Called at end of frame (after Draw, used for state management).
        /// Records measured frames to buffer, handles warmup/measured transitions.
        /// Note: WallClockFrameInterval was already set in OnGlobalFrameStart.
        /// </summary>
        public void OnGlobalFrameEnd()
        {
            if (_state == ProfileState.Inactive)
                return;

            // Skip first frame entirely (no valid interval)
            if (_isFirstFrame)
                return;

            // Compute derived metrics
            _currentFrameMetrics.CpuUpdateDrawMs = _currentFrameMetrics.TotalUpdateMs + _currentFrameMetrics.TotalDrawMs;
            _currentFrameMetrics.UnaccountedWallTimeMs = _currentFrameMetrics.WallClockFrameIntervalMs - _currentFrameMetrics.CpuUpdateDrawMs;

            // Determine if this frame is in warmup or measured window
            bool isWarmupFrame = (_warmupFramesRemaining > 0);

            if (isWarmupFrame)
            {
                _warmupFramesRemaining--;
                _frameCount++;
                // Discard warmup frames (don't record)
                return;
            }

            // Measured frame: record to buffer
            if (_measuredFramesRemaining > 0)
            {
                int bufferIndex = MaxFramesPerRun - _measuredFramesRemaining;
                if (bufferIndex < MaxFramesPerRun)
                {
                    _frameMetrics[bufferIndex] = _currentFrameMetrics;
                    _frameCalls[bufferIndex] = _currentFrameCalls;
                }
                _measuredFramesRemaining--;
                _frameCount++;
            }

            // Check for completion and state transitions
            if (_measuredFramesRemaining == 0)
            {
                if (_state == ProfileState.ValidationPass)
                {
                    _state = ProfileState.Inactive;  // End validation, must restart for next mode
                }
                else if (_state == ProfileState.SmokeTest)
                {
                    _state = ProfileState.Inactive;  // End smoke test, must restart for next mode
                }
                else if (_state == ProfileState.BaselineWarmup)
                {
                    _state = ProfileState.BaselineCapture;
                    _warmupFramesRemaining = 0;
                    _measuredFramesRemaining = MaxFramesPerRun;
                    _frameCount = 0;
                }
                else if (_state == ProfileState.BaselineCapture)
                {
                    _state = ProfileState.Inactive;  // End baseline
                }
            }
        }

        private double TicksToMs(long ticks)
        {
            return (ticks / (double)Stopwatch.Frequency) * 1000.0;
        }

        public bool IsActive => _state != ProfileState.Inactive;
        public ProfileState CurrentState => _state;
        public LightingMode CurrentMode => _currentMode;

        // Data structures
        public struct FrameMetrics
        {
            // CPU timing
            public double TotalUpdateMs;
            public double TotalDrawMs;

            // Wall-clock timing (includes GPU, Present, VSync waits)
            public double WallClockFrameIntervalMs;
            public double CpuUpdateDrawMs;           // TotalUpdateMs + TotalDrawMs
            public double UnaccountedWallTimeMs;     // WallClockFrameIntervalMs - CpuUpdateDrawMs

            // Update phase breakdown
            public double FoundationUpdateMs;
            public double ClassificationMs;
            public double OccluderBuildMs;
            public double SunVisibilityBuildMs;

            // Draw phase breakdown
            public double WorldRenderMs;
            public double DebugRendererMs;
            public double RenderSunVisibilityMs;
            public double DebugCompositeMs;
            public double HudDrawMs;

            public double TotalFrameMs => TotalUpdateMs + TotalDrawMs;
            public double FPS => TotalFrameMs > 0 ? 1000.0 / TotalFrameMs : 0;
        }

        public struct CallCounts
        {
            public int FoundationUpdateCalls;
            public int BuildClassificationCalls;
            public int BuildOccluderCalls;
            public int BuildSunVisibilityCalls;
            public int DebugRendererCalls;
            public int DebugRendererEarlyReturns;        // When early exit in mode=None
            public int RenderSunVisibilityCalls;
            public int DebugCompositeCalls;
            public int HudDrawCalls;
        }

        public struct ProfileResult
        {
            // Validity
            public bool IsValid;
            public string InvalidReason;

            // Configuration (must not change during run)
            public LightingMode Mode;
            public int ConfiguredResolutionWidth;
            public int ConfiguredResolutionHeight;
            public float ConfiguredZoom;
            public int ConfiguredActiveRegionWidth;
            public int ConfiguredActiveRegionHeight;
            public int ConfiguredSampleCount;

            // Data
            public int FramesRecorded;
            public FrameMetrics[] Metrics;
            public CallCounts[] Calls;
        }
    }
}
