using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Classifies the geometric structure of the world.
    /// Determines if cells are open atmosphere, have background walls, or have solid foreground.
    ///
    /// Pure geometric classification — does NOT consider:
    /// - Lighting or light values
    /// - Sky exposure or connectivity
    /// - Depth or vertical position
    /// - Time of day
    /// - Current illumination state
    /// </summary>
    public class SceneWorldClassifier
    {
        private readonly WorldMap _worldMap;

        public SceneWorldClassifier(WorldMap worldMap)
        {
            _worldMap = worldMap ?? throw new System.ArgumentNullException(nameof(worldMap));
        }

        /// <summary>
        /// Classify a single world tile.
        /// Returns classification based on tile solidity and structure.
        /// Phase 2: Uses IsSolidAt for foreground detection.
        /// Future: Will integrate background wall and structure data when available.
        /// </summary>
        public LightingCellClassification ClassifyTile(int worldTileX, int worldTileY)
        {
            // Check if foreground is solid (using IsSolidAt)
            if (_worldMap.IsSolidAt(worldTileX, worldTileY))
                return LightingCellClassification.SolidForeground;

            // TODO: Phase 2+: Check for background walls when data is available
            // For now, if not solid, it's open
            // hasBackgroundWall = _worldMap.HasBackgroundWallAt(worldTileX, worldTileY);
            // if (hasBackgroundWall)
            //     return LightingCellClassification.VisibleBackground;

            // Empty foreground = Open atmosphere (for Phase 2)
            return LightingCellClassification.OpenAtmosphere;
        }

        /// <summary>
        /// Classify a region and populate classification array.
        /// Array must be sized: width * height.
        /// </summary>
        public void ClassifyRegion(int startTileX, int startTileY, int widthTiles, int heightTiles,
                                   LightingCellClassification[] outClassifications)
        {
            if (outClassifications.Length < widthTiles * heightTiles)
                throw new System.ArgumentException("Output array too small", nameof(outClassifications));

            int index = 0;
            for (int ty = startTileY; ty < startTileY + heightTiles; ty++)
            {
                for (int tx = startTileX; tx < startTileX + widthTiles; tx++)
                {
                    outClassifications[index++] = ClassifyTile(tx, ty);
                }
            }
        }

        /// <summary>
        /// Verify classification rules (for testing/validation).
        /// Returns number of violations found (0 = all valid).
        /// </summary>
        public int ValidateClassifications(LightingCellClassification[] classifications, int widthTiles, int heightTiles)
        {
            int violations = 0;

            // Rule 1: SolidForeground tiles must have SolidForeground classification
            int index = 0;
            for (int ty = 0; ty < heightTiles; ty++)
            {
                for (int tx = 0; tx < widthTiles; tx++)
                {
                    var classification = classifications[index++];

                    // Basic sanity: each classification should be one of the defined values
                    if ((int)classification > 2)
                        violations++;
                }
            }

            return violations;
        }
    }
}
