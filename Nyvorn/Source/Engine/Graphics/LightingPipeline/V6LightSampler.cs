using System;
using Microsoft.Xna.Framework;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// V6 Light Sampler: Query light at arbitrary positions with bilinear interpolation.
    ///
    /// ETAPA 3: Player uses shared V6LightMap directly.
    /// No legacy lighting consulted. Only V6LightMap.
    ///
    /// Sample position: Entity.Position (world coordinates in pixels)
    /// Conversion: worldPosition / TileSize → tile coordinates
    /// Interpolation: Bilinear over 4 neighboring cells in tile space
    /// Fallback: Clamp to buffer edges if outside active buffer
    ///
    /// Used by player, entities, and world objects to sample RGB light from V6LightMap.
    /// Implements IEntityLightSampler for compatibility with entity rendering pipeline.
    /// </summary>
    public sealed class V6LightSampler : IEntityLightSampler
    {
        private readonly V6LightMap lightMap;
        private readonly WorldMap worldMap;

        public V6LightSampler(V6LightMap lightMap, WorldMap worldMap)
        {
            this.lightMap = lightMap ?? throw new ArgumentNullException(nameof(lightMap));
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
        }

        /// <summary>
        /// Sample light at world position with bilinear interpolation.
        /// Returns Color; outside buffer returns dark fallback.
        /// </summary>
        public Color SampleLightAt(float worldX, float worldY)
        {
            var (r, g, b) = SampleLightAtRGB(worldX, worldY);
            return new Color(
                (int)Math.Clamp(r * 255, 0, 255),
                (int)Math.Clamp(g * 255, 0, 255),
                (int)Math.Clamp(b * 255, 0, 255)
            );
        }

        /// <summary>
        /// Sample light at world position, return as normalized RGB (0..1).
        ///
        /// UNIT CONVERSION:
        /// Input: worldX, worldY in pixels (world coordinates)
        /// Convert: divide by TileSize to get tile coordinates (fractional)
        /// Local: subtract BufferOriginTile to get position within active buffer
        /// Bilinear: interpolate over 4 neighboring tile cells
        /// </summary>
        public (float r, float g, float b) SampleLightAtRGB(float worldX, float worldY)
        {
            int tileSize = worldMap.TileSize;

            // Convert from world pixels to world tile coordinates (fractional)
            float worldTileX = worldX / tileSize;
            float worldTileY = worldY / tileSize;

            int originX = lightMap.BufferOriginTileX;
            int originY = lightMap.BufferOriginTileY;

            // Convert to local buffer coordinates (relative to buffer origin)
            float localX = worldTileX - originX;
            float localY = worldTileY - originY;

            // Clamp to buffer bounds (not dark fallback, just clamp)
            if (localX < 0f)
                localX = 0f;
            if (localX >= lightMap.BufferWidth)
                localX = lightMap.BufferWidth - 0.5f;  // Slightly inside to avoid x1 out of bounds
            if (localY < 0f)
                localY = 0f;
            if (localY >= lightMap.BufferHeight)
                localY = lightMap.BufferHeight - 0.5f;

            // Integer and fractional parts
            int ix = (int)localX;
            int iy = (int)localY;
            float fx = localX - ix;
            float fy = localY - iy;

            // Bilinear interpolation over 2x2 neighborhood
            // Always safe because we clamped localX/Y above
            int x0 = Math.Max(0, ix);
            int x1 = Math.Min(lightMap.BufferWidth - 1, ix + 1);
            int y0 = Math.Max(0, iy);
            int y1 = Math.Min(lightMap.BufferHeight - 1, iy + 1);

            var (r00, g00, b00) = lightMap.GetLightAtLocal(x0, y0);
            var (r10, g10, b10) = lightMap.GetLightAtLocal(x1, y0);
            var (r01, g01, b01) = lightMap.GetLightAtLocal(x0, y1);
            var (r11, g11, b11) = lightMap.GetLightAtLocal(x1, y1);

            float r = Bilinear(r00, r10, r01, r11, fx, fy);
            float g = Bilinear(g00, g10, g01, g11, fx, fy);
            float b = Bilinear(b00, b10, b01, b11, fx, fy);

            return (r, g, b);
        }

        private static float Bilinear(float v00, float v10, float v01, float v11, float fx, float fy)
        {
            float v0 = v00 * (1 - fx) + v10 * fx;
            float v1 = v01 * (1 - fx) + v11 * fx;
            return v0 * (1 - fy) + v1 * fy;
        }

        // IEntityLightSampler implementation
        Color IEntityLightSampler.SampleLightAt(Vector2 worldPosition)
        {
            return SampleLightAt(worldPosition.X, worldPosition.Y);
        }

        IEntityLightMetrics IEntityLightSampler.GetMetrics()
        {
            return new V6LightSamplerMetrics();
        }

        /// <summary>
        /// Simple metrics recorder for V6 sampling (minimal tracking).
        /// </summary>
        private class V6LightSamplerMetrics : IEntityLightMetrics
        {
            public void RecordDraw() { }
            public void RecordPlayerLightSample() { }
            public void RecordEntityTintApply() { }
        }
    }
}
