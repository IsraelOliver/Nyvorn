using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Gameplay.Entities.Enemies;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.UI;
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
        private static readonly Color SandPixelColor = new Color(214, 196, 150);
        private static readonly Color SandTopEdgeColor = new Color(168, 145, 102);

        private readonly List<WorldChunkCoord> activeSimulationChunks = new();

        public required WorldMap WorldMap { get; init; }
        public SandSystem SandSystem { get; set; }
        public required Player Player { get; init; }
        public required List<Enemy> Enemies { get; init; }
        public required List<WorldItem> WorldItems { get; init; }
        public required Camera2D Camera { get; init; }
        public required Texture2D DebugPixel { get; init; }
        public required WorldHealthBarRenderer HealthBarRenderer { get; init; }
        public required HudRenderer HudRenderer { get; init; }
        public required WorldMinimapRenderer WorldMinimapRenderer { get; init; }
        public required ElyraSkyRenderer ElyraSkyRenderer { get; init; }
        public required WorldTilePreviewRenderer TilePreviewRenderer { get; init; }
        public required TissueNetwork TissueNetwork { get; init; }
        public required IReadOnlySet<int> ActivatedTissueHubKeys { get; init; }

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

        public void FollowPlayer(int screenWidth, int screenHeight)
        {
            Camera.Follow(Player.Position + new Vector2(8f, 12f), screenWidth, screenHeight);
        }

        public void DrawTerrain(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, Rectangle hoveredTileBounds, WorldTilePreviewState hoveredTileState)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);

            WorldMap.Draw(spriteBatch, startTileX, endTileX, startTileY, endTileY);
            DrawSandPixels(spriteBatch, screenWidth, screenHeight, worldOffsetX);
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

        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
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

                enemy.Draw(spriteBatch);
                HealthBarRenderer.Draw(spriteBatch, enemy.Position + new Vector2(0f, -30f), enemy.Health, enemy.MaxHealth, 22, 3);
            }

            foreach (WorldItem worldItem in WorldItems)
            {
                if (!IntersectsVisibleArea(worldItem.WorldBounds, localLeft, localTop, localRight, localBottom))
                    continue;

                worldItem.Draw(spriteBatch);
            }
        }

        public void DrawSky(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ElyraSkyRenderer.Draw(spriteBatch, screenWidth, screenHeight);
        }

        public void DrawHud(SpriteBatch spriteBatch, Hotbar hotbar, int selectedHotbarIndex, int screenWidth, int screenHeight)
        {
            HudRenderer.Draw(spriteBatch, hotbar, selectedHotbarIndex, Player.Health, Player.MaxHealth, screenWidth, screenHeight);
        }

        public void DrawMinimap(SpriteBatch spriteBatch, int screenWidth, int screenHeight, bool tissueMode)
        {
            WorldMinimapRenderer.Draw(spriteBatch, WorldMap, TissueNetwork, Camera, Player.Position, screenWidth, screenHeight, tissueMode, ActivatedTissueHubKeys);
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
            DrawWrappedSandRange(spriteBatch, startPixelX, endPixelX, startPixelY, endPixelY, SandTopEdgeColor, topEdgesOnly: true);
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
