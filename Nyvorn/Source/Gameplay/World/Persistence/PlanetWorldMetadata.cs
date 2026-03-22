using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlanetWorldMetadata
    {
        public required string WorldId { get; init; }
        public required string PlanetName { get; init; }
        public required int Seed { get; init; }
        public required WorldSizePreset SizePreset { get; init; }
        public required int WorldWidth { get; init; }
        public required int WorldHeight { get; init; }
        public required int TileSize { get; init; }

        public static PlanetWorldMetadata Create(string planetName, WorldGenSettings settings, string worldId = null)
        {
            return new PlanetWorldMetadata
            {
                WorldId = string.IsNullOrWhiteSpace(worldId) ? System.Guid.NewGuid().ToString("N") : worldId,
                PlanetName = planetName,
                Seed = settings.Seed,
                SizePreset = settings.SizePreset,
                WorldWidth = settings.WorldWidth,
                WorldHeight = settings.WorldHeight,
                TileSize = settings.TileSize
            };
        }
    }
}
