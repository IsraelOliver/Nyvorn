namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class WorldBoundsPass : IWorldGenPass
    {
        public string Name => "WorldBounds";

        public void Apply(WorldGenContext context)
        {
            for (int x = 0; x < context.WorldMap.Width; x++)
                context.WorldMap.SetTile(x, context.WorldMap.Height - 1, TileType.Stone);
        }
    }
}
