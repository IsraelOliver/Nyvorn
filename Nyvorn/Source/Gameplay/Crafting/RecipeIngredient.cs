using Nyvorn.Source.Gameplay.Items;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class RecipeIngredient
    {
        public required ItemId ItemId { get; init; }
        public required int Quantity { get; init; }
    }
}
