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
            _mode = (LightingDebugMode)(((int)_mode + 1) % 5);
        }

        /// <summary>
        /// Render debug visualization to backbuffer (after main rendering).
        /// </summary>
        public void Render(SpriteBatch spriteBatch, LightingV3Foundation foundation, Texture2D pixelTexture, int tileSize)
        {
            if (_mode == LightingDebugMode.None)
                return;

            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);

            switch (_mode)
            {
                case LightingDebugMode.Classification:
                    RenderClassification(spriteBatch, foundation, pixelTexture, tileSize);
                    break;
                case LightingDebugMode.SunOpacity:
                    RenderOpacity(spriteBatch, foundation, pixelTexture, tileSize, isLocal: false);
                    break;
                case LightingDebugMode.LocalLightOpacity:
                    RenderOpacity(spriteBatch, foundation, pixelTexture, tileSize, isLocal: true);
                    break;
                case LightingDebugMode.SampleGrid:
                    RenderSampleGrid(spriteBatch, foundation, pixelTexture, tileSize);
                    break;
            }

            spriteBatch.End();

            // Draw mode label
            spriteBatch.Begin();
            DrawText(spriteBatch, $"Debug: {_mode}", 5, 5, Color.White);
            spriteBatch.End();
        }

        private void RenderClassification(SpriteBatch spriteBatch, LightingV3Foundation foundation, Texture2D pixelTexture, int tileSize)
        {
            var region = foundation.GetActiveRegion();
            var classifications = foundation.GetTileClassifications();

            int leftTile = (int)System.Math.Floor(region.WorldOriginX / tileSize);
            int topTile = (int)System.Math.Floor(region.WorldOriginY / tileSize);

            int index = 0;
            for (int ty = 0; ty < region.RegionHeightTiles; ty++)
            {
                for (int tx = 0; tx < region.RegionWidthTiles; tx++)
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

        private void RenderOpacity(SpriteBatch spriteBatch, LightingV3Foundation foundation, Texture2D pixelTexture, int tileSize, bool isLocal)
        {
            var region = foundation.GetActiveRegion();
            var field = foundation.GetOccluderField();

            // Render each sample as a small pixel
            int sampleSize = tileSize / region.RegionWidthSamples * region.RegionWidthTiles;
            if (sampleSize < 1) sampleSize = 1;

            for (int sy = 0; sy < region.RegionHeightSamples; sy++)
            {
                for (int sx = 0; sx < region.RegionWidthSamples; sx++)
                {
                    int flatIndex = field.SampleToFlatIndex(sx, sy);
                    if (flatIndex < 0)
                        continue;

                    float opacity = isLocal ? field.GetLocalLightOpacity(flatIndex) : field.GetSunOpacity(flatIndex);

                    // Grayscale: 0=black, 1=white
                    byte value = (byte)(opacity * 255);
                    Color color = new Color((float)value / 255f, (float)value / 255f, (float)value / 255f, 200f / 255f);

                    var worldPos = region.LocalSampleToWorldPosition(sx, sy, tileSize);
                    var rect = new Rectangle((int)worldPos.X, (int)worldPos.Y, sampleSize, sampleSize);
                    spriteBatch.Draw(pixelTexture, rect, color);
                }
            }
        }

        private void RenderSampleGrid(SpriteBatch spriteBatch, LightingV3Foundation foundation, Texture2D pixelTexture, int tileSize)
        {
            var region = foundation.GetActiveRegion();

            // Draw crosshair at each sample position
            for (int sy = 0; sy < region.RegionHeightSamples; sy++)
            {
                for (int sx = 0; sx < region.RegionWidthSamples; sx++)
                {
                    var worldPos = region.LocalSampleToWorldPosition(sx, sy, tileSize);

                    // Draw small crosshair
                    float x = worldPos.X;
                    float y = worldPos.Y;
                    int size = 2;

                    spriteBatch.Draw(pixelTexture, new Rectangle((int)(x - size), (int)y, size * 2 + 1, 1), Color.Yellow);
                    spriteBatch.Draw(pixelTexture, new Rectangle((int)x, (int)(y - size), 1, size * 2 + 1), Color.Yellow);
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
