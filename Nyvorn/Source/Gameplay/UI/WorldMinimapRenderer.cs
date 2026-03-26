using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class WorldMinimapRenderer
    {
        private readonly record struct MinimapLayout(Rectangle Panel, Rectangle SourceRect);

        private readonly GraphicsDevice graphicsDevice;
        private readonly Texture2D pixel;
        private Texture2D minimapTexture;
        private Color[] minimapPixels = System.Array.Empty<Color>();
        private int cachedTileRevision = -1;
        private int cachedSourceWidth;
        private int cachedSourceHeight;
        private float zoom = 1f;
        private Vector2? viewCenterTile;
        private bool isDragging;
        private Point lastDragMousePosition;
        private const float MinZoom = 1f;
        private const float MaxZoom = 18f;
        private const float ZoomStep = 1.2f;

        public WorldMinimapRenderer(GraphicsDevice graphicsDevice)
        {
            this.graphicsDevice = graphicsDevice;
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public bool HandleInput(WorldMap worldMap, Vector2 playerPosition, int screenWidth, int screenHeight, Vector2 mouseScreenPosition, int mouseWheelDelta, bool pointerDown)
        {
            MinimapLayout layout = GetLayout(worldMap, playerPosition, screenWidth, screenHeight);
            Point mousePoint = mouseScreenPosition.ToPoint();
            bool pointerOverMap = layout.Panel.Contains(mousePoint);
            bool consumedMouse = pointerOverMap;

            if (mouseWheelDelta != 0)
                AdjustZoomAtPoint(worldMap, playerPosition, screenWidth, screenHeight, layout, mouseScreenPosition, mouseWheelDelta, pointerOverMap);

            if (zoom > MinZoom && pointerDown)
            {
                if (!isDragging && pointerOverMap)
                {
                    isDragging = true;
                    lastDragMousePosition = mousePoint;
                    consumedMouse = true;
                }
                else if (isDragging)
                {
                    PanFromDrag(worldMap, playerPosition, screenWidth, screenHeight, mousePoint);
                    consumedMouse = true;
                }
            }
            else
            {
                isDragging = false;
            }

            return consumedMouse || isDragging;
        }

        public void Draw(SpriteBatch spriteBatch, WorldMap worldMap, Camera2D camera, Vector2 playerPosition, int screenWidth, int screenHeight)
        {
            EnsureMinimapTexture(worldMap);

            MinimapLayout layout = GetLayout(worldMap, playerPosition, screenWidth, screenHeight);
            Rectangle panel = layout.Panel;
            Rectangle sourceRect = layout.SourceRect;

            Rectangle backdrop = panel;
            backdrop.Inflate(20, 20);
            DrawRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight), new Color(3, 8, 12, 180));
            DrawRect(spriteBatch, backdrop, new Color(8, 18, 24, 235));
            DrawRectOutline(spriteBatch, backdrop, 2, new Color(133, 179, 191));

            spriteBatch.Draw(minimapTexture, panel, sourceRect, Color.White);

            Rectangle cameraRect = GetCameraRect(worldMap, camera, panel, sourceRect);
            DrawRectOutline(spriteBatch, cameraRect, 2, new Color(255, 241, 193));

            Point playerPoint = GetWorldPointOnMinimap(worldMap, playerPosition, panel, sourceRect);
            DrawRect(spriteBatch, new Rectangle(playerPoint.X - 2, playerPoint.Y - 2, 5, 5), new Color(255, 120, 120));

            DrawRect(spriteBatch, new Rectangle(backdrop.X, backdrop.Y - 26, 180, 22), new Color(8, 18, 24, 235));
            DrawRectOutline(spriteBatch, new Rectangle(backdrop.X, backdrop.Y - 26, 180, 22), 1, new Color(133, 179, 191));
        }

        private void AdjustZoomAtPoint(WorldMap worldMap, Vector2 playerPosition, int screenWidth, int screenHeight, MinimapLayout layout, Vector2 mouseScreenPosition, int mouseWheelDelta, bool pointerOverMap)
        {
            float normalizedX = 0.5f;
            float normalizedY = 0.5f;
            float anchorTileX = GetPlayerTileX(worldMap, playerPosition);
            float anchorTileY = GetPlayerTileY(worldMap, playerPosition);

            if (pointerOverMap)
            {
                normalizedX = System.Math.Clamp((mouseScreenPosition.X - layout.Panel.X) / layout.Panel.Width, 0f, 1f);
                normalizedY = System.Math.Clamp((mouseScreenPosition.Y - layout.Panel.Y) / layout.Panel.Height, 0f, 1f);
                anchorTileX = layout.SourceRect.X + (normalizedX * layout.SourceRect.Width);
                anchorTileY = layout.SourceRect.Y + (normalizedY * layout.SourceRect.Height);
            }

            int steps = System.Math.Abs(mouseWheelDelta / 120);
            if (steps == 0)
                steps = 1;

            float factor = System.MathF.Pow(ZoomStep, steps);
            zoom = mouseWheelDelta > 0 ? zoom * factor : zoom / factor;
            zoom = System.Math.Clamp(zoom, MinZoom, MaxZoom);

            if (zoom <= MinZoom)
            {
                viewCenterTile = null;
                isDragging = false;
                return;
            }

            int newSourceWidth = System.Math.Max(1, (int)System.MathF.Round(worldMap.Width / zoom));
            int newSourceHeight = System.Math.Max(1, (int)System.MathF.Round(worldMap.Height / zoom));
            float sourceX = anchorTileX - (normalizedX * newSourceWidth);
            float sourceY = anchorTileY - (normalizedY * newSourceHeight);
            float centerX = sourceX + (newSourceWidth * 0.5f);
            float centerY = sourceY + (newSourceHeight * 0.5f);
            viewCenterTile = ClampViewCenter(worldMap, new Vector2(centerX, centerY), newSourceWidth, newSourceHeight);
        }

        private void EnsureMinimapTexture(WorldMap worldMap)
        {
            int targetWidth = worldMap.Width;
            int targetHeight = worldMap.Height;

            bool needsResize = minimapTexture == null ||
                               cachedSourceWidth != targetWidth ||
                               cachedSourceHeight != targetHeight;

            if (needsResize)
            {
                minimapTexture?.Dispose();
                minimapTexture = new Texture2D(graphicsDevice, targetWidth, targetHeight, false, SurfaceFormat.Color);
                minimapPixels = new Color[targetWidth * targetHeight];
                cachedSourceWidth = targetWidth;
                cachedSourceHeight = targetHeight;
                cachedTileRevision = -1;
            }

            if (cachedTileRevision == worldMap.TileRevision)
                return;

            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                    minimapPixels[(y * targetWidth) + x] = GetTileColor(worldMap.GetTile(x, y));
            }

            minimapTexture.SetData(minimapPixels);
            cachedTileRevision = worldMap.TileRevision;
        }

        private MinimapLayout GetLayout(WorldMap worldMap, Vector2 playerPosition, int screenWidth, int screenHeight)
        {
            Rectangle sourceRect = GetSourceRect(worldMap, playerPosition);
            int maxWidth = screenWidth - 120;
            int maxHeight = screenHeight - 120;
            float scale = System.MathF.Min(maxWidth / (float)worldMap.Width, maxHeight / (float)worldMap.Height);
            scale = System.Math.Clamp(scale, 0.05f, 1f);
            int drawWidth = System.Math.Max(1, (int)System.MathF.Round(worldMap.Width * scale));
            int drawHeight = System.Math.Max(1, (int)System.MathF.Round(worldMap.Height * scale));
            Rectangle panel = new Rectangle(
                (screenWidth - drawWidth) / 2,
                (screenHeight - drawHeight) / 2,
                drawWidth,
                drawHeight);
            return new MinimapLayout(panel, sourceRect);
        }

        private Rectangle GetSourceRect(WorldMap worldMap, Vector2 playerPosition)
        {
            int sourceWidth = System.Math.Max(1, (int)System.MathF.Round(worldMap.Width / zoom));
            int sourceHeight = System.Math.Max(1, (int)System.MathF.Round(worldMap.Height / zoom));
            Vector2 centerTile = viewCenterTile ?? new Vector2(GetPlayerTileX(worldMap, playerPosition), GetPlayerTileY(worldMap, playerPosition));
            centerTile = ClampViewCenter(worldMap, centerTile, sourceWidth, sourceHeight);
            int sourceX = (int)System.MathF.Round(centerTile.X - (sourceWidth * 0.5f));
            int sourceY = (int)System.MathF.Round(centerTile.Y - (sourceHeight * 0.5f));
            sourceX = System.Math.Clamp(sourceX, 0, System.Math.Max(0, worldMap.Width - sourceWidth));
            sourceY = System.Math.Clamp(sourceY, 0, System.Math.Max(0, worldMap.Height - sourceHeight));
            return new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight);
        }

        private void PanFromDrag(WorldMap worldMap, Vector2 playerPosition, int screenWidth, int screenHeight, Point mousePoint)
        {
            MinimapLayout layout = GetLayout(worldMap, playerPosition, screenWidth, screenHeight);
            Point delta = mousePoint - lastDragMousePosition;
            lastDragMousePosition = mousePoint;

            if (delta == Point.Zero)
                return;

            float tileDeltaX = delta.X * (layout.SourceRect.Width / (float)layout.Panel.Width);
            float tileDeltaY = delta.Y * (layout.SourceRect.Height / (float)layout.Panel.Height);
            Vector2 centerTile = viewCenterTile ?? new Vector2(GetPlayerTileX(worldMap, playerPosition), GetPlayerTileY(worldMap, playerPosition));
            centerTile.X -= tileDeltaX;
            centerTile.Y -= tileDeltaY;
            viewCenterTile = ClampViewCenter(worldMap, centerTile, layout.SourceRect.Width, layout.SourceRect.Height);
        }

        private Vector2 ClampViewCenter(WorldMap worldMap, Vector2 centerTile, int sourceWidth, int sourceHeight)
        {
            float halfWidth = sourceWidth * 0.5f;
            float halfHeight = sourceHeight * 0.5f;
            float minCenterX = halfWidth;
            float maxCenterX = worldMap.Width - halfWidth;
            float minCenterY = halfHeight;
            float maxCenterY = worldMap.Height - halfHeight;
            return new Vector2(
                System.Math.Clamp(centerTile.X, minCenterX, maxCenterX),
                System.Math.Clamp(centerTile.Y, minCenterY, maxCenterY));
        }

        private float GetPlayerTileX(WorldMap worldMap, Vector2 playerPosition)
        {
            return System.Math.Clamp(playerPosition.X / worldMap.TileSize, 0f, worldMap.Width - 1f);
        }

        private float GetPlayerTileY(WorldMap worldMap, Vector2 playerPosition)
        {
            return System.Math.Clamp(playerPosition.Y / worldMap.TileSize, 0f, worldMap.Height - 1f);
        }

        private Color GetTileColor(TileType tileType)
        {
            return tileType switch
            {
                TileType.Empty => new Color(8, 14, 18),
                TileType.Grass => new Color(46, 126, 74),
                TileType.Stone => new Color(142, 146, 152),
                TileType.Sand => new Color(192, 172, 108),
                _ => new Color(126, 92, 72)
            };
        }

        private Rectangle GetCameraRect(WorldMap worldMap, Camera2D camera, Rectangle panel, Rectangle sourceRect)
        {
            float leftTile = camera.Position.X / worldMap.TileSize;
            float topTile = camera.Position.Y / worldMap.TileSize;
            float widthTiles = (graphicsDevice.PresentationParameters.BackBufferWidth / camera.Zoom) / worldMap.TileSize;
            float heightTiles = (graphicsDevice.PresentationParameters.BackBufferHeight / camera.Zoom) / worldMap.TileSize;

            int rectX = panel.X + (int)System.MathF.Round(((leftTile - sourceRect.X) / sourceRect.Width) * panel.Width);
            int rectY = panel.Y + (int)System.MathF.Round(((topTile - sourceRect.Y) / sourceRect.Height) * panel.Height);
            int rectW = System.Math.Max(2, (int)System.MathF.Round((widthTiles / sourceRect.Width) * panel.Width));
            int rectH = System.Math.Max(2, (int)System.MathF.Round((heightTiles / sourceRect.Height) * panel.Height));
            return new Rectangle(rectX, rectY, rectW, rectH);
        }

        private Point GetWorldPointOnMinimap(WorldMap worldMap, Vector2 worldPosition, Rectangle panel, Rectangle sourceRect)
        {
            float tileX = worldPosition.X / worldMap.TileSize;
            float tileY = worldPosition.Y / worldMap.TileSize;
            int drawX = panel.X + (int)System.MathF.Round(((tileX - sourceRect.X) / sourceRect.Width) * panel.Width);
            int drawY = panel.Y + (int)System.MathF.Round(((tileY - sourceRect.Y) / sourceRect.Height) * panel.Height);
            return new Point(drawX, drawY);
        }

        private void DrawRect(SpriteBatch spriteBatch, Rectangle rect, Color color)
        {
            spriteBatch.Draw(pixel, rect, color);
        }

        private void DrawRectOutline(SpriteBatch spriteBatch, Rectangle rect, int thickness, Color color)
        {
            DrawRect(spriteBatch, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
            DrawRect(spriteBatch, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
            DrawRect(spriteBatch, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
            DrawRect(spriteBatch, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
        }
    }
}
