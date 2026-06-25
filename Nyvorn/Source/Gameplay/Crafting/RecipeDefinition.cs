using Nyvorn.Source.Gameplay.Items;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class RecipeDefinition
    {
        public required string Id { get; init; }
        public required ItemId ResultItemId { get; init; }
        public required int ResultQuantity { get; init; }
        public required CraftTier RequiredTier { get; init; }
        public required string DisplayName { get; init; }
        public required string DisplayCost { get; init; }
        public required IReadOnlyList<RecipeIngredient> Ingredients { get; init; }
    }
}
