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

            TissueGenerationStats stats = generation.Stats;
            context.DebugStats["Tissue.GenerationMode"] = "CosmicWeb";
            context.DebugStats["Tissue.Points"] = stats.PointCount.ToString();
            context.DebugStats["Tissue.Edges"] = stats.EdgeCount.ToString();
            context.DebugStats["Tissue.MicroFilaments"] = stats.MicroFilamentCount.ToString();
            context.DebugStats["Tissue.Nests"] = stats.NestCount.ToString();
            context.DebugStats["Tissue.RasterizedTiles"] = stats.RasterizedTileCount.ToString();
            context.DebugStats["Tissue.Degree"] = $"{stats.MinDegree}/{stats.AverageDegree:0.00}/{stats.MaxDegree}";
            context.DebugStats["Tissue.DominantComponent"] = stats.DominantComponentRatio.ToString("0.000");
            context.DebugStats["Tissue.VerticalPoints"] = $"{stats.UpperPointCount}/{stats.MiddlePointCount}/{stats.DeepPointCount}";
            context.DebugStats["Tissue.Hash"] = stats.DeterministicHash.ToString("X16");
            context.DebugStats["Tissue.Schema"] = "Presence,Vitality,Corruption,MemoryDensity,Flow";

            context.ProgressReporter?.Report(Name, 1f, "Rede cosmica consolidada");
            context.ProgressReporter?.Complete(Name, "Tecido cosmico preparado");
        }
    }
}
