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

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = context.SurfaceHeights[x];
                BiomeDefinition biome = context.SampleBiome(x).PrimaryDefinition;

                for (int y = 0; y < context.WorldMap.Height; y++)
                {
                    if (y < surfaceY)
                    {
                        context.WorldMap.SetTile(x, y, TileType.Empty);
                    }
                    else if (y == surfaceY)
                    {
                        context.WorldMap.SetTile(x, y, biome.SurfaceTile);
                    }
                    else
                    {
                        TileType fillTile = y <= surfaceY + biome.SubsurfaceDepth
                            ? biome.SubsurfaceTile
                            : TileType.Dirt;
                        context.WorldMap.SetTile(x, y, fillTile);
                    }
                }

                if ((x & 31) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Preenchendo crosta");
            }

            PromoteSurfaceGrassShell(context);

            FillBackgroundShallowUnderground(context);

            context.ProgressReporter?.Complete(Name, "Crosta preenchida");
        }

        private static void FillBackgroundShallowUnderground(WorldGenContext context)
        {
            WorldLayerDefinition shallowLayer = context.GetLayerDefinition(WorldLayerType.ShallowUnderground);

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = context.SurfaceHeights[x];

                for (int y = shallowLayer.StartY; y <= shallowLayer.EndY; y++)
                {
                    context.WorldMap.SetBackgroundTile(x, y, TileType.Dirt);
                }
            }
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

    }
}
