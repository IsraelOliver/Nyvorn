using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Interface for sources of occlusion data.
    /// Allows different systems to contribute opacity values to the lighting field.
    ///
    /// Phase 2 mandatory implementations:
    /// - ForegroundTileOccluderProvider
    ///
    /// Phase 2+ optional extensions:
    /// - TreeOccluderProvider (wait for actual tree data before inventing approximations)
    /// - StructureOccluderProvider
    /// - DynamicObjectOccluderProvider
    ///
    /// Each provider contributes independently and results are combined.
    /// </summary>
    public interface IOccluderProvider
    {
        /// <summary>
        /// Human-readable name for debugging.
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Apply this provider's occlusion data to the field.
        /// Called once per lighting update for the active region.
        /// </summary>
        void ApplyOcclusion(OccluderField field, ActiveLightingRegion region, int tileSize);
    }

    /// <summary>
    /// Built-in provider: Foreground tiles provide occlusion.
    /// Mandatory for Phase 2.
    /// </summary>
    public class ForegroundTileOccluderProvider : IOccluderProvider
    {
        private readonly ILightingWorldGeometryProvider _geometryProvider;

        public string ProviderName => "ForegroundTiles";

        public ForegroundTileOccluderProvider(ILightingWorldGeometryProvider geometryProvider)
        {
            _geometryProvider = geometryProvider ?? throw new System.ArgumentNullException(nameof(geometryProvider));
        }

        public void ApplyOcclusion(OccluderField field, ActiveLightingRegion region, int tileSize)
        {
            var classifier = new SceneWorldClassifier(_geometryProvider);

            // Process all tiles in the region
            int leftTile = (int)System.Math.Floor(region.WorldOriginX / tileSize);
            int topTile = (int)System.Math.Floor(region.WorldOriginY / tileSize);

            for (int ty = 0; ty < region.RegionHeightTiles; ty++)
            {
                for (int tx = 0; tx < region.RegionWidthTiles; tx++)
                {
                    int worldTileX = leftTile + tx;
                    int worldTileY = topTile + ty;

                    var classification = classifier.ClassifyTile(worldTileX, worldTileY);

                    // Apply classification to all samples in this tile
                    int baseSampleX = tx * region.RegionWidthSamples / region.RegionWidthTiles;
                    int baseSampleY = ty * region.RegionHeightSamples / region.RegionHeightTiles;
                    int samplesPerAxis = region.RegionWidthSamples / region.RegionWidthTiles;

                    float sunOpacity = 0f;
                    float localLightOpacity = 0f;

                    if (classification == LightingCellClassification.SolidForeground)
                    {
                        sunOpacity = 1f;
                        localLightOpacity = 1f;
                    }
                    else if (classification == LightingCellClassification.VisibleBackground ||
                             classification == LightingCellClassification.OpenAtmosphere)
                    {
                        sunOpacity = 0f;
                        localLightOpacity = 0f;
                    }

                    for (int sy = 0; sy < samplesPerAxis && baseSampleY + sy < field.HeightSamples; sy++)
                    {
                        for (int sx = 0; sx < samplesPerAxis && baseSampleX + sx < field.WidthSamples; sx++)
                        {
                            int sampleX = baseSampleX + sx;
                            int sampleY = baseSampleY + sy;
                            int flatIndex = field.SampleToFlatIndex(sampleX, sampleY);

                            if (flatIndex >= 0)
                                field.SetSample(flatIndex, sunOpacity, localLightOpacity);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Placeholder provider for trees.
    /// Do NOT invent approximations without real tree data.
    /// This marker allows architecture to be extended when tree masks are available.
    /// </summary>
    public class TreeOccluderProvider : IOccluderProvider
    {
        public string ProviderName => "Trees (Phase 2+ - awaiting tree data)";

        public void ApplyOcclusion(OccluderField field, ActiveLightingRegion region, int tileSize)
        {
            // Phase 2: Do nothing. Providers cannot be invented without source data.
            // This provider exists so the architecture supports adding tree occlusion later.
        }
    }

    /// <summary>
    /// Placeholder provider for structures.
    /// Extended when structure occlusion data is available.
    /// </summary>
    public class StructureOccluderProvider : IOccluderProvider
    {
        public string ProviderName => "Structures (Phase 2+ - awaiting structure data)";

        public void ApplyOcclusion(OccluderField field, ActiveLightingRegion region, int tileSize)
        {
            // Phase 2: Do nothing. Awaiting real data.
        }
    }
}
