using Microsoft.Xna.Framework;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public static class WorldObjectSupport
    {
        public static bool HasFullBaseSupport(WorldMap worldMap, Rectangle bounds)
        {
            if (worldMap == null)
                return false;

            int supportTileY = GetBaseSupportTileY(bounds, worldMap.TileSize);
            GetHorizontalTileSpan(bounds, worldMap.TileSize, out int startTileX, out int endTileX);

            for (int x = startTileX; x <= endTileX; x++)
            {
                if (!worldMap.IsSolidAt(x, supportTileY))
                    return false;
            }

            return true;
        }

        public static bool IsBaseSupportTile(Rectangle bounds, Point tile, int tileSize)
        {
            int supportTileY = GetBaseSupportTileY(bounds, tileSize);
            if (tile.Y != supportTileY)
                return false;

            GetHorizontalTileSpan(bounds, tileSize, out int startTileX, out int endTileX);
            return tile.X >= startTileX && tile.X <= endTileX;
        }

        public static bool RemoveObjectsWithBrokenBaseSupport<T>(
            IList<T> objects,
            Point brokenTile,
            WorldMap worldMap,
            Action<T> onObjectRemoved)
            where T : IBaseSupportedWorldObject
        {
            if (objects == null || worldMap == null || !worldMap.InBounds(brokenTile.X, brokenTile.Y))
                return false;

            Point wrappedTile = new Point(worldMap.WrapTileX(brokenTile.X), brokenTile.Y);
            bool removedAny = false;

            for (int i = objects.Count - 1; i >= 0; i--)
            {
                T worldObject = objects[i];
                if (!IsBaseSupportTile(worldObject.Bounds, wrappedTile, worldMap.TileSize))
                    continue;

                onObjectRemoved?.Invoke(worldObject);
                objects.RemoveAt(i);
                removedAny = true;
            }

            return removedAny;
        }

        private static int GetBaseSupportTileY(Rectangle bounds, int tileSize)
            => bounds.Bottom / tileSize;

        private static void GetHorizontalTileSpan(Rectangle bounds, int tileSize, out int startTileX, out int endTileX)
        {
            startTileX = bounds.Left / tileSize;
            endTileX = (bounds.Right - 1) / tileSize;
        }
    }
}
