// PHASE_A0_0_INTEGRATION_CODE.cs
// Copy and adapt these code snippets into your Game.cs, PlayingState.cs, LightingV3Foundation.cs, etc.
// This is a reference, not a complete file.

// ============================================================
// GAME.CS ADDITIONS
// ============================================================

using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using System.Diagnostics;

public class Game : Microsoft.Xna.Framework.Game
{
    private RenderedFrameProfiler _renderedFrameProfiler = new RenderedFrameProfiler();

    // Hotkey state (edge detection - prevents repeat while key held)
    private bool _prevKeyPState = false;
    private bool _prevKeyOState = false;
    private bool _prevKeyUState = false;
    private bool _prevKeyEState = false;

    // ============================================================
    // Update() - Accumulate Update timing + Watchdog + Hotkeys
    // ============================================================
    public override void Update(GameTime gameTime)
    {
        // Validate configuration hasn't changed
        ValidateProfilerConfiguration();

        // Watchdog check (detect 2+ second starvation)
        _renderedFrameProfiler.OnUpdateCheckWatchdog();

        // Update timing
        _renderedFrameProfiler.OnUpdateStart();

        try
        {
            // Your existing update code here
            if (_playingState != null)
                _playingState.Update(gameTime);

            base.Update(gameTime);
        }
        finally
        {
            _renderedFrameProfiler.OnUpdateEnd();
        }

        // Record IsRunningSlowly state
        _renderedFrameProfiler.OnUpdateSetIsRunningSlowly(gameTime.IsRunningSlowly);

        // Handle hotkeys (edge detection)
        HandleProfilerHotkeys();
    }

    private void ValidateProfilerConfiguration()
    {
        if (_playingState == null) return;

        int screenWidth = GraphicsDevice.Viewport.Width;
        int screenHeight = GraphicsDevice.Viewport.Height;
        float zoom = _playingState.Camera.Zoom;

        // Get ActiveRegion - adjust field names to match your code
        int arWidth = 0, arHeight = 0;
        if (_playingState.GetFoundation() != null)
        {
            arWidth = _playingState.GetFoundation().ActiveRegion.RegionWidthTiles;
            arHeight = _playingState.GetFoundation().ActiveRegion.RegionHeightTiles;
        }

        int sampleCount = arWidth * arHeight * 4; // 4 samples per tile (typical)
        RenderedFrameProfiler.LightingMode mode = GetLightingMode();

        _renderedFrameProfiler.ValidateConfiguration(screenWidth, screenHeight, zoom, arWidth, arHeight, sampleCount, mode);
    }

    private RenderedFrameProfiler.LightingMode GetLightingMode()
    {
        if (_playingState == null)
            return RenderedFrameProfiler.LightingMode.Legacy;

        // Adjust to match your code's V3 enable/disable logic
        if (!_playingState.IsLightingV3Active)
            return RenderedFrameProfiler.LightingMode.Legacy;

        if (_playingState.V3DebugMode == LightingV3DebugMode.None)
            return RenderedFrameProfiler.LightingMode.V3Mode_None;

        return RenderedFrameProfiler.LightingMode.V3Mode_SunVisibility;
    }

    private void HandleProfilerHotkeys()
    {
        var keyboardState = Keyboard.GetState();

        // Ctrl+Shift+P: Start Validation Pass (edge detection - no repeat while held)
        bool currentKeyPState = keyboardState.IsKeyDown(Keys.P) &&
                                keyboardState.IsKeyDown(Keys.LeftControl) &&
                                keyboardState.IsKeyDown(Keys.LeftShift);

        if (currentKeyPState && !_prevKeyPState && !_renderedFrameProfiler.IsActive)
        {
            StartValidationPass();
        }
        _prevKeyPState = currentKeyPState;

        // Ctrl+Shift+O: Start Smoke Test
        bool currentKeyOState = keyboardState.IsKeyDown(Keys.O) &&
                                keyboardState.IsKeyDown(Keys.LeftControl) &&
                                keyboardState.IsKeyDown(Keys.LeftShift);

        if (currentKeyOState && !_prevKeyOState && !_renderedFrameProfiler.IsActive)
        {
            StartSmokeTest();
        }
        _prevKeyOState = currentKeyOState;

        // Ctrl+Shift+U: Start Baseline
        bool currentKeyUState = keyboardState.IsKeyDown(Keys.U) &&
                                keyboardState.IsKeyDown(Keys.LeftControl) &&
                                keyboardState.IsKeyDown(Keys.LeftShift);

        if (currentKeyUState && !_prevKeyUState && !_renderedFrameProfiler.IsActive)
        {
            StartBaseline();
        }
        _prevKeyUState = currentKeyUState;

        // Ctrl+Shift+E: End Measurement and Write Results
        bool currentKeyEState = keyboardState.IsKeyDown(Keys.E) &&
                                keyboardState.IsKeyDown(Keys.LeftControl) &&
                                keyboardState.IsKeyDown(Keys.LeftShift);

        if (currentKeyEState && !_prevKeyEState && !_renderedFrameProfiler.IsActive)
        {
            EndMeasurementAndWrite();
        }
        _prevKeyEState = currentKeyEState;
    }

