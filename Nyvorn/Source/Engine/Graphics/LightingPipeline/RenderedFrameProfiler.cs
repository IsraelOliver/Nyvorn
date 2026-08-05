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
            BaselineCapture,
            EmergencyV3None,
            EmergencyV3SunVisibility,
            EmergencyAblationA_PipelinePure,
            EmergencyAblationB_FullDebugNone,
            EmergencyAblationC_FullSunVisibilityDebug,
            EmergencyAblationD_FoundationWithoutSunVisibilityDebugNone
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
        private LightingMode _requestedLightingMode = LightingMode.Legacy;  // For emergency auto-mode switch
        private long _lastBeginDrawTicks = 0;
        private long _lastUpdateTicks = 0;
        private int _warmupFramesRemaining = 0;
        private int _measuredFramesRemaining = 0;
        private bool _isFirstFrame = true;

        // Emergency capture state
        private int _emergencyRenderedFramesRemaining = 0;
        private int _emergencyUpdateCountRemaining = 0;
        private long _emergencyMeasurementStartTicks = 0;
        private double _emergencyMaxUpdateMs = 0;
        private bool _emergencyModeApplied = false;
        private EmergencyCompletionReason _emergencyCompletionReason = EmergencyCompletionReason.None;

        public enum EmergencyCompletionReason
        {
            None,
            CompletedRenderedFrameLimit,
            CompletedUpdateLimit,
            NoRenderedFrameTimeout,
            SingleUpdateBudgetExceeded,
            EndDrawFailed,
            StageException
        }

        public enum EmergencyLastStage
        {
            None = 0,
            EnterGameUpdate = 1,
            EnterPlayingStateUpdate = 2,
            BeforeFoundation = 3,
            AfterActiveRegion = 4,
            BeforeClassification = 5,
            AfterClassification = 6,
            BeforeOccluderBuild = 7,
            AfterOccluderBuild = 8,
            BeforeSunVisibility = 9,
            AfterSunVisibility = 10,
            AfterFoundation = 11,
            ExitPlayingStateUpdate = 12,
            ExitGameUpdate = 13,
            BeginDrawAccepted = 14,
            EnterDraw = 15,
            ExitDraw = 16,
            EnterEndDraw = 17,
            ExitEndDraw = 18
        }

        public enum EmergencyAblationMode
        {
            V3Full = 0,
            V3PipelineOnly = 1,
            V3FoundationWithoutSunVisibility = 2
        }

        public enum AblationTest
        {
            None = 0,
            A_PipelinePure = 1,
            B_FullDebugNone = 2,
            C_FullSunVisibilityDebug = 3,
            D_FoundationWithoutSunVisibilityDebugNone = 4
        }

        public struct AblationConfiguration
        {
            public bool FoundationEnabled;
            public bool ClassificationEnabled;
            public bool OccluderBuildEnabled;
            public bool SunVisibilityBuildEnabled;
            public bool DebugRendererEnabled;
            public bool DebugCompositeEnabled;
        }

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
        private long _lastAcceptedBeginDrawTicks = 0;

        // Starvation accumulators (for emergency capture without rendered frames)
        private int _starvedUpdateCount = 0;
        private double _starvedTotalUpdateMs = 0.0;
        private double _starvedTotalFoundationMs = 0.0;
        private double _starvedTotalClassificationMs = 0.0;
        private double _starvedTotalOccluderMs = 0.0;
        private double _starvedTotalSunVisibilityMs = 0.0;
        private int _starvedFoundationCalls = 0;
        private int _starvedClassificationCalls = 0;
        private int _starvedOccluderCalls = 0;
        private int _starvedSunVisibilityCalls = 0;
        private long _lastSuccessfulBeginDrawTimestamp = 0;

        // Emergency diagnostics
        private long _lastStageLong = (long)EmergencyLastStage.None;  // Use long for Interlocked operations
        private EmergencyAblationMode _emergencyAblationMode = EmergencyAblationMode.V3Full;
        private AblationTest _ablationTestType = AblationTest.None;
        private AblationConfiguration _ablationConfig = new AblationConfiguration();
        private long _ablationStartWallClockTicks = 0;
        private const double AblationWallClockTimeoutSeconds = 5.0;

        // Ablation counters
        private int _ablationFoundationCalls = 0;
        private int _ablationClassificationCalls = 0;
        private int _ablationOccluderBuildCalls = 0;
        private int _ablationSunVisibilityBuildCalls = 0;
        private int _ablationDebugRendererCalls = 0;
        private int _ablationRenderSunVisibilityCalls = 0;
        private int _ablationDebugCompositeCalls = 0;
        private bool _emergencyLoggedEnterGameUpdate = false;
        private bool _emergencyLoggedEnterPlayingStateUpdate = false;
        private bool _emergencyLoggedBeforeFoundation = false;
        private bool _emergencyLoggedAfterActiveRegion = false;
        private bool _emergencyLoggedBeforeClassification = false;
        private bool _emergencyLoggedAfterClassification = false;
        private bool _emergencyLoggedBeforeOccluderBuild = false;
        private bool _emergencyLoggedAfterOccluderBuild = false;
        private bool _emergencyLoggedBeforeSunVisibility = false;
        private bool _emergencyLoggedAfterSunVisibility = false;
        private bool _emergencyLoggedAfterFoundation = false;
        private bool _emergencyLoggedExitPlayingStateUpdate = false;
        private bool _emergencyLoggedExitGameUpdate = false;
        private bool _emergencyLoggedBeginDrawAccepted = false;
        private bool _emergencyLoggedEnterDraw = false;
        private bool _emergencyLoggedExitDraw = false;
        private bool _emergencyLoggedEnterEndDraw = false;
        private bool _emergencyLoggedExitEndDraw = false;

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
            long now = Stopwatch.GetTimestamp();
            _measurementStartTicks = now;
            _lastUpdateTicks = now;
            _lastAcceptedBeginDrawTicks = now;

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
            long now = Stopwatch.GetTimestamp();
            _measurementStartTicks = now;
            _lastUpdateTicks = now;
            _lastAcceptedBeginDrawTicks = now;

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
            long now = Stopwatch.GetTimestamp();
            _measurementStartTicks = now;
            _lastUpdateTicks = now;
            _lastAcceptedBeginDrawTicks = now;

            LockConfiguration(resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
        }

        public void StartEmergencyV3None(int resolutionWidth, int resolutionHeight,
            float zoom, int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            _state = ProfileState.EmergencyV3None;
            _currentMode = LightingMode.Legacy;  // Start in Legacy
            _requestedLightingMode = LightingMode.V3Mode_None;  // Request V3 None
            _emergencyRenderedFramesRemaining = 5;
            _emergencyUpdateCountRemaining = 20;
            _emergencyModeApplied = false;
            _emergencyCompletionReason = EmergencyCompletionReason.None;
            _emergencyMaxUpdateMs = 0;
            ResetStarvationAccumulators();

            _isFirstFrame = true;
            _beginDrawRejectedCount = 0;
            _renderedFrameCount = 0;
            _noRenderedFrameTimeoutCount = 0;
            _maxUpdatesBeforeRenderedFrame = 0;
            MeasurementValid = true;
            InvalidReason = InvalidReasonEnum.None;
            long now = Stopwatch.GetTimestamp();
            _measurementStartTicks = now;
            _emergencyMeasurementStartTicks = now;
            _lastUpdateTicks = now;
            _lastAcceptedBeginDrawTicks = now;
            _lastSuccessfulBeginDrawTimestamp = now;

            LockConfiguration(resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
        }

        public void StartEmergencyV3SunVisibility(int resolutionWidth, int resolutionHeight,
            float zoom, int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            _state = ProfileState.EmergencyV3SunVisibility;
            _currentMode = LightingMode.Legacy;  // Start in Legacy
            _requestedLightingMode = LightingMode.V3Mode_SunVisibility;  // Request V3 SunVisibility
            _emergencyRenderedFramesRemaining = 5;
            _emergencyUpdateCountRemaining = 20;
            _emergencyModeApplied = false;
            _emergencyCompletionReason = EmergencyCompletionReason.None;
            _emergencyMaxUpdateMs = 0;
            ResetStarvationAccumulators();

            _isFirstFrame = true;
            _beginDrawRejectedCount = 0;
            _renderedFrameCount = 0;
            _noRenderedFrameTimeoutCount = 0;
            _maxUpdatesBeforeRenderedFrame = 0;
            MeasurementValid = true;
            InvalidReason = InvalidReasonEnum.None;
            long now = Stopwatch.GetTimestamp();
            _measurementStartTicks = now;
            _emergencyMeasurementStartTicks = now;
            _lastUpdateTicks = now;
            _lastAcceptedBeginDrawTicks = now;
            _lastSuccessfulBeginDrawTimestamp = now;

            LockConfiguration(resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
        }

        public void StartAblationTestA_PipelinePure(int resolutionWidth, int resolutionHeight, float zoom, int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            InitializeAblationTest(AblationTest.A_PipelinePure, ProfileState.EmergencyAblationA_PipelinePure, resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
            _ablationConfig = new AblationConfiguration
            {
                FoundationEnabled = false,
                ClassificationEnabled = false,
                OccluderBuildEnabled = false,
                SunVisibilityBuildEnabled = false,
                DebugRendererEnabled = false,
                DebugCompositeEnabled = false
            };
        }

        public void StartAblationTestB_FullDebugNone(int resolutionWidth, int resolutionHeight, float zoom, int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            InitializeAblationTest(AblationTest.B_FullDebugNone, ProfileState.EmergencyAblationB_FullDebugNone, resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
            _ablationConfig = new AblationConfiguration
            {
                FoundationEnabled = true,
                ClassificationEnabled = true,
                OccluderBuildEnabled = true,
                SunVisibilityBuildEnabled = true,
                DebugRendererEnabled = false,
                DebugCompositeEnabled = false
            };
        }

        public void StartAblationTestC_FullSunVisibilityDebug(int resolutionWidth, int resolutionHeight, float zoom, int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            InitializeAblationTest(AblationTest.C_FullSunVisibilityDebug, ProfileState.EmergencyAblationC_FullSunVisibilityDebug, resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
            _ablationConfig = new AblationConfiguration
            {
                FoundationEnabled = true,
                ClassificationEnabled = true,
                OccluderBuildEnabled = true,
                SunVisibilityBuildEnabled = true,
                DebugRendererEnabled = true,
                DebugCompositeEnabled = true
            };
        }

        public void StartAblationTestD_FoundationWithoutSunVisibilityDebugNone(int resolutionWidth, int resolutionHeight, float zoom, int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            InitializeAblationTest(AblationTest.D_FoundationWithoutSunVisibilityDebugNone, ProfileState.EmergencyAblationD_FoundationWithoutSunVisibilityDebugNone, resolutionWidth, resolutionHeight, zoom, activeRegionWidth, activeRegionHeight, sampleCount);
            _ablationConfig = new AblationConfiguration
            {
                FoundationEnabled = true,
                ClassificationEnabled = true,
                OccluderBuildEnabled = true,
                SunVisibilityBuildEnabled = false,
                DebugRendererEnabled = false,
                DebugCompositeEnabled = false
            };
        }

        private void InitializeAblationTest(AblationTest testType, ProfileState state, int resolutionWidth, int resolutionHeight, float zoom, int activeRegionWidth, int activeRegionHeight, int sampleCount)
        {
            _ablationTestType = testType;
            _state = state;
            _currentMode = LightingMode.Legacy;
            _requestedLightingMode = LightingMode.V3Mode_SunVisibility;

            _ablationFoundationCalls = 0;
            _ablationClassificationCalls = 0;
            _ablationOccluderBuildCalls = 0;
            _ablationSunVisibilityBuildCalls = 0;
            _ablationDebugRendererCalls = 0;
            _ablationRenderSunVisibilityCalls = 0;
            _ablationDebugCompositeCalls = 0;

            _isFirstFrame = true;
            _beginDrawRejectedCount = 0;
            _renderedFrameCount = 0;
            _noRenderedFrameTimeoutCount = 0;
            _maxUpdatesBeforeRenderedFrame = 0;
            MeasurementValid = true;
            InvalidReason = InvalidReasonEnum.None;

            long now = Stopwatch.GetTimestamp();
            _measurementStartTicks = now;
            _ablationStartWallClockTicks = now;
            _lastUpdateTicks = now;
            _lastAcceptedBeginDrawTicks = now;
            _lastSuccessfulBeginDrawTimestamp = now;

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
            double updateMs = TicksToMs(ticks - _updateStartTicks);
            _updateCpuMsThisFrame += updateMs;
            _updateCallsThisFrame++;
            _lastUpdateTicks = ticks;

            // Emergency capture: track individual update times and accumulated starvation
            if (IsEmergencyCapture && _emergencyModeApplied)
            {
                _starvedUpdateCount++;
                _starvedTotalUpdateMs += updateMs;

                // Check if single update exceeded budget
                if (updateMs > 500.0)
                {
                    _emergencyCompletionReason = EmergencyCompletionReason.SingleUpdateBudgetExceeded;
                    _emergencyMaxUpdateMs = updateMs;
                    _state = ProfileState.Inactive;
                }

                // Check if update count exceeded
                if (_emergencyUpdateCountRemaining > 0)
                    _emergencyUpdateCountRemaining--;
                if (_emergencyUpdateCountRemaining == 0)
                {
                    _emergencyCompletionReason = EmergencyCompletionReason.CompletedUpdateLimit;
                    _state = ProfileState.Inactive;
                }
            }
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

            // Ablation tests use wall-clock timeout (5 seconds)
            if (IsAblationTest)
            {
                double elapsedWallClockSeconds = TicksToMs(now - _ablationStartWallClockTicks) / 1000.0;
                if (elapsedWallClockSeconds > AblationWallClockTimeoutSeconds)
                {
                    _emergencyCompletionReason = EmergencyCompletionReason.NoRenderedFrameTimeout;
                    _state = ProfileState.Inactive;
                    _noRenderedFrameTimeoutCount++;
                    return;
                }
            }

            // Normal emergency capture: check for starvation without rendered frames
            double elapsedSinceLastBeginDrawSeconds = TicksToMs(now - _lastAcceptedBeginDrawTicks) / 1000.0;

            if (elapsedSinceLastBeginDrawSeconds > WatchdogTimeoutSeconds)
            {
                if (IsEmergencyCapture)
                {
                    // Emergency capture: freeze on starvation
                    _emergencyCompletionReason = EmergencyCompletionReason.NoRenderedFrameTimeout;
                }
                else
                {
                    // Normal capture: invalidate
                    MeasurementValid = false;
                    InvalidReason = InvalidReasonEnum.NoRenderedFrameTimeout;
                }
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
            double ms = TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
            _foundationUpdateMsThisFrame += ms;

            // Starvation tracking for emergency
            if (IsEmergencyCapture && _emergencyModeApplied)
            {
                _starvedTotalFoundationMs += ms;
                _starvedFoundationCalls++;
            }
        }

        public void OnClassificationStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnClassificationEnd()
        {
            if (_state == ProfileState.Inactive) return;
            double ms = TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
            _classificationMsThisFrame += ms;

            // Starvation tracking for emergency
            if (IsEmergencyCapture && _emergencyModeApplied)
            {
                _starvedTotalClassificationMs += ms;
                _starvedClassificationCalls++;
            }
        }

        public void OnOccluderBuildStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnOccluderBuildEnd()
        {
            if (_state == ProfileState.Inactive) return;
            double ms = TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
            _occluderBuildMsThisFrame += ms;

            // Starvation tracking for emergency
            if (IsEmergencyCapture && _emergencyModeApplied)
            {
                _starvedTotalOccluderMs += ms;
                _starvedOccluderCalls++;
            }
        }

        public void OnSunVisibilityBuildStart()
        {
            if (_state == ProfileState.Inactive) return;
            _updateStartTicks = Stopwatch.GetTimestamp();
        }

        public void OnSunVisibilityBuildEnd()
        {
            if (_state == ProfileState.Inactive) return;
            double ms = TicksToMs(Stopwatch.GetTimestamp() - _updateStartTicks);
            _sunVisibilityBuildMsThisFrame += ms;
            _sunVisibilityBuildCallsThisFrame++;

            // Starvation tracking for emergency
            if (IsEmergencyCapture && _emergencyModeApplied)
            {
                _starvedTotalSunVisibilityMs += ms;
                _starvedSunVisibilityCalls++;
            }
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
            _lastAcceptedBeginDrawTicks = Stopwatch.GetTimestamp();

            long now = _lastAcceptedBeginDrawTicks;

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

            // Emergency capture: check rendered frame limit
            if (IsEmergencyCapture && _emergencyModeApplied)
            {
                if (_emergencyRenderedFramesRemaining > 0)
                    _emergencyRenderedFramesRemaining--;
                if (_emergencyRenderedFramesRemaining == 0)
                {
                    _emergencyCompletionReason = EmergencyCompletionReason.CompletedRenderedFrameLimit;
                    _state = ProfileState.Inactive;
                }
            }

            _lastSuccessfulBeginDrawTimestamp = now;
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
            if (IsEmergencyCapture)
            {
                _emergencyCompletionReason = EmergencyCompletionReason.EndDrawFailed;
            }
            else
            {
                MeasurementValid = false;
                InvalidReason = InvalidReasonEnum.EndDrawFailed;
            }
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

        private void ResetStarvationAccumulators()
        {
            _starvedUpdateCount = 0;
            _starvedTotalUpdateMs = 0.0;
            _starvedTotalFoundationMs = 0.0;
            _starvedTotalClassificationMs = 0.0;
            _starvedTotalOccluderMs = 0.0;
            _starvedTotalSunVisibilityMs = 0.0;
            _starvedFoundationCalls = 0;
            _starvedClassificationCalls = 0;
            _starvedOccluderCalls = 0;
            _starvedSunVisibilityCalls = 0;
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
        public LightingMode RequestedLightingMode => _requestedLightingMode;
        public bool IsEmergencyCapture => _state == ProfileState.EmergencyV3None || _state == ProfileState.EmergencyV3SunVisibility;
        public EmergencyCompletionReason CompletionReason => _emergencyCompletionReason;
        public EmergencyAblationMode AblationMode => _emergencyAblationMode;

        public void SetAblationMode(EmergencyAblationMode mode)
        {
            _emergencyAblationMode = mode;
        }

        public bool ShouldSkipFoundationUpdate()
        {
            return IsEmergencyCapture && _emergencyAblationMode == EmergencyAblationMode.V3PipelineOnly;
        }

        public bool ShouldSkipSunVisibilityBuild()
        {
            return IsEmergencyCapture && _emergencyAblationMode == EmergencyAblationMode.V3FoundationWithoutSunVisibility;
        }

        public AblationTest CurrentAblationTest => _ablationTestType;
        public AblationConfiguration CurrentAblationConfig => _ablationConfig;

        public bool IsAblationTest => _state >= ProfileState.EmergencyAblationA_PipelinePure && _state <= ProfileState.EmergencyAblationD_FoundationWithoutSunVisibilityDebugNone;

        public bool ShouldSkipFoundationInAblation()
        {
            return IsAblationTest && !_ablationConfig.FoundationEnabled;
        }

        public bool ShouldSkipSunVisibilityBuildInAblation()
        {
            return IsAblationTest && !_ablationConfig.SunVisibilityBuildEnabled;
        }

        public void AblationLog_FoundationCall() { if (IsAblationTest) _ablationFoundationCalls++; }
        public void AblationLog_ClassificationCall() { if (IsAblationTest) _ablationClassificationCalls++; }
        public void AblationLog_OccluderBuildCall() { if (IsAblationTest) _ablationOccluderBuildCalls++; }
        public void AblationLog_SunVisibilityBuildCall() { if (IsAblationTest) _ablationSunVisibilityBuildCalls++; }
        public void AblationLog_DebugRendererCall() { if (IsAblationTest) _ablationDebugRendererCalls++; }
        public void AblationLog_RenderSunVisibilityCall() { if (IsAblationTest) _ablationRenderSunVisibilityCalls++; }
        public void AblationLog_DebugCompositeCall() { if (IsAblationTest) _ablationDebugCompositeCalls++; }

        // Signal that emergency mode has been applied
        public void NotifyEmergencyModeApplied()
        {
            if (IsEmergencyCapture)
                _emergencyModeApplied = true;
        }

        // Atomically update last stage for watchdog visibility
        public EmergencyLastStage GetLastStage()
        {
            return (EmergencyLastStage)System.Threading.Interlocked.Read(ref _lastStageLong);
        }

        private void SetLastStageAndLog(EmergencyLastStage stage, ref bool logged)
        {
            System.Threading.Interlocked.Exchange(ref _lastStageLong, (long)stage);

            if (!IsEmergencyCapture || logged)
                return;

            logged = true;
            string stageName = stage.ToString();
            System.Console.WriteLine($"[Emergency] {stageName}");
            System.Console.Out.Flush();
        }

        public void EmergencyLog_EnterGameUpdate()
        {
            SetLastStageAndLog(EmergencyLastStage.EnterGameUpdate, ref _emergencyLoggedEnterGameUpdate);
        }

        public void EmergencyLog_EnterPlayingStateUpdate()
        {
            SetLastStageAndLog(EmergencyLastStage.EnterPlayingStateUpdate, ref _emergencyLoggedEnterPlayingStateUpdate);
        }

        public void EmergencyLog_BeforeFoundation()
        {
            SetLastStageAndLog(EmergencyLastStage.BeforeFoundation, ref _emergencyLoggedBeforeFoundation);
        }

        public void EmergencyLog_AfterActiveRegion()
        {
            SetLastStageAndLog(EmergencyLastStage.AfterActiveRegion, ref _emergencyLoggedAfterActiveRegion);
        }

        public void EmergencyLog_BeforeClassification()
        {
            SetLastStageAndLog(EmergencyLastStage.BeforeClassification, ref _emergencyLoggedBeforeClassification);
        }

        public void EmergencyLog_AfterClassification()
        {
            SetLastStageAndLog(EmergencyLastStage.AfterClassification, ref _emergencyLoggedAfterClassification);
        }

        public void EmergencyLog_BeforeOccluderBuild()
        {
            SetLastStageAndLog(EmergencyLastStage.BeforeOccluderBuild, ref _emergencyLoggedBeforeOccluderBuild);
        }

        public void EmergencyLog_AfterOccluderBuild()
        {
            SetLastStageAndLog(EmergencyLastStage.AfterOccluderBuild, ref _emergencyLoggedAfterOccluderBuild);
        }

        public void EmergencyLog_BeforeSunVisibility()
        {
            SetLastStageAndLog(EmergencyLastStage.BeforeSunVisibility, ref _emergencyLoggedBeforeSunVisibility);
        }

        public void EmergencyLog_AfterSunVisibility()
        {
            SetLastStageAndLog(EmergencyLastStage.AfterSunVisibility, ref _emergencyLoggedAfterSunVisibility);
        }

        public void EmergencyLog_AfterFoundation()
        {
            SetLastStageAndLog(EmergencyLastStage.AfterFoundation, ref _emergencyLoggedAfterFoundation);
        }

        public void EmergencyLog_ExitPlayingStateUpdate()
        {
            SetLastStageAndLog(EmergencyLastStage.ExitPlayingStateUpdate, ref _emergencyLoggedExitPlayingStateUpdate);
        }

        public void EmergencyLog_ExitGameUpdate()
        {
            SetLastStageAndLog(EmergencyLastStage.ExitGameUpdate, ref _emergencyLoggedExitGameUpdate);
        }

        public void EmergencyLog_BeginDrawAccepted()
        {
            SetLastStageAndLog(EmergencyLastStage.BeginDrawAccepted, ref _emergencyLoggedBeginDrawAccepted);
        }

        public void EmergencyLog_EnterDraw()
        {
            SetLastStageAndLog(EmergencyLastStage.EnterDraw, ref _emergencyLoggedEnterDraw);
        }

        public void EmergencyLog_ExitDraw()
        {
            SetLastStageAndLog(EmergencyLastStage.ExitDraw, ref _emergencyLoggedExitDraw);
        }

        public void EmergencyLog_EnterEndDraw()
        {
            SetLastStageAndLog(EmergencyLastStage.EnterEndDraw, ref _emergencyLoggedEnterEndDraw);
        }

        public void EmergencyLog_ExitEndDraw()
        {
            SetLastStageAndLog(EmergencyLastStage.ExitEndDraw, ref _emergencyLoggedExitEndDraw);
        }

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
