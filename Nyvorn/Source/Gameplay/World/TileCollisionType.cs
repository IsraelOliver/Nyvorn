namespace Nyvorn.Source.World
{
    public enum TileCollisionType
    {
        Empty = 0,      // Sem colisão
        Solid = 1,      // Bloco sólido completo (8px de altura)
        Platform = 2    // One-way platform (4px de altura no topo)
    }

    public static class TileCollisionTypeExtensions
    {
        public static TileCollisionType GetCollisionType(TileType tileType)
        {
            return tileType switch
            {
                TileType.Empty => TileCollisionType.Empty,
                TileType.Dirt => TileCollisionType.Solid,
                TileType.Grass => TileCollisionType.Solid,
                TileType.Stone => TileCollisionType.Solid,
                TileType.Sand => TileCollisionType.Solid,
                TileType.Wood => TileCollisionType.Solid,
                TileType.IronOre => TileCollisionType.Solid,
                TileType.Platform => TileCollisionType.Platform,
                _ => TileCollisionType.Empty
            };
        }
    }
}
