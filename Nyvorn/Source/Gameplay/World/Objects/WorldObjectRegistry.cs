using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public sealed class WorldObjectRegistry
    {
        private readonly List<IWorldObjectOccupancyProvider> occupancyProviders = new();
        private readonly List<IWorldObjectMovementBlocker> movementBlockers = new();
        private readonly List<IForegroundTileBreakListener> foregroundTileBreakListeners = new();

        public void Register(object worldObjectSystem)
        {
            if (worldObjectSystem == null)
                return;

            if (worldObjectSystem is IWorldObjectOccupancyProvider occupancyProvider &&
                !occupancyProviders.Contains(occupancyProvider))
            {
                occupancyProviders.Add(occupancyProvider);
            }

            if (worldObjectSystem is IWorldObjectMovementBlocker movementBlocker &&
                !movementBlockers.Contains(movementBlocker))
            {
                movementBlockers.Add(movementBlocker);
            }

            if (worldObjectSystem is IForegroundTileBreakListener foregroundTileBreakListener &&
                !foregroundTileBreakListeners.Contains(foregroundTileBreakListener))
            {
                foregroundTileBreakListeners.Add(foregroundTileBreakListener);
            }
        }

        public bool IsObjectOccupyingTile(int tileX, int tileY)
        {
            for (int i = 0; i < occupancyProviders.Count; i++)
            {
                if (occupancyProviders[i].IsObjectOccupyingTile(tileX, tileY))
                    return true;
            }

            return false;
        }

        public bool IsMovementBlockingTile(int tileX, int tileY)
        {
            for (int i = 0; i < movementBlockers.Count; i++)
            {
                if (movementBlockers[i].IsMovementBlockingTile(tileX, tileY))
                    return true;
            }

            return false;
        }

        public void NotifyForegroundTileBroken(ForegroundTileBrokenContext context)
        {
            for (int i = 0; i < foregroundTileBreakListeners.Count; i++)
                foregroundTileBreakListeners[i].OnForegroundTileBroken(context);
        }
    }
}