    private void StartValidationPass()
    {
        if (_playingState == null) return;

        int screenWidth = GraphicsDevice.Viewport.Width;
        int screenHeight = GraphicsDevice.Viewport.Height;
        float zoom = _playingState.Camera.Zoom;
        int arWidth = 0, arHeight = 0;
        if (_playingState.GetFoundation() != null)
        {
            arWidth = _playingState.GetFoundation().ActiveRegion.RegionWidthTiles;
            arHeight = _playingState.GetFoundation().ActiveRegion.RegionHeightTiles;
        }
        int sampleCount = arWidth * arHeight * 4;
        RenderedFrameProfiler.LightingMode mode = GetLightingMode();

        _renderedFrameProfiler.StartValidationPass(mode, screenWidth, screenHeight, zoom, arWidth, arHeight, sampleCount);
    }

    private void StartSmokeTest()
    {
        if (_playingState == null) return;

        int screenWidth = GraphicsDevice.Viewport.Width;
        int screenHeight = GraphicsDevice.Viewport.Height;
        float zoom = _playingState.Camera.Zoom;
        int arWidth = 0, arHeight = 0;
        if (_playingState.GetFoundation() != null)
        {
            arWidth = _playingState.GetFoundation().ActiveRegion.RegionWidthTiles;
            arHeight = _playingState.GetFoundation().ActiveRegion.RegionHeightTiles;
        }
        int sampleCount = arWidth * arHeight * 4;
        RenderedFrameProfiler.LightingMode mode = GetLightingMode();

        _renderedFrameProfiler.StartSmokeTest(mode, screenWidth, screenHeight, zoom, arWidth, arHeight, sampleCount);
    }

    private void StartBaseline()
    {
        if (_playingState == null) return;

        int screenWidth = GraphicsDevice.Viewport.Width;
        int screenHeight = GraphicsDevice.Viewport.Height;
        float zoom = _playingState.Camera.Zoom;
        int arWidth = 0, arHeight = 0;
        if (_playingState.GetFoundation() != null)
        {
            arWidth = _playingState.GetFoundation().ActiveRegion.RegionWidthTiles;
            arHeight = _playingState.GetFoundation().ActiveRegion.RegionHeightTiles;
        }
        int sampleCount = arWidth * arHeight * 4;
        RenderedFrameProfiler.LightingMode mode = GetLightingMode();

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

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = $"VALIDATION_{result.Mode}_{timestamp}.txt";
        string outputPath = Path.Combine("Measurements", filename);

        Directory.CreateDirectory("Measurements");
        RenderedFrameResultWriter.WriteToFile(result, outputPath);

        System.Console.WriteLine($"[Phase A0.0] Results written to {outputPath}");
    }

    // ============================================================
    // BeginDraw() - Rendered frame boundary
    // ============================================================
    protected override bool BeginDraw()
    {
        bool drawProceed = base.BeginDraw();

        if (!drawProceed)
        {
            return false;  // Rendering skipped by MonoGame
        }

        // OnBeginDraw handles frame boundary, warmup/measured logic, watchdog
        _renderedFrameProfiler.OnBeginDraw();

        return true;
    }

    // ============================================================
    // Draw() - Measure draw-phase timing
    // ============================================================
    public override void Draw(GameTime gameTime)
    {
        _renderedFrameProfiler.OnDrawStart();

        try
        {
            GraphicsDevice.Clear(Color.Black);

            if (_playingState != null)
            {
                // World rendering
                _renderedFrameProfiler.OnWorldRenderStart();
                try
                {
                    _playingState.DrawWorld(_spriteBatch);
                }
                finally
                {
                    _renderedFrameProfiler.OnWorldRenderEnd();
                }

                // Debug visualization (if active)
                if (_playingState.V3DebugMode != LightingV3DebugMode.None)
                {
                    _renderedFrameProfiler.OnDebugRendererStart();
                    try
                    {
                        _playingState.DrawDebugRenderer(_spriteBatch);
                    }
                    finally
                    {
                        _renderedFrameProfiler.OnDebugRendererEnd();
                    }
                }

                // HUD
                _renderedFrameProfiler.OnHudDrawStart();
                try
                {
                    _playingState.DrawHUD(_spriteBatch);
                }
                finally
                {
                    _renderedFrameProfiler.OnHudDrawEnd();
                }
            }

            base.Draw(gameTime);
        }
        finally
        {
            _renderedFrameProfiler.OnDrawEnd();
        }
    }

