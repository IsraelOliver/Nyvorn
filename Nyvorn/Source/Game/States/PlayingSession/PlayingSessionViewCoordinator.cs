using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
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

        private readonly List<WorldChunkCoord> activeSimulationChunks = new();
        private Vector2 smoothedCameraTarget;
        private bool hasSmoothedCameraTarget;
        private bool wasFocusingInterior;
        private bool returningFromInterior;

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

        public IReadOnlyList<WorldChunkCoord> ActiveSimulationChunks => activeSimulationChunks;

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

        public void DrawEntities(SpriteBatch spriteBatch, WorldLightingSystem lightingSystem)
        {
            Player.Draw(spriteBatch, GetAmbientTintAt(lightingSystem, Player.Position));
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

        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, WorldLightingSystem lightingSystem)
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
            TorchRuntimeSystem?.Draw(spriteBatch);
            DoorRuntimeSystem?.Draw(spriteBatch);
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
