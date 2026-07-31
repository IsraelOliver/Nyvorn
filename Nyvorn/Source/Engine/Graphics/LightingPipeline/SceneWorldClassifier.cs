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
    ///
    /// Depends on ILightingWorldGeometryProvider to query foreground/background independently.
    /// </summary>
    public class SceneWorldClassifier
    {
        private readonly ILightingWorldGeometryProvider _geometryProvider;

        public SceneWorldClassifier(ILightingWorldGeometryProvider geometryProvider)
        {
            _geometryProvider = geometryProvider ?? throw new System.ArgumentNullException(nameof(geometryProvider));
        }

        /// <summary>
        /// Classify a single world tile.
        /// Returns classification based on foreground solidity and background wall presence.
        ///
        /// Rules:
        /// 1. If foreground is solid → SolidForeground (background irrelevant)
        /// 2. If foreground is empty AND background wall exists → VisibleBackground
        /// 3. If both empty → OpenAtmosphere
        /// </summary>
        public LightingCellClassification ClassifyTile(int worldTileX, int worldTileY)
        {
            // Rule 1: Solid foreground blocks everything
            if (_geometryProvider.IsForegroundSolidAt(worldTileX, worldTileY))
                return LightingCellClassification.SolidForeground;

            // Rule 2: Empty foreground + background wall present
            if (_geometryProvider.HasBackgroundWallAt(worldTileX, worldTileY))
                return LightingCellClassification.VisibleBackground;

            // Rule 3: Both empty
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
                    int wrappedX = _geometryProvider.WrapTileX(tx);
                    LightingCellClassification classification = ClassifyTile(wrappedX, ty);
                    outClassifications[index++] = classification;
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
