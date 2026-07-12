namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenDebugOptions
    {
        public bool EnableLayerBoundaryPass { get; init; } = true;
        public bool EnableBiomeFieldPass { get; init; } = true;
        public bool EnableSurfaceProfilePass { get; init; } = true;
        public bool EnableBaseTerrainFillPass { get; init; } = true;
        public bool EnableDirtToStoneTransitionPass { get; init; } = true;
        public bool EnableDesertCirclePass { get; init; } = true;
        public bool EnableHydrologyPass { get; init; } = true;
        public bool EnableDesertPixelSandPass { get; init; } = true;
        public bool EnableDesertCavePass { get; init; } = true;
        public bool EnableIronOreVeinPass { get; init; } = true;
        public bool EnableTreeGenerationPass { get; init; } = true;
        public bool EnableWorldBoundsPass { get; init; } = true;

        public bool IsEnabled(string passName)
        {
            return passName switch
            {
                "LayerBoundary" => EnableLayerBoundaryPass,
                "BiomeField" => EnableBiomeFieldPass,
                "SurfaceProfile" => EnableSurfaceProfilePass,
                "BaseTerrainFill" => EnableBaseTerrainFillPass,
                "DirtToStoneTransition" => EnableDirtToStoneTransitionPass,
                "DesertCircle" => EnableDesertCirclePass,
                "Hydrology" => EnableHydrologyPass,
                "DesertPixelSand" => EnableDesertPixelSandPass,
                "DesertCave" => EnableDesertCavePass,
                "IronOreVein" => EnableIronOreVeinPass,
                "TreeGeneration" => EnableTreeGenerationPass,
                "WorldBounds" => EnableWorldBoundsPass,
                _ => true
            };
        }
    }
}
