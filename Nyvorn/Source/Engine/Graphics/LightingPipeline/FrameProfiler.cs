using System;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Central frame profiler for Phase A0.0 runtime sanity baseline.
    /// Zero allocations in hot path - uses pre-allocated circular buffers.
    /// Measures Legacy, V3 Mode=None, and V3 Mode=SunVisibility timing.
    /// </summary>
    public class FrameProfiler
    {
        // Profiler states
        public enum ProfileState
        {
            Inactive,
            ValidationPass, // 30 frames per mode (sanity check)
            SmokeTest,      // 120 frames per mode (legacy, v3none, v3sun)
            BaselineWarmup, // 300 frames warmup
            BaselineCapture // 600 frames measured
        }

        public enum LightingMode
        {
            Legacy,
            V3Mode_None,
            V3Mode_SunVisibility
        }

        // Configuration
        private const int MaxFramesPerRun = 600;
        private const int ValidationFrames = 30;
        private const int SmokeTestFrames = 120;
        private const int WarmupFrames = 300;

        // State
        private ProfileState _state = ProfileState.Inactive;
        private LightingMode _currentMode = LightingMode.Legacy;
        private int _frameCount = 0;
        private int _captureStartIndex = 0;

        // Circular buffers (pre-allocated)
        private FrameMetrics[] _frameMetrics = new FrameMetrics[MaxFramesPerRun];
        private CallCounts[] _frameCalls = new CallCounts[MaxFramesPerRun];

        // Current frame data (filled during frame, added to buffer at end)
        private FrameMetrics _currentFrameMetrics;
        private CallCounts _currentFrameCalls;

        // Timing snapshots
        private long _globalFrameStartTicks;  // Start of frame (before Update)
        private long _globalFrameEndTicks;    // End of frame (after Draw)
        private long _updateStartTicks;
        private long _drawStartTicks;

        // Aggregated one-time values
        private double _firstV3ActivationMs = 0;
        private double _firstRenderTargetCreationMs = 0;
        private double _firstBufferResizeMs = 0;

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
        /// Start validation pass: 30 frames per mode to sanity-check instrumentation.
        /// </summary>
        public void StartValidationPass(LightingMode initialMode)
        {
            _state = ProfileState.ValidationPass;
            _currentMode = initialMode;
            _frameCount = 0;
            _captureStartIndex = 0;
        }

        /// <summary>
        /// Start smoke test: 120 frames Legacy, then 120 V3 None, then 120 V3 SunVisibility.
        /// </summary>
        public void StartSmokeTest(LightingMode initialMode)
        {
            _state = ProfileState.SmokeTest;
            _currentMode = initialMode;
            _frameCount = 0;
            _captureStartIndex = 0;
        }

        /// <summary>
        /// Start baseline: 300 frames warmup, then 600 frames measured.
        /// </summary>
        public void StartBaseline(LightingMode mode)
        {
            _state = ProfileState.BaselineWarmup;
            _currentMode = mode;
            _frameCount = 0;
            _captureStartIndex = WarmupFrames;
        }

        /// <summary>
        /// Stop profiling and return data for analysis.
        /// </summary>
        public ProfileResult EndProfile()
        {
            if (_state == ProfileState.Inactive)
                return new ProfileResult { IsValid = false };

            ProfileResult result = new ProfileResult
            {
                IsValid = true,
                Mode = _currentMode,
                FramesRecorded = Math.Min(_frameCount, MaxFramesPerRun),
                FirstV3ActivationMs = _firstV3ActivationMs,
                FirstRenderTargetCreationMs = _firstRenderTargetCreationMs,
                FirstBufferResizeMs = _firstBufferResizeMs,
                Metrics = new FrameMetrics[Math.Min(_frameCount, MaxFramesPerRun)],
                Calls = new CallCounts[Math.Min(_frameCount, MaxFramesPerRun)]
            };

            Array.Copy(_frameMetrics, result.Metrics, result.FramesRecorded);
            Array.Copy(_frameCalls, result.Calls, result.FramesRecorded);

            _state = ProfileState.Inactive;
            return result;
        }

        /// <summary>
        /// Called at very start of frame (before Update). Measures wall-clock time.
        /// </summary>
        public void OnGlobalFrameStart()
        {
            if (_state == ProfileState.Inactive)
                return;

            _globalFrameStartTicks = Stopwatch.GetTimestamp();
            _currentFrameMetrics = new FrameMetrics();
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
        /// Called at very end of frame (after all rendering and Present).
        /// Computes wall-clock frame interval and handles state transitions.
        /// </summary>
        public void OnGlobalFrameEnd()
        {
            if (_state == ProfileState.Inactive)
                return;

            long ticks = Stopwatch.GetTimestamp();
            _currentFrameMetrics.WallClockFrameIntervalMs = TicksToMs(ticks - _globalFrameStartTicks);
            _currentFrameMetrics.CpuUpdateDrawMs = _currentFrameMetrics.TotalUpdateMs + _currentFrameMetrics.TotalDrawMs;
            _currentFrameMetrics.UnaccountedWallTimeMs = _currentFrameMetrics.WallClockFrameIntervalMs - _currentFrameMetrics.CpuUpdateDrawMs;

            // Record frame (only if in capture window)
            if (_frameCount >= _captureStartIndex && _frameCount < MaxFramesPerRun)
            {
                int bufferIndex = _frameCount - _captureStartIndex;
                _frameMetrics[bufferIndex] = _currentFrameMetrics;
                _frameCalls[bufferIndex] = _currentFrameCalls;
            }

            _frameCount++;

            // State transitions (no console output during capture)
            if (_state == ProfileState.ValidationPass)
            {
                if (_frameCount >= ValidationFrames)
                {
                    if (_currentMode == LightingMode.Legacy)
                    {
                        _frameCount = 0;
                        _currentMode = LightingMode.V3Mode_None;
                    }
                    else if (_currentMode == LightingMode.V3Mode_None)
                    {
                        _frameCount = 0;
                        _currentMode = LightingMode.V3Mode_SunVisibility;
                    }
                    else
                    {
                        _state = ProfileState.Inactive;
                    }
                }
            }
            else if (_state == ProfileState.SmokeTest)
            {
                if (_frameCount >= SmokeTestFrames)
                {
                    if (_currentMode == LightingMode.Legacy)
                    {
                        _frameCount = 0;
                        _currentMode = LightingMode.V3Mode_None;
                    }
                    else if (_currentMode == LightingMode.V3Mode_None)
                    {
                        _frameCount = 0;
                        _currentMode = LightingMode.V3Mode_SunVisibility;
                    }
                    else
                    {
                        _state = ProfileState.Inactive;
                    }
                }
            }
            else if (_state == ProfileState.BaselineWarmup)
            {
                if (_frameCount >= WarmupFrames)
                {
                    _state = ProfileState.BaselineCapture;
                    _frameCount = 0;
                }
            }
            else if (_state == ProfileState.BaselineCapture)
            {
                if (_frameCount >= MaxFramesPerRun)
                {
                    _state = ProfileState.Inactive;
                }
            }
        }

        private double TicksToMs(long ticks)
        {
            return (ticks / (double)Stopwatch.Frequency) * 1000.0;
        }

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
            public bool IsValid;
            public LightingMode Mode;
            public int FramesRecorded;
            public double FirstV3ActivationMs;
            public double FirstRenderTargetCreationMs;
            public double FirstBufferResizeMs;
            public FrameMetrics[] Metrics;
            public CallCounts[] Calls;
        }
    }
}
