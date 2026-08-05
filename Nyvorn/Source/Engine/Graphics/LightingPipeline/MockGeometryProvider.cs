using System;
using System.Collections.Generic;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Mock geometry provider for deterministic testing.
    /// Allows tests to set up specific tile configurations without depending on WorldMap.
    ///
    /// Usage:
    /// <code>
    /// var mock = new MockGeometryProvider();
    /// mock.SetTile(0, 0, foregroundSolid: true, backgroundWall: false);
    ///
    /// var classifier = new SceneWorldClassifier(mock);
    /// var result = classifier.ClassifyTile(0, 0);
    /// Assert(result == LightingCellClassification.SolidForeground);
    /// </code>
    /// </summary>
    public class MockGeometryProvider : ILightingWorldGeometryProvider
    {
        private Dictionary<(int, int), (bool foregroundSolid, bool backgroundWall)> _tiles = new();
        private int _worldWidth = 1000;
        private int _worldHeight = 1000;

        public MockGeometryProvider()
        {
        }

        /// <summary>
        /// Set tile configuration.
        /// </summary>
        public void SetTile(int tileX, int tileY, bool foregroundSolid, bool backgroundWall)
        {
            _tiles[(tileX, tileY)] = (foregroundSolid, backgroundWall);
        }

        /// <summary>
        /// Clear all tiles (reset to default empty state).
        /// </summary>
        public void Clear()
        {
            _tiles.Clear();
        }

        /// <summary>
        /// Set world dimensions for bounds checking.
        /// </summary>
        public void SetWorldSize(int width, int height)
        {
            _worldWidth = width;
            _worldHeight = height;
        }

        // ILightingWorldGeometryProvider implementation

        public bool IsForegroundSolidAt(int tileX, int tileY)
        {
            if (!IsInBounds(tileX, tileY))
                return false;

            return _tiles.TryGetValue((tileX, tileY), out var t) && t.foregroundSolid;
        }

        public bool HasBackgroundWallAt(int tileX, int tileY)
        {
            if (!IsInBounds(tileX, tileY))
                return false;

            return _tiles.TryGetValue((tileX, tileY), out var t) && t.backgroundWall;
        }

        public int WrapTileX(int tileX)
        {
            if (_worldWidth <= 0)
                return tileX;

            int wrapped = tileX % _worldWidth;
            return wrapped < 0 ? wrapped + _worldWidth : wrapped;
        }

        public bool IsInBounds(int tileX, int tileY)
        {
            return tileY >= 0 && tileY < _worldHeight;
        }

        public bool HasOpenSkyAbove(int tileX, int tileY)
        {
            // Mock implementation: check for open path upward from this tile to y=0
            // Scan upward and return false if we hit any solid or background wall
            int wrappedX = WrapTileX(tileX);
            for (int scanY = tileY - 1; scanY >= 0; scanY--)
            {
                if (IsForegroundSolidAt(wrappedX, scanY) || HasBackgroundWallAt(wrappedX, scanY))
                    return false;
            }
            return true;
        }

        public int WorldWidthTiles => _worldWidth;

        public int WorldHeightTiles => _worldHeight;
    }


    /// <summary>
    /// Test cases for Phase 3.1: Sun direction and color.
    /// Verify immutability, normalization, and frame integration.
    /// </summary>
    public static class Phase3_1Tests
    {
    }
}
