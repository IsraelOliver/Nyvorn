namespace Nyvorn.Source.World
{
    public readonly struct WorldCollisionQuery
    {
        private readonly WorldMap worldMap;

        public WorldCollisionQuery(WorldMap worldMap, WorldCollisionQueryMode mode)
        {
            this.worldMap = worldMap;
            Mode = mode;
        }

        public WorldCollisionQueryMode Mode { get; }
        public int TileSize => worldMap.TileSize;

        public static WorldCollisionQuery SolidTiles(WorldMap worldMap)
            => new(worldMap, WorldCollisionQueryMode.SolidTiles);

        public static WorldCollisionQuery MovementBlockers(WorldMap worldMap)
            => new(worldMap, WorldCollisionQueryMode.MovementBlockers);

        public bool IsBlockedAt(int tileX, int tileY)
        {
            return Mode == WorldCollisionQueryMode.MovementBlockers
                ? worldMap.IsMovementBlockedAt(tileX, tileY)
                : worldMap.IsSolidAt(tileX, tileY);
        }

        public bool HasBlockedInColumn(int tileX, int tileYTop, int tileYBottom)
        {
            for (int y = tileYTop; y <= tileYBottom; y++)
            {
                if (IsBlockedAt(tileX, y))
                    return true;
            }

            return false;
        }

        public bool HasBlockedInRow(int tileY, int tileXLeft, int tileXRight)
        {
            for (int x = tileXLeft; x <= tileXRight; x++)
            {
                if (IsBlockedAt(x, tileY))
                    return true;
            }

            return false;
        }

        public bool HasBlockedInArea(int tileXLeft, int tileXRight, int tileYTop, int tileYBottom)
        {
            for (int y = tileYTop; y <= tileYBottom; y++)
            {
                for (int x = tileXLeft; x <= tileXRight; x++)
                {
                    if (IsBlockedAt(x, y))
                        return true;
                }
            }

            return false;
        }
    }
}
