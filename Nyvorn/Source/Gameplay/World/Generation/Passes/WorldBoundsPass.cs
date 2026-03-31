namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class WorldBoundsPass : IWorldGenPass
    {
        public string Name => "WorldBounds";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Selando limites do mundo");

            for (int x = 0; x < context.WorldMap.Width; x++)
                context.WorldMap.SetTile(x, context.WorldMap.Height - 1, TileType.Stone);

            context.ProgressReporter?.Complete(Name, "Limites do mundo selados");
        }
    }
}
