namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Immutable snapshot of lighting V3 foundation state for a single frame.
    /// Ensures renderer cannot read mismatched region/classification/occlusion data.
    /// Published atomically after Foundation.Update() completes.
    /// </summary>
    public class LightingV3FrameData
    {
        /// <summary>
        /// Unique update ID (incremented each Foundation.Update).
        /// Renderer should log this to verify consistency.
        /// </summary>
        public int FoundationUpdateId { get; }

        /// <summary>
        /// Snapshot of active region (immutable).
        /// </summary>
        public ActiveLightingRegion ActiveRegion { get; }

        /// <summary>
        /// Snapshot of tile classifications (copied, not referenced).
        /// </summary>
        public LightingCellClassification[] TileClassifications { get; }

        /// <summary>
        /// Snapshot of occluder field (immutable).
        /// </summary>
        public OccluderField OccluderField { get; }

        /// <summary>
        /// Tile size used for this frame (for coordinate calculations).
        /// </summary>
        public int TileSize { get; }

        public LightingV3FrameData(
            int updateId,
            ActiveLightingRegion activeRegion,
            LightingCellClassification[] tileClassifications,
            OccluderField occluderField,
            int tileSize)
        {
            FoundationUpdateId = updateId;
            ActiveRegion = activeRegion;
            TileClassifications = tileClassifications;
            OccluderField = occluderField;
            TileSize = tileSize;
        }
    }
}
