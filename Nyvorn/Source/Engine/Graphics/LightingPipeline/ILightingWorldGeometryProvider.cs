namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Interface for querying geometric world data (foreground/background only).
    /// Used by SceneWorldClassifier to classify tiles without direct WorldMap coupling.
    ///
    /// Provides access to:
    /// - Foreground solidity (actual blocks)
    /// - Background walls (solid background tiles)
    ///
    /// Does NOT provide:
    /// - Lighting data
    /// - Sky exposure
    /// - Vertical connectivity
    /// - Decorations or dynamic objects (those use IOccluderProvider)
    /// </summary>
    public interface ILightingWorldGeometryProvider
    {
        /// <summary>
        /// Check if foreground tile at (tileX, tileY) is solid.
        /// </summary>
        bool IsForegroundSolidAt(int tileX, int tileY);

        /// <summary>
        /// Check if background wall (solid background tile) exists at (tileX, tileY).
        /// </summary>
        bool HasBackgroundWallAt(int tileX, int tileY);

        /// <summary>
        /// Wrap tile X coordinate (handles horizontal world wrapping).
        /// </summary>
        int WrapTileX(int tileX);

        /// <summary>
        /// Check if tile coordinates are in world bounds.
        /// </summary>
        bool IsInBounds(int tileX, int tileY);

        /// <summary>
        /// Get world width in tiles.
        /// </summary>
        int WorldWidthTiles { get; }

        /// <summary>
        /// Get world height in tiles.
        /// </summary>
        int WorldHeightTiles { get; }

        /// <summary>
        /// Check if there is a clear vertical line to the top of the world (y=0).
        /// Used for detecting sky exposure in lighting systems.
        /// </summary>
        bool HasOpenSkyAbove(int tileX, int tileY);
    }

}
