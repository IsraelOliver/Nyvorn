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
    }

    private void StartValidationPass()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        int screenWidth = _graphics.PreferredBackBufferWidth;
        int screenHeight = _graphics.PreferredBackBufferHeight;
        float zoom = playingState.Session.Camera.Zoom;

        // Get ActiveRegion
        int arWidth = playingState.Session.ViewCoordinator.LightingV3Foundation?.ActiveTileCount ?? 0;
        int arHeight = arWidth > 0 ? arWidth : 0;  // Placeholder
        int sampleCount = arWidth * 4 * arHeight;  // Approximate

        var mode = GetCurrentLightingMode();
        _renderedFrameProfiler.StartValidationPass(mode, screenWidth, screenHeight, zoom, arWidth, arHeight, sampleCount);
    }

    private void StartSmokeTest()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        int screenWidth = _graphics.PreferredBackBufferWidth;
        int screenHeight = _graphics.PreferredBackBufferHeight;
        float zoom = playingState.Session.Camera.Zoom;
        int arWidth = playingState.Session.ViewCoordinator.LightingV3Foundation?.ActiveTileCount ?? 0;
        int arHeight = arWidth > 0 ? arWidth : 0;
        int sampleCount = arWidth * 4 * arHeight;
        var mode = GetCurrentLightingMode();

        _renderedFrameProfiler.StartSmokeTest(mode, screenWidth, screenHeight, zoom, arWidth, arHeight, sampleCount);
    }

    private void StartBaseline()
    {
        var playingState = GetCurrentPlayingState();
        if (playingState == null) return;

        int screenWidth = _graphics.PreferredBackBufferWidth;
        int screenHeight = _graphics.PreferredBackBufferHeight;
        float zoom = playingState.Session.Camera.Zoom;
        int arWidth = playingState.Session.ViewCoordinator.LightingV3Foundation?.ActiveTileCount ?? 0;
        int arHeight = arWidth > 0 ? arWidth : 0;
        int sampleCount = arWidth * 4 * arHeight;
        var mode = GetCurrentLightingMode();

        _renderedFrameProfiler.StartBaseline(mode, screenWidth, screenHeight, zoom, arWidth, arHeight, sampleCount);
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

        return true;
    }

    protected override void Draw(GameTime gameTime)
    {
        // Phase A0.0: Profiler draw timing
        _renderedFrameProfiler.OnDrawStart();

        try
        {
            GraphicsDevice.Clear(ElyraSkyBaseColor);

            _stateMachine.Draw(gameTime, _spriteBatch);

            base.Draw(gameTime);
        }
        finally
        {
            _renderedFrameProfiler.OnDrawEnd();
        }
    }

    protected override void EndDraw()
    {
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
