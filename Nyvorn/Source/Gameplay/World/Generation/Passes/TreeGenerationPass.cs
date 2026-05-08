using Nyvorn.Source.World.Decorations;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class TreeGenerationPass : IWorldGenPass
    {
        private readonly TreeGenerator generator = new(TreeGenerationSettings.Default);

        public string Name => "TreeGeneration";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Plantando arvores modulares");
            context.WorldMap.SetTrees(generator.Generate(context));
            context.DebugStats["Trees.Count"] = context.WorldMap.Trees.Count.ToString();
            context.ProgressReporter?.Complete(Name, "Arvores modulares prontas");
        }
    }
}
