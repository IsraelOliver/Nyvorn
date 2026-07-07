namespace Nyvorn.Source.World.Generation
{
    public readonly record struct WorldSeedSet(
        string SeedText,
        ulong MasterSeed,
        int LegacyIntSeed,
        ulong TerrainSeed,
        ulong BiomeSeed,
        ulong CaveSeed,
        ulong MaterialSeed,
        ulong TissueSeed,
        ulong DecorationSeed,
        ulong HydrologySeed,
        ulong EnvironmentSeed,
        ulong LootSeed,
        ulong CorruptionSeed)
    {
        public static WorldSeedSet FromSeedText(string seedText)
        {
            string normalized = SeedHash.NormalizeSeedText(seedText);
            ulong masterSeed = SeedHash.Hash64($"{SeedHash.Domain}:master:{normalized}");
            return Create(normalized, masterSeed, SeedHash.ToIntSeed(masterSeed));
        }

        public static WorldSeedSet FromLegacyInt(int seed)
        {
            string seedText = seed.ToString();
            ulong legacySeed = unchecked((ulong)(uint)seed);
            return new WorldSeedSet(
                seedText,
                legacySeed,
                seed,
                legacySeed,
                legacySeed,
                legacySeed,
                legacySeed,
                legacySeed,
                legacySeed,
                legacySeed,
                legacySeed,
                SeedHash.Derive(legacySeed, "loot"),
                SeedHash.Derive(legacySeed, "corruption"));
        }

        public static WorldSeedSet FromMetadata(string seedText, ulong masterSeed, int legacySeed)
        {
            if (masterSeed == 0UL)
                return FromLegacyInt(legacySeed);

            string normalized = string.IsNullOrWhiteSpace(seedText)
                ? legacySeed.ToString()
                : seedText.Trim();
            int intSeed = legacySeed != 0 ? legacySeed : SeedHash.ToIntSeed(masterSeed);
            return Create(normalized, masterSeed, intSeed);
        }

        public static WorldSeedSet CreateRandom()
        {
            return FromSeedText(SeedHash.CreateRandomSeedText());
        }

        private static WorldSeedSet Create(string seedText, ulong masterSeed, int legacyIntSeed)
        {
            return new WorldSeedSet(
                seedText,
                masterSeed,
                legacyIntSeed,
                SeedHash.Derive(masterSeed, "terrain"),
                SeedHash.Derive(masterSeed, "biome"),
                SeedHash.Derive(masterSeed, "cave"),
                SeedHash.Derive(masterSeed, "material"),
                SeedHash.Derive(masterSeed, "tissue"),
                SeedHash.Derive(masterSeed, "decoration"),
                SeedHash.Derive(masterSeed, "hydrology"),
                SeedHash.Derive(masterSeed, "environment"),
                SeedHash.Derive(masterSeed, "loot"),
                SeedHash.Derive(masterSeed, "corruption"));
        }
    }
}
