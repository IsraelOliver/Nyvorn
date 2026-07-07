using System.Collections.Generic;
using Nyvorn.Source.Gameplay.World.Simulation;
using Nyvorn.Source.World.Generation.Biomes;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class BaseTerrainFillPass : IWorldGenPass
    {
        public string Name => "BaseTerrainFill";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Preenchendo crosta");

            int airCount = 0;
            int dirtCount = 0;
            int grassCount = 0;
            int sandCount = 0;

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = context.SurfaceHeights[x];
                BiomeDefinition biome = context.SampleBiome(x).PrimaryDefinition;

                for (int y = 0; y < context.WorldMap.Height; y++)
                {
                    if (y < surfaceY)
                    {
                        context.WorldMap.SetTile(x, y, TileType.Empty);
                        airCount++;
                    }
                    else if (y == surfaceY)
                    {
                        context.WorldMap.SetTile(x, y, biome.SurfaceTile);
                        if (biome.SurfaceTile == TileType.Grass)
                            grassCount++;
                        else if (biome.SurfaceTile == TileType.Dirt)
                            dirtCount++;
                        else if (IsSandLike(biome.SurfaceTile))
                            sandCount++;
                    }
                    else
                    {
                        TileType fillTile = y <= surfaceY + biome.SubsurfaceDepth
                            ? biome.SubsurfaceTile
                            : TileType.Dirt;
                        context.WorldMap.SetTile(x, y, fillTile);
                        if (fillTile == TileType.Grass)
                            grassCount++;
                        else if (IsSandLike(fillTile))
                            sandCount++;
                        else
                            dirtCount++;
                    }
                }

                if ((x & 31) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Preenchendo crosta");
            }

            int promotedGrassCount = PromoteSurfaceGrassShell(context);
            grassCount += promotedGrassCount;
            dirtCount -= promotedGrassCount;

            context.DebugStats["BaseTerrain.AirTiles"] = airCount.ToString();
            context.DebugStats["BaseTerrain.DirtTiles"] = dirtCount.ToString();
            context.DebugStats["BaseTerrain.GrassTiles"] = grassCount.ToString();
            context.DebugStats["BaseTerrain.SandTiles"] = sandCount.ToString();
            context.DebugStats["BaseTerrain.StoneTiles"] = "0";
            context.ProgressReporter?.Complete(Name, "Crosta preenchida");
        }

        private static int PromoteSurfaceGrassShell(WorldGenContext context)
        {
            int promotedTotal = 0;
            bool promotedAny;

            do
            {
                promotedAny = false;
                List<(int X, int Y)> grassTiles = new();

                for (int x = 0; x < context.WorldMap.Width; x++)
                {
                    for (int y = 0; y < context.WorldMap.Height; y++)
                    {
                        if (GrassSimulation.CanDirtBecomeGrass(context.WorldMap, x, y))
                        {
                            grassTiles.Add((x, y));
                        }
                    }
                }

                for (int i = 0; i < grassTiles.Count; i++)
                {
                    (int x, int y) = grassTiles[i];
                    context.WorldMap.SetTile(x, y, TileType.Grass);
                }

                if (grassTiles.Count > 0)
                {
                    promotedAny = true;
                    promotedTotal += grassTiles.Count;
                }
            }
            while (promotedAny);

            return promotedTotal;
        }

        private static bool IsSandLike(TileType tile)
        {
            return tile == TileType.Sand || tile == TileType.HardenedSand;
        }

    }
}
