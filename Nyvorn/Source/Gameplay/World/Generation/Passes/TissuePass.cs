using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class TissuePass : IWorldGenPass
    {
        public string Name => "Tissue";

        public void Apply(WorldGenContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.ProgressReporter?.Begin(Name, "Preparando campo biologico do tecido");

            TissueField field = new(context.WorldMap.Width, context.WorldMap.Height);
            context.TissueField = field;
            context.WorldMap.SetTissueField(field);
            context.WorldMap.RebuildTissueAnalysis();

            context.DebugStats["Tissue.GenerationMode"] = "NeutralStub";
            context.DebugStats["Tissue.ActiveTiles"] = "0";
            context.DebugStats["Tissue.Schema"] = "Presence,Vitality,Corruption,MemoryDensity,Flow";

            // Arquitetura intencional: a geracao procedural antiga baseada em noise,
            // threshold global e bool foi removida. O proximo gerador deve preencher
            // TissueCellState por regiao/bioma sem depender de distribuicao uniforme.
            context.ProgressReporter?.Report(Name, 1f, "Campo biologico neutro preparado");
            context.ProgressReporter?.Complete(Name, "Tecido pronto para expansao futura");
        }
    }
}
