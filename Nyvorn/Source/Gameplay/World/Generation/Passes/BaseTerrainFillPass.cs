namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class BaseTerrainFillPass : IWorldGenPass
    {
        public string Name => "BaseTerrainFill";

        public void Apply(WorldGenContext context)
        {
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
            }

            context.DebugStats["BaseTerrain.AirTiles"] = airCount.ToString();
            context.DebugStats["BaseTerrain.DirtTiles"] = dirtCount.ToString();
            context.DebugStats["BaseTerrain.StoneTiles"] = "0";
        }
    }
}
