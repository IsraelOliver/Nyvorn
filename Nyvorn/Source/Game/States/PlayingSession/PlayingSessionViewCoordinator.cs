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
        private static readonly Vector4 WaterShaderColor = new Color(34, 128, 205, 199).ToVector4();

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
        public Effect WaterEffect { get; init; }
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

        public void DrawTerrain(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, Rectangle hoveredTileBounds, WorldTilePreviewState hoveredTileState)
        {
            DrawTerrainBase(spriteBatch, screenWidth, screenHeight, worldOffsetX);
            DrawWater(spriteBatch, screenWidth, screenHeight, worldOffsetX);
            DrawTerrainOverlay(spriteBatch, hoveredTileBounds, hoveredTileState);
        }

        public void DrawTerrainBase(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);
            WorldMap.Draw(spriteBatch, startTileX, endTileX, startTileY, endTileY);
            DrawSandPixels(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public Effect PrepareWaterEffect(float timeSeconds, GraphicsDevice graphicsDevice, Matrix transformMatrix)
        {
            if (WaterEffect == null || graphicsDevice == null)
                return null;

            SetEffectValue("MatrixTransform", CreateSpriteBatchMatrixTransform(graphicsDevice, transformMatrix));
            SetEffectValue("WaterColor", WaterShaderColor);
            return WaterEffect;
        }

        public void DrawWater(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            DrawLiquidPixels(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawTerrainOverlay(SpriteBatch spriteBatch, Rectangle hoveredTileBounds, WorldTilePreviewState hoveredTileState)
        {
            TilePreviewRenderer.Draw(spriteBatch, hoveredTileBounds, hoveredTileState);
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

        public void DrawEntities(SpriteBatch spriteBatch)
        {
            Player.Draw(spriteBatch);
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

        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);
            WorldMap.DrawBackground(spriteBatch, startTileX, endTileX, startTileY, endTileY);

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

                enemy.Draw(spriteBatch);
                HealthBarRenderer.Draw(spriteBatch, enemy.Position + new Vector2(0f, -30f), enemy.Health, enemy.MaxHealth, 22, 3);
            }

            foreach (WorldItem worldItem in WorldItems)
            {
                if (!IntersectsVisibleArea(worldItem.WorldBounds, localLeft, localTop, localRight, localBottom))
                    continue;

                worldItem.Draw(spriteBatch);
            }

            BlockParticleSystem.Draw(spriteBatch, localLeft, localTop, localRight, localBottom);
            WorkbenchRuntimeSystem?.Draw(spriteBatch);
            DoorRuntimeSystem?.Draw(spriteBatch);
        }

        public void DrawSky(SpriteBatch spriteBatch, int screenWidth, int screenHeight, Color skyColor)
        {
            ElyraSkyRenderer.Draw(spriteBatch, screenWidth, screenHeight, skyColor);
        }

        public void DrawSky(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            ElyraSkyRenderer.Draw(spriteBatch, screenWidth, screenHeight, skyState);
        }

        public void DrawNightOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight, Color tint)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || tint.A == 0)
                return;

            spriteBatch.Draw(DebugPixel, new Rectangle(0, 0, screenWidth, screenHeight), tint);
        }

        public void DrawHud(SpriteBatch spriteBatch, Hotbar hotbar, int selectedHotbarIndex, int screenWidth, int screenHeight, string clockText)
        {
            HudRenderer.Draw(spriteBatch, hotbar, selectedHotbarIndex, Player.Health, Player.MaxHealth, screenWidth, screenHeight, clockText);
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
                    : SandSystem.GetVisibleSegments(wrappedStartX, wrappedEndX, startPixelY, endPixelY);

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
                    ? LiquidSystem.GetVisibleSurfaceSegments(wrappedStartX, wrappedEndX, startPixelY, endPixelY)
                    : LiquidSystem.GetVisibleSegments(wrappedStartX, wrappedEndX, startPixelY, endPixelY);

                foreach (Rectangle segment in segments)
                {
                    Rectangle drawBounds = new Rectangle(segment.X + drawOffsetX, segment.Y, segment.Width, segment.Height);
                    spriteBatch.Draw(DebugPixel, drawBounds, tint);
                }

                currentRawStartX = currentRawEndX + 1;
            }
        }

        private void SetEffectValue(string parameterName, float value)
        {
            WaterEffect.Parameters[parameterName]?.SetValue(value);
        }

        private void SetEffectValue(string parameterName, Vector4 value)
        {
            WaterEffect.Parameters[parameterName]?.SetValue(value);
        }

        private void SetEffectValue(string parameterName, Matrix value)
        {
            WaterEffect.Parameters[parameterName]?.SetValue(value);
        }

        private static Matrix CreateSpriteBatchMatrixTransform(GraphicsDevice graphicsDevice, Matrix transformMatrix)
        {
            Viewport viewport = graphicsDevice.Viewport;
            Matrix.CreateOrthographicOffCenter(
                0,
                viewport.Width,
                viewport.Height,
                0,
                0,
                -1,
                out Matrix projection);

            if (graphicsDevice.UseHalfPixelOffset)
            {
                projection.M41 += -0.5f * projection.M11;
                projection.M42 += -0.5f * projection.M22;
            }

            return transformMatrix * projection;
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
