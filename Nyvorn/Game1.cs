using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Game;
using Nyvorn.Source.Game.States;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using System.Diagnostics;

namespace Nyvorn;

public class Game1 : Game
{
    private static readonly Color ElyraSkyBaseColor = new Color(143, 211, 255);
    private const int TargetWidth = 1920;
    private const int TargetHeight = 1080;
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;

    private StateMachine _stateMachine;
    private KeyboardState _previousKeyboard;

    // Phase A0.0: Rendered frame profiler
    private RenderedFrameProfiler _renderedFrameProfiler = new RenderedFrameProfiler();
    private bool _prevKeyPState = false;  // Hotkey edge detection
    private bool _prevKeyOState = false;
    private bool _prevKeyUState = false;
    private bool _prevKeyEState = false;
    private bool _prevKey7State = false;  // Emergency V3 None
    private bool _prevKey8State = false;  // Emergency V3 SunVisibility
    private bool _prevKey4State = false;  // Ablation Test A - Pipeline Pure
    private bool _prevKey5State = false;  // Ablation Test B - Full Debug None
    private bool _prevKey6State = false;  // Ablation Test C - Full SunVisibility Debug
    private bool _prevKeyAltWState = false;  // Ablation Test D - Foundation Without SunVisibility

    // Emergency capture auto-mode switching
    private RenderedFrameProfiler.LightingMode _emergencyRequestedMode = RenderedFrameProfiler.LightingMode.Legacy;
    private bool _emergencyShouldExport = false;

    // Phase A0.0: Configuration snapshot for consistent measurements
    private struct CaptureEnvironmentSnapshot
    {
        public int BackBufferWidth;
        public int BackBufferHeight;
        public int ViewportWidth;
        public int ViewportHeight;
        public int ClientWidth;
        public int ClientHeight;
        public float Zoom;
        public int ActiveRegionCount;
        public int SampleCount;
        public bool IsDebuggerAttached;

