using Nyvorn.Source.Gameplay.Items;

namespace Nyvorn.Source.World
{
    public static class TileItemMapper
    {
        public static bool TryGetItemId(TileType tileType, out ItemId itemId)
        {
            itemId = tileType switch
            {
                TileType.Dirt => ItemId.DirtBlock,
                TileType.Grass => ItemId.DirtBlock,
                TileType.Stone => ItemId.StoneBlock,
                TileType.Sand => ItemId.SandBlock,
                TileType.HardenedSand => ItemId.SandBlock,
                TileType.Wood => ItemId.RawWood,
                _ => ItemId.None
            };

            return itemId != ItemId.None;
        }

        public static bool TryGetTileType(ItemId itemId, out TileType tileType)
        {
            tileType = itemId switch
            {
                ItemId.DirtBlock => TileType.Dirt,
                ItemId.StoneBlock => TileType.Stone,
                ItemId.SandBlock => TileType.Sand,
                ItemId.RawWood => TileType.Wood,
                _ => TileType.Empty
            };

            return tileType != TileType.Empty;
        }
    }
}
