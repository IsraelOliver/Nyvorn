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
    /// Preserves all timing metrics:
    /// - Foundation stages: Classification, OccluderBuild, SunVisibilityBuild
    /// - Draw stages: WorldRender, DebugRenderer, HudDraw, Present
    /// - Catch-up metrics: CatchUpUpdateCount, BeginDrawRejectedCount
    /// - Diagnostic: IsRunningSlowlyUpdateCount, NoRenderedFrameTimeoutCount
    /// </summary>
    public class RenderedFrameProfiler
    {
        public enum ProfileState
        {
            Inactive,
            ValidationPass,
            SmokeTest,
            BaselineWarmup,
            BaselineCapture
        }

        public enum LightingMode
        {
            Legacy,
            V3Mode_None,
            V3Mode_SunVisibility
        }

        // Frame counts
        private const int ValidationWarmupFrames = 10;
        private const int ValidationMeasuredFrames = 30;
        private const int SmokeTestWarmupFrames = 30;
        private const int SmokeTestMeasuredFrames = 120;
        private const int BaselineWarmupFrames = 300;
        private const int MaxFramesPerRun = 600;
        private const double WatchdogTimeoutSeconds = 2.0;

        // State
        private ProfileState _state = ProfileState.Inactive;
        private LightingMode _currentMode = LightingMode.Legacy;
        private long _lastBeginDrawTicks = 0;
        private long _lastUpdateTicks = 0;
        private int _warmupFramesRemaining = 0;
        private int _measuredFramesRemaining = 0;
        private bool _isFirstFrame = true;

        // Per-rendered-frame accumulation
        private long _frameStartTicksBeginDraw;
        private long _updateStartTicks;
        private long _drawStartTicks;

        // Update accumulation
        private int _updateCallsThisFrame = 0;
        private double _updateCpuMsThisFrame = 0.0;
        private int _isRunningSlowlyUpdateCount = 0;

        // Foundation stage timings (accumulated from all Updates this frame)
        private double _foundationUpdateMsThisFrame = 0.0;
        private double _classificationMsThisFrame = 0.0;
        private double _occluderBuildMsThisFrame = 0.0;
        private double _sunVisibilityBuildMsThisFrame = 0.0;
        private int _sunVisibilityBuildCallsThisFrame = 0;

        // Draw stage timings
        private double _worldRenderMsThisFrame = 0.0;
        private double _debugRendererMsThisFrame = 0.0;
        private double _renderSunVisibilityMsThisFrame = 0.0;
        private double _debugCompositeMsThisFrame = 0.0;
        private double _hudDrawMsThisFrame = 0.0;
        private double _presentMsThisFrame = 0.0;
        private double _beginDrawGateMsThisFrame = 0.0;

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
            BeginDrawRejected = 7,
            EndDrawFailed = 8,
            UnknownFailure = 99
        }

        // Circular buffers
        private RenderedFrameMetrics[] _frameMetrics = new RenderedFrameMetrics[MaxFramesPerRun];
        private RenderedFrameCallCounts[] _frameCalls = new RenderedFrameCallCounts[MaxFramesPerRun];

        // Aggregate diagnostics
        private int _beginDrawRejectedCount = 0;
        private int _renderedFrameCount = 0;
        private int _noRenderedFrameTimeoutCount = 0;
        private int _maxUpdatesBeforeRenderedFrame = 0;
        private long _measurementStartTicks = 0;

        public RenderedFrameProfiler()
        {
            for (int i = 0; i < MaxFramesPerRun; i++)
            {
                _frameMetrics[i] = new RenderedFrameMetrics();
                _frameCalls[i] = new RenderedFrameCallCounts();
            }
        }

        public void StartValidationPass(LightingMode initialMode, int resolutionWidth, int resolutionHeight,
            float zoom, int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            _state = ProfileState.ValidationPass;
            _currentMode = initialMode;
            _warmupFramesRemaining = ValidationWarmupFrames;
            _measuredFramesRemaining = ValidationMeasuredFrames;
            _isFirstFrame = true;
            _beginDrawRejectedCount = 0;
            _renderedFrameCount = 0;
            _noRenderedFrameTimeoutCount = 0;
            _maxUpdatesBeforeRenderedFrame = 0;
            MeasurementValid = true;
            InvalidReason = InvalidReasonEnum.None;
            _measurementStartTicks = Stopwatch.GetTimestamp();
            _lastUpdateTicks = Stopwatch.GetTimestamp();

            LockConfiguration(resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
        }

        public void StartSmokeTest(LightingMode initialMode, int resolutionWidth, int resolutionHeight,
            float zoom, int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            _state = ProfileState.SmokeTest;
            _currentMode = initialMode;
            _warmupFramesRemaining = SmokeTestWarmupFrames;
            _measuredFramesRemaining = SmokeTestMeasuredFrames;
            _isFirstFrame = true;
            _beginDrawRejectedCount = 0;
            _renderedFrameCount = 0;
            _noRenderedFrameTimeoutCount = 0;
            _maxUpdatesBeforeRenderedFrame = 0;
            MeasurementValid = true;
            InvalidReason = InvalidReasonEnum.None;
            _measurementStartTicks = Stopwatch.GetTimestamp();
            _lastUpdateTicks = Stopwatch.GetTimestamp();

            LockConfiguration(resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
        }

        public void StartBaseline(LightingMode mode, int resolutionWidth, int resolutionHeight,
            float zoom, int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            _state = ProfileState.BaselineWarmup;
            _currentMode = mode;
            _warmupFramesRemaining = BaselineWarmupFrames;
            _measuredFramesRemaining = MaxFramesPerRun;
            _isFirstFrame = true;
            _beginDrawRejectedCount = 0;
            _renderedFrameCount = 0;
            _noRenderedFrameTimeoutCount = 0;
            _maxUpdatesBeforeRenderedFrame = 0;
            MeasurementValid = true;
            InvalidReason = InvalidReasonEnum.None;
            _measurementStartTicks = Stopwatch.GetTimestamp();
            _lastUpdateTicks = Stopwatch.GetTimestamp();

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
            }
            else if (resolutionWidth != _configuredResolutionWidth || resolutionHeight != _configuredResolutionHeight)
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.ResolutionChanged;
                _state = ProfileState.Inactive;
            }
            else if (Math.Abs(zoom - _configuredZoom) > 0.0001f)
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.ZoomChanged;
                _state = ProfileState.Inactive;
            }
            else if (activeRegionWidth != _configuredActiveRegionWidth || activeRegionHeight != _configuredActiveRegionHeight)
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.ActiveRegionSizeChanged;
                _state = ProfileState.Inactive;
            }
            else if (sampleCount != _configuredSampleCount)
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.SampleCountChanged;
                _state = ProfileState.Inactive;
            }
        }

        // Update phase - accumulates timing and call counts
        public void OnUpdateStart()
        {
            if (_state == ProfileState.Inactive)
                return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnUpdateEnd()
        {
            if (_state == ProfileState.Inactive)
                return;
            long ticks = Stopwatch.GetTimestamp();
            _updateCpuMsThisFrame += TicksToMs(ticks - _updateStartTicks);
            _updateCallsThisFrame++;
            _lastUpdateTicks = ticks;
        }

        public void OnUpdateSetIsRunningSlowly(bool isRunningSlowly)
        {
            if (_state == ProfileState.Inactive)
                return;
            if (isRunningSlowly)
                _isRunningSlowlyUpdateCount++;
        }

        // Watchdog check - must be called from Update to detect starvation
        public void OnUpdateCheckWatchdog()
        {
            if (_state == ProfileState.Inactive)
                return;

            long now = Stopwatch.GetTimestamp();
            double elapsedSeconds = TicksToMs(now - _measurementStartTicks) / 1000.0;

            if (elapsedSeconds > WatchdogTimeoutSeconds && _renderedFrameCount == 0)
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.NoRenderedFrameTimeout;
                _state = ProfileState.Inactive;
                _noRenderedFrameTimeoutCount++;
            }
        }

        // Foundation stage timings
        public void OnFoundationUpdateStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnFoundationUpdateEnd()
        {
            if (_state == ProfileState.Inactive) return;
            _foundationUpdateMsThisFrame += TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
        }

        public void OnClassificationStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnClassificationEnd()
        {
            if (_state == ProfileState.Inactive) return;
            _classificationMsThisFrame += TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
        }

        public void OnOccluderBuildStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnOccluderBuildEnd()
        {
            if (_state == ProfileState.Inactive) return;
            _occluderBuildMsThisFrame += TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
        }

        public void OnSunVisibilityBuildStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnSunVisibilityBuildEnd()
        {
            if (_state == ProfileState.Inactive) return;
            _sunVisibilityBuildMsThisFrame += TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
            _sunVisibilityBuildCallsThisFrame++;
        }

        // BeginDraw - marks rendered frame boundary
        public bool OnBeginDraw()
        {
            if (_state == ProfileState.Inactive)
                return true;

            long beginDrawStartTicks = Stopwatch.GetTimestamp();
            bool drawProceed = true;  // Placeholder - actual result from base.BeginDraw

            // Measure BeginDraw gate time
            _beginDrawGateMsThisFrame = TicksToMs(Stopwatch.GetTimestamp() - beginDrawStartTicks);

            if (!drawProceed)
            {
                _beginDrawRejectedCount++;
                // Do NOT reset accumulators, do NOT create frame record
                return false;
            }

            // BeginDraw succeeded - this is a new rendered frame boundary

            long now = Stopwatch.GetTimestamp();

            // First frame: initialize only
            if (_isFirstFrame)
            {
                _isFirstFrame = false;
                _lastBeginDrawTicks = now;
                _frameStartTicksBeginDraw = now;
                ResetFrameAccumulators();
                return true;
            }

            // Calculate wall-clock interval since last BeginDraw
            double wallClockIntervalMs = TicksToMs(now - _lastBeginDrawTicks);
            _lastBeginDrawTicks = now;
            _frameStartTicksBeginDraw = now;

            // Catch-up count: how many extra updates this frame?
            int catchUpUpdateCount = Math.Max(0, _updateCallsThisFrame - 1);
            _maxUpdatesBeforeRenderedFrame = Math.Max(_maxUpdatesBeforeRenderedFrame, _updateCallsThisFrame);

            // Create frame record for this rendered frame
            RenderedFrameMetrics metrics = new RenderedFrameMetrics
            {
                WallClockFrameIntervalMs = wallClockIntervalMs,
                UpdateCallsSinceLastDraw = _updateCallsThisFrame,
                CatchUpUpdateCount = catchUpUpdateCount,
                TotalUpdateCpuMs = _updateCpuMsThisFrame,
                IsRunningSlowlyUpdateCount = _isRunningSlowlyUpdateCount,

                // Foundation timings
                FoundationUpdateMs = _foundationUpdateMsThisFrame,
                ClassificationMs = _classificationMsThisFrame,
                OccluderBuildMs = _occluderBuildMsThisFrame,
                SunVisibilityBuildMs = _sunVisibilityBuildMsThisFrame,

                // Draw timings (will be filled in later)
                BeginDrawGateMs = _beginDrawGateMsThisFrame
            };

            RenderedFrameCallCounts calls = new RenderedFrameCallCounts
            {
                UpdateCallsCount = _updateCallsThisFrame,
                SunVisibilityBuildCalls = _sunVisibilityBuildCallsThisFrame,
                IsRunningSlowlyUpdateCount = _isRunningSlowlyUpdateCount
            };

            // Determine if warmup or measured
            bool isWarmupFrame = (_warmupFramesRemaining > 0);

            if (isWarmupFrame)
            {
                _warmupFramesRemaining--;
                ResetFrameAccumulators();
                return true;
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
                _renderedFrameCount++;
            }

            ResetFrameAccumulators();

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

        // Draw timing
        public void OnDrawStart()
        {
            if (_state == ProfileState.Inactive) return;
            _drawStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnWorldRenderStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnWorldRenderEnd()
        {
            if (_state == ProfileState.Inactive) return;
            _worldRenderMsThisFrame += TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
        }

        public void OnDebugRendererStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnDebugRendererEnd()
        {
            if (_state == ProfileState.Inactive) return;
            _debugRendererMsThisFrame += TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
        }

        public void OnRenderSunVisibilityStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnRenderSunVisibilityEnd()
        {
            if (_state == ProfileState.Inactive) return;
            _renderSunVisibilityMsThisFrame += TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
        }

        public void OnDebugCompositeStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnDebugCompositeEnd()
        {
            if (_state == ProfileState.Inactive) return;
            _debugCompositeMsThisFrame += TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
        }

        public void OnHudDrawStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnHudDrawEnd()
        {
            if (_state == ProfileState.Inactive) return;
            _hudDrawMsThisFrame += TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
        }

        public void OnDrawEnd()
        {
            if (_state == ProfileState.Inactive) return;
            // OnDrawEnd just marks end of Draw() method, actual metrics recorded in OnPresentMeasured
        }

        // Present timing
        public void OnPresentMeasured(double presentMs)
        {
            if (_state == ProfileState.Inactive) return;
            _presentMsThisFrame = presentMs;

            // Update last recorded frame with draw timings
            if (_measuredFramesRemaining >= 0 && _measuredFramesRemaining < MaxFramesPerRun)
            {
                int bufferIndex = MaxFramesPerRun - _measuredFramesRemaining - 1;
                if (bufferIndex >= 0 && bufferIndex < MaxFramesPerRun)
                {
                    // Fill in draw-phase timings
                    _frameMetrics[bufferIndex].DrawCpuMs = _drawStartTicks > 0 ?
                        TicksToMs(Stopwatch.GetTimestamp() - _drawStartTicks) : 0.0;
                    _frameMetrics[bufferIndex].WorldRenderMs = _worldRenderMsThisFrame;
                    _frameMetrics[bufferIndex].DebugRendererMs = _debugRendererMsThisFrame;
                    _frameMetrics[bufferIndex].RenderSunVisibilityMs = _renderSunVisibilityMsThisFrame;
                    _frameMetrics[bufferIndex].DebugCompositeMs = _debugCompositeMsThisFrame;
                    _frameMetrics[bufferIndex].HudDrawMs = _hudDrawMsThisFrame;
                    _frameMetrics[bufferIndex].PresentMs = presentMs;
                }
            }
        }

        public void OnEndDrawFailed()
        {
            if (_state == ProfileState.Inactive) return;
            MeasurementValid = false;
            InvalidReason = InvalidReasonEnum.EndDrawFailed;
            _state = ProfileState.Inactive;
        }

        private void ResetFrameAccumulators()
        {
            _updateCallsThisFrame = 0;
            _updateCpuMsThisFrame = 0.0;
            _isRunningSlowlyUpdateCount = 0;
            _foundationUpdateMsThisFrame = 0.0;
            _classificationMsThisFrame = 0.0;
            _occluderBuildMsThisFrame = 0.0;
            _sunVisibilityBuildMsThisFrame = 0.0;
            _sunVisibilityBuildCallsThisFrame = 0;
            _worldRenderMsThisFrame = 0.0;
            _debugRendererMsThisFrame = 0.0;
            _renderSunVisibilityMsThisFrame = 0.0;
            _debugCompositeMsThisFrame = 0.0;
            _hudDrawMsThisFrame = 0.0;
            _presentMsThisFrame = 0.0;
            _beginDrawGateMsThisFrame = 0.0;
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
                BeginDrawRejectedCount = _beginDrawRejectedCount,
                RenderedFrameCount = _renderedFrameCount,
                NoRenderedFrameTimeoutCount = _noRenderedFrameTimeoutCount,
                MaxUpdatesBeforeRenderedFrame = _maxUpdatesBeforeRenderedFrame,
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
            // Wall-clock
            public double WallClockFrameIntervalMs;

            // Update accumulation
            public double TotalUpdateCpuMs;
            public int UpdateCallsSinceLastDraw;
            public int CatchUpUpdateCount;
            public int IsRunningSlowlyUpdateCount;

            // Foundation stages (nested within Update)
            public double FoundationUpdateMs;
            public double ClassificationMs;
            public double OccluderBuildMs;
            public double SunVisibilityBuildMs;

            // Draw stages
            public double DrawCpuMs;
            public double WorldRenderMs;
            public double DebugRendererMs;
            public double RenderSunVisibilityMs;
            public double DebugCompositeMs;
            public double HudDrawMs;
            public double PresentMs;
            public double BeginDrawGateMs;

            // Derived
            public double TotalCpuMs => TotalUpdateCpuMs + DrawCpuMs;
            public double UnaccountedWallTimeMs => WallClockFrameIntervalMs - TotalCpuMs;
            public double FPS => WallClockFrameIntervalMs > 0 ? 1000.0 / WallClockFrameIntervalMs : 0;
        }

        public struct RenderedFrameCallCounts
        {
            public int UpdateCallsCount;
            public int SunVisibilityBuildCalls;
            public int IsRunningSlowlyUpdateCount;
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
            public int BeginDrawRejectedCount;
            public int RenderedFrameCount;
            public int NoRenderedFrameTimeoutCount;
            public int MaxUpdatesBeforeRenderedFrame;
            public RenderedFrameMetrics[] Metrics;
            public RenderedFrameCallCounts[] Calls;
        }
    }
}
