using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Debug visualization modes for Phase 2 foundation.
    /// Shows classification, opacity fields, and sample grid without altering game state.
    /// </summary>
    public enum LightingDebugMode
    {
        /// <summary>
        /// No debug visualization.
        /// </summary>
        None = 0,

        /// <summary>
        /// Show cell classifications:
        /// - Blue: OpenAtmosphere
        /// - Green: VisibleBackground
        /// - Red: SolidForeground
        /// </summary>
        Classification = 1,

        /// <summary>
        /// Show sun opacity as grayscale:
        /// - Black: 0 (fully transparent)
        /// - White: 1 (fully opaque)
        /// - Gray: 0.5 (partial)
        /// </summary>
        SunOpacity = 2,

        /// <summary>
        /// Show local light opacity as grayscale.
        /// </summary>
        LocalLightOpacity = 3,

        /// <summary>
        /// Show sample grid points (sub-tile sampling positions).
        /// </summary>
        SampleGrid = 4,

        /// <summary>
        /// Show sun visibility as grayscale (Phase 3.2A):
        /// - Black: 0 (completely blocked)
        /// - White: 1 (completely free)
        /// - Gray: 0.5 (partial transmission)
        /// </summary>
        SunVisibility = 5,
    }

    /// <summary>
    /// Renders debug visualizations for the lighting foundation.
    /// </summary>
    public class LightingDebugVisualization
    {
        private LightingDebugMode _mode = LightingDebugMode.None;

        public LightingDebugMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        /// <summary>
        /// Toggle debug mode cycling through all modes.
        /// </summary>
        public void CycleMode()
        {
            _mode = (LightingDebugMode)(((int)_mode + 1) % 6);
        }

        /// <summary>
        /// Render debug visualization to backbuffer (after main rendering).
        /// </summary>
        public void Render(SpriteBatch spriteBatch, LightingV3Foundation foundation, Texture2D pixelTexture, int tileSize)
        {
            if (_mode == LightingDebugMode.None || foundation == null)
                return;

            var frameData = foundation.GetFrameData();
            if (!frameData.HasValue)
                return;

            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);

            switch (_mode)
            {
                case LightingDebugMode.Classification:
                    RenderClassification(spriteBatch, frameData.Value, pixelTexture, tileSize);
                    break;
                case LightingDebugMode.SunOpacity:
                    RenderOpacity(spriteBatch, frameData.Value, pixelTexture, tileSize, isLocal: false);
                    break;
                case LightingDebugMode.LocalLightOpacity:
                    RenderOpacity(spriteBatch, frameData.Value, pixelTexture, tileSize, isLocal: true);
                    break;
                case LightingDebugMode.SampleGrid:
                    RenderSampleGrid(spriteBatch, frameData.Value, pixelTexture, tileSize);
                    break;
                case LightingDebugMode.SunVisibility:
                    RenderSunVisibility(spriteBatch, frameData.Value, pixelTexture, tileSize);
                    break;
            }

            spriteBatch.End();

            // Draw mode label
            spriteBatch.Begin();
            DrawText(spriteBatch, $"Debug: {_mode}", 5, 5, Color.White);
            spriteBatch.End();
        }

        private void RenderClassification(SpriteBatch spriteBatch, LightingV3FrameData frameData, Texture2D pixelTexture, int tileSize)
        {
            var region = frameData.Region;
            var classifications = frameData.TileClassifications;

            int leftTile = region.WorldOriginX / tileSize;
            int topTile = region.WorldOriginY / tileSize;

            int index = 0;
            for (int ty = 0; ty < region.TileHeight; ty++)
            {
                for (int tx = 0; tx < region.TileWidth; tx++)
                {
                    if (index >= classifications.Length)
                        break;

                    var classification = classifications[index++];
                    Color color = classification switch
                    {
                        LightingCellClassification.OpenAtmosphere => Color.Blue * 0.5f,
                        LightingCellClassification.VisibleBackground => Color.Green * 0.5f,
                        LightingCellClassification.SolidForeground => Color.Red * 0.5f,
                        _ => Color.Black
                    };

                    int worldTileX = leftTile + tx;
                    int worldTileY = topTile + ty;
                    int pixelX = worldTileX * tileSize;
                    int pixelY = worldTileY * tileSize;

                    // Draw semi-transparent tile
                    var rect = new Rectangle(pixelX, pixelY, tileSize, tileSize);
                    spriteBatch.Draw(pixelTexture, rect, color);
                }
            }
        }

        private void RenderOpacity(SpriteBatch spriteBatch, LightingV3FrameData frameData, Texture2D pixelTexture, int tileSize, bool isLocal)
        {
            var region = frameData.Region;
            var opacityBuffer = isLocal ? frameData.LocalOpacityBuffer : frameData.SunOpacityBuffer;

            // Render each sample as a small pixel
            int sampleSize = tileSize / region.SampleWidth * region.TileWidth;
            if (sampleSize < 1) sampleSize = 1;

            for (int sy = 0; sy < region.SampleHeight; sy++)
            {
                for (int sx = 0; sx < region.SampleWidth; sx++)
                {
                    int flatIndex = sy * region.SampleWidth + sx;
                    if (flatIndex >= opacityBuffer.Length)
                        continue;

                    float opacity = opacityBuffer[flatIndex];

                    // Grayscale: 0=black, 1=white
                    byte value = (byte)(opacity * 255);
                    Color color = new Color((float)value / 255f, (float)value / 255f, (float)value / 255f, 200f / 255f);

                    // Calculate world position
                    float worldX = region.WorldOriginX + (sx + 0.5f) * (region.TileWidth * tileSize) / region.SampleWidth;
                    float worldY = region.WorldOriginY + (sy + 0.5f) * (region.TileHeight * tileSize) / region.SampleHeight;

                    var rect = new Rectangle((int)worldX, (int)worldY, sampleSize, sampleSize);
                    spriteBatch.Draw(pixelTexture, rect, color);
                }
            }
        }

        private void RenderSampleGrid(SpriteBatch spriteBatch, LightingV3FrameData frameData, Texture2D pixelTexture, int tileSize)
        {
            var region = frameData.Region;

            // Draw crosshair at each sample position
            for (int sy = 0; sy < region.SampleHeight; sy++)
            {
                for (int sx = 0; sx < region.SampleWidth; sx++)
                {
                    // Calculate world position
                    float worldX = region.WorldOriginX + (sx + 0.5f) * (region.TileWidth * tileSize) / region.SampleWidth;
                    float worldY = region.WorldOriginY + (sy + 0.5f) * (region.TileHeight * tileSize) / region.SampleHeight;

                    // Draw small crosshair
                    float x = worldX;
                    float y = worldY;
                    int size = 2;

                    spriteBatch.Draw(pixelTexture, new Rectangle((int)(x - size), (int)y, size * 2 + 1, 1), Color.Yellow);
                    spriteBatch.Draw(pixelTexture, new Rectangle((int)x, (int)(y - size), 1, size * 2 + 1), Color.Yellow);
                }
            }
        }

        private void RenderSunVisibility(SpriteBatch spriteBatch, LightingV3FrameData frameData, Texture2D pixelTexture, int tileSize)
        {
            var region = frameData.Region;
            var sunVisibilityBuffer = frameData.SunVisibilityBuffer;

            if (sunVisibilityBuffer == null || sunVisibilityBuffer.Length == 0)
                return;

            // Render each sample as a grayscale pixel
            // 0 = black (blocked), 1 = white (free)
            int sampleSize = tileSize / region.SampleWidth * region.TileWidth;
            if (sampleSize < 1) sampleSize = 1;

            for (int sy = 0; sy < region.SampleHeight; sy++)
            {
                for (int sx = 0; sx < region.SampleWidth; sx++)
                {
                    int flatIndex = sy * region.SampleWidth + sx;
                    if (flatIndex >= sunVisibilityBuffer.Length)
                        continue;

                    float visibility = sunVisibilityBuffer[flatIndex];

                    // Grayscale: 0=black, 1=white
                    byte value = (byte)(visibility * 255);
                    Color color = new Color((float)value / 255f, (float)value / 255f, (float)value / 255f, 200f / 255f);

                    // Calculate world position
                    float worldX = region.WorldOriginX + (sx + 0.5f) * (region.TileWidth * tileSize) / region.SampleWidth;
                    float worldY = region.WorldOriginY + (sy + 0.5f) * (region.TileHeight * tileSize) / region.SampleHeight;

                    var rect = new Rectangle((int)worldX, (int)worldY, sampleSize, sampleSize);
                    spriteBatch.Draw(pixelTexture, rect, color);
                }
            }
        }

        private void DrawText(SpriteBatch spriteBatch, string text, int x, int y, Color color)
        {
            // Simple text output - requires a font, but we'll use console as fallback
            // In a real implementation, would use spriteBatch.DrawString with a SpriteFont
            System.Console.WriteLine($"[{x},{y}] {text}");
        }
    }
}
