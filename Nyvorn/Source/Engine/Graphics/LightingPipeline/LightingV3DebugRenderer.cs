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
        private int _sunVisibilitySamplesDrawn = 0;

        // Bounds diagnostics (reset each frame)
        private int _minDrawWorldX = int.MaxValue;
        private int _maxDrawWorldX = int.MinValue;
        private int _minDrawWorldY = int.MaxValue;
        private int _maxDrawWorldY = int.MinValue;
        private float _sunVisibilityMinWorldX = float.MaxValue;
        private float _sunVisibilityMaxWorldX = float.MinValue;
        private float _sunVisibilityMinWorldY = float.MaxValue;
        private float _sunVisibilityMaxWorldY = float.MinValue;
        private float _sunVisibilityMinValue = float.MaxValue;
        private float _sunVisibilityMaxValue = float.MinValue;
        private float _sunVisibilityAverageValue = 0f;

        // Transform matrix (set each render)
        private Matrix _worldViewTransform = Matrix.Identity;

        // Probe logging (log only on UpdateId change)
        private int _lastLoggedUpdateId = -1;

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
        /// Get sun visibility samples drawn from last frame (for SunVisibility mode).
        /// </summary>
        public int SunVisibilitySamplesDrawn => _sunVisibilitySamplesDrawn;

        /// <summary>
        /// Get sun visibility instrumentation from last frame.
        /// </summary>
        public float SunVisibilityMinimumWorldX => _sunVisibilityMinWorldX == float.MaxValue ? 0f : _sunVisibilityMinWorldX;
        public float SunVisibilityMaximumWorldX => _sunVisibilityMaxWorldX == float.MinValue ? 0f : _sunVisibilityMaxWorldX;
        public float SunVisibilityMinimumWorldY => _sunVisibilityMinWorldY == float.MaxValue ? 0f : _sunVisibilityMinWorldY;
        public float SunVisibilityMaximumWorldY => _sunVisibilityMaxWorldY == float.MinValue ? 0f : _sunVisibilityMaxWorldY;
        public float SunVisibilityMinimumValue => _sunVisibilityMinValue == float.MaxValue ? 0f : _sunVisibilityMinValue;
        public float SunVisibilityMaximumValue => _sunVisibilityMaxValue == float.MinValue ? 0f : _sunVisibilityMaxValue;
        public float SunVisibilityAverageValue => _sunVisibilityAverageValue;

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
        /// frameData must be captured ONCE by caller and passed here (immutable snapshot).
        /// </summary>
        public void Render(SpriteBatch spriteBatch, LightingV3FrameData frameData,
                          LightingDebugMode mode, int tileSize, Matrix worldViewTransform)
        {
            // Store the world-view transform for use in render methods
            _worldViewTransform = worldViewTransform;

            // Store for diagnostic dump only (not logged every frame)
            _lastLoggedUpdateId = frameData.UpdateId;

            // Reset frame counters and bounds
            _classificationTilesDrawn = 0;
            _sunOpacitySamplesDrawn = 0;
            _localOpacitySamplesDrawn = 0;
            _sampleGridPointsDrawn = 0;
            _sunVisibilitySamplesDrawn = 0;
            _minDrawWorldX = int.MaxValue;
            _maxDrawWorldX = int.MinValue;
            _minDrawWorldY = int.MaxValue;
            _maxDrawWorldY = int.MinValue;
            _sunVisibilityMinWorldX = float.MaxValue;
            _sunVisibilityMaxWorldX = float.MinValue;
            _sunVisibilityMinWorldY = float.MaxValue;
            _sunVisibilityMaxWorldY = float.MinValue;
            _sunVisibilityMinValue = float.MaxValue;
            _sunVisibilityMaxValue = float.MinValue;
            _sunVisibilityAverageValue = 0f;

            if (mode == LightingDebugMode.None)
                return;

            if (mode != LightingDebugMode.None)
            {
                switch (mode)
                {
                    case LightingDebugMode.Classification:
                        RenderClassification(spriteBatch, frameData, tileSize);
                        break;

                    case LightingDebugMode.SunVisibility:
                        RenderSunVisibility(spriteBatch, frameData, tileSize);
                        break;

                    case LightingDebugMode.SunOpacity:
                        RenderOpacity(spriteBatch, frameData, tileSize, isLocal: false);
                        break;

                    case LightingDebugMode.LocalLightOpacity:
                        RenderOpacity(spriteBatch, frameData, tileSize, isLocal: true);
                        break;

                    case LightingDebugMode.SampleGrid:
                        RenderSampleGrid(spriteBatch, frameData, tileSize);
                        break;
                }
            }
        }

        /// <summary>
        /// Render classification with colors (Blue/Green/Red) - strong colors for diagnosis.
        /// </summary>
        private void RenderClassification(SpriteBatch spriteBatch, LightingV3FrameData frameData, int tileSize)
        {
            var region = frameData.Region;
            var classifications = frameData.TileClassifications;

            int leftTile = region.WorldOriginX / tileSize;
            int topTile = region.WorldOriginY / tileSize;

            int tileIndex = 0;
            for (int ty = 0; ty < region.TileHeight; ty++)
            {
                for (int tx = 0; tx < region.TileWidth; tx++)
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
        private void RenderOpacity(SpriteBatch spriteBatch, LightingV3FrameData frameData,
                                  int tileSize, bool isLocal)
        {
            var region = frameData.Region;
            var opacityBuffer = isLocal ? frameData.LocalOpacityBuffer : frameData.SunOpacityBuffer;

            float sampleSpacingPixels = region.TileWidth > 0
                ? (float)(region.TileWidth * tileSize) / region.SampleWidth
                : 1f;

            for (int sy = 0; sy < region.SampleHeight; sy++)
            {
                for (int sx = 0; sx < region.SampleWidth; sx++)
                {
                    int flatIndex = sy * region.SampleWidth + sx;
                    if (flatIndex >= opacityBuffer.Length)
                        continue;

                    float opacity = opacityBuffer[flatIndex];

                    // Grayscale: 0=black, 1=white (with higher alpha for visibility)
                    byte value = (byte)(opacity * 255);
                    float normalized = value / 255f;
                    Color color = new Color(normalized, normalized, normalized, 0.8f);

                    // Calculate world position from local sample coordinates
                    float worldX = region.WorldOriginX + (sx + 0.5f) * (region.TileWidth * tileSize) / region.SampleWidth;
                    float worldY = region.WorldOriginY + (sy + 0.5f) * (region.TileHeight * tileSize) / region.SampleHeight;

                    int sampleSizePixels = (int)sampleSpacingPixels;
                    if (sampleSizePixels < 1) sampleSizePixels = 1;

                    var rect = new Rectangle((int)worldX - sampleSizePixels / 2,
                                           (int)worldY - sampleSizePixels / 2,
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
        private void RenderSampleGrid(SpriteBatch spriteBatch, LightingV3FrameData frameData, int tileSize)
        {
            var region = frameData.Region;

            for (int sy = 0; sy < region.SampleHeight; sy++)
            {
                for (int sx = 0; sx < region.SampleWidth; sx++)
                {
                    // Calculate world position from local sample coordinates
                    float worldX = region.WorldOriginX + (sx + 0.5f) * (region.TileWidth * tileSize) / region.SampleWidth;
                    float worldY = region.WorldOriginY + (sy + 0.5f) * (region.TileHeight * tileSize) / region.SampleHeight;

                    float x = worldX;
                    float y = worldY;
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
        /// Render sun visibility field as grayscale (Phase 3.2A).
        /// Instruments counters and bounds for diagnostics.
        /// </summary>
        private void RenderSunVisibility(SpriteBatch spriteBatch, LightingV3FrameData frameData, int tileSize)
        {
            var region = frameData.Region;
            var sunVisibility = frameData.SunVisibility;
            int sampleCount = frameData.SampleCount;

            if (sampleCount == 0 || sunVisibility.Length == 0)
                return;

            // Calculate sample spacing
            float sampleSpacingX = region.SampleWidth > 0
                ? (float)(region.TileWidth * tileSize) / region.SampleWidth
                : 1f;
            float sampleSpacingY = region.SampleHeight > 0
                ? (float)(region.TileHeight * tileSize) / region.SampleHeight
                : 1f;

            int sampleSize = (int)System.Math.Max(2, System.Math.Min(sampleSpacingX, sampleSpacingY));
            double sumVisibility = 0.0;

            // Draw and track metrics
            for (int sy = 0; sy < region.SampleHeight; sy++)
            {
                for (int sx = 0; sx < region.SampleWidth; sx++)
                {
                    int flatIndex = sy * region.SampleWidth + sx;
                    if (flatIndex >= sampleCount || flatIndex >= sunVisibility.Length)
                        continue;

                    float visibility = sunVisibility[flatIndex];

                    // Grayscale: 0=black (blocked), 1=white (free)
                    byte value = (byte)(visibility * 255);
                    Color color = new Color((byte)value, (byte)value, (byte)value, (byte)220);

                    // Calculate world position
                    float worldX = region.WorldOriginX + sx * sampleSpacingX;
                    float worldY = region.WorldOriginY + sy * sampleSpacingY;

                    var rect = new Rectangle((int)worldX, (int)worldY, sampleSize, sampleSize);
                    spriteBatch.Draw(_pixelTexture, rect, color);

                    // Track instrumentation
                    _sunVisibilitySamplesDrawn++;
                    sumVisibility += visibility;

                    if (worldX < _sunVisibilityMinWorldX)
                        _sunVisibilityMinWorldX = worldX;
                    if (worldX > _sunVisibilityMaxWorldX)
                        _sunVisibilityMaxWorldX = worldX;

                    if (worldY < _sunVisibilityMinWorldY)
                        _sunVisibilityMinWorldY = worldY;
                    if (worldY > _sunVisibilityMaxWorldY)
                        _sunVisibilityMaxWorldY = worldY;

                    if (visibility < _sunVisibilityMinValue)
                        _sunVisibilityMinValue = visibility;
                    if (visibility > _sunVisibilityMaxValue)
                        _sunVisibilityMaxValue = visibility;
                }
            }

            // Calculate average
            _sunVisibilityAverageValue = _sunVisibilitySamplesDrawn > 0
                ? (float)(sumVisibility / _sunVisibilitySamplesDrawn)
                : 0f;
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
