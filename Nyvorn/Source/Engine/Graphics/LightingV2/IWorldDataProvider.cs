using Microsoft.Xna.Framework;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Minimal interface for Lighting V2 to query world data.
    /// Shields V2 from direct WorldMap coupling.
    /// </summary>
    public interface IWorldDataProvider
    {
        /// <summary>Tile size in pixels (typically 16).</summary>
        int TileSize { get; }

        /// <summary>World width in tiles.</summary>
        int WorldWidth { get; }

        /// <summary>World height in tiles.</summary>
        int WorldHeight { get; }

        /// <summary>Check if foreground tile at (tileX, tileY) is solid.</summary>
        bool IsForegroundSolidAt(int tileX, int tileY);

        /// <summary>Get the foreground tile type.</summary>
        TileType GetForegroundTile(int tileX, int tileY);

        /// <summary>Check if there is a background wall (solid background tile) at this location.</summary>
        bool HasBackgroundWallAt(int tileX, int tileY);

        /// <summary>Check if background tile at (tileX, tileY) is solid (legacy API compatibility).</summary>
        bool IsBackgroundSolidAt(int tileX, int tileY);

        /// <summary>Get the background tile type.</summary>
        TileType GetBackgroundTile(int tileX, int tileY);

        /// <summary>Check if there is open sky above this tile (clear vertical line to y=0).</summary>
        bool HasOpenSkyAbove(int tileX, int tileY);

        /// <summary>Wrap tile X coordinate (world is toroidal horizontally).</summary>
        int WrapTileX(int tileX);

        /// <summary>Check if tile coordinates are in world bounds.</summary>
        bool IsInBounds(int tileX, int tileY);
    }
}
