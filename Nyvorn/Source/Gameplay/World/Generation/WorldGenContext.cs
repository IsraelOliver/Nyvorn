namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenContext
    {
        public required WorldMap WorldMap { get; init; }
        public required WorldGenSettings Settings { get; init; }
        public required FastNoiseLite SurfaceNoise { get; init; }
        public required FastNoiseLite SurfaceDetailNoise { get; init; }
        public required FastNoiseLite SurfaceWarpNoise { get; init; }
        public required FastNoiseLite CaveNoise { get; init; }
        public required FastNoiseLite CaveRoomNoise { get; init; }
        public required FastNoiseLite BiomeNoise { get; init; }
        public required FastNoiseLite MaterialNoise { get; init; }

        public int[] SurfaceHeights { get; set; }
        public bool[] SandColumns { get; set; }
        public int[] NaturalEntrances { get; set; }
        public WorldLayerProfile LayerProfile { get; set; }
    }
}
