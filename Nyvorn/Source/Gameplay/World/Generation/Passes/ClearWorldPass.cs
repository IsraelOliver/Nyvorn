namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class ClearWorldPass : IWorldGenPass
    {
        public string Name => "ClearWorld";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Limpando mapa base");

            for (int y = 0; y < context.WorldMap.Height; y++)
            {
                for (int x = 0; x < context.WorldMap.Width; x++)
                    context.WorldMap.SetTile(x, y, TileType.Empty);

                if ((y & 15) == 0 || y == context.WorldMap.Height - 1)
                    context.ProgressReporter?.Report(Name, (y + 1) / (float)context.WorldMap.Height, "Limpando mapa base");
            }

            context.ProgressReporter?.Complete(Name, "Mapa base limpo");
        }
    }
}
