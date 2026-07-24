using Nyvorn.Source.World.Decorations;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class SurfaceDecorationPass : IWorldGenPass
    {
        private readonly SurfaceDecorationGenerator generator = new();

        public string Name => "SurfaceDecoration";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Espalhando cogumelos");
            try
            {
                context.WorldMap.SetSurfaceDecorations(generator.Generate(context));
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Surface decoration generation error: {ex}");
            }
            context.ProgressReporter?.Complete(Name, "Cogumelos espalhados");
        }
    }
}
