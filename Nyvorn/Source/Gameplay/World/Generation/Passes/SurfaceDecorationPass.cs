namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class SurfaceDecorationPass : IWorldGenPass
    {
        public string Name => "SurfaceDecoration";

        public void Apply(WorldGenContext context)
        {
            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = FindSurfaceTileY(context.WorldMap, x);
                if (surfaceY < 0)
                    continue;

                TileType tile = context.WorldMap.GetTile(x, surfaceY);
                if (tile == TileType.Dirt && IsAir(context.WorldMap, x, surfaceY - 1))
                    context.WorldMap.SetTile(x, surfaceY, TileType.Grass);
            }
        }

        private int FindSurfaceTileY(WorldMap worldMap, int x)
        {
            for (int y = 0; y < worldMap.Height; y++)
            {
                if (worldMap.IsSolidAt(x, y))
                    return y;
            }

            return -1;
        }

        private bool IsAir(WorldMap worldMap, int x, int y)
        {
            return worldMap.GetTile(x, y) == TileType.Empty;
        }
    }
}
