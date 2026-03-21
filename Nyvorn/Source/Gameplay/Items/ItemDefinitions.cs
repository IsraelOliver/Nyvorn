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
                    SpriteSheetCell = new Point(0, 1)
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
