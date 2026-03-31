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

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = context.SurfaceHeights[x];

                for (int y = 0; y < context.WorldMap.Height; y++)
                {
                    if (y < surfaceY)
                    {
                        context.WorldMap.SetTile(x, y, TileType.Empty);
                        airCount++;
                    }
                    else
                    {
                        context.WorldMap.SetTile(x, y, TileType.Dirt);
                        dirtCount++;
                    }
                }

                if ((x & 31) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Preenchendo crosta");
            }

            context.DebugStats["BaseTerrain.AirTiles"] = airCount.ToString();
            context.DebugStats["BaseTerrain.DirtTiles"] = dirtCount.ToString();
            context.DebugStats["BaseTerrain.StoneTiles"] = "0";
            context.ProgressReporter?.Complete(Name, "Crosta preenchida");
        }
    }
}
