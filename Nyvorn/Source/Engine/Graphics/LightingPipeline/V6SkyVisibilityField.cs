using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// V6 Direct Sky Visibility: geometric classification of direct exposure to sky.
    ///
    /// For each column X, stores the first (lowest) Y coordinate that blocks direct sky.
    /// Queries: IsDirectSkyVisible(x,y) returns true iff y < firstSkyOccluderY[wrappedX].
    ///
    /// SEMANTICS (geometric, layer-independent):
    /// "Does there exist an unobstructed vertical line from this cell upward to y=0?"
    ///
    /// A cell blocks DirectSky when:
    /// - IsSolidAt(x, y) == true (foreground present), OR
    /// - GetBackgroundTile(x, y) != TileType.Empty (background present)
    ///
    /// This is the same "sky-open" definition used in V5:
    /// sky-open = NOT foreground AND NOT background
    ///
    /// Independent of HasOpenSkyAbove (which has layer-specific behavior).
    /// No hardcoded layer thresholds (y >= 960).
    /// </summary>
    public sealed class V6SkyVisibilityField
    {
        private readonly WorldMap worldMap;
        private readonly int[] firstSkyOccluderY;  // Per-column: smallest Y that occludes sky

        public int Width => worldMap.Width;
        public int Height => worldMap.Height;

        public V6SkyVisibilityField(WorldMap worldMap)
        {
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
            this.firstSkyOccluderY = new int[worldMap.Width];

            // Initial build: find first sky occluder per column
            BuildAllColumns();
        }

        /// <summary>
        /// Check if a cell has direct sky visibility.
        /// O(1) query: returns true iff y < firstSkyOccluderY[wrappedX].
        ///
        /// Note: This returns false for cells that ARE solid/background themselves.
        /// For light sampling, check IsSolidAt/GetBackgroundTile first if needed.
        /// </summary>
        public bool IsDirectSkyVisible(int tileX, int tileY)
        {
            if (tileY < 0)
                return true;  // Above world
            if (tileY >= Height)
                return false;  // Below world

            int wrappedX = worldMap.WrapTileX(tileX);
            return tileY < firstSkyOccluderY[wrappedX];
        }

        /// <summary>
        /// Build all columns: scan each column from y=0 upward to find first sky occluder.
        /// </summary>
        private void BuildAllColumns()
        {
            for (int x = 0; x < Width; x++)
            {
                RecomputeColumn(x);
            }
        }

        /// <summary>
        /// Recompute a single column: find the first Y that occludes sky.
        /// Called on tile changes within that column.
        /// </summary>
        public void RecomputeColumn(int x)
        {
            // Default: no occluder in this column
            firstSkyOccluderY[x] = Height;

            // Scan from y=0 upward to find first occluder
            for (int y = 0; y < Height; y++)
            {
                // Check foreground (always occludes if present)
                if (worldMap.IsSolidAt(x, y))
                {
                    firstSkyOccluderY[x] = y;
                    return;  // First occluder found
                }

                // Check background (occludes if present, regardless of layer)
                if (worldMap.GetBackgroundTile(x, y) != TileType.Empty)
                {
                    firstSkyOccluderY[x] = y;
                    return;  // First occluder found
                }
            }
        }

        /// <summary>
        /// Invalidate a column after a tile change.
        /// Called by WorldMap.SetTile() or SetBackgroundTile() integration.
        /// </summary>
        public void InvalidateColumn(int x)
        {
            int wrappedX = worldMap.WrapTileX(x);
            RecomputeColumn(wrappedX);
        }

        /// <summary>
        /// Invalidate all columns (expensive, use only if needed).
        /// </summary>
        public void InvalidateAll()
        {
            BuildAllColumns();
        }

        /// <summary>
        /// DEBUG ONLY: Brute-force reference implementation.
        /// Scans upward from y to y=0 looking for any occluder (foreground or background).
        /// Used to validate the optimized firstSkyOccluderY algorithm.
        /// Returns true if there exists a clear vertical line to y=0.
        /// </summary>
        public bool ReferenceDirectSky(int tileX, int tileY)
        {
            if (tileY < 0)
                return true;
            if (tileY >= Height)
                return false;

            int wrappedX = worldMap.WrapTileX(tileX);

            // Scan upward from tileY-1 to 0
            for (int scanY = tileY - 1; scanY >= 0; scanY--)
            {
                // If foreground present, sky is blocked
                if (worldMap.IsSolidAt(wrappedX, scanY))
                    return false;

                // If background present, sky is blocked
                if (worldMap.GetBackgroundTile(wrappedX, scanY) != TileType.Empty)
                    return false;
            }

            // No occluder found between tileY and y=0
            return true;
        }

        /// <summary>
        /// DEBUG: Compare optimized vs reference implementation.
        /// </summary>
        public bool ValidatePosition(int tileX, int tileY, out string mismatchReason)
        {
            mismatchReason = "";

            bool optimized = IsDirectSkyVisible(tileX, tileY);
            bool reference = ReferenceDirectSky(tileX, tileY);

            if (optimized == reference)
                return true;

            mismatchReason = $"Mismatch at ({tileX},{tileY}): optimized={optimized}, reference={reference}";
            return false;
        }
    }
}
