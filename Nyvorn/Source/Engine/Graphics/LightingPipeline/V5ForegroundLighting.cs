using Microsoft.Xna.Framework;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Applies sky ambient light to foreground (solid) tiles based on depth from nearest open space.
    /// Uses configurable per-layer weights: 5 layers of attenuation.
    /// </summary>
    public sealed class V5ForegroundLighting
    {
        private readonly WorldMap worldMap;
        private readonly V5SkyAmbientConfig config;
        private readonly V5SkyAmbientField field;

        // Cache: distance to nearest open sky for each foreground tile
        // -1 = not computed, 0-4 = layer, 5+ = deep shadow
        private int[] foregroundDepthCache = System.Array.Empty<int>();
        private int cacheOriginX, cacheOriginY, cacheWidth, cacheHeight;

        public V5ForegroundLighting(WorldMap worldMap, V5SkyAmbientConfig config, V5SkyAmbientField field)
        {
            this.worldMap = worldMap;
            this.config = config;
            this.field = field;
        }

        /// <summary>
        /// Prepare foreground depth cache for the visible window.
        /// </summary>
        public void PrepareDepthCache(int windowX, int windowY, int windowWidth, int windowHeight)
        {
            int margin = config.CameraMarginTiles;
            cacheOriginX = windowX - margin;
            cacheOriginY = windowY - margin;
            cacheWidth = windowWidth + (margin * 2);
            cacheHeight = windowHeight + (margin * 2);

            int cellCount = cacheWidth * cacheHeight;
            if (foregroundDepthCache.Length < cellCount)
                foregroundDepthCache = new int[cellCount];

            // Mark all as uncomputed
            for (int i = 0; i < cellCount; i++)
                foregroundDepthCache[i] = -1;

            // Compute depth for all solid tiles in window
            for (int y = 0; y < cacheHeight; y++)
            {
                for (int x = 0; x < cacheWidth; x++)
                {
                    int worldX = cacheOriginX + x;
                    int worldY = cacheOriginY + y;

                    if (!worldMap.IsSolidAt(worldX, worldY))
                        continue;

                    int depth = ComputeDepthToOpenSpace(worldX, worldY);
                    int index = (y * cacheWidth) + x;
                    foregroundDepthCache[index] = depth;
                }
            }
        }

        /// <summary>
        /// Get the light contribution for a foreground tile based on ambient field and depth.
        /// </summary>
        public float GetForegroundLightIntensity(int tileX, int tileY)
        {
            int localX = tileX - cacheOriginX;
            int localY = tileY - cacheOriginY;

            if (localX < 0 || localX >= cacheWidth || localY < 0 || localY >= cacheHeight)
                return 0f;

            int index = (localY * cacheWidth) + localX;
            int depth = foregroundDepthCache[index];

            if (depth < 0)
                return 0f; // Not a solid tile or uncomputed

            float weight = depth switch
            {
                0 => config.ForegroundLayer1Weight,
                1 => config.ForegroundLayer2Weight,
                2 => config.ForegroundLayer3Weight,
                3 => config.ForegroundLayer4Weight,
                _ => config.ForegroundLayer5PlusWeight
            };

            // Sample the field at the surface (first solid tile of this column)
            int topY = tileY;
            while (topY >= 0 && worldMap.IsSolidAt(tileX, topY))
                topY--;
            topY++; // Back to the first solid

            float fieldIntensity = field.SampleField(tileX, topY);
            return fieldIntensity * weight;
        }

        /// <summary>
        /// Compute how many layers deep this solid tile is from the nearest open space.
        /// 0 = directly on surface, 1 = one layer deep, etc.
        /// Used for foreground depth-based attenuation.
        /// </summary>
        private int ComputeDepthToOpenSpace(int tileX, int tileY)
        {
            if (!worldMap.IsSolidAt(tileX, tileY))
                return -1;

            // Scan upward to find the surface
            int depth = 0;
            for (int y = tileY; y >= 0; y--)
            {
                if (!worldMap.IsSolidAt(tileX, y))
                {
                    // Found open space
                    depth = tileY - y;
                    break;
                }

                if (y == 0)
                    depth = tileY + 1; // Deep
            }

            return System.Math.Min(depth, 5); // Cap at 5
        }
    }
}
