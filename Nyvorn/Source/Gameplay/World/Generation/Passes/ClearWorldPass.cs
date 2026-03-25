namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class ClearWorldPass : IWorldGenPass
    {
        public string Name => "ClearWorld";

        public void Apply(WorldGenContext context)
        {
            for (int y = 0; y < context.WorldMap.Height; y++)
            {
                for (int x = 0; x < context.WorldMap.Width; x++)
                    context.WorldMap.SetTile(x, y, TileType.Empty);
            }
        }
    }
}
