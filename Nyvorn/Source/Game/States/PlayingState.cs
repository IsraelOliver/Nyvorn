using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using Nyvorn.Source.Engine.Input;
using System;
using System.Diagnostics;
using System.IO;
using Nyvorn.Source.Game;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.Interaction;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.Gameplay.World.Simulation;
using Nyvorn.Source.World.Decorations;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;
using Nyvorn.Source.World.Tissue;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nyvorn.Source.Game.States
{
    /// <summary>
    /// PlayingState orchestrates game rendering through a dual-pipeline architecture:
    ///
    /// **DUAL PIPELINE ARCHITECTURE:**
    /// - Legacy Pipeline (UseNewLightingPipeline=false): Direct backbuffer rendering with per-loop draw calls
    /// - New Lighting Pipeline (UseNewLightingPipeline=true): RenderTarget-based composition with unified scene capture
    ///
    /// **NEW LIGHTING PIPELINE (7-PHASE ARCHITECTURE):**
    /// PHASE 0 (Prepare): Allocate/validate RenderTarget, set as active render target
    /// PHASE 1 (Atmosphere): Draw sky, sun, moons, mountains to RenderTarget
    /// PHASE 2 (World Scene): Draw terrain, decorations, entities to RenderTarget
    /// PHASE 3 (Lighting): Apply lightmap multiply-blend over scene in RenderTarget
    /// PHASE 4 (Composite): Set backbuffer as target, draw RenderTarget to it
    /// PHASE 5 (Interior Overlays): Draw focus/interior overlays (screen-space)
    /// PHASE 6 (Screen Effects): Draw rain, overlay, torch glow (screen-space)
    /// PHASE 7 (HUD): Draw HUD, minimap, inventory, console (screen-space)
    ///
    /// **CRITICAL DESIGN PRINCIPLE:**
    /// All world-space content (phases 1-3) is rendered to RenderTarget to ensure atomic composition
    /// and lighting consistency. Screen-space content (phases 5-7) renders directly to backbuffer
    /// after RenderTarget composition, avoiding re-lighting of UI elements.
    ///
    /// **FLAG BEHAVIOR:**
    /// - UseNewLightingPipeline (PlayingSessionViewCoordinator): Selects pipeline mode
    /// - LegacyNightOverlayMode (PlayingSessionViewCoordinator): Independent flag for night overlay
    /// - Ctrl+L toggles UseNewLightingPipeline; overlay flag remains independent
    ///
    /// **SINGLE PROPRIETOR PATTERN:**
    /// PlayingSessionViewCoordinator owns RenderTarget2D and related graphics resources.
    /// PlayingState calls session methods; does NOT create/dispose graphics resources directly.
    /// </summary>
    public class PlayingState : IGameState
    {
        public bool UpdateBelow => false;
        public bool DrawBelow => false;
        public bool BlockInputBelow => true;

        // Phase A0.0: Profiler reference
        private RenderedFrameProfiler _renderedFrameProfiler;
        public PlayingSession Session => session;

        // Darkens whatever's already drawn (destination *= source) instead of alpha-compositing
        // over it, so the wetness overlay tints the tile actually on screen rather than needing to
        // duplicate/replace its draw.
        private static readonly BlendState MultiplyBlend = new()
        {
            ColorSourceBlend = Blend.DestinationColor,
            ColorDestinationBlend = Blend.Zero,
            AlphaSourceBlend = Blend.DestinationAlpha,
            AlphaDestinationBlend = Blend.Zero
        };

        private readonly GraphicsDevice graphicsDevice;
        private readonly StateMachine stateMachine;
        private readonly ContentManager content;
        private readonly PlayingSession session;
        private readonly PlanetSaveService saveService = new();
        private readonly InputService inputService = new();
        private readonly SpriteFont consoleFont;
        private readonly Texture2D consolePixel;
        private readonly PlayerHubUI playerHubUI;
        private bool deathStatePushed;
        private bool minimapVisible;
        private bool minimapTissueMode;
        private bool consoleOpen;
        private string consoleInput = string.Empty;
        private string consoleMessage = string.Empty;
        private readonly List<string> consoleHistory = new();
        private int consoleCursor;
        private int consoleSelectionAnchor = -1;
        private int commandHistoryIndex;
        private string commandHistoryDraft = string.Empty;
        private float consoleCursorBlinkTimer;
        private Vector2 consoleTargetWorld;
        private KeyboardState previousConsoleKeyboard;
        private float autoSaveTimer;
        private const float AutoSaveInterval = 60f;
        private const int MaxConsoleInputLength = 96;
        private const int MaxConsoleHistoryLines = 40;
        private const int HelpCommandsPerPage = 10;
        private bool showFps;
        private float fpsSmoothed;
        private bool debugPixelLightBuffer;

        // Presentation mode for lighting rendering
        private enum LightingPresentationMode
        {
            Tile,        // Legacy: ProductionTexture in WorldColorRT (fallback)
            Pixel        // Official: RawLightTexture with PixelComposite
        }
        private LightingPresentationMode presentationMode = LightingPresentationMode.Pixel;

        // P2-A1: Pixel light reconstruction mode (PIXEL mode only)
        private enum PixelLightReconstructionMode
        {
            Point,       // Nearest-neighbor: baseline coarse appearance (for A/B comparison)
            Linear       // Bilinear interpolation: smoother gradients (P2-A approved default)
        }
        private PixelLightReconstructionMode pixelLightReconstructionMode = PixelLightReconstructionMode.Linear;

        // V6 Terraria-inspired Lighting System (ETAPA 5.2)
        private V6LightingSystem v6LightingSystem;
        private V6LightSampler v6LightSampler;
        private V6LightMapRenderer v6LightMapRenderer;

        // P1D-A: Neutral sampler for pixel-mode entity lighting
        private Engine.Graphics.LightingPipeline.NeutralEntityLightSampler neutralEntityLightSampler;
        private readonly System.Diagnostics.Stopwatch fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();

        // P1C-2: PixelComposite effect for sprite-based compositing
        private Effect pixelCompositeEffect;

        // private float debugOutputCooldown;  // Used only when debug output is uncommented
        // private const float DebugOutputInterval = 2f;  // Log debug info every 2 seconds

        // Phase 2: Logging flags (one-time per session)
        private bool _hasLoggedFirstFoundationUpdate = false;
        private int _foundationUpdateCount = 0;

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine)
            : this(graphicsDevice, content, stateMachine, new PlayingSessionFactory(graphicsDevice, content).Create())
        {
            _renderedFrameProfiler = null;  // Will be set by Game1.SetPlayingStateProfiler if profiler is active
        }

        public PlayingState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine, PlayingSession session)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
            this.stateMachine = stateMachine;
            this.session = session;
            deathStatePushed = false;
            minimapVisible = false;
            minimapTissueMode = false;
            consoleOpen = false;
            commandHistoryIndex = session.ConsoleCommandHistory.Count;
            previousConsoleKeyboard = Keyboard.GetState();
            consoleFont = content.Load<SpriteFont>("ui/UIFont");
            consolePixel = new Texture2D(graphicsDevice, 1, 1);
            consolePixel.SetData(new[] { Color.White });
            playerHubUI = new PlayerHubUI(graphicsDevice, session);
            autoSaveTimer = AutoSaveInterval;

            // P1C-2: Load PixelComposite effect
            pixelCompositeEffect = content.Load<Effect>("effects/PixelComposite");

            // Initialize V6 Terraria-inspired Lighting System (ETAPA 5.2)
            var swV6 = System.Diagnostics.Stopwatch.StartNew();
            v6LightingSystem = new V6LightingSystem(session.WorldMap);
            v6LightSampler = new V6LightSampler(v6LightingSystem.LightMap, session.WorldMap);
            v6LightMapRenderer = new V6LightMapRenderer(graphicsDevice, v6LightingSystem.LightMap);

            // P1D-A: Initialize neutral sampler for pixel-mode player lighting
            neutralEntityLightSampler = new Engine.Graphics.LightingPipeline.NeutralEntityLightSampler(
                LightingPipelineCoordinator.I
            );

            // Set door occlusion callback for light transport blocking
            v6LightingSystem.SetDoorOcclusionCallback(
                (x, y) => session.DoorRuntimeSystem.IsMovementBlockingTile(x, y)
            );

            // ETAPA 7: Set artificial light sources (torches)
            v6LightingSystem.SetArtificialLightSources(
                () => GetArtificialLightSourcesForV6(session)
            );
            swV6.Stop();
        }

        private static System.Collections.Generic.IEnumerable<ArtificialLightSource> GetArtificialLightSourcesForV6(PlayingSession session)
        {
            if (session.TorchRuntimeSystem == null)
                yield break;

            var torchColor = V6LightingConfig.TorchLightColor;
            var torchIntensity = V6LightingConfig.TorchLightIntensity;
            var torchRadiusTiles = V6LightingConfig.TorchLightRadiusTiles;
            var torchCoreColor = V6LightingConfig.TorchLightCoreColor;
            var torchColorCoreExponent = V6LightingConfig.TorchLightColorCoreExponent;
            var torchColorShapingEnabled = V6LightingConfig.TorchLightColorShapingEnabled;

            // P2-B3: Read visual time once per frame for flicker calculation
            float visualTimeSeconds = session.EnvironmentSystem.SkyState.VisualTimeSeconds;

            foreach (var torch in session.TorchRuntimeSystem.Torches)
            {
                float outputMultiplier = 1.0f;

                // P2-B3: Calculate subtle flicker based on torch position and time
                if (V6LightingConfig.TorchLightFlickerEnabled)
                {
                    Vector2 lightOrigin = torch.LightOrigin;

                    float phase = lightOrigin.X * 0.013f + lightOrigin.Y * 0.017f;

                    float wave1 = MathF.Sin(visualTimeSeconds * 6.7f + phase);
                    float wave2 = MathF.Sin(visualTimeSeconds * 11.3f + phase * 1.37f);
                    float wave3 = MathF.Sin(visualTimeSeconds * 17.9f + phase * 0.73f);

                    float noise = wave1 * 0.50f + wave2 * 0.30f + wave3 * 0.20f;

                    outputMultiplier = 1.0f + noise * V6LightingConfig.TorchLightFlickerAmount;
                }

                yield return new ArtificialLightSource
                {
                    PositionPixels = torch.LightOrigin,
                    ColorRGB = torchColor,
                    CoreColorRGB = torchCoreColor,
                    ColorCoreExponent = torchColorCoreExponent,
                    UseColorShaping = torchColorShapingEnabled,
                    Intensity = torchIntensity,
                    RadiusTiles = torchRadiusTiles,
                    OutputMultiplier = outputMultiplier
                };
            }
        }

        public void SetProfiler(RenderedFrameProfiler profiler)
        {
            _renderedFrameProfiler = profiler;
        }

        private RenderedFrameProfiler.LightingMode GetProfilerLightingMode()
        {
            return RenderedFrameProfiler.LightingMode.Legacy;
        }

        public void OnEnter() { }

        public void OnExit()
        {
            saveService.Save(session);
            session.ViewCoordinator.DisposeSceneRenderTarget();
            session.ViewCoordinator.DisposeWorldColorRenderTarget();
            session.ViewCoordinator.DisposePixelLightBuffer();
            session.ViewCoordinator.DisposeLightingMaskRenderTarget();
            session.ViewCoordinator.DisposeDirectionalSunlightMap();
            session.ViewCoordinator.DisposeForegroundBlockageMap();
            session.ViewCoordinator.DisposePenumbraMap();
        }

        public void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (consoleOpen)
                consoleCursorBlinkTimer += dt;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            // Phase A0.0: Validate configuration if profiler active
            // Use PresentationParameters as canonical source (actual rendered resolution)
            if (_renderedFrameProfiler != null)
            {
                float zoom = session.Camera.Zoom;
                var mode = GetProfilerLightingMode();
                _renderedFrameProfiler.ValidateConfiguration(screenW, screenH, zoom, 0, 0, 0, mode);
            }

            InputState input = inputService.Update();
            consoleTargetWorld = session.Camera.ScreenToWorld(input.MouseScreenPosition);
            KeyboardState keyboard = Keyboard.GetState();
            bool handledConsoleThisFrame = false;

            if (input.ToggleDebugPressed)
            {
                consoleOpen = !consoleOpen;
                if (consoleOpen)
                {
                    ResetConsoleEditor();
                    consoleMessage = string.Empty;
                }

                previousConsoleKeyboard = keyboard;
                input = input.ConsumeGameplayInput();
                handledConsoleThisFrame = true;
            }

            if (consoleOpen)
            {
                HandleConsoleInput(keyboard);
                previousConsoleKeyboard = keyboard;
                input = input.ConsumeGameplayInput();
                handledConsoleThisFrame = true;
            }

            session.EnsureCurrentTissueHubActivated();

            if (!handledConsoleThisFrame && input.CancelPressed)
            {
                if (playerHubUI.IsOpen)
                {
                    playerHubUI.Close();
                    previousConsoleKeyboard = keyboard;
                    input = input.ConsumeGameplayInput();
                    handledConsoleThisFrame = true;
                }
                else if (minimapVisible)
                {
                    minimapVisible = false;
                    previousConsoleKeyboard = keyboard;
                    input = input.ConsumeGameplayInput();
                    handledConsoleThisFrame = true;
                }
                else
                {
                    previousConsoleKeyboard = keyboard;
                    stateMachine.PushState(new PauseMenuState(graphicsDevice, content, stateMachine, session));
                    return;
                }
            }

            if (!handledConsoleThisFrame && input.TogglePlayerHubPressed)
                playerHubUI.Toggle();

            if (!handledConsoleThisFrame && input.ToggleMapPressed)
                minimapVisible = !minimapVisible;

            if (!handledConsoleThisFrame && input.ToggleConstructionModePressed)
                session.ToggleConstructionMode();

            // DEBUG HOTKEY: Ctrl+Shift+M to dump metrics (PHASE 0: Validation)
            if (!handledConsoleThisFrame)
            {
                bool ctrlPressed = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
                bool shiftPressed = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
                bool mPressed = keyboard.IsKeyDown(Keys.M);

                if (ctrlPressed && shiftPressed && mPressed && !previousConsoleKeyboard.IsKeyDown(Keys.M))
                {
                    LightingPipelineCoordinator.I.DumpMetricsToConsole();
                }
            }

            // DEBUG HOTKEY: Ctrl+Alt+T to run Phase 3.2A test suite
            if (!handledConsoleThisFrame)
            {
                bool ctrlPressed = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
                bool altPressed = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);
                bool tPressed = keyboard.IsKeyDown(Keys.T);

                if (ctrlPressed && altPressed && tPressed && !previousConsoleKeyboard.IsKeyDown(Keys.T))
                {
                    // V3 tests removed (Legacy only)
                }
            }

            // DEBUG HOTKEY: Shift+P to toggle presentation mode (TILE vs PIXEL_TEST)
            if (!handledConsoleThisFrame)
            {
                bool shiftPressed = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
                bool pPressed = keyboard.IsKeyDown(Keys.P);

                if (shiftPressed && pPressed && !previousConsoleKeyboard.IsKeyDown(Keys.P))
                {
                    presentationMode = presentationMode == LightingPresentationMode.Tile
                        ? LightingPresentationMode.Pixel
                        : LightingPresentationMode.Tile;
                }
            }

            // P2-A1: Shift+O to toggle pixel light reconstruction mode (PIXEL mode only)
            if (!handledConsoleThisFrame && presentationMode == LightingPresentationMode.Pixel)
            {
                bool shiftPressed = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
                bool oPressed = keyboard.IsKeyDown(Keys.O);

                if (shiftPressed && oPressed && !previousConsoleKeyboard.IsKeyDown(Keys.O))
                {
                    pixelLightReconstructionMode = pixelLightReconstructionMode == PixelLightReconstructionMode.Point
                        ? PixelLightReconstructionMode.Linear
                        : PixelLightReconstructionMode.Point;
                }
            }

            if (minimapVisible)
            {
                WorldMinimapInteractionResult minimapInteraction = session.UpdateMinimapInteraction(
                    input,
                    screenW,
                    screenH,
                    minimapTissueMode);

                if (minimapInteraction.ToggleTissueMode)
                    minimapTissueMode = !minimapTissueMode;

                if (minimapInteraction.TravelHubIndex >= 0 &&
                    session.TryFastTravelToTissueHub(minimapInteraction.TravelHubIndex))
                {
                    minimapVisible = false;
                    session.Camera.CenterOn(session.Player.Position + new Vector2(8f, 12f), screenW, screenH);
                }

                if (minimapInteraction.ConsumedMouse)
                    input = input.ConsumeWorldMouseInput();
            }

            Vector2 mouseWorld = session.Camera.ScreenToWorld(input.MouseScreenPosition);
            session.WorkbenchRuntimeSystem.UpdateHover(mouseWorld);
            session.FurnaceRuntimeSystem.UpdateHover(mouseWorld);

            CraftTier craftTier = session.WorkbenchRuntimeSystem.GetNearbyCraftTier() | session.FurnaceRuntimeSystem.GetNearbyCraftTier();
            playerHubUI.Update(input, craftTier);
            if (playerHubUI.IsOpen && playerHubUI.ContainsMouse(input.MouseScreenPosition.ToPoint(), craftTier))
                input = input.ConsumeWorldMouseInput();

            if (!handledConsoleThisFrame && input.InteractPressed)
            {
                bool interactedWithDoor = session.DoorRuntimeSystem.TryInteract(session.Player);
                if (!interactedWithDoor &&
                    session.WorkbenchRuntimeSystem.TryInteract(session.Player, out InteractionResult interactionResult) &&
                    interactionResult.OpenPlayerHub)
                {
                    playerHubUI.Open();
                    craftTier |= interactionResult.CraftTier;
                }
                else if (!interactedWithDoor &&
                    session.FurnaceRuntimeSystem.TryInteract(session.Player, out InteractionResult furnaceInteractionResult) &&
                    furnaceInteractionResult.OpenPlayerHub)
                {
                    playerHubUI.Open();
                    craftTier |= furnaceInteractionResult.CraftTier;
                }
            }

            if (!handledConsoleThisFrame && input.CyclePowerPressed)
                session.PowerSystem.CycleNextPower();

            session.UpdateSimulationViewport(screenW, screenH);

            if (!handledConsoleThisFrame && !session.IsConstructionMode && input.ActivePowerJustPressed)
                session.PowerSystem.TryActivateCurrentPower();

            if (_renderedFrameProfiler != null)
                _renderedFrameProfiler.OnFoundationUpdateStart();

            try
            {
                session.Update(dt, input, mouseWorld, screenW, screenH);
            }
            finally
            {
                if (_renderedFrameProfiler != null)
                    _renderedFrameProfiler.OnFoundationUpdateEnd();
            }

            autoSaveTimer -= dt;
            if (autoSaveTimer <= 0f)
            {
                if (session.HasUnsavedWorldChanges)
                    saveService.Save(session);
                else
                    saveService.SavePlayerOnly(session);

                autoSaveTimer = AutoSaveInterval;
            }

            if (!session.Player.IsAlive && !deathStatePushed)
            {
                deathStatePushed = true;
                previousConsoleKeyboard = keyboard;
                stateMachine.PushState(new DeathState(graphicsDevice, content, RetryFromDeath));
                return;
            }

            session.FollowCamera(dt, screenW, screenH);

            // Phase A0.0: Emergency logging
            if (_renderedFrameProfiler != null)
                _renderedFrameProfiler.EmergencyLog_EnterPlayingStateUpdate();

            if (_renderedFrameProfiler != null)
                _renderedFrameProfiler.EmergencyLog_ExitPlayingStateUpdate();

            previousConsoleKeyboard = keyboard;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            // PHASE 0: Start frame for pipeline isolation tracking
            LightingPipelineCoordinator.I.BeginFrame();

            // Real wall-clock time between Draw calls, not GameTime - MonoGame's default fixed
            // timestep can report a near-constant ElapsedGameTime from both Update and Draw
            // regardless of actual rendering performance, which would mask real slowdowns.
            float drawDt = (float)fpsStopwatch.Elapsed.TotalSeconds;
            fpsStopwatch.Restart();
            if (drawDt > 0f)
            {
                float instantFps = 1f / drawDt;
                fpsSmoothed = fpsSmoothed <= 0f ? instantFps : MathHelper.Lerp(fpsSmoothed, instantFps, 0.1f);
            }

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            // ETAPA 5.2: Compute V6 Terraria Lighting System before rendering
            var skyColor = session.EnvironmentSystem.SkyState.AmbientLight;
            var camera = session.Camera;
            int tileSize = session.WorldMap.TileSize;
            int camTileX = (int)(camera.Position.X / tileSize);
            int camTileY = (int)(camera.Position.Y / tileSize);
            int tileWindowWidth = (screenW + tileSize - 1) / tileSize;
            int tileWindowHeight = (screenH + tileSize - 1) / tileSize;

            v6LightingSystem.Update(
                (int)camera.Position.X,
                (int)camera.Position.Y,
                screenW,
                screenH,
                tileSize,
                skyColor);

            v6LightMapRenderer.Update();
            float worldWidthPixels = session.WorldMap.PixelWidth;
            IReadOnlyList<int> visibleLoopOffsets = GetVisibleLoopOffsets(screenW, worldWidthPixels);

            // UpdateDebugOutput((float)gameTime.ElapsedGameTime.TotalSeconds, screenW, screenH, visibleLoopOffsets);  // Uncomment for periodic diagnostics

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                session.PrepareTerrainRender(graphicsDevice, screenW, screenH, worldOffset);
            }

            try
            {
                // PHASE 0: Draw world with V6 lighting
                DrawGameplayWorld(spriteBatch, screenW, screenH, visibleLoopOffsets, worldWidthPixels);
            }
            finally
            {
                // PHASE 0: End frame for pipeline isolation tracking
                LightingPipelineCoordinator.I.EndFrame();

                // Validate isolation in debug mode
#if DEBUG
                if (!LightingPipelineCoordinator.I.Metrics.IsIsolationValid)
                {
                    System.Console.WriteLine($"[WARNING] Pipeline isolation violation: {LightingPipelineCoordinator.I.Metrics}");
                }
#endif
            }
        }

        private void UpdateDebugOutput(float dt, int screenW, int screenH, IReadOnlyList<int> visibleLoopOffsets)
        {
            // Periodic debug output (disabled for production builds)
            // Uncomment to see pipeline state and performance metrics
            /*
            debugOutputCooldown -= dt;
            if (debugOutputCooldown <= 0f)
            {
                debugOutputCooldown = DebugOutputInterval;

                var sceneRenderTarget = session.ViewCoordinator.GetSceneRenderTarget();
                string rtInfo = sceneRenderTarget != null
                    ? $"{sceneRenderTarget.Width}x{sceneRenderTarget.Height}"
                    : "NULL";

                string pipelineMode = session.UseNewLightingPipeline ? "NEW" : "OLD";
                string overlayMode = session.LegacyNightOverlayMode ? "ON" : "OFF";

                System.Console.WriteLine($"[DEBUG] Pipeline={pipelineMode} | Overlay={overlayMode} | RenderTarget={rtInfo} | Screen={screenW}x{screenH} | VisibleLoops={visibleLoopOffsets.Count} | FPS={fpsSmoothed:F1}");
            }
            */
        }

        /// <summary>
        /// Draws atmospheric background: sky, sun glow, moons, parallax mountains.
        /// Used by both pipelines. Renders sky with LinearClamp (smooth gradients),
        /// sun/moons directly, and mountains with parallax effect.
        /// </summary>
        private void DrawAtmosphericBackground(SpriteBatch spriteBatch, int screenW, int screenH)
        {
            spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
            session.DrawSky(spriteBatch, screenW, screenH);
            spriteBatch.End();

            session.DrawSunGlow(spriteBatch, screenW, screenH);
            session.DrawMoons(spriteBatch, screenW, screenH);

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            session.DrawParallaxMountains(spriteBatch, screenW, screenH);
            spriteBatch.End();
        }

        /// <summary>
        /// P2-E-BG1: Draw subterranean background placeholders (Cavern and DeepCavern layers).
        /// World-Y anchored solid colored rectangles, no parallax or animation.
        /// Drawn after atmospheric background and before pixel composite.
        /// </summary>
        private void DrawSubterraneanBackgroundPlaceholders(SpriteBatch spriteBatch, int screenW, int screenH)
        {
            if (session?.LayerDefinitions == null || session.LayerDefinitions.Count == 0)
                return;

            const int CAVERN_R = 72;
            const int CAVERN_G = 52;
            const int CAVERN_B = 38;
            const int CAVERN_A = 255;
            Color cavernColor = new Color(CAVERN_R, CAVERN_G, CAVERN_B, CAVERN_A);

            const int DEEP_R = 52;
            const int DEEP_G = 48;
            const int DEEP_B = 44;
            const int DEEP_A = 255;
            Color deepCavernColor = new Color(DEEP_R, DEEP_G, DEEP_B, DEEP_A);

            int tileSize = session.WorldMap.TileSize;
            Camera2D camera = session.ViewCoordinator.Camera;

            float visibleWorldTopY = camera.Position.Y;
            float visibleWorldBottomY = camera.Position.Y + (screenH / camera.Zoom);

            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);

            // Draw Cavern layer
            foreach (var layer in session.LayerDefinitions)
            {
                if (layer.LayerType == WorldLayerType.Cavern)
                {
                    DrawLayerBackground(spriteBatch, layer, cavernColor, tileSize, visibleWorldTopY, visibleWorldBottomY, camera, screenW, screenH);
                }
                else if (layer.LayerType == WorldLayerType.DeepCavern)
                {
                    DrawLayerBackground(spriteBatch, layer, deepCavernColor, tileSize, visibleWorldTopY, visibleWorldBottomY, camera, screenW, screenH);
                }
            }

            spriteBatch.End();
        }

        private void DrawLayerBackground(
            SpriteBatch spriteBatch,
            WorldLayerDefinition layer,
            Color color,
            int tileSize,
            float visibleWorldTopY,
            float visibleWorldBottomY,
            Camera2D camera,
            int screenW,
            int screenH)
        {
            // Convert tile coordinates to world pixels
            float layerWorldTopY = layer.StartY * tileSize;
            float layerWorldBottomY = (layer.EndY + 1) * tileSize;

            // Calculate intersection with visible viewport
            float visibleTop = System.Math.Max(layerWorldTopY, visibleWorldTopY);
            float visibleBottom = System.Math.Min(layerWorldBottomY, visibleWorldBottomY);

            if (visibleBottom <= visibleTop)
                return;

            // Convert world Y coordinates to screen space
            float screenTop = (visibleTop - camera.Position.Y) * camera.Zoom;
            float screenBottom = (visibleBottom - camera.Position.Y) * camera.Zoom;

            // Clamp to screen bounds and create rectangle
            int screenY = System.Math.Max(0, (int)System.Math.Floor(screenTop));
            int screenHeight = System.Math.Min(screenH, (int)System.Math.Ceiling(screenBottom)) - screenY;

            if (screenHeight <= 0)
                return;

            Rectangle drawRect = new Rectangle(0, screenY, screenW, screenHeight);
            spriteBatch.Draw(session.ViewCoordinator.DebugPixel, drawRect, color);
        }

        /// <summary>
        /// Builds lighting mask alongside world scene in PHASE 2.
        /// Renderiza branco (1.0) para elementos ilumináveis, preto (0.0) para atmosfera/emissivos.
        /// Executado enquanto SceneRenderTarget é preenchido - os dois RenderTargets crescem juntos.
        /// </summary>
        /// <summary>
        /// Compute sun color based on time of day.
        /// Early morning (0.25/6am): deep orange/red (~5500K)
        /// Noon (0.5/12pm): bright yellow-white (~6500K)
        /// Evening (0.75/6pm): orange/red (~3500K)
        /// Night: returns black (no sunlight)
        /// </summary>
        private Vector3 ComputeSunColor(float timeOfDay01)
        {
            float normalized = timeOfDay01 % 1.0f;

            // Night: no sunlight
            if (normalized < 0.25f || normalized > 0.75f)
                return Vector3.Zero;

            // Day: 0.25 to 0.75 (6am to 6pm)
            float daylight01 = (normalized - 0.25f) / 0.5f;  // 0 = sunrise, 0.5 = noon, 1 = sunset

            // Color temperature cycle:
            // Sunrise (0.0): warm orange (1.0, 0.6, 0.2)
            // Noon (0.5): bright white (1.0, 0.95, 0.9)
            // Sunset (1.0): warm orange (1.0, 0.5, 0.1)
            Vector3 sunriseColor = new Vector3(1.0f, 0.6f, 0.2f);
            Vector3 noonColor = new Vector3(1.0f, 0.95f, 0.9f);
            Vector3 sunsetColor = new Vector3(1.0f, 0.5f, 0.1f);

            Vector3 sunColor;
            if (daylight01 < 0.5f)
            {
                // Sunrise to noon: interpolate sunrise -> noon
                float t = daylight01 * 2.0f;  // 0 to 1
                sunColor = Vector3.Lerp(sunriseColor, noonColor, t);
            }
            else
            {
                // Noon to sunset: interpolate noon -> sunset
                float t = (daylight01 - 0.5f) * 2.0f;  // 0 to 1
                sunColor = Vector3.Lerp(noonColor, sunsetColor, t);
            }

            return sunColor;
        }

        /// <summary>
        /// Compute sun direction based on time of day (0.0 = midnight, 0.5 = noon, 1.0 = midnight again).
        /// Returns angle in radians: 0 = pointing right, PI/2 = pointing down, PI = pointing left, etc.
        /// Sun moves from left to right: rises at 0.25 (6am), peaks at 0.5 (noon), sets at 0.75 (6pm).
        /// </summary>
        private float ComputeSunDirectionRadians(float timeOfDay01)
        {
            // Normalize to 0-1 for one full day cycle
            float normalized = timeOfDay01 % 1.0f;

            // Sun is above horizon from ~0.25 (6am) to ~0.75 (6pm)
            // At 0.25: sun rises on left (angle = PI)
            // At 0.5: sun at top (angle = PI/2)
            // At 0.75: sun sets on right (angle = 0)

            // Map time to angle: PI (left) -> PI/2 (top) -> 0 (right)
            float angleRadians;
            if (normalized < 0.25f || normalized > 0.75f)
            {
                // Night: sun is "below" - we can still light from below or just return a neutral value
                angleRadians = 0f;  // Neutral (no directional sunlight at night)
            }
            else
            {
                // Day: sun is visible
                // normalized goes from 0.25 to 0.75 (0.5 span)
                // Remap to 0-1 within daylight hours
                float daylight01 = (normalized - 0.25f) / 0.5f;  // 0 = sunrise, 0.5 = noon, 1 = sunset

                // Sun travels from left (PI) to right (0) during the day
                // At noon (daylight01 = 0.5), sun is at top (PI/2)
                angleRadians = MathHelper.Pi - (daylight01 * MathHelper.Pi);
            }

            return angleRadians;
        }

        /// <summary>
        /// Build penumbra map - soft shadows from partial blockage.
        /// Penumbra = areas that receive some direct light but are shadowed.
        /// For Fase 6: simple implementation using blockage blur.
        /// </summary>
        private void BuildPenumbraMap(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch,
                                      RenderTarget2D penumbraMap, RenderTarget2D foregroundBlockageMap,
                                      int screenW, int screenH)
        {
            if (penumbraMap == null || foregroundBlockageMap == null)
                return;

            graphicsDevice.SetRenderTarget(penumbraMap);

            // For Fase 6: simple approach - copy blockage and apply soft gradient
            // Blockage edges create soft penumbra zone
            spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.Opaque);
            spriteBatch.Draw(foregroundBlockageMap, Vector2.Zero, Color.White);
            spriteBatch.End();

            // Future phases can add:
            // - Gaussian blur on edges for softer shadows
            // - Distance attenuation for penumbra zone size
            // - Multiple light sources creating complex penumbra
        }

        /// <summary>
        /// Build directional sunlight map showing where sun rays reach.
        /// Considers foreground blockage and background wall transmission.
        /// For Fase 6: adds sun color variation by time of day.
        /// </summary>
        private void BuildDirectionalSunlightMap(GraphicsDevice graphicsDevice, RenderTarget2D directionalSunlightMap,
                                                  RenderTarget2D foregroundBlockageMap, float timeOfDay01)
        {
            if (directionalSunlightMap == null || foregroundBlockageMap == null)
                return;

            // Compute current sun direction
            float sunDirection = ComputeSunDirectionRadians(timeOfDay01);

            // Check if we already computed this direction (dirty flag optimization)
            if (MathF.Abs(sunDirection - session.ViewCoordinator.GetLastSunDirection()) < 0.01f)
                return;  // Sun direction unchanged, keep existing map

            session.ViewCoordinator.SetLastSunDirection(sunDirection);

            graphicsDevice.SetRenderTarget(directionalSunlightMap);

            // Compute dynamic sun color based on time of day
            Vector3 sunColorVec = ComputeSunColor(timeOfDay01);
            session.ViewCoordinator.SetLastSunColor(sunColorVec);

            // During night, no directional sunlight
            if (MathF.Abs(sunDirection) < 0.01f || (sunColorVec.X < 0.01f && sunColorVec.Y < 0.01f && sunColorVec.Z < 0.01f))
            {
                graphicsDevice.Clear(Color.Black);
            }
            else
            {
                // During day: modulate sunlight intensity by time of day
                // Peak at noon (timeOfDay01 = 0.5), zero at sunrise/sunset
                float daylight01 = (timeOfDay01 - 0.25f) / 0.5f;
                daylight01 = MathHelper.Clamp(daylight01, 0f, 1f);

                // Sun intensity: 1.0 at noon, 0 at edges
                // Use smoothstep for natural falloff
                float sunIntensity = 1.0f - (4.0f * daylight01 * daylight01 * (daylight01 - 1.0f) * (daylight01 - 1.0f));

                // Apply dynamic sun color
                byte r = (byte)MathHelper.Clamp(255 * sunColorVec.X * sunIntensity * 0.5f, 0, 255);
                byte g = (byte)MathHelper.Clamp(255 * sunColorVec.Y * sunIntensity * 0.5f, 0, 255);
                byte b = (byte)MathHelper.Clamp(255 * sunColorVec.Z * sunIntensity * 0.5f, 0, 255);
                Color sunColor = new Color(r, g, b, (byte)255);

                // Base illumination with color (before blockage)
                graphicsDevice.Clear(sunColor);

                // For Fase 6: Apply foreground blockage with penumbra
                // Where foreground blocks (white in blockage map), darken the sunlight
                // Penumbra creates soft shadow edges
            }
        }

        /// <summary>
        /// Build foreground blockage map - shows which pixels block directional sunlight.
        /// White (1.0) = blocks sunlight (solid foreground)
        /// Black (0.0) = transparent to sunlight (air, background)
        /// </summary>
        private void BuildForegroundBlockageMap(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch,
                                                RenderTarget2D foregroundBlockageMap, int screenW, int screenH,
                                                IReadOnlyList<int> visibleLoopOffsets, float worldWidthPixels)
        {
            graphicsDevice.SetRenderTarget(foregroundBlockageMap);
            graphicsDevice.Clear(Color.Black);  // Start transparent to light

            // Foreground solid geometry blocks sunlight rays
            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                // Only terrain overlay and front trees block light
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainOverlay(spriteBatch);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Front);
                spriteBatch.End();
            }
        }

        private void BuildLightingMask(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch,
                                       RenderTarget2D lightingMaskRenderTarget, int screenW, int screenH,
                                       IReadOnlyList<int> visibleLoopOffsets, float worldWidthPixels)
        {
            graphicsDevice.SetRenderTarget(lightingMaskRenderTarget);

            var neutralSampler = new NeutralEntityLightSampler(LightingPipelineCoordinator.I);

            // Render lighting mask: white for all illuminable world geometry
            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                // All illuminable world layers get white in mask
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Back);
                session.DrawBackgroundWalls(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Front);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawWater(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainBase(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainOverlay(spriteBatch);
                spriteBatch.End();

                // P1D-C: Use neutral sampler for both Enemy and WorldItem in PIXEL mode
                var enemyLightSampler = (presentationMode == LightingPresentationMode.Tile)
                    ? v6LightSampler
                    : (IEntityLightSampler)neutralEntityLightSampler;

                var worldItemLightSampler = (presentationMode == LightingPresentationMode.Tile)
                    ? v6LightSampler
                    : (IEntityLightSampler)neutralEntityLightSampler;

                // P1E-C: In PIXEL mode, draw torch body only (flames drawn separately after composite)
                bool drawTorchFlames = true;

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawLoopedWorldEntities(spriteBatch, screenW, screenH, worldOffset, enemyLightSampler, worldItemLightSampler, drawTorchFlames: drawTorchFlames);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawEntities(spriteBatch, neutralSampler);
                spriteBatch.End();

                // Tissue also receives lighting
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTissueHalo(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTissueCore(spriteBatch, screenW, screenH, worldOffset);
                session.DrawTissueFieldOverlay(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }
        }

        /// <summary>
        /// Captures all world-space visual layers to RenderTarget in unified loop over visible horizontal wraps.
        /// Layers: background walls, back trees, front trees, water, terrain base, wetness, terrain overlay,
        /// looped entities, tissue (halo/core/field overlay). Called only by new lighting pipeline (PHASE 2).
        /// Uses PointClamp for pixel-perfect rendering; MultiplyBlend for wetness and lighting combinations.
        /// </summary>
        private void DrawWorldSceneToRenderTarget(SpriteBatch spriteBatch, int screenW, int screenH,
                                                   IReadOnlyList<int> visibleLoopOffsets, float worldWidthPixels)
        {
            var neutralSampler = new NeutralEntityLightSampler(LightingPipelineCoordinator.I);

            // Render all world layers in unified loop
            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                // Background walls
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Back);
                session.DrawBackgroundWalls(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                // Front trees
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Front);
                spriteBatch.End();

                // Water
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawWater(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                // Terrain base
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainBase(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                // Wetness overlay
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: MultiplyBlend, transformMatrix: transform);
                session.DrawWetnessOverlay(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                // Terrain overlay
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainOverlay(spriteBatch);
                spriteBatch.End();

                // Looped entities (enemies, items, particles, furniture)
                // P1D-C: Use neutral sampler for both Enemy and WorldItem in PIXEL mode
                var enemyLightSampler = (presentationMode == LightingPresentationMode.Tile)
                    ? v6LightSampler
                    : (IEntityLightSampler)neutralEntityLightSampler;

                var worldItemLightSampler = (presentationMode == LightingPresentationMode.Tile)
                    ? v6LightSampler
                    : (IEntityLightSampler)neutralEntityLightSampler;

                // P1E-C: In PIXEL mode, draw torch body only (flames drawn separately after composite)
                bool drawTorchFlames = true;

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawLoopedWorldEntities(spriteBatch, screenW, screenH, worldOffset, enemyLightSampler, worldItemLightSampler, drawTorchFlames: drawTorchFlames);
                spriteBatch.End();

                // Player (with Color.White to avoid double lighting)
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawEntities(spriteBatch, neutralSampler);
                spriteBatch.End();

                // Tissue (will be multiplied - limitation documented)
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive, transformMatrix: transform);
                session.DrawTissueHalo(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawTissueCore(spriteBatch, screenW, screenH, worldOffset);
                session.DrawTissueFieldOverlay(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawTissueDebug(spriteBatch);
                spriteBatch.End();
            }
        }

        /// <summary>
        /// Applies lighting layer to RenderTarget using MultiplyBlend (destination *= source).
        /// Darkens the scene based on BFS-propagated light levels. Called only by new lighting pipeline (PHASE 3).
        /// Uses LinearClamp for smooth light sampling across world wraps.
        /// </summary>
        /// <summary>
        /// Applies lighting shader composition when compositing to backbuffer.
        /// Combines scene with ambient + directional lighting using mask.
        /// Shader receives: SceneTexture, LightingMaskTexture, AmbientLightMap, DirectionalSunlightMap
        /// Output: scene with selective lighting (mask 0.0 = no lighting, 1.0 = full) to backbuffer
        /// This is called AFTER SetRenderTarget(null), so output goes to screen.
        /// </summary>
        private void ApplyLightingComposition(GraphicsDevice gd, SpriteBatch spriteBatch,
                                              RenderTarget2D sceneRenderTarget, RenderTarget2D lightingMaskRenderTarget,
                                              Texture2D lightingMapTexture, RenderTarget2D directionalSunlightMap,
                                              Effect composeLightingEffect)
        {
            if (sceneRenderTarget == null)
                return;

            // If any required resource is missing, fallback to simple draw
            if (lightingMaskRenderTarget == null || lightingMapTexture == null ||
                directionalSunlightMap == null || composeLightingEffect == null)
            {
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
                spriteBatch.Draw(sceneRenderTarget, Vector2.Zero, Color.White);
                spriteBatch.End();
                return;
            }

            // Configure shader for lighting composition
            composeLightingEffect.CurrentTechnique = composeLightingEffect.Techniques["ComposeLighting"];
            composeLightingEffect.Parameters["MatrixTransform"]?.SetValue(Matrix.Identity);
            composeLightingEffect.Parameters["LightingIntensity"]?.SetValue(1.0f);
            composeLightingEffect.Parameters["SunlightIntensity"]?.SetValue(0.5f);

            // Draw scene (which already has BFS lighting + directional sunlight applied in PHASE 2)
            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
            spriteBatch.Draw(sceneRenderTarget, Vector2.Zero, Color.White);
            spriteBatch.End();
        }

        /// <summary>
        /// Composites RenderTarget (complete lit scene) to backbuffer at screen-space (0,0).
        /// Uses PointClamp to preserve pixel-perfect scaling and AlphaBlend to composite.
        /// Called once per frame by new lighting pipeline (PHASE 4) after phases 1-3 complete.
        /// After this, screen-space overlays (HUD, effects) render directly to backbuffer.
        /// </summary>
        private void ComposeRenderTargetToBackbuffer(SpriteBatch spriteBatch, RenderTarget2D sceneRenderTarget)
        {
            if (sceneRenderTarget == null)
            {
                System.Console.WriteLine("[PIPELINE] ✗ FATAL: Cannot compose NULL RenderTarget");
                return;
            }

            // System.Console.WriteLine($"[PIPELINE]   Compositing RenderTarget {sceneRenderTarget.Width}x{sceneRenderTarget.Height} to backbuffer...");

            // Composite to backbuffer (screen-space, PointClamp)
            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
            spriteBatch.Draw(sceneRenderTarget, Vector2.Zero, Color.White);
            spriteBatch.End();

            // System.Console.WriteLine("[PIPELINE]   ✓ RenderTarget composited (Begin/Draw/End completed)");
        }

private void DrawGameplayWorld(SpriteBatch spriteBatch, int screenW, int screenH,
                                            IReadOnlyList<int> visibleLoopOffsets, float worldWidthPixels)
        {
            // P1A: WORLD COLOR RENDER TARGET FOUNDATION
            // Correct order: render world to RT first, then composite back to backbuffer WITH sky.

            // PHASE 0A: Prepare WorldColorRenderTarget
            session.ViewCoordinator.EnsureWorldColorRenderTarget(graphicsDevice, screenW, screenH);

            // PHASE 0B: Render world content to WorldColorRenderTarget (preserves alpha)
            graphicsDevice.SetRenderTarget(session.ViewCoordinator.GetWorldColorRenderTarget());
            graphicsDevice.Clear(Color.Transparent);

            // P1C-1: Control whether ProductionTexture is applied to WorldColorRT
            bool applyProductionTexture = (presentationMode == LightingPresentationMode.Tile);
            DrawWorldContentToRenderTarget(spriteBatch, screenW, screenH, visibleLoopOffsets, worldWidthPixels, applyProductionTexture);

            // PHASE 0C: P1B - Build PixelLightBuffer from V6 coarse lighting (offscreen preparation)
            BuildPixelLightBuffer(spriteBatch, screenW, screenH, visibleLoopOffsets, worldWidthPixels);

            // PHASE 1: Return to backbuffer and draw complete frame
            graphicsDevice.SetRenderTarget(null);

            // PHASE 1A: Clear backbuffer
            graphicsDevice.Clear(Color.Black);

            // PHASE 1B: Draw atmospheric background (sky, sun, moons, parallax)
            DrawAtmosphericBackground(spriteBatch, screenW, screenH);

            // P2-E-BG1: Draw subterranean background placeholders
            DrawSubterraneanBackgroundPlaceholders(spriteBatch, screenW, screenH);

            // PHASE 1C: Composite WorldColorRenderTarget onto backbuffer (over sky)
            if (presentationMode == LightingPresentationMode.Pixel && pixelCompositeEffect != null)
            {
                // P1C-2: Use PixelComposite effect for PIXEL_TEST mode
                // P1C-2B: Set MatrixTransform with orthographic projection (screen-space coordinates)
                Matrix projection = Matrix.CreateOrthographicOffCenter(
                    left: 0,
                    right: screenW,
                    bottom: screenH,
                    top: 0,
                    zNearPlane: 0,
                    zFarPlane: -1
                );
                pixelCompositeEffect.Parameters["MatrixTransform"]?.SetValue(projection);

                // P1C-3B: Set PixelLightBuffer as effect parameter
                // Effect handles texture register assignment internally
                pixelCompositeEffect.Parameters["LightBuffer"]?.SetValue(
                    session.ViewCoordinator.GetPixelLightBuffer()
                );

                spriteBatch.Begin(
                    samplerState: SamplerState.PointClamp,
                    blendState: BlendState.AlphaBlend,
                    effect: pixelCompositeEffect
                );
                spriteBatch.Draw(
                    session.ViewCoordinator.GetWorldColorRenderTarget(),
                    new Rectangle(0, 0, screenW, screenH),
                    Color.White
                );
                spriteBatch.End();
            }
            else
            {
                // TILE mode: baseline drawing without effect
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
                spriteBatch.Draw(session.ViewCoordinator.GetWorldColorRenderTarget(), Vector2.Zero, Color.White);
                spriteBatch.End();
            }

            // PHASE 2: Screen-space overlays (rain, night overlay)
            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
            session.DrawRainFront(spriteBatch, screenW, screenH);
            // Night overlay: TILE mode only (legacy behavior), PIXEL mode has no overlay
            if (presentationMode == LightingPresentationMode.Tile && session.LegacyNightOverlayMode)
                session.DrawNightOverlay(spriteBatch, screenW, screenH);
            spriteBatch.End();

            // PHASE 3: HUD (screen-space)
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            session.DrawHud(spriteBatch, screenW, screenH);
            if (minimapVisible)
                session.DrawMinimap(spriteBatch, screenW, screenH, minimapTissueMode);
            playerHubUI.Draw(spriteBatch, session.WorkbenchRuntimeSystem.GetNearbyCraftTier() | session.FurnaceRuntimeSystem.GetNearbyCraftTier());
            if (showFps)
                DrawFpsCounter(spriteBatch);
            if (consoleOpen)
                DrawConsole(spriteBatch, screenW);
            spriteBatch.End();

            // DEBUG: Presentation mode label (Shift+P to toggle)
            string modeLabel = presentationMode == LightingPresentationMode.Tile ? "TILE" : "PIXEL";
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            spriteBatch.DrawString(consoleFont, $"LIGHTING: {modeLabel} (Shift+P)", new Vector2(10, 10), Color.Yellow);

            // Show reconstruction mode diagnostic when in Pixel mode
            if (presentationMode == LightingPresentationMode.Pixel)
            {
                string reconstructionLabel = pixelLightReconstructionMode == PixelLightReconstructionMode.Point ? "POINT" : "LINEAR";
                spriteBatch.DrawString(consoleFont, $"PIXEL LIGHT: {reconstructionLabel} (Shift+O)", new Vector2(10, 25), Color.Cyan);
            }

            spriteBatch.End();

            // DEBUG: P1B PixelLightBuffer visualization
            if (debugPixelLightBuffer && session.ViewCoordinator.GetPixelLightBuffer() != null)
            {
                spriteBatch.Begin(samplerState: SamplerState.PointClamp);
                spriteBatch.Draw(session.ViewCoordinator.GetPixelLightBuffer(), Vector2.Zero, Color.White);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp);
                spriteBatch.DrawString(consoleFont, "P1B DEBUG: PIXEL LIGHT BUFFER (active)", new Vector2(10, 30), Color.Yellow);
                spriteBatch.End();
            }
        }

        private void DrawWorldContentToRenderTarget(SpriteBatch spriteBatch, int screenW, int screenH,
                                                     IReadOnlyList<int> visibleLoopOffsets, float worldWidthPixels,
                                                     bool applyProductionTexture = true)
        {
            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Back);
                session.DrawBackgroundWalls(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Front);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawWater(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainBase(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                // P2-C5: Surface night tint for Grass (PIXEL mode only, applied before wetness)
                if (presentationMode == LightingPresentationMode.Pixel)
                {
                    spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                    session.DrawSurfaceNightTint(spriteBatch, screenW, screenH, worldOffset);
                    spriteBatch.End();
                }

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: MultiplyBlend, transformMatrix: transform);
                session.DrawWetnessOverlay(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawWorldLitObjects(spriteBatch);
                spriteBatch.End();

                // P1C-1: Apply ProductionTexture only if requested
                if (applyProductionTexture && v6LightMapRenderer.ProductionTexture != null)
                {
                    int bufferOriginTileX = v6LightingSystem.LightMap.BufferOriginTileX;
                    int bufferOriginTileY = v6LightingSystem.LightMap.BufferOriginTileY;
                    int bufferWidth = v6LightingSystem.LightMap.BufferWidth;
                    int bufferHeight = v6LightingSystem.LightMap.BufferHeight;
                    int tileSize = session.WorldMap.TileSize;
                    int worldWidthTiles = (int)(worldWidthPixels / tileSize);

                    int bufferLoopIndex = bufferOriginTileX / worldWidthTiles;
                    if (bufferLoopIndex == loopIndex)
                    {
                        int bufferXInLoop = (bufferOriginTileX % worldWidthTiles) * tileSize;
                        Rectangle destRect = new Rectangle(
                            bufferXInLoop,
                            bufferOriginTileY * tileSize,
                            bufferWidth * tileSize,
                            bufferHeight * tileSize
                        );

                        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: MultiplyBlend, transformMatrix: transform);
                        spriteBatch.Draw(v6LightMapRenderer.ProductionTexture, destRect, Color.White);
                        spriteBatch.End();
                    }
                }

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainOverlay(spriteBatch);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                // P1D-C: Use neutral sampler for both Enemy and WorldItem in PIXEL mode
                var enemyLightSampler = (presentationMode == LightingPresentationMode.Tile)
                    ? v6LightSampler
                    : (IEntityLightSampler)neutralEntityLightSampler;

                var worldItemLightSampler = (presentationMode == LightingPresentationMode.Tile)
                    ? v6LightSampler
                    : (IEntityLightSampler)neutralEntityLightSampler;

                // P1E-C: In PIXEL mode, draw torch body only (flames drawn separately after composite)
                bool drawTorchFlames = true;

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawLoopedWorldEntities(spriteBatch, screenW, screenH, worldOffset, enemyLightSampler, worldItemLightSampler, drawTorchFlames: drawTorchFlames);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive, transformMatrix: transform);
                session.DrawTissueHalo(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawTissueCore(spriteBatch, screenW, screenH, worldOffset);
                session.DrawTissueFieldOverlay(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            // P1D-A: Use neutral sampler in PIXEL mode to avoid double-lighting Player
            var playerLightSampler = (presentationMode == LightingPresentationMode.Tile)
                ? (IEntityLightSampler)v6LightSampler
                : neutralEntityLightSampler;

            spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: session.Camera.GetViewMatrix());
            session.DrawEntities(spriteBatch, playerLightSampler);
            spriteBatch.End();

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawInteriorFocusOverlay(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawTissueDebug(spriteBatch);
                spriteBatch.End();
            }
        }

        private void BuildPixelLightBuffer(SpriteBatch spriteBatch, int screenW, int screenH,
                                          IReadOnlyList<int> visibleLoopOffsets, float worldWidthPixels)
        {
            // P2-C1: Build PixelLightBuffer from V6 coarse RawLightTexture
            // Upscale V6 (~247×141 tiles) with real RGB data to pixel resolution (1920×1080)
            // Uses LINEAR/POINT reconstruction mode (P2-A2)

            session.ViewCoordinator.EnsurePixelLightBuffer(graphicsDevice, screenW, screenH);
            graphicsDevice.SetRenderTarget(session.ViewCoordinator.GetPixelLightBuffer());
            graphicsDevice.Clear(Color.White);

            if (v6LightMapRenderer.RawLightTexture != null)
            {
                int bufferOriginTileX = v6LightingSystem.LightMap.BufferOriginTileX;
                int bufferOriginTileY = v6LightingSystem.LightMap.BufferOriginTileY;
                int bufferWidth = v6LightingSystem.LightMap.BufferWidth;
                int bufferHeight = v6LightingSystem.LightMap.BufferHeight;
                int tileSize = session.WorldMap.TileSize;
                int worldWidthTiles = (int)(worldWidthPixels / tileSize);

                for (int i = 0; i < visibleLoopOffsets.Count; i++)
                {
                    int loopIndex = visibleLoopOffsets[i];
                    float worldOffset = loopIndex * worldWidthPixels;
                    Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                    int bufferLoopIndex = bufferOriginTileX / worldWidthTiles;
                    if (bufferLoopIndex == loopIndex)
                    {
                        int bufferXInLoop = (bufferOriginTileX % worldWidthTiles) * tileSize;
                        Rectangle destRect = new Rectangle(
                            bufferXInLoop,
                            bufferOriginTileY * tileSize,
                            bufferWidth * tileSize,
                            bufferHeight * tileSize
                        );

                        // P2-A2: Reconstruction sampler mode (POINT vs LINEAR)
                        SamplerState reconstructionSampler =
                            pixelLightReconstructionMode == PixelLightReconstructionMode.Point
                                ? SamplerState.PointClamp
                                : SamplerState.LinearClamp;

                        spriteBatch.Begin(samplerState: reconstructionSampler, blendState: BlendState.Opaque, transformMatrix: transform);
                        spriteBatch.Draw(v6LightMapRenderer.RawLightTexture, destRect, Color.White);
                        spriteBatch.End();
                    }
                }
            }

            graphicsDevice.SetRenderTarget(null);
        }

        /// <summary>
        /// Composition-neutral rendering: renders the scene without any lighting applied.
        /// Used for composition validation and baseline comparison.
        /// </summary>
        private void DrawWithCompositionNeutralPipeline(SpriteBatch spriteBatch, int screenW, int screenH,
                                                        IReadOnlyList<int> visibleLoopOffsets, float worldWidthPixels)
        {

            // Clear backbuffer
            graphicsDevice.Clear(Color.Black);

            // Atmospheric background (sky, sun, moons, mountains)
            DrawAtmosphericBackground(spriteBatch, screenW, screenH);

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Back);
                session.DrawBackgroundWalls(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Front);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawWater(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainBase(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: MultiplyBlend, transformMatrix: transform);
                session.DrawWetnessOverlay(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                // PHASE 0: NO LIGHTING DRAWS HERE
                // - Skipped: session.DrawWorldLighting (legacy light map multiply)

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawTerrainOverlay(spriteBatch);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                // P1D-C: Use neutral sampler for both Enemy and WorldItem in PIXEL mode
                var enemyLightSampler = (presentationMode == LightingPresentationMode.Tile)
                    ? v6LightSampler
                    : (IEntityLightSampler)neutralEntityLightSampler;

                var worldItemLightSampler = (presentationMode == LightingPresentationMode.Tile)
                    ? v6LightSampler
                    : (IEntityLightSampler)neutralEntityLightSampler;

                // P1E-C: In PIXEL mode, draw torch body only (flames drawn separately after composite)
                bool drawTorchFlames = true;

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                session.DrawLoopedWorldEntities(spriteBatch, screenW, screenH, worldOffset, enemyLightSampler, worldItemLightSampler, drawTorchFlames: drawTorchFlames);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive, transformMatrix: transform);
                session.DrawTissueHalo(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawTissueCore(spriteBatch, screenW, screenH, worldOffset);
                session.DrawTissueFieldOverlay(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            var neutralSampler = new NeutralEntityLightSampler(LightingPipelineCoordinator.I);

            spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: session.Camera.GetViewMatrix());
            session.DrawEntities(spriteBatch, neutralSampler);
            spriteBatch.End();

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawInteriorFocusOverlay(spriteBatch, screenW, screenH, worldOffset);
                spriteBatch.End();
            }

            for (int i = 0; i < visibleLoopOffsets.Count; i++)
            {
                int loopIndex = visibleLoopOffsets[i];
                float worldOffset = loopIndex * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

                spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                session.DrawTissueDebug(spriteBatch);
                spriteBatch.End();
            }

            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
            session.DrawRainFront(spriteBatch, screenW, screenH);
            // PHASE 0: NO NIGHT OVERLAY DRAWN HERE (legacy only)

            spriteBatch.End();

            // PHASE 0: NO TORCH GLOW DRAWN HERE (legacy only)

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            session.DrawHud(spriteBatch, screenW, screenH);
            if (minimapVisible)
                session.DrawMinimap(spriteBatch, screenW, screenH, minimapTissueMode);
            playerHubUI.Draw(spriteBatch, session.WorkbenchRuntimeSystem.GetNearbyCraftTier() | session.FurnaceRuntimeSystem.GetNearbyCraftTier());
            if (showFps)
                DrawFpsCounter(spriteBatch);
            if (consoleOpen)
                DrawConsole(spriteBatch, screenW);
            spriteBatch.End();
        }

        private void DrawFpsCounter(SpriteBatch spriteBatch)
        {
            string text = $"FPS: {fpsSmoothed:0}";

            // Measure text size to position it properly
            var textSize = consoleFont.MeasureString(text);
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            // Bottom-right corner with 8px margin
            Vector2 position = new Vector2(screenW - textSize.X - 8f, screenH - textSize.Y - 8f);

            spriteBatch.DrawString(consoleFont, text, position + Vector2.One, Color.Black);
            spriteBatch.DrawString(consoleFont, text, position, Color.White);
        }

        private void HandleConsoleInput(KeyboardState keyboard)
        {
            consoleCursor = System.Math.Clamp(consoleCursor, 0, consoleInput.Length);
            bool control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

            foreach (Keys key in keyboard.GetPressedKeys())
            {
                if (previousConsoleKeyboard.IsKeyDown(key))
                    continue;

                if (key == Keys.Escape)
                {
                    consoleOpen = false;
                    return;
                }

                if (key == Keys.Enter)
                {
                    ExecuteConsoleCommand();
                    return;
                }

                if (control && key == Keys.A)
                {
                    consoleSelectionAnchor = 0;
                    consoleCursor = consoleInput.Length;
                    ResetConsoleCursorBlink();
                    continue;
                }

                if (control && key == Keys.C)
                {
                    CopyConsoleSelection();
                    continue;
                }

                if (control && key == Keys.X)
                {
                    CutConsoleSelection();
                    continue;
                }

                if (control && key == Keys.V)
                {
                    PasteConsoleClipboard();
                    continue;
                }

                if (key == Keys.Up)
                {
                    NavigateCommandHistory(-1);
                    continue;
                }

                if (key == Keys.Down)
                {
                    NavigateCommandHistory(1);
                    continue;
                }

                if (key == Keys.Left)
                {
                    MoveConsoleCursor(consoleCursor - 1, shift);
                    continue;
                }

                if (key == Keys.Right)
                {
                    MoveConsoleCursor(consoleCursor + 1, shift);
                    continue;
                }

                if (key == Keys.Home)
                {
                    MoveConsoleCursor(0, shift);
                    continue;
                }

                if (key == Keys.End)
                {
                    MoveConsoleCursor(consoleInput.Length, shift);
                    continue;
                }

                if (key == Keys.Back)
                {
                    BackspaceConsoleInput();
                    continue;
                }

                if (key == Keys.Delete)
                {
                    DeleteConsoleInput();
                    continue;
                }

                if (control)
                    continue;

                if (TryGetConsoleCharacter(keyboard, key, out char character))
                    InsertConsoleText(character.ToString());
            }
        }

        private bool HasConsoleSelection => consoleSelectionAnchor >= 0 && consoleSelectionAnchor != consoleCursor;

        private void ResetConsoleEditor()
        {
            consoleInput = string.Empty;
            consoleCursor = 0;
            consoleSelectionAnchor = -1;
            commandHistoryIndex = session.ConsoleCommandHistory.Count;
            commandHistoryDraft = string.Empty;
            ResetConsoleCursorBlink();
        }

        private void ResetConsoleCursorBlink()
        {
            consoleCursorBlinkTimer = 0f;
        }

        private void MoveConsoleCursor(int target, bool selecting)
        {
            if (selecting)
            {
                if (consoleSelectionAnchor < 0)
                    consoleSelectionAnchor = consoleCursor;
            }
            else
            {
                consoleSelectionAnchor = -1;
            }

            consoleCursor = System.Math.Clamp(target, 0, consoleInput.Length);
            if (consoleSelectionAnchor == consoleCursor)
                consoleSelectionAnchor = -1;
            ResetConsoleCursorBlink();
        }

        private void NavigateCommandHistory(int direction)
        {
            IReadOnlyList<string> history = session.ConsoleCommandHistory;
            if (history.Count == 0)
                return;

            if (commandHistoryIndex < 0 || commandHistoryIndex > history.Count)
                commandHistoryIndex = history.Count;
            if (commandHistoryIndex == history.Count && direction < 0)
                commandHistoryDraft = consoleInput;

            commandHistoryIndex = System.Math.Clamp(commandHistoryIndex + direction, 0, history.Count);
            consoleInput = commandHistoryIndex == history.Count
                ? commandHistoryDraft
                : history[commandHistoryIndex];
            consoleCursor = consoleInput.Length;
            consoleSelectionAnchor = -1;
            ResetConsoleCursorBlink();
        }

        private void CopyConsoleSelection()
        {
            string text = GetSelectedConsoleText();
            ClipboardService.TrySetText(text);
            ResetConsoleCursorBlink();
        }

        private void CutConsoleSelection()
        {
            CopyConsoleSelection();
            if (HasConsoleSelection)
                DeleteConsoleSelection();
            else if (consoleInput.Length > 0)
                ReplaceConsoleRange(0, consoleInput.Length, string.Empty);
        }

        private void PasteConsoleClipboard()
        {
            ClipboardService.TryGetText(out string clipboardText);
            InsertConsoleText(SanitizeConsolePaste(clipboardText));
        }

        private string GetSelectedConsoleText()
        {
            if (!HasConsoleSelection)
                return consoleInput;

            GetConsoleSelectionRange(out int start, out int length);
            return consoleInput.Substring(start, length);
        }

        private void BackspaceConsoleInput()
        {
            if (DeleteConsoleSelection())
                return;
            if (consoleCursor <= 0)
                return;

            ReplaceConsoleRange(consoleCursor - 1, 1, string.Empty);
        }

        private void DeleteConsoleInput()
        {
            if (DeleteConsoleSelection())
                return;
            if (consoleCursor >= consoleInput.Length)
                return;

            ReplaceConsoleRange(consoleCursor, 1, string.Empty);
        }

        private bool DeleteConsoleSelection()
        {
            if (!HasConsoleSelection)
                return false;

            GetConsoleSelectionRange(out int start, out int length);
            ReplaceConsoleRange(start, length, string.Empty);
            return true;
        }

        private void InsertConsoleText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            int start = consoleCursor;
            int removeLength = 0;
            if (HasConsoleSelection)
                GetConsoleSelectionRange(out start, out removeLength);

            int available = MaxConsoleInputLength - (consoleInput.Length - removeLength);
            if (available <= 0)
                return;
            if (text.Length > available)
                text = text[..available];

            ReplaceConsoleRange(start, removeLength, text);
        }

        private void ReplaceConsoleRange(int start, int length, string replacement)
        {
            consoleInput = consoleInput.Remove(start, length).Insert(start, replacement);
            consoleCursor = start + replacement.Length;
            consoleSelectionAnchor = -1;
            commandHistoryIndex = session.ConsoleCommandHistory.Count;
            commandHistoryDraft = string.Empty;
            ResetConsoleCursorBlink();
        }

        private void GetConsoleSelectionRange(out int start, out int length)
        {
            start = System.Math.Min(consoleCursor, consoleSelectionAnchor);
            int end = System.Math.Max(consoleCursor, consoleSelectionAnchor);
            length = end - start;
        }

        private static string SanitizeConsolePaste(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            StringBuilder sanitized = new(text.Length);
            bool previousWasSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '\r' || character == '\n' || character == '\t')
                    character = ' ';
                if (character < ' ' || character > '~')
                    continue;
                if (character == ' ' && previousWasSpace)
                    continue;

                sanitized.Append(character);
                previousWasSpace = character == ' ';
            }

            return sanitized.ToString();
        }

        private void ExecuteConsoleCommand()
        {
            string command = consoleInput.Trim();
            if (command.Length == 0)
                return;

            AddConsoleHistory("> " + command);
            ResetConsoleEditor();
            if (!command.StartsWith("/", System.StringComparison.Ordinal))
            {
                SetConsoleMessage("Comandos devem comecar com /. Digite /help");
                return;
            }

            session.AddConsoleCommand(command);
            string commandBody = command[1..].Trim();
            string normalized = commandBody.ToLowerInvariant();
            if (normalized == "help" || normalized.StartsWith("help ", System.StringComparison.Ordinal))
            {
                int page = 1;
                string[] helpParts = commandBody.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (helpParts.Length > 1 && int.TryParse(helpParts[1], out int parsedPage))
                    page = parsedPage;

                ShowConsoleHelp(page);
                return;
            }

            if (normalized == "debugfly")
            {
                bool enabled = session.ToggleDebugFly();
                SetConsoleMessage(enabled
                    ? "Debug fly ativado: W/A/S/D para voar e atravessar blocos"
                    : "Debug fly desativado: movimento normal restaurado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "debugfly on")
            {
                session.SetDebugFly(true);
                SetConsoleMessage("Debug fly ativado: W/A/S/D para voar e atravessar blocos");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "debugfly off")
            {
                session.SetDebugFly(false);
                SetConsoleMessage("Debug fly desativado: movimento normal restaurado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuevisual on")
            {
                session.SetTissueVisualEnabled(true);
                SetConsoleMessage("Tissue cosmic web ativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuevisual off")
            {
                session.SetTissueVisualEnabled(false);
                SetConsoleMessage("Tissue cosmic web desativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuevisual")
            {
                SetConsoleMessage("Uso: /tissuevisual on ou /tissuevisual off");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuefield on")
            {
                session.SetTissueFieldVisualEnabled(true);
                SetConsoleMessage("TissueField real ativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuefield off")
            {
                session.SetTissueFieldVisualEnabled(false);
                SetConsoleMessage("TissueField real desativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "tissuefield")
            {
                SetConsoleMessage("Uso: /tissuefield on ou /tissuefield off");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "fps on")
            {
                showFps = true;
                SetConsoleMessage("Contador de FPS ativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "fps off")
            {
                showFps = false;
                SetConsoleMessage("Contador de FPS desativado");
                consoleInput = string.Empty;
                return;
            }

            if (normalized == "fps")
            {
                SetConsoleMessage("Uso: /fps on ou /fps off");
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteGetCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteSpawnCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteTickCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteTimeCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteEventCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteWaterCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteTissuePulseCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteTissueMutationCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteGrassCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteDebugCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            if (TryExecuteWorldCommand(commandBody))
            {
                consoleInput = string.Empty;
                return;
            }

            SetConsoleMessage($"Comando desconhecido: {command}");
            consoleInput = string.Empty;
        }

        private void SetConsoleMessage(string message)
        {
            consoleMessage = message;
            if (!string.IsNullOrWhiteSpace(message))
                AddConsoleHistory(message);
        }

        private void AddConsoleHistory(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            consoleHistory.Add(line);
            while (consoleHistory.Count > MaxConsoleHistoryLines)
                consoleHistory.RemoveAt(0);
        }

        private static readonly string[] ConsoleCommands =
        {
            "/help [pagina]",
            "/debugfly [on|off]",
            "/tissuevisual on|off",
            "/tissuefield on|off",
            "/fps on|off",
            "/tissuepulse",
            "/tissuepulse <speed|trail|fade|memory|curve|intensity|node> <valor>",
            "/tissuepulse reset",
            "/tissuedamage <valor> [raio]",
            "/tissueheal <valor> [raio]",
            "/tissuecorrupt <valor> [raio]",
            "/tissuememory <valor> [raio]",
            "/tissueflow <valor> [raio]",
            "/tissueremove [raio]",
            "/tissuereset [raio]",
            "/get <item> [quantidade]",
            "/get list",
            "/spawn <entidade> (reservado)",
            "/tick status",
            "/tick",
            "/tick speed <1..16>",
            "/tick pause",
            "/tick resume",
            "/tick reset",
            "/tick step [1..600]",
            "/time",
            "/time status",
            "/time day",
            "/time night",
            "/time dawn",
            "/time sunrise",
            "/time noon",
            "/time sunset",
            "/time midnight",
            "/event status",
            "/event rain start|stop",
            "/event eclipse start|stop",
            "/event clear",
            "/water status",
            "/water tune",
            "/water tune slow|balanced|fast",
            "/water tune <tps|fall|side|search|cells> <valor>",
            "/water place [raio]",
            "/water drain [raio]",
            "/water clear",
            "/grass grow [1..10000]",
            "/debug ticks",
            "/debug light",
            "/world save"
        };

        private void ShowConsoleHelp(int page)
        {
            consoleHistory.Clear();

            int totalPages = System.Math.Max(1, (int)System.Math.Ceiling(ConsoleCommands.Length / (float)HelpCommandsPerPage));
            page = System.Math.Clamp(page, 1, totalPages);

            consoleMessage = $"Comandos disponiveis (pagina {page}/{totalPages})";
            AddConsoleHistory($"Comandos disponiveis (pagina {page}/{totalPages}):");

            int startIndex = (page - 1) * HelpCommandsPerPage;
            int endIndex = System.Math.Min(startIndex + HelpCommandsPerPage, ConsoleCommands.Length);
            for (int i = startIndex; i < endIndex; i++)
                AddConsoleHistory(ConsoleCommands[i]);

            if (page < totalPages)
                AddConsoleHistory($"/help {page + 1} para mais comandos");
        }

        private bool TryExecuteTickCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("tick", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1 || parts[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
            {
                SetConsoleMessage(session.WorldTicksPaused
                    ? $"Tick speed: {session.WorldTickTimeScale:0.##}x (paused)"
                    : $"Tick speed: {session.WorldTickTimeScale:0.##}x");
                return true;
            }

            if (parts[1].Equals("reset", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTickTimeScale(1f);
                session.SetWorldTicksPaused(false);
                SetConsoleMessage("Tick speed: 1x");
                return true;
            }

            if (parts[1].Equals("pause", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTicksPaused(true);
                SetConsoleMessage("World ticks pausados");
                return true;
            }

            if (parts[1].Equals("resume", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTicksPaused(false);
                SetConsoleMessage($"Tick speed: {session.WorldTickTimeScale:0.##}x");
                return true;
            }

            if (parts[1].Equals("step", System.StringComparison.OrdinalIgnoreCase))
            {
                int cycles = 1;
                if (parts.Length >= 3 && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out cycles))
                {
                    SetConsoleMessage("Uso: /tick step [1..600]");
                    return true;
                }

                cycles = System.Math.Clamp(cycles, 1, 600);
                session.StepWorldTicks(cycles);
                SetConsoleMessage($"Ticks manuais: {cycles}");
                return true;
            }

            if (parts[1].Equals("speed", System.StringComparison.OrdinalIgnoreCase) &&
                parts.Length >= 3 &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float speed))
            {
                session.SetWorldTickTimeScale(speed);
                session.SetWorldTicksPaused(false);
                SetConsoleMessage($"Tick speed: {session.WorldTickTimeScale:0.##}x");
                return true;
            }

            SetConsoleMessage("Uso: /tick speed 1..16, /tick step [n], /tick pause, /tick resume, /tick reset, /tick status");
            return true;
        }

        private bool TryExecuteTimeCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("time", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1 || parts[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("day", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.DayCommandTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("night", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.NightCommandTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("dawn", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.PreDawnStartTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("sunrise", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.SunriseStartTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("noon", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.NoonStartTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("sunset", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.SunsetStartTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            if (parts[1].Equals("midnight", System.StringComparison.OrdinalIgnoreCase))
            {
                session.SetWorldTimeOfDay(WorldDayNightCycle.DeepNightStartTimeOfDay01);
                ShowTimeStatus();
                return true;
            }

            SetConsoleMessage("Uso: /time status|day|night|dawn|sunrise|noon|sunset|midnight");
            return true;
        }

        private void ShowTimeStatus()
        {
            string paused = session.WorldTicksPaused ? " paused" : string.Empty;
            SetConsoleMessage(
                $"Horario:{session.WorldClockText24h} fase:{session.WorldTimePhase} ciclo:{session.WorldCycleIndex} " +
                $"noite:{session.WorldNightStrength:0.00} speed:{session.WorldTickTimeScale:0.##}x{paused}");
            AddConsoleHistory(session.WorldEnvironmentStatusText);
        }

        private bool TryExecuteEventCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("event", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1 || parts[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowEventStatus();
                return true;
            }

            if (parts[1].Equals("clear", System.StringComparison.OrdinalIgnoreCase))
            {
                session.EnvironmentSystem.ClearEvents();
                SetConsoleMessage("Eventos ambientais limpos");
                ShowEventStatus();
                return true;
            }

            if (parts.Length < 3)
            {
                ShowEventUsage();
                return true;
            }

            string target = parts[1].ToLowerInvariant();
            string action = parts[2].ToLowerInvariant();
            if (target == "rain")
            {
                if (action == "start")
                {
                    session.EnvironmentSystem.ForceRain();
                    SetConsoleMessage("Chuva forcada: pressagio iniciado");
                    ShowEventStatus();
                    return true;
                }

                if (action == "stop")
                {
                    session.EnvironmentSystem.StopRain();
                    SetConsoleMessage("Chuva dissipando");
                    ShowEventStatus();
                    return true;
                }
            }

            if (target == "eclipse")
            {
                if (action == "start")
                {
                    session.EnvironmentSystem.ForceEclipse();
                    SetConsoleMessage("Eclipse forcado: transicao iniciada");
                    ShowEventStatus();
                    return true;
                }

                if (action == "stop")
                {
                    session.EnvironmentSystem.StopEclipse();
                    SetConsoleMessage("Eclipse dissipando");
                    ShowEventStatus();
                    return true;
                }
            }

            ShowEventUsage();
            return true;
        }

        private void ShowEventStatus()
        {
            AddConsoleHistory("Eventos: " + session.WorldEnvironmentStatusText);
            AddConsoleHistory(
                $"Weather rain:{session.WeatherState.RainIntensity:0.00} cloud:{session.WeatherState.CloudCover:0.00} " +
                $"wind:{session.WeatherState.Wind:0.00} wet:{session.WeatherState.Wetness:0.00}");
            AddConsoleHistory(
                $"Tissue correction:{session.TissueCycleState.CorrectionStrength:0.00} " +
                $"stage:{session.TissueCycleState.Stage}");
        }

        private void ShowEventUsage()
        {
            SetConsoleMessage("Uso: /event status, /event rain start|stop, /event eclipse start|stop, /event clear");
        }

        private bool TryExecuteWaterCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("water", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1 || parts[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
            {
                SetConsoleMessage(session.GetWaterStatusText());
                return true;
            }

            if (parts[1].Equals("tune", System.StringComparison.OrdinalIgnoreCase))
            {
                ExecuteWaterTuneCommand(parts);
                return true;
            }

            if (parts[1].Equals("clear", System.StringComparison.OrdinalIgnoreCase))
            {
                int removed = session.ClearWater();
                SetConsoleMessage($"Water clear: {removed} tiles removidos");
                return true;
            }

            if (parts[1].Equals("place", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseWaterRadius(parts, defaultRadius: 1, out int radiusTiles))
                {
                    SetConsoleMessage("Uso: /water place [raio 0..16]");
                    return true;
                }

                int placed = session.PlaceWaterAtMouse(consoleTargetWorld, radiusTiles);
                SetConsoleMessage($"Water place: {placed} tiles");
                return true;
            }

            if (parts[1].Equals("drain", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseWaterRadius(parts, defaultRadius: 2, out int radiusTiles))
                {
                    SetConsoleMessage("Uso: /water drain [raio 0..16]");
                    return true;
                }

                int removed = session.DrainWaterAtMouse(consoleTargetWorld, radiusTiles);
                SetConsoleMessage($"Water drain: {removed} tiles");
                return true;
            }

            SetConsoleMessage("Uso: /water status, /water tune, /water place [raio], /water drain [raio], /water clear");
            return true;
        }

        private void ExecuteWaterTuneCommand(string[] parts)
        {
            if (parts.Length == 2 ||
                (parts.Length == 3 && parts[2].Equals("status", System.StringComparison.OrdinalIgnoreCase)))
            {
                SetConsoleMessage(session.GetWaterTuningText());
                return;
            }

            if (parts.Length == 3)
            {
                session.TryApplyWaterTuningPreset(parts[2], out string presetMessage);
                SetConsoleMessage(presetMessage);
                return;
            }

            if (parts.Length == 4 &&
                int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                session.TrySetWaterTuningValue(parts[2], value, out string tuneMessage);
                SetConsoleMessage(tuneMessage);
                return;
            }

            SetConsoleMessage("Uso: /water tune, /water tune slow|balanced|fast, /water tune <tps|fall|side|search|cells> <valor>");
        }

        private static bool TryParseWaterRadius(string[] parts, int defaultRadius, out int radiusTiles)
        {
            radiusTiles = defaultRadius;
            if (parts.Length <= 2)
                return true;

            if (parts.Length > 3 ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out radiusTiles))
            {
                return false;
            }

            radiusTiles = System.Math.Clamp(radiusTiles, 0, 16);
            return true;
        }

        private bool TryExecuteGrassCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("grass", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length >= 2 && parts[1].Equals("grow", System.StringComparison.OrdinalIgnoreCase))
            {
                int samples = 256;
                if (parts.Length >= 3 && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out samples))
                {
                    SetConsoleMessage("Uso: /grass grow [samples]");
                    return true;
                }

                samples = System.Math.Clamp(samples, 1, 10000);
                int grown = session.ForceGrassGrowthSamples(samples);
                SetConsoleMessage($"Grass grow: {grown}/{samples}");
                return true;
            }

            SetConsoleMessage("Uso: /grass grow [samples]");
            return true;
        }

        private bool TryExecuteTissuePulseCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("tissuepulse", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1 ||
                parts[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowTissuePulseStatus();
                return true;
            }

            if (parts[1].Equals("reset", System.StringComparison.OrdinalIgnoreCase))
            {
                TissueConfig.Resonance.ResetTuning();
                SetConsoleMessage("Tissue pulse: parametros restaurados");
                ShowTissuePulseStatus();
                return true;
            }

            if (parts[1].Equals("help", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowTissuePulseUsage();
                return true;
            }

            if (parts.Length < 3 ||
                !float.TryParse(parts[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) ||
                float.IsInfinity(value))
            {
                ShowTissuePulseUsage();
                return true;
            }

            string parameter = parts[1].ToLowerInvariant();
            float appliedValue;
            switch (parameter)
            {
                case "speed":
                    appliedValue = TissueConfig.Resonance.SetPulseSpeed(value);
                    break;
                case "trail":
                    appliedValue = TissueConfig.Resonance.SetPulseTrailLength(value);
                    break;
                case "fade":
                    appliedValue = TissueConfig.Resonance.SetPulseFadePower(value);
                    break;
                case "memory":
                    appliedValue = TissueConfig.Resonance.SetMemoryLifetime(value);
                    break;
                case "curve":
                    appliedValue = TissueConfig.Resonance.SetMemoryFadeCurve(value);
                    break;
                case "intensity":
                    appliedValue = TissueConfig.Resonance.SetMemoryIntensity(value);
                    break;
                case "node":
                case "nodeafterglow":
                case "nodelifetime":
                    parameter = "node";
                    appliedValue = TissueConfig.Resonance.SetNodeAfterglowLifetime(value);
                    break;
                default:
                    ShowTissuePulseUsage();
                    return true;
            }

            SetConsoleMessage($"Tissue pulse {parameter}: {appliedValue:0.###}");
            return true;
        }

        private bool TryExecuteTissueMutationCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            string operation = parts[0].ToLowerInvariant();
            bool hasValue = operation is
                "tissuedamage" or
                "tissueheal" or
                "tissuecorrupt" or
                "tissuememory" or
                "tissueflow";
            bool isSimpleOperation = operation is "tissueremove" or "tissuereset";
            if (!hasValue && !isSimpleOperation)
                return false;

            float value = 0f;
            int radiusPartIndex;
            if (hasValue)
            {
                if (parts.Length < 2 || parts.Length > 3 ||
                    !float.TryParse(
                        parts[1].Replace(',', '.'),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out value) ||
                    float.IsNaN(value) ||
                    float.IsInfinity(value) ||
                    value < 0f ||
                    value > 1f ||
                    (operation != "tissueflow" && value <= 0f))
                {
                    ShowTissueMutationUsage(operation);
                    return true;
                }

                radiusPartIndex = 2;
            }
            else
            {
                if (parts.Length > 2)
                {
                    ShowTissueMutationUsage(operation);
                    return true;
                }

                radiusPartIndex = 1;
            }

            int radius = 0;
            if (parts.Length > radiusPartIndex &&
                (!int.TryParse(
                    parts[radiusPartIndex],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out radius) ||
                 radius < 0 || radius > 16))
            {
                ShowTissueMutationUsage(operation);
                return true;
            }

            Point target = session.WorldMap.WorldToTile(consoleTargetWorld);
            int centerX = session.WorldMap.WrapTileX(target.X);
            int changedCount = 0;
            int radiusSquared = radius * radius;
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                int tileY = target.Y + offsetY;
                if (tileY < 0 || tileY >= session.WorldMap.Height)
                    continue;

                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (radius > 0 &&
                        ((offsetX * offsetX) + (offsetY * offsetY)) > radiusSquared)
                    {
                        continue;
                    }

                    int tileX = centerX + offsetX;
                    bool changed = operation switch
                    {
                        "tissuedamage" => session.TissueMutations.DamageTile(tileX, tileY, value),
                        "tissueheal" => session.TissueMutations.RestoreTile(tileX, tileY, value),
                        "tissuecorrupt" => session.TissueMutations.AddCorruption(tileX, tileY, value),
                        "tissuememory" => session.TissueMutations.AddMemory(tileX, tileY, value),
                        "tissueflow" => session.TissueMutations.SetFlow(tileX, tileY, value),
                        "tissueremove" => session.TissueMutations.RemoveTissue(tileX, tileY),
                        "tissuereset" => session.TissueMutations.ResetTile(tileX, tileY),
                        _ => false
                    };
                    if (changed)
                        changedCount++;
                }
            }

            SetConsoleMessage(
                $"{operation}: {changedCount} tile(s) alterado(s) em ({centerX}, {target.Y}), raio {radius}");
            return true;
        }

        private void ShowTissueMutationUsage(string operation)
        {
            if (operation is "tissueremove" or "tissuereset")
            {
                SetConsoleMessage($"Uso: /{operation} [raio 0..16]");
                return;
            }

            SetConsoleMessage($"Uso: /{operation} <valor 0..1> [raio 0..16]");
        }

        private bool TryExecuteGetCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("get", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1)
            {
                ShowGetUsage();
                return true;
            }

            if (parts.Length == 2 && parts[1].Equals("list", System.StringComparison.OrdinalIgnoreCase))
            {
                ShowGetItemList();
                return true;
            }

            int identifierPartCount = parts.Length - 1;
            int quantity = 1;
            if (parts.Length >= 3 &&
                int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedQuantity))
            {
                quantity = parsedQuantity;
                identifierPartCount--;
            }

            if (quantity < 1 || quantity > 9999 || identifierPartCount <= 0)
            {
                ShowGetUsage();
                return true;
            }

            string itemIdentifier = string.Join(' ', parts, 1, identifierPartCount);
            if (!ItemDefinitions.TryResolveCommandId(itemIdentifier, out ItemDefinition definition))
            {
                SetConsoleMessage($"Item desconhecido: {itemIdentifier}. Use /get list");
                return true;
            }

            int added = session.StoreItem(definition.Id, quantity, preferInventory: true);
            string commandId = ItemDefinitions.GetCommandId(definition.Id);
            if (added == quantity)
            {
                SetConsoleMessage($"Adicionado: {added}x {definition.Name} [{commandId}]");
            }
            else if (added > 0)
            {
                SetConsoleMessage($"Inventario cheio: adicionado {added}/{quantity}x {definition.Name} [{commandId}]");
            }
            else
            {
                SetConsoleMessage($"Inventario cheio: nenhum {definition.Name} foi adicionado");
            }

            return true;
        }

        private void ShowGetUsage()
        {
            SetConsoleMessage("Uso: /get <item> [quantidade 1..9999]");
            AddConsoleHistory("Use /get list para ver todos os IDs disponíveis");
        }

        private void ShowGetItemList()
        {
            SetConsoleMessage("Itens disponiveis para /get:");
            foreach (ItemDefinition definition in ItemDefinitions.GetAll())
            {
                string stack = definition.Stackable
                    ? $"stack {definition.MaxStack}"
                    : "nao empilhavel";
                AddConsoleHistory(
                    $"{ItemDefinitions.GetCommandId(definition.Id)} (#{(byte)definition.Id}) - {definition.Name}, {stack}");
            }
        }

        private bool TryExecuteSpawnCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("spawn", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length == 1)
            {
                SetConsoleMessage("Uso: /spawn <entidade>. Nenhuma entidade debug registrada ainda");
                return true;
            }

            string identifier = string.Join(' ', parts, 1, parts.Length - 1).ToLowerInvariant();
            if (identifier == "enemy" || identifier == "enemy fsm")
            {
                SpawnDebugEnemy(Nyvorn.Source.Gameplay.Entities.Enemies.EnemyConfig.Default);
                SetConsoleMessage("Inimigo spawnado perto do jogador");
                return true;
            }

            if (identifier == "enemy signature" || identifier == "enemy utility")
            {
                SpawnDebugEnemy(Nyvorn.Source.Gameplay.Entities.Enemies.EnemyConfig.Signature);
                SetConsoleMessage("Inimigo assinatura (alcance de percepcao maior) spawnado perto do jogador");
                return true;
            }

            if (ItemDefinitions.TryResolveCommandId(identifier, out ItemDefinition item))
            {
                SetConsoleMessage(
                    $"{item.Name} e item. Use /get {ItemDefinitions.GetCommandId(item.Id)} [quantidade]");
                return true;
            }

            SetConsoleMessage($"Entidade desconhecida: {identifier}. Use /spawn enemy ou /spawn enemy signature");
            return true;
        }

        private void SpawnDebugEnemy(Nyvorn.Source.Gameplay.Entities.Enemies.EnemyConfig enemyConfig)
        {
            Vector2 spawnPosition = session.Player.Position + new Vector2(48f, 0f);
            Nyvorn.Source.Gameplay.Entities.Enemies.Enemy enemy = session.EntityRuntimeSystem.EnemyRespawnController.SpawnAt(spawnPosition, enemyConfig);
            session.Enemies.Add(enemy);
        }

        private void ShowTissuePulseStatus()
        {
            SetConsoleMessage(
                $"Pulse speed:{TissueConfig.Resonance.PulseSpeed:0.##} " +
                $"trail:{TissueConfig.Resonance.PulseTrailLength:0.##} " +
                $"fade:{TissueConfig.Resonance.PulseFadePower:0.##}");
            AddConsoleHistory(
                $"Memory lifetime:{TissueConfig.Resonance.MemoryLifetime:0.##} " +
                $"curve:{TissueConfig.Resonance.MemoryFadeCurve:0.##} " +
                $"intensity:{TissueConfig.Resonance.MemoryIntensity:0.##} " +
                $"node:{TissueConfig.Resonance.NodeAfterglowLifetime:0.##}");
        }

        private void ShowTissuePulseUsage()
        {
            SetConsoleMessage("Uso: /tissuepulse <speed|trail|fade|memory|curve|intensity|node> <valor>");
            AddConsoleHistory("Use /tissuepulse para status ou /tissuepulse reset para restaurar");
        }

        private bool TryExecuteDebugCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("debug", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length >= 2 && parts[1].Equals("ticks", System.StringComparison.OrdinalIgnoreCase))
            {
                string paused = session.WorldTicksPaused ? " paused" : string.Empty;
                SetConsoleMessage(
                    $"F:{session.FastTickCount} M:{session.MediumTickCount} S:{session.SlowTickCount} " +
                    $"samples:{session.LastRandomTileSampleCount} grass:{session.LastGrassGrowthCount} " +
                    $"chunks:{session.ActiveSimulationChunks.Count} speed:{session.WorldTickTimeScale:0.##}x{paused}");
                return true;
            }

            SetConsoleMessage("Uso: /debug ticks");
            return true;
        }

        private bool TryExecuteWorldCommand(string command)
        {
            string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts[0].Equals("world", System.StringComparison.OrdinalIgnoreCase))
                return false;

            if (parts.Length >= 2 && parts[1].Equals("save", System.StringComparison.OrdinalIgnoreCase))
            {
                saveService.Save(session);
                autoSaveTimer = AutoSaveInterval;
                SetConsoleMessage("World saved");
                return true;
            }

            SetConsoleMessage("Uso: /world save");
            return true;
        }

        private void DrawConsole(SpriteBatch spriteBatch, int screenWidth)
        {
            int lineHeight = (int)System.MathF.Ceiling(consoleFont.LineSpacing * 0.9f);
            int inputHeight = lineHeight + 10;
            int inputY = graphicsDevice.PresentationParameters.BackBufferHeight - inputHeight;
            Rectangle inputBounds = new Rectangle(0, inputY, screenWidth, inputHeight);

            int historyCount = consoleHistory.Count;
            if (historyCount > 0)
            {
                int historyHeight = (lineHeight * historyCount) + 8;
                Rectangle historyBounds = new Rectangle(0, System.Math.Max(0, inputY - historyHeight), screenWidth, historyHeight);
                spriteBatch.Draw(consolePixel, historyBounds, Color.Black * 0.58f);

                int firstLineY = historyBounds.Y + 4;
                for (int i = 0; i < historyCount; i++)
                {
                    string line = consoleHistory[i];
                    Color color = line.StartsWith("> ", System.StringComparison.Ordinal) ? new Color(180, 220, 255) : new Color(220, 235, 238);
                    spriteBatch.DrawString(consoleFont, line, new Vector2(10, firstLineY + (i * lineHeight)), color);
                }
            }

            spriteBatch.Draw(consolePixel, inputBounds, Color.Black * 0.82f);
            spriteBatch.Draw(consolePixel, new Rectangle(0, inputBounds.Y, screenWidth, 2), new Color(143, 211, 255));

            const string promptPrefix = "> ";
            Vector2 promptPosition = new(10, inputBounds.Y + 5);
            if (HasConsoleSelection)
            {
                GetConsoleSelectionRange(out int selectionStart, out int selectionLength);
                float selectionX = promptPosition.X + consoleFont.MeasureString(promptPrefix + consoleInput[..selectionStart]).X;
                float selectionWidth = consoleFont.MeasureString(consoleInput.Substring(selectionStart, selectionLength)).X;
                spriteBatch.Draw(
                    consolePixel,
                    new Rectangle(
                        (int)System.MathF.Floor(selectionX),
                        inputBounds.Y + 4,
                        System.Math.Max(2, (int)System.MathF.Ceiling(selectionWidth)),
                        lineHeight),
                    new Color(55, 115, 170, 190));
            }

            spriteBatch.DrawString(consoleFont, promptPrefix + consoleInput, promptPosition, Color.White);
            if ((consoleCursorBlinkTimer % 1f) < 0.58f)
            {
                float cursorX = promptPosition.X + consoleFont.MeasureString(promptPrefix + consoleInput[..consoleCursor]).X;
                spriteBatch.Draw(
                    consolePixel,
                    new Rectangle((int)System.MathF.Round(cursorX), inputBounds.Y + 6, 2, lineHeight - 4),
                    new Color(190, 235, 255));
            }

            const string shortcutHint = "Ctrl+A/C/X/V  Shift+Left/Right  Up/Down: history";
            float hintWidth = consoleFont.MeasureString(shortcutHint).X;
            float hintX = screenWidth - hintWidth - 10f;
            if (consoleInput.Length == 0 && hintX > 420f)
            {
                spriteBatch.DrawString(
                    consoleFont,
                    shortcutHint,
                    new Vector2(hintX, inputBounds.Y + 5),
                    new Color(135, 165, 178));
            }
        }

        private static bool TryGetConsoleCharacter(KeyboardState keyboard, Keys key, out char character)
        {
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);

            if (key >= Keys.A && key <= Keys.Z)
            {
                char baseChar = (char)('a' + (key - Keys.A));
                character = shift ? char.ToUpperInvariant(baseChar) : baseChar;
                return true;
            }

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                character = (char)('0' + (key - Keys.D0));
                return true;
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                character = (char)('0' + (key - Keys.NumPad0));
                return true;
            }

            character = key switch
            {
                Keys.Space => ' ',
                Keys.OemMinus => shift ? '_' : '-',
                Keys.OemPeriod => '.',
                Keys.OemComma => ',',
                Keys.OemQuestion => '/',
                _ => '\0'
            };

            return character != '\0';
        }

        private IReadOnlyList<int> GetVisibleLoopOffsets(int screenWidth, float worldWidthPixels)
        {
            if (worldWidthPixels <= 0f)
                return new[] { 0 };

            float viewWidth = screenWidth / session.Camera.Zoom;
            float left = session.Camera.Position.X;
            float right = left + viewWidth;
            int minLoop = (int)System.MathF.Floor(left / worldWidthPixels);
            int maxLoop = (int)System.MathF.Floor((right - 0.001f) / worldWidthPixels);
            int count = System.Math.Max(1, maxLoop - minLoop + 1);
            int[] offsets = new int[count];

            for (int i = 0; i < count; i++)
                offsets[i] = minLoop + i;

            return offsets;
        }

        private void RetryFromDeath()
        {
            session.RespawnPlayerAtWorldCenter();
            deathStatePushed = false;
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            session.Camera.CenterOn(session.Player.Position + new Vector2(8f, 12f), screenW, screenH);
            saveService.SavePlayerOnly(session);
            autoSaveTimer = AutoSaveInterval;
            stateMachine.PopState();
        }
    }
}
