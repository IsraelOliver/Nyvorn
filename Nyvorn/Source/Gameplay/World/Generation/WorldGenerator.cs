using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation.Passes;
using System;

namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenerator
    {
        private readonly IWorldGenPass[] generationPasses;
        private WorldGenConfig lastConfig;

        public WorldGenerator()
        {
            generationPasses = new IWorldGenPass[]
            {
                new ClearWorldPass(),
                new LayerBoundaryPass(),
                new SurfaceProfilePass(),
                new BaseTerrainFillPass(),
                new CavePass(),
                new CaveEntrancePass(),
                new TissuePass(),
                new WorldBoundsPass()
            };
        }

        public void Generate(WorldMap worldMap, WorldGenConfig config)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            lastConfig = config;

            WorldGenContext context = new WorldGenContext
            {
                WorldMap = worldMap,
                Config = config,
                Random = new Random(config.Seed)
            };

            for (int i = 0; i < generationPasses.Length; i++)
            {
                if (!config.Debug.IsEnabled(generationPasses[i].Name))
                    continue;

                generationPasses[i].Apply(context);
            }
        }

        public int GetSurfaceTileY(WorldMap worldMap, int tileX)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            int clampedX = Math.Clamp(tileX, 0, worldMap.Width - 1);
            for (int y = 0; y < worldMap.Height; y++)
            {
                if (worldMap.IsSolidAt(clampedX, y))
                    return y;
            }

            return lastConfig?.SurfaceBaseHeight ?? Math.Max(0, worldMap.Height / 3);
        }

        public Vector2 GetSurfaceSpawnPosition(WorldMap worldMap, int tileX, int tilesAboveSurface = 0)
        {
            int clampedX = Math.Clamp(tileX, 0, worldMap.Width - 1);
            int surfaceY = GetSurfaceTileY(worldMap, clampedX);
            float x = worldMap.GetTileCenter(clampedX, surfaceY).X;
            float y = (surfaceY - tilesAboveSurface) * worldMap.TileSize;
            return new Vector2(x, y);
        }
    }
}
