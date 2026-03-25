using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nyvorn.Source.Gameplay.Items
{
    public static class ItemDefinitions
    {
        private static readonly Dictionary<ItemId, ItemDefinition> definitions = new()
        {
            {
                ItemId.ShortStick,
                new ItemDefinition
                {
                    Id = ItemId.ShortStick,
                    Name = "Short Stick",
                    TexturePath = "weapons/shortStick",
                    Stackable = false,
                    MaxStack = 1,
                    GravityScale = 1.0f,
                    WorldSize = new Point(32, 32),
                    WorldPivot = new Point(10, 20),
                    SpriteSheetCell = new Point(0, 1),
                    WorldCollisionRect = new Rectangle(6, 18, 20, 8)
                }
            },
            {
                ItemId.Pickaxe,
                new ItemDefinition
                {
                    Id = ItemId.Pickaxe,
                    Name = "Pickaxe",
                    TexturePath = "weapons/Pickaxe-Sheet",
                    Stackable = false,
                    MaxStack = 1,
                    GravityScale = 1.0f,
                    WorldSize = new Point(32, 32),
                    WorldPivot = new Point(9, 19),
                    SpriteSheetCell = new Point(0, 1),
                    WorldCollisionRect = new Rectangle(6, 18, 20, 8)
                }
            },
            {
                ItemId.DirtBlock,
                new ItemDefinition
                {
                    Id = ItemId.DirtBlock,
                    Name = "Dirt Block",
                    TexturePath = "blocks/dirt_spritesheet",
                    Stackable = true,
                    MaxStack = 999,
                    GravityScale = 1.0f,
                    WorldSize = new Point(8, 8),
                    WorldPivot = new Point(4, 8),
                    SpriteSheetCell = new Point(0, 0),
                    WorldCollisionRect = new Rectangle(0, 0, 8, 8)
                }
            },
            {
                ItemId.StoneBlock,
                new ItemDefinition
                {
                    Id = ItemId.StoneBlock,
                    Name = "Stone Block",
                    TexturePath = "blocks/stone_spritesheet",
                    Stackable = true,
                    MaxStack = 999,
                    GravityScale = 1.0f,
                    WorldSize = new Point(8, 8),
                    WorldPivot = new Point(4, 8),
                    SpriteSheetCell = new Point(0, 0),
                    WorldCollisionRect = new Rectangle(0, 0, 8, 8)
                }
            },
            {
                ItemId.SandBlock,
                new ItemDefinition
                {
                    Id = ItemId.SandBlock,
                    Name = "Sand Block",
                    TexturePath = "blocks/sand_spritesheet",
                    Stackable = true,
                    MaxStack = 999,
                    GravityScale = 1.0f,
                    WorldSize = new Point(8, 8),
                    WorldPivot = new Point(4, 8),
                    SpriteSheetCell = new Point(0, 0),
                    WorldCollisionRect = new Rectangle(0, 0, 8, 8)
                }
            }
        };

        public static ItemDefinition Get(ItemId id)
        {
            return definitions[id];
        }

        public static bool TryGet(ItemId id, out ItemDefinition definition)
        {
            return definitions.TryGetValue(id, out definition);
        }

        public static IReadOnlyCollection<ItemDefinition> GetAll()
        {
            return new ReadOnlyCollection<ItemDefinition>(new List<ItemDefinition>(definitions.Values));
        }
    }
}
