using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Renders V5 Sky Ambient result as a colored texture overlay.
    /// Handles upload to GPU and composition into the final scene.
    /// </summary>
    public sealed class V5SkyAmbientRenderer : IDisposable
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly WorldMap worldMap;
        private readonly V5SkyAmbientCompositor compositor;

        private Texture2D resultTexture;
        private int lastBufferWidth;
        private int lastBufferHeight;

        // Temporary buffer for CPU->GPU transfer
        private Color[] textureData = Array.Empty<Color>();

        public V5SkyAmbientRenderer(GraphicsDevice graphicsDevice, WorldMap worldMap, V5SkyAmbientCompositor compositor)
        {
            this.graphicsDevice = graphicsDevice;
            this.worldMap = worldMap;
            this.compositor = compositor;
        }

        /// <summary>
        /// Upload computed lighting result to a texture.
        /// </summary>
        public void UploadResult(
            V5SkyAmbientField field,
            V5ForegroundLighting foreground,
            Color skyAmbientColor,
            int windowOriginX, int windowOriginY,
            int windowWidth, int windowHeight)
        {
            int originX = field.GetBufferOriginTileX();
            int originY = field.GetBufferOriginTileY();
            int width = field.GetBufferWidth();
            int height = field.GetBufferHeight();

            // Recreate texture if size changed
            if (resultTexture == null || lastBufferWidth != width || lastBufferHeight != height)
            {
                resultTexture?.Dispose();
                resultTexture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
                lastBufferWidth = width;
                lastBufferHeight = height;
            }

            // Prepare texture data
            if (textureData.Length < (width * height))
                textureData = new Color[width * height];

            // Sample all tiles in buffer
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int worldX = originX + x;
                    int worldY = originY + y;
                    bool isForeground = worldMap.IsSolidAt(worldX, worldY);

                    Color tileColor = compositor.GetSkyAmbientColor(worldX, worldY, skyAmbientColor, isForeground);
                    int index = (y * width) + x;
                    textureData[index] = tileColor;
                }
            }

            resultTexture.SetData(textureData);
        }

        /// <summary>
        /// Draw the texture to screen. Called from PlayingState rendering.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, int tileSize, int windowOriginX, int windowOriginY)
        {
            if (resultTexture == null)
                return;

            int pixelX = windowOriginX * tileSize;
            int pixelY = windowOriginY * tileSize;

            spriteBatch.Draw(resultTexture, new Vector2(pixelX, pixelY), Color.White);
        }

        public void Dispose()
        {
            resultTexture?.Dispose();
        }
    }
}
