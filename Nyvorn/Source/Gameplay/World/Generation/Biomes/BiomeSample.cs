namespace Nyvorn.Source.World.Generation.Biomes
{
    public readonly record struct BiomeSample(
        BiomeType Primary,
        BiomeType Secondary,
        float Blend)
    {
        public BiomeDefinition PrimaryDefinition => BiomeDefinition.Get(Primary);
        public BiomeDefinition SecondaryDefinition => BiomeDefinition.Get(Secondary);
    }
}
