namespace Nyvorn.Source.World
{
    public enum TileType : byte
    {
        Empty = 0,
        Dirt  = 1,
        Stone = 2,
        Sand  = 3,
        Grass = 4
    }

    public readonly struct TileMiningDefinition
    {
        public TileMiningDefinition(bool isMineable, float hardness, int requiredMiningPower)
        {
            IsMineable = isMineable;
            Hardness = hardness;
            RequiredMiningPower = requiredMiningPower;
        }

        public bool IsMineable { get; }
        public float Hardness { get; }
        public int RequiredMiningPower { get; }
    }

    public static class TileMiningDefinitions
    {
        public static TileMiningDefinition Get(TileType tileType)
        {
            return tileType switch
            {
                TileType.Dirt => new TileMiningDefinition(isMineable: true, hardness: 1f, requiredMiningPower: 0),
                TileType.Grass => new TileMiningDefinition(isMineable: true, hardness: 1f, requiredMiningPower: 0),
                TileType.Sand => new TileMiningDefinition(isMineable: true, hardness: 0.8f, requiredMiningPower: 0),
                TileType.Stone => new TileMiningDefinition(isMineable: true, hardness: 3f, requiredMiningPower: 1),
                _ => new TileMiningDefinition(isMineable: false, hardness: 0f, requiredMiningPower: 0)
            };
        }
    }
}
