using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Physics.Liquids;
using Nyvorn.Source.Engine.Physics.Sand;
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
        // Open air used to decay slowly enough (0.045/tile, ~22-tile reach) that a single straight
        // tunnel connected to the sky - even one whose mouth sat outside the visible screen, inside
        // only the padded search buffer - could carry full-strength light most of the way across the
        // view in one relaxation pass, reading as a sharp, unnatural "ray" cutting through solid rock
        // instead of a soft local glow. Tightened so light exhausts within the buffer padding itself
        // (reach < SkylightBufferPadding) - a tunnel actually visible on screen still glows near its
        // mouth, but one lighting up from an off-screen opening no longer happens.
        private const float OpenAirLightDecayPerTile = 0.12f;
        private const float SolidLightDecayPerTile = 0.16f;
        private const int SkylightPropagationPasses = 16;
        private const int SkylightBufferPadding = 10;
        private const float MaxSkylightShadowAlpha = 1f;
        // The flood-fill recompute below (16 passes x 4 sweeps over the whole buffer, plus a
        // per-column surface scan) is expensive enough to cost real FPS if it reruns every single
        // frame. Shadows don't need 60Hz freshness - recomputing a few times a second instead is
        // visually indistinguishable and cuts that cost by ~SkylightRecomputeEveryNFrames. Drawing
        // (below) still happens every frame, just reusing whichever buffer was last computed.
        private const int SkylightRecomputeEveryNFrames = 6;
        private static readonly Color SkylightShadowColor = new Color(4, 5, 10);
        private float[,] skylightBuffer;
        private int skylightBufferStartX;
        private int skylightBufferStartY;
        private int skylightRecomputeCounter;

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
        public WorkbenchRuntimeSystem WorkbenchRuntimeSystem { get; init; }
        public FurnaceRuntimeSystem FurnaceRuntimeSystem { get; init; }
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

        // Terraria/Starbound-style skylight: light starts at full strength on every open-air tile
        // that has a clear straight line to the true sky, then floods outward through the visible
        // area, fading a little per tile through open air and a lot per tile through solid rock.
        // Because light can bend around corners this way (crawl sideways through a tunnel, then
        // back up into a pocket), a floating platform blocks only the direct column beneath it, not
        // every tile below it on the map -- unlike a naive per-column depth count. Solid tiles are
        // then tinted dark in inverse proportion to how much light reached them.
        public void DrawSkylightShadows(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);

            RecomputeSkylightBufferIfDue(startTileX, endTileX, startTileY, endTileY);
            if (skylightBuffer == null)
                return;

            int bufferWidth = skylightBuffer.GetLength(0);
            int bufferHeight = skylightBuffer.GetLength(1);

            for (int tileX = startTileX; tileX <= endTileX; tileX++)
            {
                int localX = tileX - skylightBufferStartX;
                // The cached buffer's window can lag a few frames behind the camera (see
                // RecomputeSkylightBufferIfDue) - a tile that's drifted outside it just skips the
                // shadow this frame rather than throwing, and picks it back up once recomputed.
                if (localX < 0 || localX >= bufferWidth)
                    continue;

                for (int tileY = System.Math.Max(0, startTileY); tileY <= System.Math.Min(WorldMap.Height - 1, endTileY); tileY++)
                {
                    int localY = tileY - skylightBufferStartY;
                    if (localY < 0 || localY >= bufferHeight)
                        continue;

                    if (!WorldMap.IsSolidAt(tileX, tileY))
                        continue;

                    float alpha = MathHelper.Clamp(1f - skylightBuffer[localX, localY], 0f, MaxSkylightShadowAlpha);
                    if (alpha <= 0.02f)
                        continue;

                    // Drawn with the tile's own sprite/source-rectangle instead of a flat square over
                    // its bounds - autotile edges have transparent corners/notches, and a plain tinted
                    // square would paint over those gaps instead of following the tile's real shape.
                    if (!WorldMap.TryGetTileSprite(tileX, tileY, out Texture2D tileTexture, out Rectangle? tileSourceRectangle))
                        continue;

                    spriteBatch.Draw(tileTexture, WorldMap.GetTileBounds(tileX, tileY), tileSourceRectangle, SkylightShadowColor * alpha);
                }
            }
        }

        private void RecomputeSkylightBufferIfDue(int startTileX, int endTileX, int startTileY, int endTileY)
        {
            skylightRecomputeCounter++;
            if (skylightBuffer != null && skylightRecomputeCounter % SkylightRecomputeEveryNFrames != 0)
                return;

            int bufferStartX = startTileX - SkylightBufferPadding;
            int bufferEndX = endTileX + SkylightBufferPadding;
            int bufferStartY = System.Math.Max(0, startTileY - SkylightBufferPadding);
            int bufferEndY = System.Math.Min(WorldMap.Height - 1, endTileY + SkylightBufferPadding);

            int width = bufferEndX - bufferStartX + 1;
            int height = bufferEndY - bufferStartY + 1;
            if (width <= 0 || height <= 0)
                return;

            skylightBufferStartX = bufferStartX;
            skylightBufferStartY = bufferStartY;

            if (skylightBuffer == null || skylightBuffer.GetLength(0) != width || skylightBuffer.GetLength(1) != height)
                skylightBuffer = new float[width, height];
            else
                System.Array.Clear(skylightBuffer, 0, skylightBuffer.Length);

            for (int localX = 0; localX < width; localX++)
            {
                int tileX = bufferStartX + localX;
                int surfaceY = FindColumnSurfaceY(tileX, bufferEndY);

                for (int localY = 0; localY < height; localY++)
                {
                    int tileY = bufferStartY + localY;
                    // Includes the sky-facing surface tile itself (not just the open air above it),
                    // so the very top layer of ground reads as fully lit and decay only kicks in one
                    // tile deeper -- matching how a real sun-facing surface looks in direct light.
                    if (tileY <= surfaceY)
                        skylightBuffer[localX, localY] = 1f;
                }
            }

            for (int pass = 0; pass < SkylightPropagationPasses; pass++)
            {
                for (int localY = 0; localY < height; localY++)
                {
                    for (int localX = 1; localX < width; localX++)
                        SpreadSkylight(bufferStartX, bufferStartY, localX, localY, localX - 1, localY);
                    for (int localX = width - 2; localX >= 0; localX--)
                        SpreadSkylight(bufferStartX, bufferStartY, localX, localY, localX + 1, localY);
                }

                for (int localX = 0; localX < width; localX++)
                {
                    for (int localY = 1; localY < height; localY++)
                        SpreadSkylight(bufferStartX, bufferStartY, localX, localY, localX, localY - 1);
                    for (int localY = height - 2; localY >= 0; localY--)
                        SpreadSkylight(bufferStartX, bufferStartY, localX, localY, localX, localY + 1);
                }
            }
        }

        private void SpreadSkylight(int bufferStartX, int bufferStartY, int targetLocalX, int targetLocalY, int sourceLocalX, int sourceLocalY)
        {
            int targetTileX = bufferStartX + targetLocalX;
            int targetTileY = bufferStartY + targetLocalY;
            float decay = WorldMap.IsSolidAt(targetTileX, targetTileY) ? SolidLightDecayPerTile : OpenAirLightDecayPerTile;
            float candidate = skylightBuffer[sourceLocalX, sourceLocalY] - decay;
            if (candidate > skylightBuffer[targetLocalX, targetLocalY])
                skylightBuffer[targetLocalX, targetLocalY] = candidate;
        }

        // Topmost solid tile in this column, scanning from the true world top (row 0) so a column
        // whose visible portion is entirely underground still resolves the same surface depth that
        // WorldMap.HasOpenSkyAbove would find.
        private int FindColumnSurfaceY(int tileX, int maxY)
        {
            for (int y = 0; y <= maxY; y++)
            {
                if (WorldMap.IsSolidAt(tileX, y))
                    return y;
            }

            return maxY + 1;
        }

        public void DrawTreeDecorations(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, TreeRenderLayer layer)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);
            WorldMap.DrawDecorations(spriteBatch, startTileX, endTileX, startTileY, endTileY, layer);
        }

        public void PrepareTerrainRender(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight, float worldOffsetX)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);
            WorldMap.PrepareVisibleChunkCache(graphicsDevice, startTileX, endTileX, startTileY, endTileY);
        }

        public void DrawEntities(SpriteBatch spriteBatch, Color ambientLight)
        {
            Player.Draw(spriteBatch, GetAmbientTintAt(ambientLight, Player.Position));
        }

        // Entities aren't cached like terrain chunks, so unlike GetChunkAmbientTint above this can
        // check each entity's own tile precisely - a buried enemy/item/player doesn't get the
        // sky's tint even while a sibling entity standing in the open right next to it does.
        private Color GetAmbientTintAt(Color ambientLight, Vector2 worldPosition)
        {
            if (ambientLight == Color.White)
                return Color.White;

            Point tile = WorldMap.WorldToTile(worldPosition);
            return WorldMap.HasOpenSkyAbove(tile.X, tile.Y) ? ambientLight : Color.White;
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

        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, Color ambientLight)
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

                enemy.Draw(spriteBatch, GetAmbientTintAt(ambientLight, enemy.Position));
                HealthBarRenderer.Draw(spriteBatch, enemy.Position + new Vector2(0f, -30f), enemy.Health, enemy.MaxHealth, 22, 3);
            }

            foreach (WorldItem worldItem in WorldItems)
            {
                if (!IntersectsVisibleArea(worldItem.WorldBounds, localLeft, localTop, localRight, localBottom))
                    continue;

                worldItem.Draw(spriteBatch, GetAmbientTintAt(ambientLight, worldItem.WorldBounds.Center.ToVector2()));
            }

            BlockParticleSystem.Draw(spriteBatch, localLeft, localTop, localRight, localBottom);
            WorkbenchRuntimeSystem?.Draw(spriteBatch);
            FurnaceRuntimeSystem?.Draw(spriteBatch);
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
            DrawWrappedSandHighlights(spriteBatch, startPixelX, endPixelX, startPixelY, endPixelY);
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

        private void DrawWrappedSandRange(SpriteBatch spriteBatch, int rawStartX, int rawEndX, int startPixelY, int endPixelY, Color tint, bool topEdgesOnly)
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
                    spriteBatch.Draw(DebugPixel, drawBounds, tint);
                }

                currentRawStartX = currentRawEndX + 1;
            }
        }

        private void DrawWrappedSandHighlights(SpriteBatch spriteBatch, int rawStartX, int rawEndX, int startPixelY, int endPixelY)
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

                DrawSandHighlightRange(spriteBatch, wrappedStartX, wrappedEndX, startPixelY, endPixelY, drawOffsetX);

                currentRawStartX = currentRawEndX + 1;
            }
        }

        private void DrawSandHighlightRange(SpriteBatch spriteBatch, int minPixelX, int maxPixelX, int minPixelY, int maxPixelY, int drawOffsetX)
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

                    spriteBatch.Draw(DebugPixel, new Rectangle(pixelX + drawOffsetX, pixelY, 1, 1), SandHighlightPixelColor);
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
