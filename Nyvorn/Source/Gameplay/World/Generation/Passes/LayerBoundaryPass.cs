namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class LayerBoundaryPass : IWorldGenPass
    {
        public string Name => "LayerBoundary";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Definindo camadas do planeta");

            int worldHeight = context.WorldMap.Height;
            int lastRow = worldHeight - 1;

            int spaceEnd = ClampBoundary((int)System.MathF.Round(worldHeight * context.Config.SpaceLayerEndPercent), 0, lastRow - 4);
            int surfaceEnd = ClampBoundary((int)System.MathF.Round(worldHeight * context.Config.SurfaceLayerEndPercent), spaceEnd + 1, lastRow - 3);
            int shallowEnd = ClampBoundary((int)System.MathF.Round(worldHeight * context.Config.ShallowLayerEndPercent), surfaceEnd + 1, lastRow - 2);
            int cavernEnd = ClampBoundary((int)System.MathF.Round(worldHeight * context.Config.CavernLayerEndPercent), shallowEnd + 1, lastRow - 1);

            context.LayerDefinitions = new[]
            {
                new WorldLayerDefinition(WorldLayerType.Space, 0, spaceEnd),
                new WorldLayerDefinition(WorldLayerType.Surface, spaceEnd + 1, surfaceEnd),
                new WorldLayerDefinition(WorldLayerType.ShallowUnderground, surfaceEnd + 1, shallowEnd),
                new WorldLayerDefinition(WorldLayerType.Cavern, shallowEnd + 1, cavernEnd),
                new WorldLayerDefinition(WorldLayerType.DeepCavern, cavernEnd + 1, lastRow)
            };

            context.ProgressReporter?.Complete(Name, "Camadas do planeta definidas");
        }

        private static int ClampBoundary(int value, int min, int max)
        {
            return System.Math.Clamp(value, min, max);
        }
    }
}