        public static CaptureEnvironmentSnapshot Capture(GraphicsDevice gd, PlayingState ps, float zoom)
        {
            return new CaptureEnvironmentSnapshot
            {
                // Canonical source: PresentationParameters (actual rendered resolution)
                BackBufferWidth = gd.PresentationParameters.BackBufferWidth,
                BackBufferHeight = gd.PresentationParameters.BackBufferHeight,

                // Diagnostic only (may differ from BackBuffer if window resized)
                ViewportWidth = gd.Viewport.Width,
                ViewportHeight = gd.Viewport.Height,
                ClientWidth = gd.DisplayMode.Width,
                ClientHeight = gd.DisplayMode.Height,

                // State
                Zoom = zoom,
                ActiveRegionCount = ps?.Session.ViewCoordinator.LightingV3Foundation?.ActiveTileCount ?? 0,
                SampleCount = (ps?.Session.ViewCoordinator.LightingV3Foundation?.ActiveTileCount ?? 0) * 4,
                IsDebuggerAttached = System.Diagnostics.Debugger.IsAttached
            };
        }
    }

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = TargetWidth;
        _graphics.PreferredBackBufferHeight = TargetHeight;
        _graphics.HardwareModeSwitch = false;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        _graphics.ApplyChanges();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _stateMachine = new StateMachine();
        _stateMachine.PushState(new WorldSelectState(GraphicsDevice, Content, _stateMachine));
    }

    protected override void Update(GameTime gameTime)
    {
        // Phase A0.0: Profiler update timing
        _renderedFrameProfiler.OnUpdateStart();
        _renderedFrameProfiler.EmergencyLog_EnterGameUpdate();

        try
        {
            KeyboardState keyboard = Keyboard.GetState();

            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
                Exit();

            if (keyboard.IsKeyDown(Keys.F11) && !_previousKeyboard.IsKeyDown(Keys.F11))
                ToggleFullscreen();

            // Phase A0.0: Watchdog check
            _renderedFrameProfiler.OnUpdateCheckWatchdog();

            // Phase A0.0: Record IsRunningSlowly
            _renderedFrameProfiler.OnUpdateSetIsRunningSlowly(gameTime.IsRunningSlowly);

            _stateMachine.Update(gameTime);
            _previousKeyboard = keyboard;

            // Phase A0.0: Pass profiler to PlayingState if created
            if (_stateMachine.CurrentState is PlayingState playingState && playingState != null)
            {
                if (_renderedFrameProfiler != null)
                    playingState.SetProfiler(_renderedFrameProfiler);
            }

            // Phase A0.0: Handle profiler hotkeys (after state update)
            HandleProfilerHotkeys();

            base.Update(gameTime);
        }
        finally
        {
            _renderedFrameProfiler.EmergencyLog_ExitGameUpdate();
            _renderedFrameProfiler.OnUpdateEnd();
        }
    }

    private void HandleProfilerHotkeys()
    {
        KeyboardState keyboard = Keyboard.GetState();

        // Ctrl+Shift+P: Start Validation Pass (edge detection)
        bool currentKeyPState = keyboard.IsKeyDown(Keys.P) &&
                                keyboard.IsKeyDown(Keys.LeftControl) &&
                                keyboard.IsKeyDown(Keys.LeftShift);
        if (currentKeyPState && !_prevKeyPState && !_renderedFrameProfiler.IsActive)
        {
            StartValidationPass();
        }
        _prevKeyPState = currentKeyPState;

        // Ctrl+Shift+O: Start Smoke Test
        bool currentKeyOState = keyboard.IsKeyDown(Keys.O) &&
                                keyboard.IsKeyDown(Keys.LeftControl) &&
                                keyboard.IsKeyDown(Keys.LeftShift);
        if (currentKeyOState && !_prevKeyOState && !_renderedFrameProfiler.IsActive)
        {
            StartSmokeTest();
        }
        _prevKeyOState = currentKeyOState;

        // Ctrl+Shift+U: Start Baseline
        bool currentKeyUState = keyboard.IsKeyDown(Keys.U) &&
                                keyboard.IsKeyDown(Keys.LeftControl) &&
                                keyboard.IsKeyDown(Keys.LeftShift);
        if (currentKeyUState && !_prevKeyUState && !_renderedFrameProfiler.IsActive)
        {
            StartBaseline();
        }
        _prevKeyUState = currentKeyUState;

        // Ctrl+Shift+E: End Measurement and Write Results
        bool currentKeyEState = keyboard.IsKeyDown(Keys.E) &&
                                keyboard.IsKeyDown(Keys.LeftControl) &&
                                keyboard.IsKeyDown(Keys.LeftShift);
        if (currentKeyEState && !_prevKeyEState && !_renderedFrameProfiler.IsActive)
        {
            EndMeasurementAndWrite();
        }
        _prevKeyEState = currentKeyEState;

        // Ctrl+Shift+7: Start Emergency V3 None Capture
        bool currentKey7State = keyboard.IsKeyDown(Keys.D7) &&
                                keyboard.IsKeyDown(Keys.LeftControl) &&
                                keyboard.IsKeyDown(Keys.LeftShift);
        if (currentKey7State && !_prevKey7State && !_renderedFrameProfiler.IsActive)
        {
            StartEmergencyV3NoneCapture();
        }
        _prevKey7State = currentKey7State;

        // Ctrl+Shift+8: Start Emergency V3 SunVisibility Capture
        bool currentKey8State = keyboard.IsKeyDown(Keys.D8) &&
                                keyboard.IsKeyDown(Keys.LeftControl) &&
                                keyboard.IsKeyDown(Keys.LeftShift);
        if (currentKey8State && !_prevKey8State && !_renderedFrameProfiler.IsActive)
        {
            StartEmergencyV3SunVisibilityCapture();
        }
        _prevKey8State = currentKey8State;

        // Ctrl+Shift+4: Start Ablation Test A - Pipeline Pure
        bool currentKey4State = keyboard.IsKeyDown(Keys.D4) &&
                                keyboard.IsKeyDown(Keys.LeftControl) &&
                                keyboard.IsKeyDown(Keys.LeftShift);
        if (currentKey4State && !_prevKey4State && !_renderedFrameProfiler.IsActive)
        {
            StartAblationTestA();
        }
        _prevKey4State = currentKey4State;

        // Ctrl+Shift+5: Start Ablation Test B - Full Debug None
        bool currentKey5State = keyboard.IsKeyDown(Keys.D5) &&
                                keyboard.IsKeyDown(Keys.LeftControl) &&
                                keyboard.IsKeyDown(Keys.LeftShift);
        if (currentKey5State && !_prevKey5State && !_renderedFrameProfiler.IsActive)
        {
            StartAblationTestB();
        }
        _prevKey5State = currentKey5State;

        // Ctrl+Shift+6: Start Ablation Test C - Full SunVisibility Debug
        bool currentKey6State = keyboard.IsKeyDown(Keys.D6) &&
                                keyboard.IsKeyDown(Keys.LeftControl) &&
                                keyboard.IsKeyDown(Keys.LeftShift);
        if (currentKey6State && !_prevKey6State && !_renderedFrameProfiler.IsActive)
        {
            StartAblationTestC();
        }
        _prevKey6State = currentKey6State;

        // Ctrl+Shift+W: Start Ablation Test D - Foundation Without SunVisibility Debug None
        bool currentKeyAltWState = keyboard.IsKeyDown(Keys.W) &&
                                   keyboard.IsKeyDown(Keys.LeftControl) &&
                                   keyboard.IsKeyDown(Keys.LeftShift);
        if (currentKeyAltWState && !_prevKeyAltWState && !_renderedFrameProfiler.IsActive)
        {
            StartAblationTestD();
        }
        _prevKeyAltWState = currentKeyAltWState;

        // Handle emergency mode switching
        HandleEmergencyModeSwitch();
    }

    private void StartValidationPass()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        var snap = CaptureEnvironmentSnapshot.Capture(GraphicsDevice, playingState, playingState.Session.Camera.Zoom);
        var mode = GetCurrentLightingMode();

        _renderedFrameProfiler.StartValidationPass(mode, snap.BackBufferWidth, snap.BackBufferHeight, snap.Zoom,
            snap.ActiveRegionCount, snap.ActiveRegionCount, snap.SampleCount);
    }

    private void StartSmokeTest()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        var snap = CaptureEnvironmentSnapshot.Capture(GraphicsDevice, playingState, playingState.Session.Camera.Zoom);
        var mode = GetCurrentLightingMode();

        _renderedFrameProfiler.StartSmokeTest(mode, snap.BackBufferWidth, snap.BackBufferHeight, snap.Zoom,
            snap.ActiveRegionCount, snap.ActiveRegionCount, snap.SampleCount);
    }

    private void StartBaseline()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        var snap = CaptureEnvironmentSnapshot.Capture(GraphicsDevice, playingState, playingState.Session.Camera.Zoom);
        var mode = GetCurrentLightingMode();

        _renderedFrameProfiler.StartBaseline(mode, snap.BackBufferWidth, snap.BackBufferHeight, snap.Zoom,
            snap.ActiveRegionCount, snap.ActiveRegionCount, snap.SampleCount);
    }

    private void EndMeasurementAndWrite()
    {
        var result = _renderedFrameProfiler.EndProfile();

        if (!result.IsValid)
        {
            System.Console.WriteLine($"[Phase A0.0] Measurement invalid: {result.InvalidReason}");
            return;
        }

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = $"VALIDATION_{result.Mode}_{timestamp}.txt";
        string outputPath = System.IO.Path.Combine("Measurements", filename);

        System.IO.Directory.CreateDirectory("Measurements");
        RenderedFrameResultWriter.WriteToFile(result, outputPath);

        System.Console.WriteLine($"[Phase A0.0] Results written to {outputPath}");
    }

    private void StartEmergencyV3NoneCapture()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        var snap = CaptureEnvironmentSnapshot.Capture(GraphicsDevice, playingState, playingState.Session.Camera.Zoom);
        _renderedFrameProfiler.StartEmergencyV3None(snap.BackBufferWidth, snap.BackBufferHeight, snap.Zoom,
            snap.ActiveRegionCount, snap.ActiveRegionCount, snap.SampleCount);
        _emergencyRequestedMode = RenderedFrameProfiler.LightingMode.V3Mode_None;
        _emergencyShouldExport = false;

        System.Console.WriteLine("[Phase A0.0] Emergency V3 None capture started (will auto-switch mode)");
    }

    private void StartEmergencyV3SunVisibilityCapture()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        var snap = CaptureEnvironmentSnapshot.Capture(GraphicsDevice, playingState, playingState.Session.Camera.Zoom);
        _renderedFrameProfiler.StartEmergencyV3SunVisibility(snap.BackBufferWidth, snap.BackBufferHeight, snap.Zoom,
            snap.ActiveRegionCount, snap.ActiveRegionCount, snap.SampleCount);
        _renderedFrameProfiler.SetAblationMode(RenderedFrameProfiler.EmergencyAblationMode.V3Full);
        _emergencyRequestedMode = RenderedFrameProfiler.LightingMode.V3Mode_SunVisibility;
        _emergencyShouldExport = false;

        System.Console.WriteLine("[Phase A0.0] Emergency V3 SunVisibility capture started (will auto-switch mode)");
    }

    private void StartAblationTestA()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        var snap = CaptureEnvironmentSnapshot.Capture(GraphicsDevice, playingState, playingState.Session.Camera.Zoom);
        _renderedFrameProfiler.StartAblationTestA_PipelinePure(snap.BackBufferWidth, snap.BackBufferHeight, snap.Zoom,
            snap.ActiveRegionCount, snap.ActiveRegionCount, snap.SampleCount);
        _emergencyRequestedMode = RenderedFrameProfiler.LightingMode.V3Mode_SunVisibility;
        _emergencyShouldExport = false;

        System.Console.WriteLine("[Ablation Test A] Pipeline Pure - starting (5 second wall-clock timeout)");
        System.Console.WriteLine("[Ablation] Mode=PipelinePure | Foundation=OFF | SunBuild=OFF | Debug=None | DebugRenderer=OFF");
    }

    private void StartAblationTestB()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        var snap = CaptureEnvironmentSnapshot.Capture(GraphicsDevice, playingState, playingState.Session.Camera.Zoom);
        _renderedFrameProfiler.StartAblationTestB_FullDebugNone(snap.BackBufferWidth, snap.BackBufferHeight, snap.Zoom,
            snap.ActiveRegionCount, snap.ActiveRegionCount, snap.SampleCount);
        _emergencyRequestedMode = RenderedFrameProfiler.LightingMode.V3Mode_SunVisibility;
        _emergencyShouldExport = false;

        System.Console.WriteLine("[Ablation Test B] Full Debug None - starting (5 second wall-clock timeout)");
        System.Console.WriteLine("[Ablation] Mode=FullDebugNone | Foundation=ON | SunBuild=ON | Debug=None | DebugRenderer=OFF");
    }

    private void StartAblationTestC()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        var snap = CaptureEnvironmentSnapshot.Capture(GraphicsDevice, playingState, playingState.Session.Camera.Zoom);
        _renderedFrameProfiler.StartAblationTestC_FullSunVisibilityDebug(snap.BackBufferWidth, snap.BackBufferHeight, snap.Zoom,
            snap.ActiveRegionCount, snap.ActiveRegionCount, snap.SampleCount);
        _emergencyRequestedMode = RenderedFrameProfiler.LightingMode.V3Mode_SunVisibility;
        _emergencyShouldExport = false;

        System.Console.WriteLine("[Ablation Test C] Full SunVisibility Debug - starting (5 second wall-clock timeout)");
        System.Console.WriteLine("[Ablation] Mode=FullSunVisibilityDebug | Foundation=ON | SunBuild=ON | Debug=SunVisibility | DebugRenderer=ON");
    }

    private void StartAblationTestD()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        var snap = CaptureEnvironmentSnapshot.Capture(GraphicsDevice, playingState, playingState.Session.Camera.Zoom);
        _renderedFrameProfiler.StartAblationTestD_FoundationWithoutSunVisibilityDebugNone(snap.BackBufferWidth, snap.BackBufferHeight, snap.Zoom,
            snap.ActiveRegionCount, snap.ActiveRegionCount, snap.SampleCount);
        _emergencyRequestedMode = RenderedFrameProfiler.LightingMode.V3Mode_SunVisibility;
        _emergencyShouldExport = false;

        System.Console.WriteLine("[Ablation Test D] Foundation Without SunVisibility Debug None - starting (5 second wall-clock timeout)");
        System.Console.WriteLine("[Ablation] Mode=FoundationWithoutSunVisibilityDebugNone | Foundation=ON | SunBuild=OFF | Debug=None | DebugRenderer=OFF");
    }

    private void HandleEmergencyModeSwitch()
    {
        if (!_renderedFrameProfiler.IsEmergencyCapture)
            return;

        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        // Check if profiler has already applied the mode
        if (!_renderedFrameProfiler.IsActive || _emergencyShouldExport)
        {
            // Capture is done, switch back to Legacy and export
            if (_emergencyRequestedMode != RenderedFrameProfiler.LightingMode.Legacy)
            {
                // Request return to Legacy
                RequestLightingMode(RenderedFrameProfiler.LightingMode.Legacy);
                _emergencyRequestedMode = RenderedFrameProfiler.LightingMode.Legacy;
                _emergencyShouldExport = true;
            }
            else if (_emergencyShouldExport)
            {
                // Safely export results
                ExportEmergencyCapture();
                _emergencyShouldExport = false;
            }
        }
        else if (!_renderedFrameProfiler.IsActive == false)
        {
            // Still capturing - check if mode needs to be applied
            var currentMode = GetCurrentLightingMode();
            var requestedMode = _renderedFrameProfiler.RequestedLightingMode;

            if (requestedMode != currentMode && requestedMode != RenderedFrameProfiler.LightingMode.Legacy)
            {
                // Request the mode switch
                RequestLightingMode(requestedMode);
            }
            else if (requestedMode == currentMode && requestedMode != RenderedFrameProfiler.LightingMode.Legacy)
            {
                // Mode has been applied - notify profiler
                _renderedFrameProfiler.NotifyEmergencyModeApplied();
            }
        }
    }

    private void RequestLightingMode(RenderedFrameProfiler.LightingMode mode)
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        if (mode == RenderedFrameProfiler.LightingMode.Legacy)
        {
            // Request Legacy mode
            LightingPipelineCoordinator.I.SetMode(LightingPipelineMode.Legacy);
        }
        else if (mode == RenderedFrameProfiler.LightingMode.V3Mode_None || mode == RenderedFrameProfiler.LightingMode.V3Mode_SunVisibility)
        {
            // Request V3 mode (if not already there)
            if (LightingPipelineCoordinator.I.IsLegacyMode)
            {
                LightingPipelineCoordinator.I.SetMode(LightingPipelineMode.V3);
            }

            // Then set debug visualization mode
            var debugCtrl = playingState.Session.ViewCoordinator.LightingV3DebugController;
            if (debugCtrl != null)
            {
                LightingDebugMode targetMode = (mode == RenderedFrameProfiler.LightingMode.V3Mode_None) ?
                    LightingDebugMode.None : LightingDebugMode.SunVisibility;

                // Cycle to the target mode if needed
                LightingDebugMode currentMode = debugCtrl.GetCurrentMode();
                while (currentMode != targetMode)
                {
                    debugCtrl.CycleMode();
                    currentMode = debugCtrl.GetCurrentMode();
                    if (currentMode == targetMode) break;
                }
            }
        }
    }

    private void ExportEmergencyCapture()
    {
        var result = _renderedFrameProfiler.EndProfile();

        string modeStr = result.Mode switch
        {
            RenderedFrameProfiler.LightingMode.Legacy => "LEGACY",
            RenderedFrameProfiler.LightingMode.V3Mode_None => "V3_NONE",
            RenderedFrameProfiler.LightingMode.V3Mode_SunVisibility => "V3_SUN_VISIBILITY",
            _ => "UNKNOWN"
        };

        string filename = $"EMERGENCY_{modeStr}.txt";
        string outputPath = System.IO.Path.Combine("Measurements", filename);

        System.IO.Directory.CreateDirectory("Measurements");

        // Write emergency capture results
        using (var writer = new System.IO.StreamWriter(outputPath))
        {
            writer.WriteLine($"EMERGENCY CAPTURE REPORT");
            writer.WriteLine($"Mode: {modeStr}");
            writer.WriteLine($"Completion Reason: {_renderedFrameProfiler.CompletionReason}");
            writer.WriteLine($"Frames Recorded: {result.FramesRecorded}");
            writer.WriteLine($"Begin Draw Rejected: {result.BeginDrawRejectedCount}");
            writer.WriteLine($"Rendered Frame Count: {result.RenderedFrameCount}");
            writer.WriteLine($"Max Updates Before Rendered Frame: {result.MaxUpdatesBeforeRenderedFrame}");
            writer.WriteLine();
        }

        System.Console.WriteLine($"[Phase A0.0] Emergency results written to {outputPath}");
    }

    private PlayingState GetCurrentPlayingState()
    {
        if (_stateMachine?.CurrentState is PlayingState playingState)
            return playingState;
        return null;
    }

    private RenderedFrameProfiler.LightingMode GetCurrentLightingMode()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null)
            return RenderedFrameProfiler.LightingMode.Legacy;

        if (LightingPipelineCoordinator.I.IsLegacyMode)
            return RenderedFrameProfiler.LightingMode.Legacy;

        if (playingState.Session.ViewCoordinator.LightingV3DebugController?.GetCurrentMode() == LightingDebugMode.None)
            return RenderedFrameProfiler.LightingMode.V3Mode_None;

        return RenderedFrameProfiler.LightingMode.V3Mode_SunVisibility;
    }

    protected override bool BeginDraw()
    {
        bool drawProceed = base.BeginDraw();

        if (!drawProceed)
        {
            return false;
        }

        // Phase A0.0: Mark rendered frame boundary
        _renderedFrameProfiler.OnBeginDraw();
        _renderedFrameProfiler.EmergencyLog_BeginDrawAccepted();

        return true;
    }

    protected override void Draw(GameTime gameTime)
    {
        // Phase A0.0: Profiler draw timing
        _renderedFrameProfiler.OnDrawStart();
        _renderedFrameProfiler.EmergencyLog_EnterDraw();

        try
        {
            GraphicsDevice.Clear(ElyraSkyBaseColor);

            _stateMachine.Draw(gameTime, _spriteBatch);

            base.Draw(gameTime);
        }
        finally
        {
            _renderedFrameProfiler.EmergencyLog_ExitDraw();
            _renderedFrameProfiler.OnDrawEnd();
        }
    }

    protected override void EndDraw()
    {
        _renderedFrameProfiler.EmergencyLog_EnterEndDraw();
        long presentStartTicks = Stopwatch.GetTimestamp();

        try
        {
            base.EndDraw();  // This calls Present internally
        }
        catch
        {
            _renderedFrameProfiler.OnEndDrawFailed();
            throw;
        }
        finally
        {
            long presentEndTicks = Stopwatch.GetTimestamp();
            double presentMs = (presentEndTicks - presentStartTicks) / (double)Stopwatch.Frequency * 1000.0;
            _renderedFrameProfiler.OnPresentMeasured(presentMs);
            _renderedFrameProfiler.EmergencyLog_ExitEndDraw();
        }
    }

    private void ToggleFullscreen()
    {
        _graphics.IsFullScreen = !_graphics.IsFullScreen;
        _graphics.PreferredBackBufferWidth = TargetWidth;
        _graphics.PreferredBackBufferHeight = TargetHeight;
        _graphics.ApplyChanges();
    }
}
