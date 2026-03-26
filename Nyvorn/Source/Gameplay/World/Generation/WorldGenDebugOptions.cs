namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenDebugOptions
    {
        public bool EnableLayerBoundaryPass { get; init; } = true;
        public bool EnableSurfaceProfilePass { get; init; } = true;
        public bool EnableBaseTerrainFillPass { get; init; } = true;
        public bool EnableSandRegionPass { get; init; } = true;
        public bool EnableCaveMaskPass { get; init; } = true;
        public bool EnableSurfaceDecorationPass { get; init; } = true;
        public bool EnableWorldBoundsPass { get; init; } = true;

        public bool IsEnabled(string passName)
        {
            return passName switch
            {
                "LayerBoundary" => EnableLayerBoundaryPass,
                "SurfaceProfile" => EnableSurfaceProfilePass,
                "BaseTerrainFill" => EnableBaseTerrainFillPass,
                "SandRegion" => EnableSandRegionPass,
                "CaveMask" => EnableCaveMaskPass,
                "SurfaceDecoration" => EnableSurfaceDecorationPass,
                "WorldBounds" => EnableWorldBoundsPass,
                _ => true
            };
        }
    }
}