    // ============================================================
    // EndDraw() - Measure Present timing
    // ============================================================
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
}

// ============================================================
// PLAYINGSTATE.CS ADDITIONS
// ============================================================

public class PlayingState : GameState
{
    private RenderedFrameProfiler _renderedFrameProfiler;

    // Called from Game to pass profiler reference
    public void SetProfiler(RenderedFrameProfiler profiler)
    {
        _renderedFrameProfiler = profiler;
    }

    // Expose Foundation for Game to query
    public LightingV3Foundation GetFoundation()
    {
        return _foundation;  // Your foundation field
    }

    // Implement in Draw methods
    public void DrawWorld(SpriteBatch spriteBatch)
    {
        // Your existing world rendering code
    }

    public void DrawDebugRenderer(SpriteBatch spriteBatch)
    {
        // Your existing debug renderer code - will call LightingV3DebugRenderer
    }

    public void DrawHUD(SpriteBatch spriteBatch)
    {
        // Your existing HUD code
    }
}

// ============================================================
// LIGHTINGV3FOUNDATION.CS ADDITIONS
// ============================================================

public class LightingV3Foundation
{
    private RenderedFrameProfiler _profiler;

    public void UpdateFromVisibleWorldRect(VisibleWorldRect rect, int tileSize, RenderedFrameProfiler profiler = null)
    {
        _profiler = profiler;

        if (_profiler != null)
            _profiler.OnFoundationUpdateStart();

        try
        {
            // Classification
            if (_profiler != null)
                _profiler.OnClassificationStart();
            try
            {
                ClassifyRegionToSlot(_backSlot);
            }
            finally
            {
                if (_profiler != null)
                    _profiler.OnClassificationEnd();
            }

            // Occluder field
            if (_profiler != null)
                _profiler.OnOccluderBuildStart();
            try
            {
                BuildOpacityFieldsToSlot(_backSlot, tileSize);
            }
            finally
            {
                if (_profiler != null)
                    _profiler.OnOccluderBuildEnd();
            }

            // Sun visibility
            if (_profiler != null)
                _profiler.OnSunVisibilityBuildStart();
            try
            {
                BuildSunVisibilityFieldToSlot(_backSlot, tileSize);
            }
            finally
            {
                if (_profiler != null)
                    _profiler.OnSunVisibilityBuildEnd();
            }

            // ... swap buffers, etc ...
        }
        finally
        {
            if (_profiler != null)
                _profiler.OnFoundationUpdateEnd();
        }
    }
}

// ============================================================
// LIGHTINGV3DEBUGRENDERER.CS ADDITIONS (for V3 modes only)
// ============================================================

public class LightingV3DebugRenderer
{
    private RenderedFrameProfiler _profiler;

    public void SetProfiler(RenderedFrameProfiler profiler)
    {
        _profiler = profiler;
    }

    public void Render(SpriteBatch spriteBatch, LightingV3Foundation foundation)
    {
        if (foundation.DebugMode == DebugMode.None)
        {
            // Early exit - no rendering
            return;
        }

        try
        {
            if (foundation.DebugMode == DebugMode.SunVisibility)
            {
                if (_profiler != null)
                    _profiler.OnRenderSunVisibilityStart();
                try
                {
                    RenderSunVisibility(spriteBatch, foundation);
                }
                finally
                {
                    if (_profiler != null)
                        _profiler.OnRenderSunVisibilityEnd();
                }
            }

            // ... other debug renders ...

            // Composite debug RT
            if (_profiler != null)
                _profiler.OnDebugCompositeStart();
            try
            {
                CompositeDebugRT(spriteBatch);
            }
            finally
            {
                if (_profiler != null)
                    _profiler.OnDebugCompositeEnd();
            }
        }
        finally
        {
            // Note: OnDebugRendererEnd is called by caller (PlayingState.Draw)
        }
    }
}
