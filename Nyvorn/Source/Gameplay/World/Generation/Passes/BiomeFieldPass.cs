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
            context.DebugStats["Biomes.ForestColumns"] = CountBiome(field, BiomeType.Forest).ToString();
            context.DebugStats["Biomes.DesertColumns"] = CountBiome(field, BiomeType.Desert).ToString();
            context.DebugStats["Biomes.Hash"] = ComputeHash(field).ToString("X16");
            context.ProgressReporter?.Complete(Name, "Biomas distribuidos");
        }

        private static int CountBiome(BiomeField field, BiomeType biome)
        {
            int count = 0;
            for (int x = 0; x < field.Width; x++)
            {
                if (field.GetBiome(x) == biome)
                    count++;
            }

            return count;
        }

        private static ulong ComputeHash(BiomeField field)
        {
            ulong hash = 14695981039346656037UL;
            for (int x = 0; x < field.Width; x++)
            {
                hash ^= (byte)field.GetBiome(x);
                hash *= 1099511628211UL;
            }

            return hash;
        }
    }
}
