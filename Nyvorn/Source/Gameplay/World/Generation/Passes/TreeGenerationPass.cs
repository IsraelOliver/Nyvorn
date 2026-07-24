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
            try
            {
                context.WorldMap.SetTrees(generator.GenerateWithGroups(context));
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tree generation error: {ex}");
            }
            context.ProgressReporter?.Complete(Name, "Arvores modulares prontas");
        }
    }
}
