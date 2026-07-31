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

        // Frame counters (reset each frame)
        private int _classificationTilesDrawn = 0;
        private int _sunOpacitySamplesDrawn = 0;
        private int _localOpacitySamplesDrawn = 0;
        private int _sampleGridPointsDrawn = 0;

        // Bounds diagnostics (reset each frame)
        private int _minDrawWorldX = int.MaxValue;
        private int _maxDrawWorldX = int.MinValue;
        private int _minDrawWorldY = int.MaxValue;
        private int _maxDrawWorldY = int.MinValue;

        // Transform matrix (set each render)
        private Matrix _worldViewTransform = Matrix.Identity;

        public LightingV3DebugRenderer(GraphicsDevice graphicsDevice)
        {
            // Create 1x1 pixel texture for primitive drawing
            _pixelTexture = new Texture2D(graphicsDevice, 1, 1);
            _pixelTexture.SetData(new[] { Color.White });
        }

        /// <summary>
        /// Get drawn tile count from last frame (for Classification mode).
        /// </summary>
        public int ClassificationTilesDrawn => _classificationTilesDrawn;

        /// <summary>
        /// Get drawn sample count from last frame (for Opacity modes).
        /// </summary>
        public int SunOpacitySamplesDrawn => _sunOpacitySamplesDrawn;
        public int LocalOpacitySamplesDrawn => _localOpacitySamplesDrawn;

        /// <summary>
        /// Get drawn point count from last frame (for SampleGrid mode).
        /// </summary>
        public int SampleGridPointsDrawn => _sampleGridPointsDrawn;

        /// <summary>
        /// Get world-space bounds of drawn primitives (for diagnostics).
        /// </summary>
        public int MinDrawWorldX => _minDrawWorldX == int.MaxValue ? 0 : _minDrawWorldX;
        public int MaxDrawWorldX => _maxDrawWorldX == int.MinValue ? 0 : _maxDrawWorldX;
        public int MinDrawWorldY => _minDrawWorldY == int.MaxValue ? 0 : _minDrawWorldY;
        public int MaxDrawWorldY => _maxDrawWorldY == int.MinValue ? 0 : _maxDrawWorldY;

        /// <summary>
        /// Render debug visualization and mode confirmation text.
        /// CRITICAL: worldViewTransform must be the EXACT matrix used for world rendering:
        ///   Matrix.CreateTranslation(worldOffset, 0f, 0f) * camera.GetViewMatrix()
        /// This ensures Classification, Opacity, and SampleGrid align with terrain/entities.
        /// For non-wrapping scenarios, use just camera.GetViewMatrix() with worldOffset=0.
        /// </summary>
        public void Render(SpriteBatch spriteBatch, LightingV3Foundation foundation,
                          LightingDebugMode mode, int tileSize, Matrix worldViewTransform,
                          string modeConfirmationText = "")
        {
            // Store the world-view transform for use in render methods
            _worldViewTransform = worldViewTransform;

            // DIAGNOSTIC: Log transform for comparison with world pass
            if (mode == LightingDebugMode.SunOpacity || mode == LightingDebugMode.LocalLightOpacity)
            {
                var region = foundation?.GetActiveRegion();
                if (region != null)
                {
                    System.Console.WriteLine($"[V3DebugRender] WorldOrigin: ({region.WorldOriginX}, {region.WorldOriginY})");

                    // Sample diagnostic: first sample position
                    var firstSampleWorldPos = region.LocalSampleToWorldPosition(0, 0, tileSize);
                    System.Console.WriteLine($"[V3DebugRender] First sample world position: ({firstSampleWorldPos.X}, {firstSampleWorldPos.Y})");
                }
            }

            // Reset frame counters and bounds
            _classificationTilesDrawn = 0;
            _sunOpacitySamplesDrawn = 0;
            _localOpacitySamplesDrawn = 0;
            _sampleGridPointsDrawn = 0;
            _minDrawWorldX = int.MaxValue;
            _maxDrawWorldX = int.MinValue;
            _minDrawWorldY = int.MaxValue;
            _maxDrawWorldY = int.MinValue;

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
        /// Render classification with colors (Blue/Green/Red) - strong colors for diagnosis.
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
                        LightingCellClassification.OpenAtmosphere => new Color(0, 80, 255, 120),
                        LightingCellClassification.VisibleBackground => new Color(0, 255, 80, 140),
                        LightingCellClassification.SolidForeground => new Color(255, 30, 30, 120),
                        _ => Color.Black
                    };

                    int worldTileX = leftTile + tx;
                    int worldTileY = topTile + ty;
                    int pixelX = worldTileX * tileSize;
                    int pixelY = worldTileY * tileSize;

                    var rect = new Rectangle(pixelX, pixelY, tileSize, tileSize);
                    spriteBatch.Draw(_pixelTexture, rect, color);
                    _classificationTilesDrawn++;

                    // Track world-space bounds
                    _minDrawWorldX = System.Math.Min(_minDrawWorldX, pixelX);
                    _maxDrawWorldX = System.Math.Max(_maxDrawWorldX, pixelX + tileSize);
                    _minDrawWorldY = System.Math.Min(_minDrawWorldY, pixelY);
                    _maxDrawWorldY = System.Math.Max(_maxDrawWorldY, pixelY + tileSize);
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

                    // Grayscale: 0=black, 1=white (with higher alpha for visibility)
                    byte value = (byte)(opacity * 255);
                    float normalized = value / 255f;
                    Color color = new Color(normalized, normalized, normalized, 0.8f);

                    var worldPos = region.LocalSampleToWorldPosition(sx, sy, tileSize);
                    int sampleSizePixels = (int)sampleSpacingPixels;
                    if (sampleSizePixels < 1) sampleSizePixels = 1;

                    var rect = new Rectangle((int)worldPos.X - sampleSizePixels / 2,
                                           (int)worldPos.Y - sampleSizePixels / 2,
                                           sampleSizePixels, sampleSizePixels);
                    spriteBatch.Draw(_pixelTexture, rect, color);

                    if (isLocal)
                        _localOpacitySamplesDrawn++;
                    else
                        _sunOpacitySamplesDrawn++;
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
                    int size = 2;  // Larger point for visibility: 5x5 pixels

                    // Horizontal line (bright yellow)
                    var hLine = new Rectangle((int)x - size, (int)y, size * 2 + 1, 1);
                    spriteBatch.Draw(_pixelTexture, hLine, Color.Yellow);

                    // Vertical line (bright yellow)
                    var vLine = new Rectangle((int)x, (int)y - size, 1, size * 2 + 1);
                    spriteBatch.Draw(_pixelTexture, vLine, Color.Yellow);

                    _sampleGridPointsDrawn++;
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
