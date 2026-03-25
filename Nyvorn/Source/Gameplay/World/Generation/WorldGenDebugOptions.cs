namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenDebugOptions
    {
        public bool EnableLayerBoundaryPass { get; init; } = true;
        public bool EnableSurfaceProfilePass { get; init; } = true;
        public bool EnableBaseTerrainFillPass { get; init; } = true;
        public bool EnableSandRegionPass { get; init; } = true;
        public bool EnableCaveFieldPass { get; init; } = false;
        public bool EnableCaveCarvingPass { get; init; } = false;
        public bool EnableChamberCarvingPass { get; init; } = false;
        public bool EnableCavernRegionPass { get; init; } = false;
        public bool EnableCaveConnectionPass { get; init; } = false;
        public bool EnableTerrainRefinementPass { get; init; } = false;
        public bool EnableSurfaceDecorationPass { get; init; } = true;
        public bool EnableSpawnSelectionPass { get; init; } = false;
        public bool EnableWorldBoundsPass { get; init; } = true;

        public bool IsEnabled(string passName)
        {
            return passName switch
            {
                "LayerBoundary" => EnableLayerBoundaryPass,
                "SurfaceProfile" => EnableSurfaceProfilePass,
                "BaseTerrainFill" => EnableBaseTerrainFillPass,
                "SandRegion" => EnableSandRegionPass,
                "CaveField" => EnableCaveFieldPass,
                "CaveCarving" => EnableCaveCarvingPass,
                "ChamberCarving" => EnableChamberCarvingPass,
                "CavernRegion" => EnableCavernRegionPass,
                "CaveConnection" => EnableCaveConnectionPass,
                "TerrainRefinement" => EnableTerrainRefinementPass,
                "SurfaceDecoration" => EnableSurfaceDecorationPass,
                "SpawnSelection" => EnableSpawnSelectionPass,
                "WorldBounds" => EnableWorldBoundsPass,
                _ => true
            };
        }
    }
}
