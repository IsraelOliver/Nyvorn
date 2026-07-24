using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics.Liquids;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Decorations;
using System;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    // Regrows mushrooms over time via the slow-tick random tile sampler, so a world the player
    // stripped bare slowly recovers. Growth keeps the worldgen theme: grass near a tree trunk, or
    // any natural floor beside water. A world-size cap keeps the total in check.
    public static class SurfaceDecorationGrowthSimulation
    {
        private const float RandomUpdateChance = 0.5f;
        private const int MinSpacingTiles = 4;
        private const int NearTreeDistanceTiles = 7;
        private const int NearTreeVerticalTiles = 4;
        private const int NearWaterRadiusX = 5;
        private const int NearWaterRadiusY = 4;
        private const int MinWorldCap = 50;
        private const int MaxWorldCap = 500;
        private const int WorldCapWidthDivisor = 20;

        public static int GetWorldCap(WorldMap worldMap)
        {
            return Math.Clamp(worldMap.Width / WorldCapWidthDivisor, MinWorldCap, MaxWorldCap);
        }

        // The sampled tile is the candidate ground tile; the mushroom sprouts in the air cell on
        // top of it.
        public static bool TryRandomUpdate(WorldMap worldMap, LiquidSystem liquidSystem, int x, int y, Random random)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            if (worldMap.SurfaceDecorations.Count >= GetWorldCap(worldMap))
                return false;

            TileType ground = worldMap.GetTile(x, y);
            if (ground != TileType.Grass && ground != TileType.Dirt && ground != TileType.Stone)
                return false;

            int spawnY = y - 1;
            if (!worldMap.InBounds(x, spawnY) || worldMap.IsSolidAt(x, spawnY))
                return false;

            // Never sprout submerged - "beside the pool", not inside it.
            if (liquidSystem != null && liquidSystem.GetLiquidAmountAtTile(x, spawnY) > 0)
                return false;

            if (HasDecorationNearby(worldMap, x, spawnY))
                return false;

            bool nearTree = ground == TileType.Grass && IsNearTree(worldMap, x, spawnY);
            if (!nearTree && !IsNearWater(worldMap, liquidSystem, x, spawnY))
                return false;

            if (random.NextSingle() > RandomUpdateChance)
                return false;

            return worldMap.TryAddSurfaceDecoration(new SurfaceDecorationInstance
            {
                Tile = new Point(worldMap.WrapTileX(x), spawnY),
                Type = SurfaceDecorationType.Mushroom,
                Seed = random.Next()
            });
        }

        private static bool HasDecorationNearby(WorldMap worldMap, int x, int y)
        {
            for (int i = 0; i < worldMap.SurfaceDecorations.Count; i++)
            {
                Point tile = worldMap.SurfaceDecorations[i].Tile;
                if (Math.Abs(tile.Y - y) > MinSpacingTiles)
                    continue;

                if (WrappedDistanceX(worldMap, tile.X, x) <= MinSpacingTiles)
                    return true;
            }

            return false;
        }

        private static bool IsNearTree(WorldMap worldMap, int x, int y)
        {
            for (int i = 0; i < worldMap.Trees.Count; i++)
            {
                Point baseTile = worldMap.Trees[i].BaseTile;
                if (Math.Abs(baseTile.Y - y) > NearTreeVerticalTiles)
                    continue;

                if (WrappedDistanceX(worldMap, baseTile.X, x) <= NearTreeDistanceTiles)
                    return true;
            }

            return false;
        }

        private static bool IsNearWater(WorldMap worldMap, LiquidSystem liquidSystem, int x, int y)
        {
            if (liquidSystem == null)
                return false;

            for (int dy = -NearWaterRadiusY; dy <= NearWaterRadiusY; dy++)
            {
                for (int dx = -NearWaterRadiusX; dx <= NearWaterRadiusX; dx++)
                {
                    if (liquidSystem.GetLiquidAmountAtTile(worldMap.WrapTileX(x + dx), y + dy) > 0)
                        return true;
                }
            }

            return false;
        }

        private static int WrappedDistanceX(WorldMap worldMap, int a, int b)
        {
            int delta = Math.Abs(worldMap.WrapTileX(a) - worldMap.WrapTileX(b));
            return Math.Min(delta, worldMap.Width - delta);
        }
    }
}
