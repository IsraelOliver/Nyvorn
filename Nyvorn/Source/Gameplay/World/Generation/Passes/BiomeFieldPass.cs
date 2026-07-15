using Nyvorn.Source.World.Generation.Biomes;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class BiomeFieldPass : IWorldGenPass
    {
        public string Name => "BiomeField";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Distribuindo biomas");

            BiomeField field = new(context.WorldMap.Width);
            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                field.SetBiome(x, BiomeType.Forest);

                if ((x & 127) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Distribuindo biomas");
            }

            context.Biomes = field;
            context.ProgressReporter?.Complete(Name, "Biomas distribuidos");
        }
    }
}
