using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Renders debug visualization for Phase 2 foundation.
    /// Draws to backbuffer after main scene rendering (before HUD).
    ///
    /// Modes:
    /// - None: Nothing rendered
    /// - Classification: Colored tiles (Blue/Green/Red)
    /// - SunOpacity: Grayscale opacity visualization
    /// - LocalLightOpacity: Grayscale opacity visualization
    /// - SampleGrid: Sample point markers
    ///
    /// Metrics printed to console via DumpMetrics().
    /// </summary>
    public class LightingV3DebugRenderer
    {
        private readonly Texture2D _pixelTexture;  // 1x1 white pixel for drawing

        public LightingV3DebugRenderer(GraphicsDevice graphicsDevice)
        {
            // Create 1x1 pixel texture for primitive drawing
            _pixelTexture = new Texture2D(graphicsDevice, 1, 1);
            _pixelTexture.SetData(new[] { Color.White });
        }

        /// <summary>
        /// Render debug visualization and mode confirmation text.
        /// Call after scene rendering, before HUD, within spriteBatch.Begin/End pair.
        /// </summary>
        public void Render(SpriteBatch spriteBatch, LightingV3Foundation foundation,
                          LightingDebugMode mode, int tileSize,
                          string modeConfirmationText = "")
        {
            if (mode == LightingDebugMode.None && string.IsNullOrEmpty(modeConfirmationText))
                return;

            if (mode != LightingDebugMode.None)
            {
                switch (mode)
                {
                    case LightingDebugMode.Classification:
                        RenderClassification(spriteBatch, foundation, tileSize);
                        break;

                    case LightingDebugMode.SunOpacity:
                        RenderOpacity(spriteBatch, foundation, tileSize, isLocal: false);
                        break;

                    case LightingDebugMode.LocalLightOpacity:
                        RenderOpacity(spriteBatch, foundation, tileSize, isLocal: true);
                        break;

                    case LightingDebugMode.SampleGrid:
                        RenderSampleGrid(spriteBatch, foundation, tileSize);
                        break;
                }
            }

            // Render mode confirmation text (if just changed mode)
            if (!string.IsNullOrEmpty(modeConfirmationText))
            {
                // Text drawn in simple console-like format in top-left
                // Since we don't have a SpriteFont available, output to console instead
                System.Console.WriteLine($"[LightingV3] {modeConfirmationText}");
            }
        }

        /// <summary>
        /// Render classification with colors (Blue/Green/Red).
        /// </summary>
        private void RenderClassification(SpriteBatch spriteBatch, LightingV3Foundation foundation, int tileSize)
        {
            var region = foundation.GetActiveRegion();
            var classifications = foundation.GetTileClassifications();

            int leftTile = (int)System.Math.Floor(region.WorldOriginX / tileSize);
            int topTile = (int)System.Math.Floor(region.WorldOriginY / tileSize);

            int tileIndex = 0;
            for (int ty = 0; ty < region.RegionHeightTiles; ty++)
            {
                for (int tx = 0; tx < region.RegionWidthTiles; tx++)
                {
                    if (tileIndex >= classifications.Length)
                        break;

                    var classification = classifications[tileIndex++];
                    Color color = classification switch
                    {
                        LightingCellClassification.OpenAtmosphere => Color.Blue * 0.4f,
                        LightingCellClassification.VisibleBackground => Color.Green * 0.4f,
                        LightingCellClassification.SolidForeground => Color.Red * 0.4f,
                        _ => Color.Black
                    };

                    int worldTileX = leftTile + tx;
                    int worldTileY = topTile + ty;
                    int pixelX = worldTileX * tileSize;
                    int pixelY = worldTileY * tileSize;

                    var rect = new Rectangle(pixelX, pixelY, tileSize, tileSize);
                    spriteBatch.Draw(_pixelTexture, rect, color);
                }
            }
        }

        /// <summary>
        /// Render opacity as grayscale (0=black, 1=white).
        /// </summary>
        private void RenderOpacity(SpriteBatch spriteBatch, LightingV3Foundation foundation,
                                  int tileSize, bool isLocal)
        {
            var region = foundation.GetActiveRegion();
            var field = foundation.GetOccluderField();

            float sampleSpacingPixels = region.RegionWidthTiles > 0
                ? (float)(region.RegionWidthTiles * tileSize) / region.RegionWidthSamples
                : 1f;

            for (int sy = 0; sy < region.RegionHeightSamples; sy++)
            {
                for (int sx = 0; sx < region.RegionWidthSamples; sx++)
                {
                    int flatIndex = field.SampleToFlatIndex(sx, sy);
                    if (flatIndex < 0)
                        continue;

                    float opacity = isLocal
                        ? field.GetLocalLightOpacity(flatIndex)
                        : field.GetSunOpacity(flatIndex);

                    // Grayscale: 0=black, 1=white
                    byte value = (byte)(opacity * 255);
                    float normalized = value / 255f;
                    Color color = new Color(normalized, normalized, normalized, 0.5f);

                    var worldPos = region.LocalSampleToWorldPosition(sx, sy, tileSize);
                    int sampleSizePixels = (int)sampleSpacingPixels;
                    if (sampleSizePixels < 1) sampleSizePixels = 1;

                    var rect = new Rectangle((int)worldPos.X - sampleSizePixels / 2,
                                           (int)worldPos.Y - sampleSizePixels / 2,
                                           sampleSizePixels, sampleSizePixels);
                    spriteBatch.Draw(_pixelTexture, rect, color);
                }
            }
        }

        /// <summary>
        /// Render sample grid as crosshairs at sample centers.
        /// </summary>
        private void RenderSampleGrid(SpriteBatch spriteBatch, LightingV3Foundation foundation, int tileSize)
        {
            var region = foundation.GetActiveRegion();

            for (int sy = 0; sy < region.RegionHeightSamples; sy++)
            {
                for (int sx = 0; sx < region.RegionWidthSamples; sx++)
                {
                    var worldPos = region.LocalSampleToWorldPosition(sx, sy, tileSize);

                    float x = worldPos.X;
                    float y = worldPos.Y;
                    int size = 1;  // Small crosshair: 3x3 pixels

                    // Horizontal line
                    var hLine = new Rectangle((int)x - size, (int)y, size * 2 + 1, 1);
                    spriteBatch.Draw(_pixelTexture, hLine, Color.Yellow * 0.7f);

                    // Vertical line
                    var vLine = new Rectangle((int)x, (int)y - size, 1, size * 2 + 1);
                    spriteBatch.Draw(_pixelTexture, vLine, Color.Yellow * 0.7f);
                }
            }
        }

        /// <summary>
        /// Dump metrics and foundation state to console.
        /// Called by Ctrl+Shift+F hotkey.
        /// </summary>
        public static void DumpMetrics(LightingV3Foundation foundation)
        {
            if (foundation == null)
                return;

            System.Console.WriteLine("\n=== LightingV3Foundation Debug Dump ===");
            foundation.DumpMetricsToConsole();
            System.Console.WriteLine("=====================================\n");
        }

        /// <summary>
        /// Dispose debug resources.
        /// </summary>
        public void Dispose()
        {
            _pixelTexture?.Dispose();
        }
    }
}
