using System;
using Nyvorn.Source.World.Tissue;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class TissuePass : IWorldGenPass
    {
        public string Name => "Tissue";

        public void Apply(WorldGenContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.ProgressReporter?.Begin(Name, "Tecendo rede cosmica subterranea");

            TissueGenerationResult generation = new TissueGenerator(SeedHash.ToIntSeed(context.Seeds.TissueSeed)).Generate(context.WorldMap);
            context.TissueField = generation.RasterizedField;
            context.TissueGeneration = generation;
            context.WorldMap.SetTissueField(generation.RasterizedField);

            context.ProgressReporter?.Report(Name, 1f, "Rede cosmica consolidada");
            context.ProgressReporter?.Complete(Name, "Tecido cosmico preparado");
        }
    }
}
