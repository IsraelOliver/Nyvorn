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
                ItemId.IronPickaxe,
                new ItemDefinition
                {
                    Id = ItemId.IronPickaxe,
                    Name = "Iron Pickaxe",
                    TexturePath = "weapons/iron-pickaxe_sheet",
                    Stackable = false,
                    MaxStack = 1,
                    GravityScale = 1.0f,
                    WorldSize = new Point(32, 32),
                    WorldPivot = new Point(9, 19),
                    SpriteSheetCell = new Point(3, 2),
                    WorldCollisionRect = new Rectangle(6, 18, 20, 8)
                }
            },
            {
                ItemId.WoodPickaxe,
                new ItemDefinition
                {
                    Id = ItemId.WoodPickaxe,
                    Name = "Wood Pickaxe",
                    TexturePath = "weapons/wood-pickaxe_sheet",
                    Stackable = false,
                    MaxStack = 1,
                    GravityScale = 1.0f,
                    WorldSize = new Point(32, 32),
                    WorldPivot = new Point(9, 19),
                    SpriteSheetCell = new Point(3, 2),
                    WorldCollisionRect = new Rectangle(6, 18, 20, 8)
                }
            },
            {
                ItemId.StonePickaxe,
                new ItemDefinition
                {
                    Id = ItemId.StonePickaxe,
                    Name = "Stone Pickaxe",
                    TexturePath = "weapons/stone-pickaxe_sheet",
                    Stackable = false,
                    MaxStack = 1,
                    GravityScale = 1.0f,
                    WorldSize = new Point(32, 32),
                    WorldPivot = new Point(9, 19),
                    SpriteSheetCell = new Point(3, 2),
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
            },
            {
                ItemId.RawWood,
                new ItemDefinition
                {
                    Id = ItemId.RawWood,
                    Name = "Raw Wood",
                    TexturePath = "blocks/raw_wood",
                    Stackable = true,
                    MaxStack = 999,
                    GravityScale = 1.0f,
                    WorldSize = new Point(16, 16),
                    WorldPivot = new Point(8, 16),
                    SpriteSheetCell = new Point(0, 0),
                    WorldCollisionRect = new Rectangle(3, 10, 10, 6)
                }
            },
            {
                ItemId.Workbench,
                new ItemDefinition
                {
                    Id = ItemId.Workbench,
                    Name = "Workbench",
                    TexturePath = "blocks/worktable-sheet",
                    Stackable = true,
                    MaxStack = 99,
                    GravityScale = 1.0f,
                    WorldSize = new Point(24, 16),
                    WorldPivot = new Point(12, 16),
                    SpriteSheetCell = new Point(0, 0),
                    WorldCollisionRect = new Rectangle(2, 10, 20, 6)
                }
            },
            {
                ItemId.WoodDoor,
                new ItemDefinition
                {
                    Id = ItemId.WoodDoor,
                    Name = "Wood Door",
                    TexturePath = "blocks/wood_door",
                    Stackable = true,
                    MaxStack = 99,
                    GravityScale = 1.0f,
                    WorldSize = new Point(8, 24),
                    WorldPivot = new Point(4, 24),
                    SpriteSheetCell = new Point(0, 0),
                    WorldCollisionRect = new Rectangle(0, 16, 8, 8)
                }
            }
        };

        private static readonly IReadOnlyCollection<ItemDefinition> allDefinitions =
            new ReadOnlyCollection<ItemDefinition>(new List<ItemDefinition>(definitions.Values));

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
            return allDefinitions;
        }
    }
}
