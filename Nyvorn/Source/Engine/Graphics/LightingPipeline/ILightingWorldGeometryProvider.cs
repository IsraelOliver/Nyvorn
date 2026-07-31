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
    }

    /// <summary>
    /// Adapter: Bridge between IWorldDataProvider (V2 API) and ILightingWorldGeometryProvider (V3 API).
    /// Allows Phase 2 foundation to use existing world data without creating new dependencies.
    /// </summary>
    public sealed class WorldDataGeometryAdapter : ILightingWorldGeometryProvider
    {
        private readonly Engine.Graphics.LightingV2.IWorldDataProvider _worldDataProvider;

        public WorldDataGeometryAdapter(Engine.Graphics.LightingV2.IWorldDataProvider worldDataProvider)
        {
            _worldDataProvider = worldDataProvider ?? throw new System.ArgumentNullException(nameof(worldDataProvider));
        }

        public bool IsForegroundSolidAt(int tileX, int tileY) => _worldDataProvider.IsForegroundSolidAt(tileX, tileY);

        public bool HasBackgroundWallAt(int tileX, int tileY) => _worldDataProvider.HasBackgroundWallAt(tileX, tileY);

        public int WrapTileX(int tileX) => _worldDataProvider.WrapTileX(tileX);

        public bool IsInBounds(int tileX, int tileY) => _worldDataProvider.IsInBounds(tileX, tileY);

        public int WorldWidthTiles => _worldDataProvider.WorldWidth;

        public int WorldHeightTiles => _worldDataProvider.WorldHeight;
    }
}
