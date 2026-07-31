using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Defines the active lighting region independent of camera render target capacity.
    /// Handles sub-tile sampling space and coordinate conversions.
    ///
    /// Region is always defined in logical render size (screenW, screenH in pixels),
    /// NOT in RenderTarget capacity (which may be larger due to grow-only allocation).
    ///
    /// Conversions:
    /// - World tile → local tile (within region)
    /// - World position → local sample (in sub-tile space)
    /// - Local sample → world position
    /// - Horizontal wrapping handled transparently
    /// </summary>
    public class ActiveLightingRegion
    {
        private readonly LightingSamplingConfig _samplingConfig;

        /// <summary>
        /// Logical render width in pixels.
        /// </summary>
        public int LogicalRenderWidth { get; private set; }

        /// <summary>
        /// Logical render height in pixels.
        /// </summary>
        public int LogicalRenderHeight { get; private set; }

        /// <summary>
        /// World origin of the region in pixels (left edge of camera view).
        /// </summary>
        public float WorldOriginX { get; private set; }

        /// <summary>
        /// World origin of the region in pixels (top edge of camera view).
        /// </summary>
        public float WorldOriginY { get; private set; }

        /// <summary>
        /// Width of the region in tiles.
        /// </summary>
        public int RegionWidthTiles { get; private set; }

        /// <summary>
        /// Height of the region in tiles.
        /// </summary>
        public int RegionHeightTiles { get; private set; }

        /// <summary>
        /// Width of the region in samples.
        /// </summary>
        public int RegionWidthSamples => RegionWidthTiles * _samplingConfig.SamplesPerAxis;

        /// <summary>
        /// Height of the region in samples.
        /// </summary>
        public int RegionHeightSamples => RegionHeightTiles * _samplingConfig.SamplesPerAxis;

        /// <summary>
        /// Total number of samples in this region.
        /// </summary>
        public int TotalSamples => RegionWidthSamples * RegionHeightSamples;

        /// <summary>
        /// Margin around camera view in tiles (for sampling beyond visible area).
        /// Default: 1 tile.
        /// </summary>
        public int MarginTiles { get; set; } = 1;

        /// <summary>
        /// World wrapping width in tiles (for horizontal wrapping).
        /// 0 = no wrapping.
        /// </summary>
        public int WorldWrappingWidthTiles { get; set; } = 0;

        public ActiveLightingRegion(LightingSamplingConfig samplingConfig)
        {
            _samplingConfig = samplingConfig ?? throw new System.ArgumentNullException(nameof(samplingConfig));
            LogicalRenderWidth = 0;
            LogicalRenderHeight = 0;
            WorldOriginX = 0f;
            WorldOriginY = 0f;
            RegionWidthTiles = 0;
            RegionHeightTiles = 0;
        }

        /// <summary>
        /// Update the active lighting region based on camera position and render size.
        /// Called before each lighting update.
        /// </summary>
        public void Update(float cameraWorldX, float cameraWorldY, int logicalRenderWidth, int logicalRenderHeight, int tileSize)
        {
            LogicalRenderWidth = logicalRenderWidth;
            LogicalRenderHeight = logicalRenderHeight;

            // Calculate region bounds in tiles (including margin)
            int visibleWidthTiles = (logicalRenderWidth + tileSize - 1) / tileSize;
            int visibleHeightTiles = (logicalRenderHeight + tileSize - 1) / tileSize;

            RegionWidthTiles = visibleWidthTiles + (MarginTiles * 2);
            RegionHeightTiles = visibleHeightTiles + (MarginTiles * 2);

            // Calculate world origin (top-left of region)
            int leftTile = (int)System.Math.Floor(cameraWorldX / tileSize);
            int topTile = (int)System.Math.Floor(cameraWorldY / tileSize);

            leftTile -= MarginTiles;
            topTile -= MarginTiles;

            WorldOriginX = leftTile * tileSize;
            WorldOriginY = topTile * tileSize;
        }

        /// <summary>
        /// Convert world tile coordinate to local tile coordinate (within region).
        /// Returns -1 if tile is outside region.
        /// </summary>
        public int WorldTileToLocalTile(int worldTileX, int tileSize)
        {
            if (WorldWrappingWidthTiles > 0)
            {
                worldTileX = ((worldTileX % WorldWrappingWidthTiles) + WorldWrappingWidthTiles) % WorldWrappingWidthTiles;
            }

            int leftTile = (int)System.Math.Floor(WorldOriginX / tileSize);
            int localTile = worldTileX - leftTile;

            if (localTile < 0 || localTile >= RegionWidthTiles)
                return -1;

            return localTile;
        }

        /// <summary>
        /// Convert world position to local sample coordinate.
        /// Returns (-1, -1) if position is outside region.
        /// </summary>
        public Point WorldPositionToLocalSample(float worldPixelX, float worldPixelY, int tileSize)
        {
            // Handle wrapping
            if (WorldWrappingWidthTiles > 0)
            {
                float wrappingPixels = WorldWrappingWidthTiles * tileSize;
                worldPixelX = ((worldPixelX % wrappingPixels) + wrappingPixels) % wrappingPixels;
            }

            float sampleX = (worldPixelX - WorldOriginX) / (_samplingConfig.SampleSpacingTiles * tileSize);
            float sampleY = (worldPixelY - WorldOriginY) / (_samplingConfig.SampleSpacingTiles * tileSize);

            int sampleIntX = (int)System.Math.Floor(sampleX);
            int sampleIntY = (int)System.Math.Floor(sampleY);

            if (sampleIntX < 0 || sampleIntX >= RegionWidthSamples ||
                sampleIntY < 0 || sampleIntY >= RegionHeightSamples)
            {
                return new Point(-1, -1);
            }

            return new Point(sampleIntX, sampleIntY);
        }

        /// <summary>
        /// Convert local sample coordinate to world position (center of sample).
        /// </summary>
        public Vector2 LocalSampleToWorldPosition(int localSampleX, int localSampleY, int tileSize)
        {
            float sampleSpacingPixels = _samplingConfig.SampleSpacingTiles * tileSize;
            float worldPixelX = WorldOriginX + (localSampleX + 0.5f) * sampleSpacingPixels;
            float worldPixelY = WorldOriginY + (localSampleY + 0.5f) * sampleSpacingPixels;

            // Apply wrapping if needed
            if (WorldWrappingWidthTiles > 0)
            {
                float wrappingPixels = WorldWrappingWidthTiles * tileSize;
                worldPixelX = ((worldPixelX % wrappingPixels) + wrappingPixels) % wrappingPixels;
            }

            return new Vector2(worldPixelX, worldPixelY);
        }

        /// <summary>
        /// Get flat index for a local sample in the 2D array.
        /// </summary>
        public int SampleToFlatIndex(int sampleX, int sampleY)
        {
            if (sampleX < 0 || sampleX >= RegionWidthSamples || sampleY < 0 || sampleY >= RegionHeightSamples)
                return -1;

            return sampleY * RegionWidthSamples + sampleX;
        }

        /// <summary>
        /// Get 2D coordinates from flat index.
        /// </summary>
        public Point FlatIndexToSample(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= TotalSamples)
                return new Point(-1, -1);

            int sampleY = flatIndex / RegionWidthSamples;
            int sampleX = flatIndex % RegionWidthSamples;

            return new Point(sampleX, sampleY);
        }

        public override string ToString()
        {
            return $"ActiveLightingRegion({RegionWidthTiles}x{RegionHeightTiles} tiles, " +
                   $"{RegionWidthSamples}x{RegionHeightSamples} samples={TotalSamples}, " +
                   $"origin=({WorldOriginX}, {WorldOriginY}))";
        }
    }
}
