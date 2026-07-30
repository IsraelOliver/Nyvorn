using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Adapter that bridges Lighting V2 to the existing WorldMap.
    /// Allows Lighting V2 to query world data without direct coupling to WorldMap.
    /// </summary>
    public sealed class WorldDataAdapter : IWorldDataProvider
    {
        private readonly WorldMap worldMap;

        public WorldDataAdapter(WorldMap worldMap)
        {
            this.worldMap = worldMap ?? throw new System.ArgumentNullException(nameof(worldMap));
        }

        public int TileSize => worldMap.TileSize;
        public int WorldWidth => worldMap.Width;
        public int WorldHeight => worldMap.Height;

        public bool IsForegroundSolidAt(int tileX, int tileY)
        {
            return worldMap.IsSolidAt(tileX, tileY);
        }

        public TileType GetForegroundTile(int tileX, int tileY)
        {
            return worldMap.GetTile(tileX, tileY);
        }

        public bool HasBackgroundWallAt(int tileX, int tileY)
        {
            return worldMap.IsBackgroundSolidAt(tileX, tileY);
        }

        public bool IsBackgroundSolidAt(int tileX, int tileY)
        {
            return worldMap.IsBackgroundSolidAt(tileX, tileY);
        }

        public TileType GetBackgroundTile(int tileX, int tileY)
        {
            return worldMap.GetBackgroundTile(tileX, tileY);
        }

        public bool HasOpenSkyAbove(int tileX, int tileY)
        {
            return worldMap.HasOpenSkyAbove(tileX, tileY);
        }

        public int WrapTileX(int tileX)
        {
            return worldMap.WrapTileX(tileX);
        }

        public bool IsInBounds(int tileX, int tileY)
        {
            return worldMap.InBounds(tileX, tileY);
        }
    }
}
