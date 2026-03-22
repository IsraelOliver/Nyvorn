using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class WorldMinimapRenderer
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly Texture2D pixel;
        private RenderTarget2D minimapTexture;
        private int cachedTileRevision = -1;
        private int cachedSourceWidth;
        private int cachedSourceHeight;

        public WorldMinimapRenderer(GraphicsDevice graphicsDevice)
        {
            this.graphicsDevice = graphicsDevice;
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void Draw(SpriteBatch spriteBatch, WorldMap worldMap, Camera2D camera, Vector2 playerPosition, int screenWidth, int screenHeight)
        {
            EnsureMinimapTexture(worldMap);

            int maxWidth = screenWidth - 120;
            int maxHeight = screenHeight - 120;
            float scale = System.MathF.Min(maxWidth / (float)cachedSourceWidth, maxHeight / (float)cachedSourceHeight);
            scale = System.MathF.Max(scale, 1f);

            int drawWidth = (int)System.MathF.Round(cachedSourceWidth * scale);
            int drawHeight = (int)System.MathF.Round(cachedSourceHeight * scale);
            Rectangle panel = new Rectangle(
                (screenWidth - drawWidth) / 2,
                (screenHeight - drawHeight) / 2,
                drawWidth,
                drawHeight);

            Rectangle backdrop = panel;
            backdrop.Inflate(20, 20);
            DrawRect(spriteBatch, new Rectangle(0, 0, screenWidth, screenHeight), new Color(3, 8, 12, 180));
            DrawRect(spriteBatch, backdrop, new Color(8, 18, 24, 235));
            DrawRectOutline(spriteBatch, backdrop, 2, new Color(133, 179, 191));

            spriteBatch.Draw(minimapTexture, panel, Color.White);

            Rectangle cameraRect = GetCameraRect(worldMap, camera, panel);
            DrawRectOutline(spriteBatch, cameraRect, 2, new Color(255, 241, 193));

            Point playerPoint = GetWorldPointOnMinimap(worldMap, playerPosition, panel);
            DrawRect(spriteBatch, new Rectangle(playerPoint.X - 2, playerPoint.Y - 2, 5, 5), new Color(255, 120, 120));

            DrawRect(spriteBatch, new Rectangle(backdrop.X, backdrop.Y - 26, 180, 22), new Color(8, 18, 24, 235));
            DrawRectOutline(spriteBatch, new Rectangle(backdrop.X, backdrop.Y - 26, 180, 22), 1, new Color(133, 179, 191));
        }

        private void EnsureMinimapTexture(WorldMap worldMap)
        {
            int targetWidth = 1200;
            float aspect = worldMap.Height / (float)worldMap.Width;
            int targetHeight = System.Math.Max(1, (int)System.MathF.Round(targetWidth * aspect));

            bool needsResize = minimapTexture == null ||
                               cachedSourceWidth != targetWidth ||
                               cachedSourceHeight != targetHeight;

            if (needsResize)
            {
                minimapTexture?.Dispose();
                minimapTexture = new RenderTarget2D(graphicsDevice, targetWidth, targetHeight, false, SurfaceFormat.Color, DepthFormat.None);
                cachedSourceWidth = targetWidth;
                cachedSourceHeight = targetHeight;
                cachedTileRevision = -1;
            }

            if (cachedTileRevision == worldMap.TileRevision)
                return;

            Color[] pixels = new Color[targetWidth * targetHeight];
            float sampleWidth = worldMap.Width / (float)targetWidth;
            float sampleHeight = worldMap.Height / (float)targetHeight;

            for (int y = 0; y < targetHeight; y++)
            {
                int sampleStartY = (int)System.MathF.Floor(y * sampleHeight);
                int sampleEndY = System.Math.Min(worldMap.Height - 1, (int)System.MathF.Ceiling((y + 1) * sampleHeight) - 1);

                for (int x = 0; x < targetWidth; x++)
                {
                    int sampleStartX = (int)System.MathF.Floor(x * sampleWidth);
                    int sampleEndX = System.Math.Min(worldMap.Width - 1, (int)System.MathF.Ceiling((x + 1) * sampleWidth) - 1);
                    pixels[(y * targetWidth) + x] = SampleTileColor(worldMap, sampleStartX, sampleEndX, sampleStartY, sampleEndY);
                }
            }

            minimapTexture.SetData(pixels);
            cachedTileRevision = worldMap.TileRevision;
        }

        private Color SampleTileColor(WorldMap worldMap, int startX, int endX, int startY, int endY)
        {
            int emptyCount = 0;
            int dirtCount = 0;
            int grassCount = 0;
            int stoneCount = 0;
            int sandCount = 0;

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    switch (worldMap.GetTile(x, y))
                    {
                        case TileType.Empty:
                            emptyCount++;
                            break;
                        case TileType.Dirt:
                            dirtCount++;
                            break;
                        case TileType.Grass:
                            grassCount++;
                            break;
                        case TileType.Stone:
                            stoneCount++;
                            break;
                        case TileType.Sand:
                            sandCount++;
                            break;
                    }
                }
            }

            if (emptyCount >= dirtCount && emptyCount >= grassCount && emptyCount >= stoneCount && emptyCount >= sandCount)
                return new Color(8, 14, 18);

            if (grassCount >= dirtCount && grassCount >= stoneCount && grassCount >= sandCount)
                return new Color(46, 126, 74);

            if (stoneCount >= dirtCount && stoneCount >= sandCount)
                return new Color(142, 146, 152);

            if (sandCount >= dirtCount)
                return new Color(192, 172, 108);

            return new Color(126, 92, 72);
        }

        private Rectangle GetCameraRect(WorldMap worldMap, Camera2D camera, Rectangle panel)
        {
            float worldWidthPixels = worldMap.PixelWidth;
            float worldHeightPixels = worldMap.Height * worldMap.TileSize;
            float left = camera.Position.X;
            float top = camera.Position.Y;
            float width = graphicsDevice.PresentationParameters.BackBufferWidth / camera.Zoom;
            float height = graphicsDevice.PresentationParameters.BackBufferHeight / camera.Zoom;

            while (left < 0f)
                left += worldWidthPixels;
            while (left >= worldWidthPixels)
                left -= worldWidthPixels;

            int rectX = panel.X + (int)System.MathF.Round((left / worldWidthPixels) * panel.Width);
            int rectY = panel.Y + (int)System.MathF.Round((top / worldHeightPixels) * panel.Height);
            int rectW = System.Math.Max(2, (int)System.MathF.Round((width / worldWidthPixels) * panel.Width));
            int rectH = System.Math.Max(2, (int)System.MathF.Round((height / worldHeightPixels) * panel.Height));
            return new Rectangle(rectX, rectY, rectW, rectH);
        }

        private Point GetWorldPointOnMinimap(WorldMap worldMap, Vector2 worldPosition, Rectangle panel)
        {
            float worldWidthPixels = worldMap.PixelWidth;
            float worldHeightPixels = worldMap.Height * worldMap.TileSize;
            float x = worldPosition.X;

            while (x < 0f)
                x += worldWidthPixels;
            while (x >= worldWidthPixels)
                x -= worldWidthPixels;

            int drawX = panel.X + (int)System.MathF.Round((x / worldWidthPixels) * panel.Width);
            int drawY = panel.Y + (int)System.MathF.Round((worldPosition.Y / worldHeightPixels) * panel.Height);
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
