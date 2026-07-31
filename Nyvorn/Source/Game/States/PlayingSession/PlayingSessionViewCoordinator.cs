using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using Nyvorn.Source.Engine.Graphics.LightingV2;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Physics.Liquids;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Gameplay.Combat;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.Entities.Enemies;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.Powers;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.Gameplay.World.Interiors;
using Nyvorn.Source.Gameplay.World.Objects;
using Nyvorn.Source.Gameplay.World.Particles;
using Nyvorn.Source.Gameplay.World.Simulation;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Decorations;
using Nyvorn.Source.World.Tissue;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    /// <summary>
    /// PlayingSessionViewCoordinator manages all graphics resources and rendering state for the playing session.
    ///
    /// **SINGLE PROPRIETOR PATTERN:**
    /// This class is the sole owner and lifecycle manager for:
    /// - sceneRenderTarget: RenderTarget2D for new lighting pipeline (PHASES 1-3 capture, PHASE 4 composite)
    /// - lightTexture: Computed BFS lighting map
    /// - glowTexture: Torch/item glow overlay
    ///
    /// PlayingState and other render clients do NOT create or dispose these resources; they call coordinator methods.
    ///
    /// **RESOURCE ALLOCATION STRATEGY:**
    /// - RenderTargets use grow-only allocation (never shrink, only expand to capacity)
    /// - Disposed via DisposeSceneRenderTarget() called from PlayingState.OnExit()
    /// - EnsureSceneRenderTarget() validates dimensions and recreates if necessary
    /// </summary>
    public sealed class PlayingSessionViewCoordinator
    {
        private const float EntityDrawPaddingPixels = 48f;
        private const int SimulationChunkBorder = 1;
        private const float DefaultCameraZoom = 2f;
        private const float InteriorCameraZoom = 3f;
        private const float CameraZoomSnapDistance = 0.01f;
        private const float CameraZoomInLerpSpeed = 4.5f;
        private const float CameraZoomOutLerpSpeed = 2.1f;
        private const float CameraFocusLerpSpeed = 2.8f;
        private const float CameraReturnFocusLerpSpeed = 3.6f;
        private const float CameraReturnSnapDistance = 1.25f;
        private const int SandHighlightCellSize = 17;
        private static readonly Color SandPixelColor = new Color(252, 222, 156);
        private static readonly Color SandTopEdgeColor = new Color(207, 179, 120);
        private static readonly Color SandHighlightPixelColor = new Color(255, 252, 232);
        private static readonly Color WaterPixelColor = new Color(34, 128, 205) * 0.78f;

        private Texture2D lightTexture;
        private int lightTextureCapacityWidth;
        private int lightTextureCapacityHeight;
        private Color[] lightTextureBuffer = System.Array.Empty<Color>();
        private int lightTextureActiveWidth;
        private int lightTextureActiveHeight;
        private int lightTextureOriginTileX;
        private int lightTextureOriginTileY;

        private Texture2D glowTexture;
        private int glowTextureCapacityWidth;
        private int glowTextureCapacityHeight;
        private Color[] glowTextureBuffer = System.Array.Empty<Color>();
        private int glowTextureActiveWidth;
        private int glowTextureActiveHeight;
        private int glowTextureOriginTileX;
        private int glowTextureOriginTileY;

        private RenderTarget2D sceneRenderTarget;
        private int sceneRenderTargetCapacityWidth;
        private int sceneRenderTargetCapacityHeight;

        private RenderTarget2D lightingMaskRenderTarget;
        private int lightingMaskCapacityWidth;
        private int lightingMaskCapacityHeight;

        private RenderTarget2D directionalSunlightMap;
        private int directionalSunlightCapacityWidth;
        private int directionalSunlightCapacityHeight;
        private float lastSunDirection = float.NaN;
        private Vector3 lastSunColor = Vector3.One;

        private RenderTarget2D penumbraMap;
        private int penumbraCapacityWidth;
        private int penumbraCapacityHeight;

        private RenderTarget2D foregroundBlockageMap;
        private int foregroundBlockageCapacityWidth;
        private int foregroundBlockageCapacityHeight;

        // PHASE 1: V3 Neutral Composition RenderTargets
        private RenderTarget2D v3AtmosphereRenderTarget;
        private int v3AtmosphereCapacityWidth;
        private int v3AtmosphereCapacityHeight;

        private RenderTarget2D v3WorldRenderTarget;
        private int v3WorldCapacityWidth;
        private int v3WorldCapacityHeight;

        private RenderTarget2D v3EntitiesRenderTarget;
        private int v3EntitiesCapacityWidth;
        private int v3EntitiesCapacityHeight;

        private RenderTarget2D v3EmissiveRenderTarget;
        private int v3EmissiveCapacityWidth;
        private int v3EmissiveCapacityHeight;

        private readonly List<WorldChunkCoord> activeSimulationChunks = new();
        private Vector2 smoothedCameraTarget;
        private bool hasSmoothedCameraTarget;
        private bool wasFocusingInterior;
        private bool returningFromInterior;

        // Lighting V2 (PHASE 1: structure only, no visual changes)
        private LightingV2Resources lightingV2Resources;
        private LightingV2System lightingV2System;
        private LightingV2Renderer lightingV2Renderer;
        private Engine.Graphics.LightingPipeline.LightingPipelineMode lightingPipelineMode = Engine.Graphics.LightingPipeline.LightingPipelineMode.Legacy;

        // Phase 2: LightingV3Foundation (Debug disabled - moved out of #if DEBUG for validation)
        private Engine.Graphics.LightingPipeline.LightingV3Foundation lightingV3Foundation;
        private Engine.Graphics.LightingPipeline.LightingV3DebugController lightingV3DebugController;
        private Engine.Graphics.LightingPipeline.LightingV3DebugRenderer lightingV3DebugRenderer;

        /// <summary>
        /// Lighting pipeline mode (OFFICIAL: New Pipeline):
        /// - false (Legacy): Old pipeline (direct backbuffer rendering, night overlay always on)
        /// - true (New): New pipeline (RenderTarget-based, night overlay optional) [DEFAULT]
        ///
        /// Toggle with Ctrl+L hotkey
        /// </summary>
        public bool UseNewLightingPipeline { get; set; } = true;

        /// <summary>
        /// Night overlay mode (black screen overlay for darkness):
        /// - true: Draw night overlay (legacy mode behavior)
        /// - false: Skip night overlay (new mode handles darkness via sky/lightmap) [DEFAULT]
        ///
        /// Toggle with Ctrl+L hotkey (toggles together with UseNewLightingPipeline)
        /// </summary>
        public bool LegacyNightOverlayMode { get; set; } = false;

        public required WorldMap WorldMap { get; init; }
        public SandSystem SandSystem { get; set; }
        public required Player Player { get; init; }
        public required List<Enemy> Enemies { get; init; }
        public required List<WorldItem> WorldItems { get; init; }
        public required Camera2D Camera { get; init; }
        public required Texture2D DebugPixel { get; init; }
        public LiquidSystem LiquidSystem { get; set; }
        public required WorldHealthBarRenderer HealthBarRenderer { get; init; }
        public required HudRenderer HudRenderer { get; init; }
        public required WorldMinimapRenderer WorldMinimapRenderer { get; init; }
        public required ElyraSkyRenderer ElyraSkyRenderer { get; init; }
        public required Effect ComposeLightingEffect { get; init; }
        public required WorldTilePreviewRenderer TilePreviewRenderer { get; init; }
        public required PowerHUD PowerHUD { get; init; }
        public required TissueNetwork TissueNetwork { get; init; }
        public required TissueNetworkRenderer TissueNetworkRenderer { get; init; }
        public required TissueFieldOverlayRenderer TissueFieldOverlayRenderer { get; init; }
        public required IReadOnlySet<int> ActivatedTissueHubKeys { get; init; }
        public required InteriorFocusSystem InteriorFocusSystem { get; init; }
        public required BlockParticleSystem BlockParticleSystem { get; init; }
        public required DamageNumberSystem DamageNumberSystem { get; init; }
        public WorkbenchRuntimeSystem WorkbenchRuntimeSystem { get; init; }
        public FurnaceRuntimeSystem FurnaceRuntimeSystem { get; init; }
        public TorchRuntimeSystem TorchRuntimeSystem { get; init; }
        public DoorRuntimeSystem DoorRuntimeSystem { get; init; }
        public ChairRuntimeSystem ChairRuntimeSystem { get; init; }
        public TableRuntimeSystem TableRuntimeSystem { get; init; }
        public PlatformRuntimeSystem PlatformRuntimeSystem { get; init; }
        public WorldObjectRegistry WorldObjectRegistry { get; init; }
        public FurnitureCollisionSystem FurnitureCollisionSystem { get; init; }

        public IReadOnlyList<WorldChunkCoord> ActiveSimulationChunks => activeSimulationChunks;

        // Lighting V2 public accessors
        public Engine.Graphics.LightingPipeline.LightingPipelineMode LightingPipelineMode
        {
            get => lightingPipelineMode;
            set => lightingPipelineMode = value;
        }

        public LightingV2System LightingV2System => lightingV2System;
        public LightingV2Renderer LightingV2Renderer => lightingV2Renderer;
        public LightingV2Resources LightingV2Resources => lightingV2Resources;

        // Phase 2: LightingV3Foundation accessors
        public Engine.Graphics.LightingPipeline.LightingV3Foundation LightingV3Foundation => lightingV3Foundation;
        public Engine.Graphics.LightingPipeline.LightingV3DebugController LightingV3DebugController => lightingV3DebugController;
        public Engine.Graphics.LightingPipeline.LightingV3DebugRenderer LightingV3DebugRenderer => lightingV3DebugRenderer;

        /// <summary>
        /// Initialize Lighting V2 subsystem with required data providers and graphics device.
        /// Called once after PlayingSessionViewCoordinator is fully constructed.
        /// </summary>
        public void InitializeLightingV2(GraphicsDevice graphicsDevice, WorldDayNightCycle dayNightCycle, WorldEnvironmentSystem environmentSystem)
        {
            if (graphicsDevice == null)
                return;

            // Create resource owner
            lightingV2Resources = new LightingV2Resources(graphicsDevice);

            // Create data adapters
            var worldDataProvider = new WorldDataAdapter(WorldMap);
            var sunCycleProvider = new SunCycleAdapter(dayNightCycle, environmentSystem);
            var localLightRegistry = new LocalLightAdapter(TorchRuntimeSystem);

            // Create settings and debug state
            var settings = new LightingV2Settings();
            var debugState = new LightingV2DebugState();

            // Create lighting system
            lightingV2System = new LightingV2System(worldDataProvider, sunCycleProvider, localLightRegistry, settings, debugState);

            // Create renderer
            lightingV2Renderer = new LightingV2Renderer(graphicsDevice, lightingV2Resources, debugState);

            // Default to Legacy mode
            lightingPipelineMode = Engine.Graphics.LightingPipeline.LightingPipelineMode.Legacy;
        }

        /// <summary>
        /// Initialize Phase 2: LightingV3Foundation with real geometry provider.
        /// Called once after PlayingSessionViewCoordinator is constructed.
        /// </summary>
        public void InitializeLightingV3(GraphicsDevice graphicsDevice)
        {
            if (graphicsDevice == null)
                return;

            // Create geometry provider adapter
            var worldDataProvider = new WorldDataAdapter(WorldMap);
            var geometryAdapter = new Engine.Graphics.LightingPipeline.WorldDataGeometryAdapter(worldDataProvider);

            // Create foundation
            lightingV3Foundation = new Engine.Graphics.LightingPipeline.LightingV3Foundation(geometryAdapter);
            System.Console.WriteLine("[LightingV3Probe] Foundation initialized");

            // Create debug system (always, for validation)
            lightingV3DebugController = new Engine.Graphics.LightingPipeline.LightingV3DebugController();
            lightingV3DebugRenderer = new Engine.Graphics.LightingPipeline.LightingV3DebugRenderer(graphicsDevice);
        }

        /// <summary>
        /// Update Phase 2 foundation (only in V3 mode).
        /// Called from PlayingState.Update.
        /// </summary>
        public void UpdateLightingV3(float cameraX, float cameraY, int logicalWidth, int logicalHeight)
        {
            if (lightingV3Foundation == null)
                return;

            lightingV3Foundation.Update(cameraX, cameraY, logicalWidth, logicalHeight, WorldMap.TileSize);
        }

        /// <summary>
        /// Dispose Phase 2 resources.
        /// Called from PlayingState.OnExit.
        /// </summary>
        public void DisposeLightingV3()
        {
            lightingV3Foundation?.Dispose();
            lightingV3DebugRenderer?.Dispose();
        }

        /// <summary>
        /// Ensure Lighting V2 resources are allocated for the current viewport size.
        /// Called every frame when V2 pipeline is active.
        /// </summary>
        public void EnsureLightingV2Resources(int logicalViewWidth, int logicalViewHeight)
        {
            if (lightingV2Resources == null)
                return;

            lightingV2Resources.EnsureResources(logicalViewWidth, logicalViewHeight);
        }

        /// <summary>
        /// Handle viewport resize for Lighting V2.
        /// </summary>
        public void OnLightingV2Resize(int logicalViewWidth, int logicalViewHeight)
        {
            if (lightingV2Renderer == null)
                return;

            lightingV2Renderer.OnResize(logicalViewWidth, logicalViewHeight);
        }

        /// <summary>
        /// PHASE 0: Prepare scene RenderTarget for V2 rendering.
        /// Call before rendering world content to V2 scene.
        /// </summary>
        public void BeginLightingV2SceneRender()
        {
            if (lightingV2Renderer == null)
                return;

            lightingV2Renderer.Phase0_BeginSceneRender();
        }

        /// <summary>
        /// End V2 scene rendering and restore backbuffer as render target.
        /// Call after all world-space content is rendered to the scene RenderTarget.
        /// </summary>
        public void EndLightingV2SceneRender()
        {
            if (lightingV2Renderer == null)
                return;

            lightingV2Renderer.EndSceneRender();
        }

        /// <summary>
        /// PHASE 5: Composite the V2 scene RenderTarget to backbuffer.
        /// Call to present the rendered scene with neutral lighting.
        /// </summary>
        public void CompositeLightingV2SceneToBackbuffer(SpriteBatch spriteBatch, Rectangle destRect)
        {
            if (lightingV2Renderer == null)
                return;

            lightingV2Renderer.Phase5_CompositeSceneToBackbuffer(spriteBatch, destRect);
        }

        /// <summary>
        /// Enable/disable debug visualization of the V2 scene RenderTarget.
        /// </summary>
        public bool LightingV2DebugSceneRenderTarget
        {
            get => lightingV2Renderer?.DebugSceneRenderTarget ?? false;
            set
            {
                if (lightingV2Renderer != null)
                    lightingV2Renderer.DebugSceneRenderTarget = value;
            }
        }

        /// <summary>
        /// Dispose Lighting V2 resources.
        /// Called from PlayingState.OnExit() after other resource cleanup.
        /// </summary>
        public void DisposeLightingV2()
        {
            lightingV2Renderer = null;
            lightingV2System = null;
            lightingV2Resources?.Dispose();
            lightingV2Resources = null;
        }

        public void UpdateSimulationViewport(int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
            {
                activeSimulationChunks.Clear();
                return;
            }

            float viewWidth = screenWidth / Camera.Zoom;
            float viewHeight = screenHeight / Camera.Zoom;
            int startTileX = (int)System.MathF.Floor(Camera.Position.X / WorldMap.TileSize);
            int endTileX = (int)System.MathF.Ceiling((Camera.Position.X + viewWidth) / WorldMap.TileSize);
            int startTileY = (int)System.MathF.Floor(Camera.Position.Y / WorldMap.TileSize);
            int endTileY = (int)System.MathF.Ceiling((Camera.Position.Y + viewHeight) / WorldMap.TileSize);

            ActiveSimulationChunkSelector.Collect(
                WorldMap,
                new Rectangle(startTileX, startTileY, System.Math.Max(1, endTileX - startTileX + 1), System.Math.Max(1, endTileY - startTileY + 1)),
                SimulationChunkBorder,
                activeSimulationChunks);
        }

        public void FollowPlayer(float dt, int screenWidth, int screenHeight)
        {
            Vector2 playerTarget = Player.Position + new Vector2(8f, 12f);
            Vector2 target = playerTarget;

            bool focusingInterior = InteriorFocusSystem.TryGetCameraFocus(out Vector2 roomFocus);
            if (focusingInterior)
            {
                target = roomFocus;
            }

            if (!hasSmoothedCameraTarget)
            {
                smoothedCameraTarget = target;
                hasSmoothedCameraTarget = true;
            }

            if (focusingInterior)
            {
                returningFromInterior = false;
                smoothedCameraTarget = Vector2.Lerp(
                    smoothedCameraTarget,
                    target,
                    MathHelper.Clamp(dt * CameraFocusLerpSpeed, 0f, 1f));
            }
            else if (wasFocusingInterior || returningFromInterior)
            {
                returningFromInterior = true;
                smoothedCameraTarget = Vector2.Lerp(
                    smoothedCameraTarget,
                    playerTarget,
                    MathHelper.Clamp(dt * CameraReturnFocusLerpSpeed, 0f, 1f));

                if (Vector2.DistanceSquared(smoothedCameraTarget, playerTarget) <= CameraReturnSnapDistance * CameraReturnSnapDistance)
                {
                    smoothedCameraTarget = playerTarget;
                    returningFromInterior = false;
                }
            }
            else
            {
                smoothedCameraTarget = playerTarget;
            }

            if (focusingInterior)
            {
                Camera.Zoom = MathHelper.Lerp(
                    Camera.Zoom,
                    InteriorCameraZoom,
                    MathHelper.Clamp(dt * CameraZoomInLerpSpeed, 0f, 1f));
            }
            else if (wasFocusingInterior || returningFromInterior ||
                     System.MathF.Abs(Camera.Zoom - DefaultCameraZoom) > CameraZoomSnapDistance)
            {
                Camera.Zoom = MathHelper.Lerp(
                    Camera.Zoom,
                    DefaultCameraZoom,
                    MathHelper.Clamp(dt * CameraZoomOutLerpSpeed, 0f, 1f));

                if (System.MathF.Abs(Camera.Zoom - DefaultCameraZoom) <= CameraZoomSnapDistance)
                {
                    Camera.Zoom = DefaultCameraZoom;
                }
            }
            else
            {
                Camera.Zoom = DefaultCameraZoom;
            }

            wasFocusingInterior = focusingInterior;
            Camera.Follow(smoothedCameraTarget, screenWidth, screenHeight);
        }

        public void DrawTerrainBase(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);

            // Sand draws first so tiles composite on top of it: any undrawn/transparent pixel
            // in a tile's own art shows the loose sand behind it (correct, since that pixel is
            // genuinely open), while every opaque tile pixel still fully occludes the sand as
            // expected. Drawing sand after tiles instead would let its edge bleed visibly paint
            // over legitimate opaque tile pixels (e.g. onto grass at a dune's edge).
            DrawSandPixels(spriteBatch, screenWidth, screenHeight, worldOffsetX);
            WorldMap.Draw(spriteBatch, startTileX, endTileX, startTileY, endTileY);
        }

        public void DrawWater(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            DrawLiquidPixels(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawTerrainOverlay(SpriteBatch spriteBatch, Rectangle hoveredTileBounds, WorldTilePreviewState hoveredTileState)
        {
            TilePreviewRenderer.Draw(spriteBatch, hoveredTileBounds, hoveredTileState);
        }

        // Recomputed every frame (see WorldMap.DrawWetnessOverlay) instead of baked into the chunk
        // cache, so it always matches current wetness rather than the stale value from whenever the
        // chunk was last re-baked by an unrelated tile edit.
        public void DrawWetnessOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);
            WorldMap.DrawWetnessOverlay(spriteBatch, startTileX, endTileX, startTileY, endTileY);
        }

        // Uploads WorldLightingSystem's current window into a small grayscale texture (1 texel per
        // tile) once per frame - called outside the per-loop-offset draw loop below, since the data
        // itself doesn't change between the 1-3 wrapped copies drawn per frame, only where on screen
        // it lands. DrawWorldLighting (below) just re-positions and re-draws this same texture per
        // copy instead of re-uploading it each time.
        public void PrepareWorldLighting(GraphicsDevice graphicsDevice, WorldLightingSystem lightingSystem)
        {
            if (lightingSystem == null || lightingSystem.WindowWidth <= 0 || lightingSystem.WindowHeight <= 0)
                return;

            int width = lightingSystem.WindowWidth;
            int height = lightingSystem.WindowHeight;
            int cellCount = width * height;

            if (lightTextureBuffer.Length < cellCount)
                lightTextureBuffer = new Color[cellCount];

            // Record at central callsite (caller already checked Legacy mode)
            LightingPipelineCoordinator.I.RecordLegacyLightGridCopy();
            lightingSystem.CopyLightGridTo(lightTextureBuffer);

            // Grown-only, like lightBuffer/lightTextureBuffer above: allocated at the largest size
            // ever needed and never recreated just because this frame's window is a tile smaller or
            // bigger than last frame's. WorldLightingSystem's window width/height is now stable
            // frame-to-frame (see the comment in its Update), but recreating a GPU texture on any
            // size mismatch was still fragile - e.g. across a zoom transition - and recreating one
            // every single frame is what actually caused the severe slowdown, so this stays
            // defensive even with that fixed.
            if (lightTexture == null || width > lightTextureCapacityWidth || height > lightTextureCapacityHeight)
            {
                lightTexture?.Dispose();
                lightTextureCapacityWidth = System.Math.Max(width, lightTextureCapacityWidth);
                lightTextureCapacityHeight = System.Math.Max(height, lightTextureCapacityHeight);
                lightTexture = new Texture2D(graphicsDevice, lightTextureCapacityWidth, lightTextureCapacityHeight, false, SurfaceFormat.Color);
            }

            lightTexture.SetData(0, new Rectangle(0, 0, width, height), lightTextureBuffer, 0, cellCount);
            lightTextureActiveWidth = width;
            lightTextureActiveHeight = height;
            lightTextureOriginTileX = lightingSystem.WindowOriginTileX;
            lightTextureOriginTileY = lightingSystem.WindowOriginTileY;
        }

        // Darkens solid tiles (and, being a texture stretched with linear filtering, smoothly blends
        // between them) wherever WorldLightingSystem found them occluded from any sky-exposed opening
        // - works day or night, unlike the old ambient tint hack (which produced zero darkening at
        // full daylight). Multiplied over the already-drawn terrain (MultiplyBlend, set by the
        // caller) rather than tinting each tile's own sprite, so the GPU's bilinear sampling of this
        // small stretched texture is what produces the soft tile-to-tile transition for free.
        public void DrawWorldLighting(SpriteBatch spriteBatch, float worldOffsetX)
        {
            if (lightTexture == null || lightTextureActiveWidth <= 0 || lightTextureActiveHeight <= 0)
                return;

            LightingPipelineCoordinator.I.RecordLegacyComposite();

            int tileSize = WorldMap.TileSize;
            // Mirrors the frame-shift correction used elsewhere: the texture was built from the
            // camera's true (unshifted) position, so a looped world-wrap copy (worldOffsetX != 0)
            // needs its destination shifted back into that copy's local draw space.
            int lightingTileOffset = (int)System.MathF.Round(worldOffsetX / tileSize);
            int destX = (lightTextureOriginTileX - lightingTileOffset) * tileSize;
            int destY = lightTextureOriginTileY * tileSize;
            Rectangle destination = new Rectangle(destX, destY, lightTextureActiveWidth * tileSize, lightTextureActiveHeight * tileSize);
            // Source-cropped to this frame's active window - the texture itself may be larger,
            // holding onto capacity from a previous, bigger frame (see PrepareWorldLighting).
            Rectangle source = new Rectangle(0, 0, lightTextureActiveWidth, lightTextureActiveHeight);

            spriteBatch.Draw(lightTexture, destination, source, Color.White);
        }

        // Mirrors PrepareWorldLighting, but for the point-light-only glow grid (no sky color mixed
        // in - see WorldLightingSystem.CopyGlowGridTo). Uploaded once per frame, same reasoning.
        public void PrepareTorchGlow(GraphicsDevice graphicsDevice, WorldLightingSystem lightingSystem)
        {
            if (lightingSystem == null || lightingSystem.WindowWidth <= 0 || lightingSystem.WindowHeight <= 0)
                return;

            int width = lightingSystem.WindowWidth;
            int height = lightingSystem.WindowHeight;
            int cellCount = width * height;

            if (glowTextureBuffer.Length < cellCount)
                glowTextureBuffer = new Color[cellCount];

            // Record at central callsite (caller already checked Legacy mode)
            LightingPipelineCoordinator.I.RecordLegacyGlowGridCopy();
            lightingSystem.CopyGlowGridTo(glowTextureBuffer);

            if (glowTexture == null || width > glowTextureCapacityWidth || height > glowTextureCapacityHeight)
            {
                glowTexture?.Dispose();
                glowTextureCapacityWidth = System.Math.Max(width, glowTextureCapacityWidth);
                glowTextureCapacityHeight = System.Math.Max(height, glowTextureCapacityHeight);
                glowTexture = new Texture2D(graphicsDevice, glowTextureCapacityWidth, glowTextureCapacityHeight, false, SurfaceFormat.Color);
            }

            glowTexture.SetData(0, new Rectangle(0, 0, width, height), glowTextureBuffer, 0, cellCount);
            glowTextureActiveWidth = width;
            glowTextureActiveHeight = height;
            glowTextureOriginTileX = lightingSystem.WindowOriginTileX;
            glowTextureOriginTileY = lightingSystem.WindowOriginTileY;
        }

        // Drawn with an additive blend (set by the caller) over the ENTIRE screen - sky, terrain,
        // entities alike - since it only ever adds warm light back in and never darkens anything.
        // This is what lets a torch punch through DrawNightOverlay's full-screen darkness even out in
        // the open, where there's no solid tile for DrawWorldLighting's masked multiply to apply to.
        public void DrawTorchGlow(SpriteBatch spriteBatch, float worldOffsetX)
        {
            if (glowTexture == null || glowTextureActiveWidth <= 0 || glowTextureActiveHeight <= 0)
                return;

            int tileSize = WorldMap.TileSize;
            int lightingTileOffset = (int)System.MathF.Round(worldOffsetX / tileSize);
            int destX = (glowTextureOriginTileX - lightingTileOffset) * tileSize;
            int destY = glowTextureOriginTileY * tileSize;
            Rectangle destination = new Rectangle(destX, destY, glowTextureActiveWidth * tileSize, glowTextureActiveHeight * tileSize);
            Rectangle source = new Rectangle(0, 0, glowTextureActiveWidth, glowTextureActiveHeight);

            spriteBatch.Draw(glowTexture, destination, source, Color.White);
        }

        /// <summary>
        /// Retrieve the current scene RenderTarget (may be null if not allocated).
        /// Called by PlayingState to validate RenderTarget state and draw it in PHASE 4.
        /// </summary>
        public RenderTarget2D GetSceneRenderTarget()
        {
            return sceneRenderTarget;
        }

        /// <summary>
        /// Allocate or resize sceneRenderTarget if needed. Uses grow-only strategy:
        /// if screenWidth or screenHeight exceed current capacity, reallocate with new dimensions as minimum.
        /// Called at start of DrawWithNewLightingPipeline (PHASE 0).
        /// Logs creation/recreation events to console for diagnostics.
        /// </summary>
        public void EnsureSceneRenderTarget(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = sceneRenderTarget == null || screenWidth > sceneRenderTargetCapacityWidth || screenHeight > sceneRenderTargetCapacityHeight;

            if (needsRecreation)
            {
                if (sceneRenderTarget != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating SceneRenderTarget: {sceneRenderTargetCapacityWidth}x{sceneRenderTargetCapacityHeight} → {screenWidth}x{screenHeight}");
                    sceneRenderTarget.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating SceneRenderTarget: {screenWidth}x{screenHeight}");
                }

                sceneRenderTargetCapacityWidth = System.Math.Max(screenWidth, sceneRenderTargetCapacityWidth);
                sceneRenderTargetCapacityHeight = System.Math.Max(screenHeight, sceneRenderTargetCapacityHeight);
                sceneRenderTarget = new RenderTarget2D(graphicsDevice, sceneRenderTargetCapacityWidth, sceneRenderTargetCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            }
        }

        /// <summary>
        /// Dispose and release sceneRenderTarget graphics memory.
        /// Called from PlayingState.OnExit() to prevent resource leak when exiting playing state.
        /// </summary>
        public void DisposeSceneRenderTarget()
        {
            sceneRenderTarget?.Dispose();
            sceneRenderTarget = null;
        }

        /// <summary>
        /// Allocate or resize lightingMaskRenderTarget if needed. Uses grow-only strategy.
        /// Mask format: R channel = lighting intensity (0.0 = no lighting, 1.0 = full lighting)
        /// Constructed during PHASE 2 (world scene rendering).
        /// </summary>
        public void EnsureLightingMaskRenderTarget(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = lightingMaskRenderTarget == null || screenWidth > lightingMaskCapacityWidth || screenHeight > lightingMaskCapacityHeight;

            if (needsRecreation)
            {
                if (lightingMaskRenderTarget != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating LightingMaskRenderTarget: {lightingMaskCapacityWidth}x{lightingMaskCapacityHeight} → {screenWidth}x{screenHeight}");
                    lightingMaskRenderTarget.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating LightingMaskRenderTarget: {screenWidth}x{screenHeight}");
                }

                lightingMaskCapacityWidth = System.Math.Max(screenWidth, lightingMaskCapacityWidth);
                lightingMaskCapacityHeight = System.Math.Max(screenHeight, lightingMaskCapacityHeight);
                lightingMaskRenderTarget = new RenderTarget2D(graphicsDevice, lightingMaskCapacityWidth, lightingMaskCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            }
        }

        /// <summary>
        /// Retrieve the current lighting mask RenderTarget (may be null if not allocated).
        /// </summary>
        public RenderTarget2D GetLightingMaskRenderTarget()
        {
            return lightingMaskRenderTarget;
        }

        /// <summary>
        /// Dispose and release lightingMaskRenderTarget graphics memory.
        /// </summary>
        public void DisposeLightingMaskRenderTarget()
        {
            lightingMaskRenderTarget?.Dispose();
            lightingMaskRenderTarget = null;
        }

        /// <summary>
        /// Retrieve the current lighting texture (BFS-computed ambient light).
        /// Used by PHASE 3 to composite lighting with mask.
        /// </summary>
        public Texture2D GetLightTexture()
        {
            return lightTexture;
        }

        /// <summary>
        /// Allocate or resize directionalSunlightMap if needed. Uses grow-only strategy.
        /// Stores directional sunlight contribution (separate from ambient BFS light).
        /// Constructed during PHASE 2 based on sun position and world geometry.
        /// </summary>
        public void EnsureDirectionalSunlightMap(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = directionalSunlightMap == null || screenWidth > directionalSunlightCapacityWidth || screenHeight > directionalSunlightCapacityHeight;

            if (needsRecreation)
            {
                if (directionalSunlightMap != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating DirectionalSunlightMap: {directionalSunlightCapacityWidth}x{directionalSunlightCapacityHeight} → {screenWidth}x{screenHeight}");
                    directionalSunlightMap.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating DirectionalSunlightMap: {screenWidth}x{screenHeight}");
                }

                directionalSunlightCapacityWidth = System.Math.Max(screenWidth, directionalSunlightCapacityWidth);
                directionalSunlightCapacityHeight = System.Math.Max(screenHeight, directionalSunlightCapacityHeight);
                directionalSunlightMap = new RenderTarget2D(graphicsDevice, directionalSunlightCapacityWidth, directionalSunlightCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            }
        }

        /// <summary>
        /// Retrieve the current directional sunlight map (may be null if not allocated).
        /// </summary>
        public RenderTarget2D GetDirectionalSunlightMap()
        {
            return directionalSunlightMap;
        }

        /// <summary>
        /// Dispose and release directionalSunlightMap graphics memory.
        /// </summary>
        public void DisposeDirectionalSunlightMap()
        {
            directionalSunlightMap?.Dispose();
            directionalSunlightMap = null;
        }

        /// <summary>
        /// Cache the last computed sun direction (in radians) to avoid redundant calculations.
        /// </summary>
        public float GetLastSunDirection()
        {
            return lastSunDirection;
        }

        /// <summary>
        /// Update cached sun direction.
        /// </summary>
        public void SetLastSunDirection(float sunDirectionRadians)
        {
            lastSunDirection = sunDirectionRadians;
        }

        /// <summary>
        /// Allocate or resize foregroundBlockageMap if needed. Uses grow-only strategy.
        /// Maps foreground solid geometry that blocks directional sunlight.
        /// White = blocks light, Black = transparent to light.
        /// </summary>
        public void EnsureForegroundBlockageMap(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = foregroundBlockageMap == null || screenWidth > foregroundBlockageCapacityWidth || screenHeight > foregroundBlockageCapacityHeight;

            if (needsRecreation)
            {
                if (foregroundBlockageMap != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating ForegroundBlockageMap: {foregroundBlockageCapacityWidth}x{foregroundBlockageCapacityHeight} → {screenWidth}x{screenHeight}");
                    foregroundBlockageMap.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating ForegroundBlockageMap: {screenWidth}x{screenHeight}");
                }

                foregroundBlockageCapacityWidth = System.Math.Max(screenWidth, foregroundBlockageCapacityWidth);
                foregroundBlockageCapacityHeight = System.Math.Max(screenHeight, foregroundBlockageCapacityHeight);
                foregroundBlockageMap = new RenderTarget2D(graphicsDevice, foregroundBlockageCapacityWidth, foregroundBlockageCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            }
        }

        /// <summary>
        /// Retrieve the foreground blockage map (may be null if not allocated).
        /// </summary>
        public RenderTarget2D GetForegroundBlockageMap()
        {
            return foregroundBlockageMap;
        }

        /// <summary>
        /// Dispose and release foregroundBlockageMap graphics memory.
        /// </summary>
        public void DisposeForegroundBlockageMap()
        {
            foregroundBlockageMap?.Dispose();
            foregroundBlockageMap = null;
        }

        /// <summary>
        /// Allocate or resize penumbraMap if needed. Uses grow-only strategy.
        /// Stores soft shadow information (penumbra from partial blockage).
        /// </summary>
        public void EnsurePenumbraMap(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = penumbraMap == null || screenWidth > penumbraCapacityWidth || screenHeight > penumbraCapacityHeight;

            if (needsRecreation)
            {
                if (penumbraMap != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating PenumbraMap: {penumbraCapacityWidth}x{penumbraCapacityHeight} → {screenWidth}x{screenHeight}");
                    penumbraMap.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating PenumbraMap: {screenWidth}x{screenHeight}");
                }

                penumbraCapacityWidth = System.Math.Max(screenWidth, penumbraCapacityWidth);
                penumbraCapacityHeight = System.Math.Max(screenHeight, penumbraCapacityHeight);
                penumbraMap = new RenderTarget2D(graphicsDevice, penumbraCapacityWidth, penumbraCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            }
        }

        /// <summary>
        /// Retrieve the penumbra map (may be null if not allocated).
        /// </summary>
        public RenderTarget2D GetPenumbraMap()
        {
            return penumbraMap;
        }

        /// <summary>
        /// Dispose and release penumbraMap graphics memory.
        /// </summary>
        public void DisposePenumbraMap()
        {
            penumbraMap?.Dispose();
            penumbraMap = null;
        }

        // ========== PHASE 1: V3 NEUTRAL COMPOSITION RENDERTARGETS ==========

        /// <summary>
        /// Allocate or resize V3 atmosphere RenderTarget. Stores sky, sun, moons, mountains.
        /// </summary>
        public void EnsureV3AtmosphereRenderTarget(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = v3AtmosphereRenderTarget == null || screenWidth > v3AtmosphereCapacityWidth || screenHeight > v3AtmosphereCapacityHeight;

            if (needsRecreation)
            {
                if (v3AtmosphereRenderTarget != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating V3AtmosphereRT: {v3AtmosphereCapacityWidth}x{v3AtmosphereCapacityHeight} → {screenWidth}x{screenHeight}");
                    v3AtmosphereRenderTarget.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating V3AtmosphereRT: {screenWidth}x{screenHeight}");
                }

                v3AtmosphereCapacityWidth = System.Math.Max(screenWidth, v3AtmosphereCapacityWidth);
                v3AtmosphereCapacityHeight = System.Math.Max(screenHeight, v3AtmosphereCapacityHeight);
                v3AtmosphereRenderTarget = new RenderTarget2D(graphicsDevice, v3AtmosphereCapacityWidth, v3AtmosphereCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            }
        }

        /// <summary>
        /// Allocate or resize V3 world RenderTarget. Stores terrain, water, decorations.
        /// </summary>
        public void EnsureV3WorldRenderTarget(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = v3WorldRenderTarget == null || screenWidth > v3WorldCapacityWidth || screenHeight > v3WorldCapacityHeight;

            if (needsRecreation)
            {
                if (v3WorldRenderTarget != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating V3WorldRT: {v3WorldCapacityWidth}x{v3WorldCapacityHeight} → {screenWidth}x{screenHeight}");
                    v3WorldRenderTarget.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating V3WorldRT: {screenWidth}x{screenHeight}");
                }

                v3WorldCapacityWidth = System.Math.Max(screenWidth, v3WorldCapacityWidth);
                v3WorldCapacityHeight = System.Math.Max(screenHeight, v3WorldCapacityHeight);
                v3WorldRenderTarget = new RenderTarget2D(graphicsDevice, v3WorldCapacityWidth, v3WorldCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            }
        }

        /// <summary>
        /// Allocate or resize V3 entities RenderTarget. Stores player, enemies, NPCs, items.
        /// </summary>
        public void EnsureV3EntitiesRenderTarget(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = v3EntitiesRenderTarget == null || screenWidth > v3EntitiesCapacityWidth || screenHeight > v3EntitiesCapacityHeight;

            if (needsRecreation)
            {
                if (v3EntitiesRenderTarget != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating V3EntitiesRT: {v3EntitiesCapacityWidth}x{v3EntitiesCapacityHeight} → {screenWidth}x{screenHeight}");
                    v3EntitiesRenderTarget.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating V3EntitiesRT: {screenWidth}x{screenHeight}");
                }

                v3EntitiesCapacityWidth = System.Math.Max(screenWidth, v3EntitiesCapacityWidth);
                v3EntitiesCapacityHeight = System.Math.Max(screenHeight, v3EntitiesCapacityHeight);
                v3EntitiesRenderTarget = new RenderTarget2D(graphicsDevice, v3EntitiesCapacityWidth, v3EntitiesCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            }
        }

        /// <summary>
        /// Allocate or resize V3 emissive RenderTarget. Reserved for future emissive objects.
        /// </summary>
        public void EnsureV3EmissiveRenderTarget(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = v3EmissiveRenderTarget == null || screenWidth > v3EmissiveCapacityWidth || screenHeight > v3EmissiveCapacityHeight;

            if (needsRecreation)
            {
                if (v3EmissiveRenderTarget != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating V3EmissiveRT: {v3EmissiveCapacityWidth}x{v3EmissiveCapacityHeight} → {screenWidth}x{screenHeight}");
                    v3EmissiveRenderTarget.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating V3EmissiveRT: {screenWidth}x{screenHeight}");
                }

                v3EmissiveCapacityWidth = System.Math.Max(screenWidth, v3EmissiveCapacityWidth);
                v3EmissiveCapacityHeight = System.Math.Max(screenHeight, v3EmissiveCapacityHeight);
                v3EmissiveRenderTarget = new RenderTarget2D(graphicsDevice, v3EmissiveCapacityWidth, v3EmissiveCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            }
        }

        /// <summary>Public getters for V3 RenderTargets.</summary>
        public RenderTarget2D GetV3AtmosphereRenderTarget() => v3AtmosphereRenderTarget;
        public RenderTarget2D GetV3WorldRenderTarget() => v3WorldRenderTarget;
        public RenderTarget2D GetV3EntitiesRenderTarget() => v3EntitiesRenderTarget;
        public RenderTarget2D GetV3EmissiveRenderTarget() => v3EmissiveRenderTarget;

        /// <summary>Dispose all V3 RenderTargets.</summary>
        public void DisposeV3RenderTargets()
        {
            v3AtmosphereRenderTarget?.Dispose();
            v3AtmosphereRenderTarget = null;

            v3WorldRenderTarget?.Dispose();
            v3WorldRenderTarget = null;

            v3EntitiesRenderTarget?.Dispose();
            v3EntitiesRenderTarget = null;

            v3EmissiveRenderTarget?.Dispose();
            v3EmissiveRenderTarget = null;
        }

        /// <summary>
        /// Cache computed sun color to avoid recalculation every frame.
        /// </summary>
        public Vector3 GetLastSunColor()
        {
            return lastSunColor;
        }

        /// <summary>
        /// Update cached sun color.
        /// </summary>
        public void SetLastSunColor(Vector3 sunColor)
        {
            lastSunColor = sunColor;
        }

        public void DrawTreeDecorations(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, TreeRenderLayer layer, Color ambientLight)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);
            WorldMap.DrawDecorations(spriteBatch, startTileX, endTileX, startTileY, endTileY, layer, ambientLight);
        }

        public void PrepareTerrainRender(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight, float worldOffsetX)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);
            WorldMap.PrepareVisibleChunkCache(graphicsDevice, startTileX, endTileX, startTileY, endTileY);
        }

        public void DrawEntities(SpriteBatch spriteBatch, IEntityLightSampler entityLightSampler)
        {
            Color tint = entityLightSampler.SampleLightAt(Player.Position);

            // Record entity draw event using type-safe metrics
            var metrics = entityLightSampler.GetMetrics();
            metrics.RecordDraw();

            // Legacy-specific: record tint application
            if (entityLightSampler is LegacyEntityLightSampler)
            {
                metrics.RecordEntityTintApply();
            }

            Player.Draw(spriteBatch, tint);
        }

        // Entities aren't tile-cached, so this can query WorldLightingSystem at each entity's own
        // exact position - a buried enemy/item/player doesn't get the sky's brightness even while a
        // sibling entity standing in the open right next to it does, and a torch nearby brightens
        // whichever entities are close to it, same as it does for solid ground.
        private Color GetAmbientTintAt(WorldLightingSystem lightingSystem, Vector2 worldPosition)
        {
            if (lightingSystem == null)
                return Color.White;

            Point tile = WorldMap.WorldToTile(worldPosition);
            return lightingSystem.GetLightAt(tile.X, tile.Y);
        }

        public void DrawTissueHalo(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            TissueNetworkRenderer.DrawHalo(spriteBatch, TissueNetwork, GetVisiblePixelBounds(screenWidth, screenHeight, worldOffsetX));
        }

        public void DrawTissueCore(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            TissueNetworkRenderer.DrawCore(spriteBatch, TissueNetwork, GetVisiblePixelBounds(screenWidth, screenHeight, worldOffsetX));
        }

        public void DrawTissueResonanceHalo(
            SpriteBatch spriteBatch,
            int screenWidth,
            int screenHeight,
            float worldOffsetX,
            TissueResonanceState resonance)
        {
            TissueNetworkRenderer.DrawResonanceHalo(
                spriteBatch,
                TissueNetwork,
                GetVisiblePixelBounds(screenWidth, screenHeight, worldOffsetX),
                resonance);
        }

        public void DrawTissueResonanceCore(
            SpriteBatch spriteBatch,
            int screenWidth,
            int screenHeight,
            float worldOffsetX,
            TissueResonanceState resonance)
        {
            TissueNetworkRenderer.DrawResonanceCore(
                spriteBatch,
                TissueNetwork,
                GetVisiblePixelBounds(screenWidth, screenHeight, worldOffsetX),
                resonance);
        }

        public void DrawTissueFieldOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            GetVisibleTileRange(
                screenWidth,
                screenHeight,
                worldOffsetX,
                out int startTileX,
                out int endTileX,
                out int startTileY,
                out int endTileY);
            TissueFieldOverlayRenderer.Draw(
                spriteBatch,
                WorldMap,
                startTileX,
                endTileX,
                startTileY,
                endTileY);
        }

        // Background wall tiles stay a separate draw so they can be issued before the foreground
        // terrain pass (they sit visually behind it) while DrawLoopedWorldEntities below - enemies,
        // items, particles, placed objects - moves to fire after the foreground terrain instead, so
        // solid ground no longer paints over them.
        public void DrawBackgroundWalls(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);
            WorldMap.DrawBackground(spriteBatch, startTileX, endTileX, startTileY, endTileY);
        }

        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, WorldLightingSystem lightingSystem, float visualTimeSeconds)
        {
            float viewWidth = screenWidth / Camera.Zoom;
            float viewHeight = screenHeight / Camera.Zoom;
            float localLeft = Camera.Position.X - worldOffsetX - EntityDrawPaddingPixels;
            float localTop = Camera.Position.Y - EntityDrawPaddingPixels;
            float localRight = localLeft + viewWidth + (EntityDrawPaddingPixels * 2f);
            float localBottom = localTop + viewHeight + (EntityDrawPaddingPixels * 2f);

            foreach (Enemy enemy in Enemies)
            {
                if (!IntersectsVisibleArea(enemy.Hurtbox, localLeft, localTop, localRight, localBottom))
                    continue;

                enemy.Draw(spriteBatch, GetAmbientTintAt(lightingSystem, enemy.Position));
                HealthBarRenderer.Draw(spriteBatch, enemy.Position + new Vector2(0f, -30f), enemy.Health, enemy.MaxHealth, 22, 3);
            }

            foreach (WorldItem worldItem in WorldItems)
            {
                if (!IntersectsVisibleArea(worldItem.WorldBounds, localLeft, localTop, localRight, localBottom))
                    continue;

                worldItem.Draw(spriteBatch, GetAmbientTintAt(lightingSystem, worldItem.WorldBounds.Center.ToVector2()));
            }

            BlockParticleSystem.Draw(spriteBatch, localLeft, localTop, localRight, localBottom);
            DamageNumberSystem.Draw(spriteBatch, HudRenderer.Font);
            WorkbenchRuntimeSystem?.Draw(spriteBatch);
            FurnaceRuntimeSystem?.Draw(spriteBatch);
            TorchRuntimeSystem?.Draw(spriteBatch, visualTimeSeconds);
            DoorRuntimeSystem?.Draw(spriteBatch);
            ChairRuntimeSystem?.Draw(spriteBatch);
            TableRuntimeSystem?.Draw(spriteBatch);
            PlatformRuntimeSystem?.Draw(spriteBatch);
        }

        public void DrawSky(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            ElyraSkyRenderer.Draw(spriteBatch, screenWidth, screenHeight, skyState, Camera.Zoom);
        }

        public void DrawRainFront(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            ElyraSkyRenderer.DrawRainFront(spriteBatch, screenWidth, screenHeight, skyState, Camera.Zoom);
        }

        public void DrawSunGlow(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            ElyraSkyRenderer.DrawSunGlow(spriteBatch, screenWidth, screenHeight, skyState, Camera.Zoom);
        }

        public void DrawMoons(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            ElyraSkyRenderer.DrawMoons(spriteBatch, screenWidth, screenHeight, skyState, Camera.Zoom, Camera.Position.X);
        }

        public void DrawParallaxMountains(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            ElyraSkyRenderer.DrawParallaxMountains(spriteBatch, screenWidth, screenHeight, skyState, Camera.Zoom, Camera.Position.X);
        }

        public void DrawNightOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight, Color tint)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || tint.A == 0)
                return;

            spriteBatch.Draw(DebugPixel, new Rectangle(0, 0, screenWidth, screenHeight), tint);
        }

        public void DrawHud(SpriteBatch spriteBatch, Hotbar hotbar, int selectedHotbarIndex, int screenWidth, int screenHeight, string clockText)
        {
            HudRenderer.Draw(spriteBatch, hotbar, selectedHotbarIndex, Player.Health, Player.MaxHealth, screenWidth, screenHeight, clockText, Camera.Zoom);
        }

        public void DrawPowerHud(SpriteBatch spriteBatch, PlayerPowerSystem powerSystem, int screenWidth, int screenHeight, bool constructionMode)
        {
            PowerHUD.Draw(spriteBatch, powerSystem, screenWidth, screenHeight, constructionMode);
        }

        public void DrawInteriorFocusOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            InteriorFocusSystem.Draw(spriteBatch, Camera, DebugPixel, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawMinimap(SpriteBatch spriteBatch, int screenWidth, int screenHeight, bool tissueMode)
        {
            WorldMinimapRenderer.Draw(spriteBatch, WorldMap, LiquidSystem, SandSystem, TissueNetwork, Camera, Player.Position, screenWidth, screenHeight, tissueMode, ActivatedTissueHubKeys);
        }

        public void DrawInventory(SpriteBatch spriteBatch, Hotbar hotbar, Inventory inventory, int selectedHotbarIndex, int screenWidth, int screenHeight)
        {
            HudRenderer.DrawInventoryPanel(spriteBatch, hotbar, inventory, selectedHotbarIndex, screenWidth, screenHeight);
        }

        public Rectangle GetInventoryPanelBounds(int screenWidth, int screenHeight)
        {
            return HudRenderer.GetInventoryPanelBounds(screenWidth, screenHeight);
        }

        private void GetVisibleTileRange(int screenWidth, int screenHeight, float worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY)
        {
            const int tilePadding = 2;
            float viewWidth = screenWidth / Camera.Zoom;
            float viewHeight = screenHeight / Camera.Zoom;
            float localLeft = Camera.Position.X - worldOffsetX;
            float localTop = Camera.Position.Y;
            float localRight = localLeft + viewWidth;
            float localBottom = localTop + viewHeight;

            startTileX = (int)System.MathF.Floor(localLeft / WorldMap.TileSize) - tilePadding;
            endTileX = (int)System.MathF.Ceiling(localRight / WorldMap.TileSize) + tilePadding;
            startTileY = (int)System.MathF.Floor(localTop / WorldMap.TileSize) - tilePadding;
            endTileY = (int)System.MathF.Ceiling(localBottom / WorldMap.TileSize) + tilePadding;
        }

        private Rectangle GetVisiblePixelBounds(int screenWidth, int screenHeight, float worldOffsetX)
        {
            float viewWidth = screenWidth / Camera.Zoom;
            float viewHeight = screenHeight / Camera.Zoom;
            return new Rectangle(
                (int)System.MathF.Floor(Camera.Position.X - worldOffsetX),
                (int)System.MathF.Floor(Camera.Position.Y),
                System.Math.Max(1, (int)System.MathF.Ceiling(viewWidth)),
                System.Math.Max(1, (int)System.MathF.Ceiling(viewHeight)));
        }

        private void DrawSandPixels(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            if (SandSystem == null)
                return;

            float viewWidth = screenWidth / Camera.Zoom;
            float viewHeight = screenHeight / Camera.Zoom;
            int startPixelX = (int)System.MathF.Floor(Camera.Position.X - worldOffsetX);
            int endPixelX = (int)System.MathF.Ceiling(Camera.Position.X - worldOffsetX + viewWidth);
            int startPixelY = System.Math.Max(0, (int)System.MathF.Floor(Camera.Position.Y));
            int endPixelY = System.Math.Min(SandSystem.Height - 1, (int)System.MathF.Ceiling(Camera.Position.Y + viewHeight));

            DrawWrappedSandRange(spriteBatch, startPixelX, endPixelX, startPixelY, endPixelY, SandPixelColor, topEdgesOnly: false);
            DrawWrappedSandHighlights(spriteBatch, startPixelX, endPixelX, startPixelY, endPixelY, SandHighlightPixelColor);
            DrawWrappedSandRange(spriteBatch, startPixelX, endPixelX, startPixelY, endPixelY, SandTopEdgeColor, topEdgesOnly: true);
        }

        private void DrawLiquidPixels(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            if (LiquidSystem == null)
                return;

            float viewWidth = screenWidth / Camera.Zoom;
            float viewHeight = screenHeight / Camera.Zoom;
            int startPixelX = (int)System.MathF.Floor(Camera.Position.X - worldOffsetX);
            int endPixelX = (int)System.MathF.Ceiling(Camera.Position.X - worldOffsetX + viewWidth);
            int startPixelY = System.Math.Max(0, (int)System.MathF.Floor(Camera.Position.Y));
            int endPixelY = System.Math.Min(LiquidSystem.Height - 1, (int)System.MathF.Ceiling(Camera.Position.Y + viewHeight));

            DrawWrappedLiquidRange(spriteBatch, startPixelX, endPixelX, startPixelY, endPixelY, WaterPixelColor, surfaceOnly: false);
        }

        // Sand's own shading (day/night, occlusion, torch glow) now comes entirely from
        // DrawWorldLighting's stretched, bilinear-filtered light texture (see
        // WorldLightingSystem.CopyLightGridTo, which treats sand as attenuating the same as a solid
        // tile) drawn on top with a multiply blend after this. That overlay's GPU-side linear sampling
        // is what fades smoothly across tile boundaries - sand itself just draws its flat base color,
        // one draw call per unbroken pixel run, same as before any per-tile lighting existed.
        //
        // A hand-rolled CPU approach (splitting each run into tile-sized chunks, or worse, 2px slices
        // with per-pixel interpolation) was tried first to fake this smoothing and tanked the framerate
        // (2-3fps in a full sand biome) - letting the overlay handle it instead is both cheaper (fewer
        // draw calls than even the tile-chunked version) and strictly smoother (continuous, not
        // one-value-per-tile).
        private void DrawWrappedSandRange(
            SpriteBatch spriteBatch, int rawStartX, int rawEndX, int startPixelY, int endPixelY,
            Color baseColor, bool topEdgesOnly)
        {
            int worldWidth = SandSystem.Width;
            if (worldWidth <= 0 || rawStartX > rawEndX || startPixelY > endPixelY)
                return;

            int currentRawStartX = rawStartX;
            while (currentRawStartX <= rawEndX)
            {
                int wrappedStartX = WrapPixelX(currentRawStartX);
                int segmentMaxLength = worldWidth - wrappedStartX;
                int currentRawEndX = System.Math.Min(rawEndX, currentRawStartX + segmentMaxLength - 1);
                int wrappedEndX = wrappedStartX + (currentRawEndX - currentRawStartX);
                int drawOffsetX = currentRawStartX - wrappedStartX;

                IEnumerable<Rectangle> segments = topEdgesOnly
                    ? SandSystem.GetVisibleTopEdgeSegments(wrappedStartX, wrappedEndX, startPixelY, endPixelY)
                    : SandSystem.GetVisibleSegmentsWithEdgeBleed(wrappedStartX, wrappedEndX, startPixelY, endPixelY);

                foreach (Rectangle segment in segments)
                {
                    Rectangle drawBounds = new Rectangle(segment.X + drawOffsetX, segment.Y, segment.Width, segment.Height);
                    spriteBatch.Draw(DebugPixel, drawBounds, baseColor);
                }

                currentRawStartX = currentRawEndX + 1;
            }
        }

        private void DrawWrappedSandHighlights(
            SpriteBatch spriteBatch, int rawStartX, int rawEndX, int startPixelY, int endPixelY, Color baseColor)
        {
            int worldWidth = SandSystem.Width;
            if (worldWidth <= 0 || rawStartX > rawEndX || startPixelY > endPixelY)
                return;

            int currentRawStartX = rawStartX;
            while (currentRawStartX <= rawEndX)
            {
                int wrappedStartX = WrapPixelX(currentRawStartX);
                int segmentMaxLength = worldWidth - wrappedStartX;
                int currentRawEndX = System.Math.Min(rawEndX, currentRawStartX + segmentMaxLength - 1);
                int wrappedEndX = wrappedStartX + (currentRawEndX - currentRawStartX);
                int drawOffsetX = currentRawStartX - wrappedStartX;

                DrawSandHighlightRange(spriteBatch, wrappedStartX, wrappedEndX, startPixelY, endPixelY, drawOffsetX, baseColor);

                currentRawStartX = currentRawEndX + 1;
            }
        }

        private void DrawSandHighlightRange(
            SpriteBatch spriteBatch, int minPixelX, int maxPixelX, int minPixelY, int maxPixelY, int drawOffsetX,
            Color baseColor)
        {
            int minCellX = minPixelX / SandHighlightCellSize;
            int maxCellX = maxPixelX / SandHighlightCellSize;
            int minCellY = minPixelY / SandHighlightCellSize;
            int maxCellY = maxPixelY / SandHighlightCellSize;

            for (int cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    int hash = GetSandHighlightHash(cellX, cellY);
                    if ((hash & 3) != 0)
                        continue;

                    int pixelX = (cellX * SandHighlightCellSize) + (hash % SandHighlightCellSize);
                    int pixelY = (cellY * SandHighlightCellSize) + ((hash >> 8) % SandHighlightCellSize);
                    if (pixelX < minPixelX || pixelX > maxPixelX || pixelY < minPixelY || pixelY > maxPixelY)
                        continue;

                    if (!IsSandHighlightCandidate(pixelX, pixelY))
                        continue;

                    spriteBatch.Draw(DebugPixel, new Rectangle(pixelX + drawOffsetX, pixelY, 1, 1), baseColor);
                }
            }
        }

        private bool IsSandHighlightCandidate(int pixelX, int pixelY)
        {
            if (pixelY <= 0 || pixelY >= SandSystem.Height - 1)
                return false;

            int leftX = WrapPixelX(pixelX - 1);
            int rightX = WrapPixelX(pixelX + 1);
            return SandSystem.HasSandAt(pixelX, pixelY) &&
                   SandSystem.HasSandAt(pixelX, pixelY - 1) &&
                   SandSystem.HasSandAt(pixelX, pixelY + 1) &&
                   (SandSystem.HasSandAt(leftX, pixelY) || SandSystem.HasSandAt(rightX, pixelY));
        }

        private static int GetSandHighlightHash(int cellX, int cellY)
        {
            unchecked
            {
                uint hash = (uint)(cellX * 73856093) ^ (uint)(cellY * 19349663);
                hash ^= hash >> 16;
                hash *= 2246822519u;
                hash ^= hash >> 13;
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        private void DrawWrappedLiquidRange(SpriteBatch spriteBatch, int rawStartX, int rawEndX, int startPixelY, int endPixelY, Color tint, bool surfaceOnly)
        {
            int worldWidth = LiquidSystem.Width;
            if (worldWidth <= 0 || rawStartX > rawEndX || startPixelY > endPixelY)
                return;

            int currentRawStartX = rawStartX;
            while (currentRawStartX <= rawEndX)
            {
                int wrappedStartX = WrapPixelX(currentRawStartX);
                int segmentMaxLength = worldWidth - wrappedStartX;
                int currentRawEndX = System.Math.Min(rawEndX, currentRawStartX + segmentMaxLength - 1);
                int wrappedEndX = wrappedStartX + (currentRawEndX - currentRawStartX);
                int drawOffsetX = currentRawStartX - wrappedStartX;

                IEnumerable<Rectangle> segments = surfaceOnly
                    ? LiquidSystem.GetVisibleSurfaceSegments(wrappedStartX, wrappedEndX, startPixelY, endPixelY, bleedEdges: true)
                    : LiquidSystem.GetVisibleSegments(wrappedStartX, wrappedEndX, startPixelY, endPixelY, bleedEdges: true);

                foreach (Rectangle segment in segments)
                {
                    Rectangle drawBounds = new Rectangle(segment.X + drawOffsetX, segment.Y, segment.Width, segment.Height);
                    spriteBatch.Draw(DebugPixel, drawBounds, tint);
                }

                currentRawStartX = currentRawEndX + 1;
            }
        }

        private int WrapPixelX(int pixelX)
        {
            int worldWidth = WorldMap.PixelWidth;
            if (worldWidth <= 0)
                return 0;

            int wrapped = pixelX % worldWidth;
            return wrapped < 0 ? wrapped + worldWidth : wrapped;
        }

        private static bool IntersectsVisibleArea(Rectangle bounds, float left, float top, float right, float bottom)
        {
            return bounds.Right >= left &&
                   bounds.Left <= right &&
                   bounds.Bottom >= top &&
                   bounds.Top <= bottom;
        }
    }
}
