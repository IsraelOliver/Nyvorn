using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class PlanetWorldMetadata
    {
        public required string WorldId { get; init; }
        public required string PlanetName { get; init; }
        public required int Seed { get; init; }
        public string SeedText { get; init; }
        public ulong MasterSeed { get; init; }
        public int WorldgenVersion { get; init; }
        public required WorldSizePreset SizePreset { get; init; }
        public required int WorldWidth { get; init; }
        public required int WorldHeight { get; init; }
        public required int TileSize { get; init; }

        public static PlanetWorldMetadata Create(string planetName, WorldGenConfig config, string worldId = null)
        {
            WorldSeedSet seedSet = config.SeedSet;
            return new PlanetWorldMetadata
            {
                WorldId = string.IsNullOrWhiteSpace(worldId) ? System.Guid.NewGuid().ToString("N") : worldId,
                PlanetName = planetName,
                Seed = seedSet.LegacyIntSeed,
                SeedText = seedSet.SeedText,
                MasterSeed = seedSet.MasterSeed,
                WorldgenVersion = WorldGenConfig.WorldgenVersion,
                SizePreset = config.SizePreset,
                WorldWidth = config.WorldWidth,
                WorldHeight = config.WorldHeight,
                TileSize = config.TileSize
            };
        }

        public WorldSeedSet CreateSeedSet()
        {
            return WorldSeedSet.FromMetadata(SeedText, MasterSeed, Seed);
        }

        public PlanetWorldMetadata WithSeedMetadataDefaults()
        {
            bool hasSeedText = !string.IsNullOrWhiteSpace(SeedText);
            bool hasMasterSeed = MasterSeed != 0UL;
            bool hasWorldgenVersion = WorldgenVersion > 0;
            if (hasSeedText && hasMasterSeed && hasWorldgenVersion)
                return this;

            WorldSeedSet seedSet = CreateSeedSet();
            return new PlanetWorldMetadata
            {
                WorldId = WorldId,
                PlanetName = PlanetName,
                Seed = seedSet.LegacyIntSeed,
                SeedText = seedSet.SeedText,
                MasterSeed = seedSet.MasterSeed,
                WorldgenVersion = hasWorldgenVersion ? WorldgenVersion : WorldGenConfig.WorldgenVersion,
                SizePreset = SizePreset,
                WorldWidth = WorldWidth,
                WorldHeight = WorldHeight,
                TileSize = TileSize
            };
        }
    }
}
