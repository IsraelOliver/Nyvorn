using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public static class GrassSimulation
    {
        private const float RandomUpdateChance = 0.28f;

        public static bool TryRandomUpdate(WorldMap worldMap, int x, int y, Random random)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            if (!CanDirtBecomeGrass(worldMap, x, y))
                return false;

            if (random.NextSingle() > RandomUpdateChance)
                return false;

            worldMap.SetTile(x, y, TileType.Grass);
            return true;
        }

        public static bool CanDirtBecomeGrass(WorldMap worldMap, int x, int y)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            if (!worldMap.InBounds(x, y) || worldMap.GetTile(x, y) != TileType.Dirt)
                return false;

            return (HasExposedSide(worldMap, x, y) && HasAdjacentGrass(worldMap, x, y))
                || HasGrassCornerConnection(worldMap, x, y);
        }

        private static bool HasExposedSide(WorldMap worldMap, int x, int y)
        {
            return worldMap.GetTile(x, y - 1) == TileType.Empty
                || worldMap.GetTile(x - 1, y) == TileType.Empty
                || worldMap.GetTile(x + 1, y) == TileType.Empty
                || worldMap.GetTile(x, y + 1) == TileType.Empty;
        }

        private static bool HasAdjacentGrass(WorldMap worldMap, int x, int y)
        {
            return worldMap.GetTile(x - 1, y) == TileType.Grass
                || worldMap.GetTile(x + 1, y) == TileType.Grass
                || worldMap.GetTile(x, y - 1) == TileType.Grass
                || worldMap.GetTile(x, y + 1) == TileType.Grass
                || worldMap.GetTile(x - 1, y - 1) == TileType.Grass
                || worldMap.GetTile(x + 1, y - 1) == TileType.Grass
                || worldMap.GetTile(x - 1, y + 1) == TileType.Grass
                || worldMap.GetTile(x + 1, y + 1) == TileType.Grass;
        }

        private static bool HasGrassCornerConnection(WorldMap worldMap, int x, int y)
        {
            return HasGrassCornerConnection(worldMap, x, y, 0, -1, -1, 0, -1, -1)
                || HasGrassCornerConnection(worldMap, x, y, 0, -1, 1, 0, 1, -1)
                || HasGrassCornerConnection(worldMap, x, y, 0, 1, -1, 0, -1, 1)
                || HasGrassCornerConnection(worldMap, x, y, 0, 1, 1, 0, 1, 1);
        }

        private static bool HasGrassCornerConnection(
            WorldMap worldMap,
            int x,
            int y,
            int firstGrassOffsetX,
            int firstGrassOffsetY,
            int secondGrassOffsetX,
            int secondGrassOffsetY,
            int diagonalAirOffsetX,
            int diagonalAirOffsetY)
        {
            return worldMap.GetTile(x + firstGrassOffsetX, y + firstGrassOffsetY) == TileType.Grass
                && worldMap.GetTile(x + secondGrassOffsetX, y + secondGrassOffsetY) == TileType.Grass
                && worldMap.GetTile(x + diagonalAirOffsetX, y + diagonalAirOffsetY) == TileType.Empty;
        }
    }
}
