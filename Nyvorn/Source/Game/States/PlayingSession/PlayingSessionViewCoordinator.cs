using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;
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

        private RenderTarget2D worldColorRenderTarget;
        private int worldColorRenderTargetCapacityWidth;
        private int worldColorRenderTargetCapacityHeight;

        private RenderTarget2D pixelLightBuffer;
        private int pixelLightBufferCapacityWidth;
        private int pixelLightBufferCapacityHeight;

        private readonly List<WorldChunkCoord> activeSimulationChunks = new();
        private Vector2 smoothedCameraTarget;
        private bool hasSmoothedCameraTarget;
        private bool wasFocusingInterior;
        private bool returningFromInterior;

        private Engine.Graphics.LightingPipeline.LightingPipelineMode lightingPipelineMode = Engine.Graphics.LightingPipeline.LightingPipelineMode.Legacy;

        // P2-E-BG2: Parallax renderer for subterranean backgrounds
        private Engine.Graphics.SubterraneanParallaxRenderer subterraneanParallaxRenderer;

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


        /// <summary>
        /// Initialize Lighting V2 subsystem with required data providers and graphics device.
        /// Called once after PlayingSessionViewCoordinator is fully constructed.
        /// </summary>

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

        /// <summary>
        /// P2-E-BG2: Initialize parallax renderer for subterranean backgrounds.
        /// Called once during session creation.
        /// </summary>
        public void InitializeSubterraneanParallax(
            Texture2D[] parallaxTextures,
            Nyvorn.Source.World.Generation.WorldLayerDefinition[] layerDefinitions)
        {
            if (parallaxTextures != null && parallaxTextures.Length == 6 && layerDefinitions != null && layerDefinitions.Length > 0)
            {
                subterraneanParallaxRenderer = new Engine.Graphics.SubterraneanParallaxRenderer(
                    parallaxTextures,
                    layerDefinitions,
                    Camera,
                    WorldMap.TileSize);
            }
        }

        /// <summary>
        /// P2-E-BG2: Draw parallax layers for Cavern and DeepCavern.
        /// Call AFTER DrawAtmosphericBackground, BEFORE PixelComposite composition.
        /// Backbuffer must be active.
        /// </summary>
        public void DrawSubterraneanParallax(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            if (subterraneanParallaxRenderer != null)
            {
                subterraneanParallaxRenderer.Draw(spriteBatch, screenWidth, screenHeight);
            }
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

        // P2-C5: Surface night tint for Grass during darkness (PIXEL mode only)
        public void DrawSurfaceNightTint(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, float nightStrength)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);
            WorldMap.DrawSurfaceNightTint(spriteBatch, startTileX, endTileX, startTileY, endTileY, nightStrength);
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
        /// P1 Pixel Lighting: WorldColorRenderTarget stores world geometry before lighting composition.
        /// Resolution: physical backbuffer (1920×1080) to preserve per-pixel lighting fidelity.
        /// Used in P1A: captures all world content (terrain, objects) with alpha preservation.
        /// </summary>
        public RenderTarget2D GetWorldColorRenderTarget()
        {
            return worldColorRenderTarget;
        }

        public void EnsureWorldColorRenderTarget(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = worldColorRenderTarget == null ||
                                   screenWidth > worldColorRenderTargetCapacityWidth ||
                                   screenHeight > worldColorRenderTargetCapacityHeight;

            if (needsRecreation)
            {
                if (worldColorRenderTarget != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating WorldColorRenderTarget: {worldColorRenderTargetCapacityWidth}x{worldColorRenderTargetCapacityHeight} → {screenWidth}x{screenHeight}");
                    worldColorRenderTarget.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating WorldColorRenderTarget: {screenWidth}x{screenHeight}");
                }

                worldColorRenderTargetCapacityWidth = System.Math.Max(screenWidth, worldColorRenderTargetCapacityWidth);
                worldColorRenderTargetCapacityHeight = System.Math.Max(screenHeight, worldColorRenderTargetCapacityHeight);
                worldColorRenderTarget = new RenderTarget2D(graphicsDevice, worldColorRenderTargetCapacityWidth, worldColorRenderTargetCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            }
        }

        public void DisposeWorldColorRenderTarget()
        {
            worldColorRenderTarget?.Dispose();
            worldColorRenderTarget = null;
        }

        /// <summary>
        /// P1B Pixel Lighting: PixelLightBuffer stores upscaled V6 lighting in pixel resolution.
        /// Resolution: 1920×1080 (or viewport logical resolution).
        /// Source: V6 ProductionTexture (coarse ~247×141) scaled by GPU to pixel resolution.
        /// This buffer is NOT YET applied to the final image (that's P1C).
        /// It exists for validation and future per-pixel lighting operations.
        /// </summary>
        public RenderTarget2D GetPixelLightBuffer()
        {
            return pixelLightBuffer;
        }

        public void EnsurePixelLightBuffer(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            bool needsRecreation = pixelLightBuffer == null ||
                                   screenWidth > pixelLightBufferCapacityWidth ||
                                   screenHeight > pixelLightBufferCapacityHeight;

            if (needsRecreation)
            {
                if (pixelLightBuffer != null)
                {
                    System.Console.WriteLine($"[GRAPHICS] Recreating PixelLightBuffer: {pixelLightBufferCapacityWidth}x{pixelLightBufferCapacityHeight} → {screenWidth}x{screenHeight}");
                    pixelLightBuffer.Dispose();
                }
                else
                {
                    System.Console.WriteLine($"[GRAPHICS] Creating PixelLightBuffer: {screenWidth}x{screenHeight}");
                }

                pixelLightBufferCapacityWidth = System.Math.Max(screenWidth, pixelLightBufferCapacityWidth);
                pixelLightBufferCapacityHeight = System.Math.Max(screenHeight, pixelLightBufferCapacityHeight);
                pixelLightBuffer = new RenderTarget2D(graphicsDevice, pixelLightBufferCapacityWidth, pixelLightBufferCapacityHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            }
        }

        public void DisposePixelLightBuffer()
        {
            pixelLightBuffer?.Dispose();
            pixelLightBuffer = null;
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
            // Sample light from 2x3 grid around player for more accurate lighting
            var hurtbox = Player.Motor.Hurtbox;
            float left = hurtbox.Left + (hurtbox.Width * 0.1f);
            float right = hurtbox.Right - (hurtbox.Width * 0.1f);
            float top = hurtbox.Top;
            float mid = hurtbox.Top + (hurtbox.Height * 0.5f);
            float bottom = hurtbox.Bottom;

            // Sample 6 points: 2 columns × 3 rows
            Color c0 = entityLightSampler.SampleLightAt(new Vector2(left, top));      // [0,0] head-left
            Color c1 = entityLightSampler.SampleLightAt(new Vector2(right, top));     // [1,0] head-right
            Color c2 = entityLightSampler.SampleLightAt(new Vector2(left, mid));      // [0,1] torso-left
            Color c3 = entityLightSampler.SampleLightAt(new Vector2(right, mid));     // [1,1] torso-right
            Color c4 = entityLightSampler.SampleLightAt(new Vector2(left, bottom));   // [0,2] feet-left
            Color c5 = entityLightSampler.SampleLightAt(new Vector2(right, bottom));  // [1,2] feet-right

            // Average all 6 samples
            int avgR = (c0.R + c1.R + c2.R + c3.R + c4.R + c5.R) / 6;
            int avgG = (c0.G + c1.G + c2.G + c3.G + c4.G + c5.G) / 6;
            int avgB = (c0.B + c1.B + c2.B + c3.B + c4.B + c5.B) / 6;
            Color tint = new Color(avgR, avgG, avgB);

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

        public void DrawWorldLitObjects(SpriteBatch spriteBatch)
        {
            WorkbenchRuntimeSystem?.Draw(spriteBatch);
            FurnaceRuntimeSystem?.Draw(spriteBatch);
            ChairRuntimeSystem?.Draw(spriteBatch);
            TableRuntimeSystem?.Draw(spriteBatch);
            DoorRuntimeSystem?.Draw(spriteBatch);
            PlatformRuntimeSystem?.Draw(spriteBatch);
        }

        // ETAPA 6: Overload for V6 lighting sampler
        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, IEntityLightSampler mobileEntitySampler, float visualTimeSeconds = 0f)
        {
            DrawLoopedWorldEntitiesInternal(
                spriteBatch,
                screenWidth,
                screenHeight,
                worldOffsetX,
                visualTimeSeconds,
                position => mobileEntitySampler.SampleLightAt(position),
                position => mobileEntitySampler.SampleLightAt(position)
            );
        }

        // P1D-B: Overload for separate enemy and world-item light samplers
        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, IEntityLightSampler enemyLightSampler, IEntityLightSampler worldItemLightSampler, float visualTimeSeconds = 0f)
        {
            DrawLoopedWorldEntitiesInternal(
                spriteBatch,
                screenWidth,
                screenHeight,
                worldOffsetX,
                visualTimeSeconds,
                position => enemyLightSampler.SampleLightAt(position),
                position => worldItemLightSampler.SampleLightAt(position)
            );
        }

        // P1E-C: Overload for torch flame control (PIXEL mode separation)
        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, IEntityLightSampler enemyLightSampler, IEntityLightSampler worldItemLightSampler, float visualTimeSeconds, bool drawTorchFlames)
        {
            DrawLoopedWorldEntitiesInternal(
                spriteBatch,
                screenWidth,
                screenHeight,
                worldOffsetX,
                visualTimeSeconds,
                position => enemyLightSampler.SampleLightAt(position),
                position => worldItemLightSampler.SampleLightAt(position),
                drawTorchFlames
            );
        }

        private void DrawLoopedWorldEntitiesInternal(
            SpriteBatch spriteBatch,
            int screenWidth,
            int screenHeight,
            float worldOffsetX,
            float visualTimeSeconds,
            System.Func<Vector2, Color> resolveEnemyTint,
            System.Func<Vector2, Color> resolveWorldItemTint,
            bool drawTorchFlames = true)
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

                enemy.Draw(spriteBatch, resolveEnemyTint(enemy.Position));
                HealthBarRenderer.Draw(spriteBatch, enemy.Position + new Vector2(0f, -30f), enemy.Health, enemy.MaxHealth, 22, 3);
            }

            foreach (WorldItem worldItem in WorldItems)
            {
                if (!IntersectsVisibleArea(worldItem.WorldBounds, localLeft, localTop, localRight, localBottom))
                    continue;

                worldItem.Draw(spriteBatch, resolveWorldItemTint(worldItem.WorldBounds.Center.ToVector2()));
            }

            BlockParticleSystem.Draw(spriteBatch, localLeft, localTop, localRight, localBottom);
            DamageNumberSystem.Draw(spriteBatch, HudRenderer.Font);

            // P1E-C: Draw torch body only, flame drawn separately in PIXEL mode
            TorchRuntimeSystem?.DrawBody(spriteBatch);
            if (drawTorchFlames)
            {
                TorchRuntimeSystem?.DrawFlames(spriteBatch, visualTimeSeconds);
            }
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
