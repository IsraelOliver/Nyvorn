using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public static class WorldObjectPlacementValidator
    {
        public static bool CanPlaceObject(WorldMap worldMap, Player player, Rectangle bounds)
        {
            return IsClearOfPlayer(player, bounds) &&
                   IsWithinInteractionRange(player, bounds) &&
                   CanOccupyGridArea(worldMap, bounds);
        }

        public static bool IsClearOfPlayer(Player player, Rectangle bounds)
            => player != null && !bounds.Intersects(player.Hurtbox);

        public static bool IsWithinInteractionRange(Player player, Rectangle bounds)
            => player != null &&
               Vector2.Distance(player.Position, bounds.Center.ToVector2()) <= player.WorldInteractionRange;

        public static bool CanOccupyGridArea(WorldMap worldMap, Rectangle bounds)
        {
            if (worldMap == null)
                return false;

            int startTileX = bounds.Left / worldMap.TileSize;
            int endTileX = (bounds.Right - 1) / worldMap.TileSize;
            int startTileY = bounds.Top / worldMap.TileSize;
            int endTileY = (bounds.Bottom - 1) / worldMap.TileSize;

            for (int y = startTileY; y <= endTileY; y++)
            {
                if (!worldMap.InBounds(startTileX, y))
                    return false;

                for (int x = startTileX; x <= endTileX; x++)
                {
                    if (worldMap.IsSolidAt(x, y) || worldMap.IsObjectOccupiedAt(x, y))
                        return false;
                }
            }

            return true;
        }
    }
}
