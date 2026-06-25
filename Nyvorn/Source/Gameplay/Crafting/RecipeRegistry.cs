using Nyvorn.Source.Data.Serialization;
using Nyvorn.Source.Gameplay.Items;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public static class RecipeRegistry
    {
        private static readonly IReadOnlyList<RecipeDefinition> recipes =
            new ReadOnlyCollection<RecipeDefinition>(LoadRecipes());

        public static IReadOnlyList<RecipeDefinition> GetAll()
        {
            return recipes;
        }

        public static List<RecipeDefinition> GetAvailable(CraftTier craftTier)
        {
            List<RecipeDefinition> result = new();
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDefinition recipe = recipes[i];
                if (recipe.RequiredTier <= craftTier)
                    result.Add(recipe);
            }

            return result;
        }

        private static List<RecipeDefinition> LoadRecipes()
        {
            List<RecipeDefinitionDto> recipeDtos = JsonLoader.LoadContentData<List<RecipeDefinitionDto>>("recipes.json");
            if (recipeDtos.Count == 0)
                throw new InvalidOperationException("recipes.json must contain at least one recipe.");

            List<RecipeDefinition> result = new();
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < recipeDtos.Count; i++)
            {
                RecipeDefinition recipe = CreateRecipe(recipeDtos[i], i);
                if (!ids.Add(recipe.Id))
                    throw new InvalidOperationException($"recipes.json contains duplicate recipe id '{recipe.Id}'.");

                result.Add(recipe);
            }

            return result;
        }

        private static RecipeDefinition CreateRecipe(RecipeDefinitionDto dto, int index)
        {
            string context = $"recipes.json[{index}]";
            string id = RequireText(dto.Id, $"{context}.id");
            ItemId resultItemId = ParseEnum<ItemId>(dto.Result, $"{context}.result");
            if (!ItemDefinitions.TryGet(resultItemId, out _))
                throw new InvalidOperationException($"{context}.result references undefined item '{resultItemId}'.");

            CraftTier requiredTier = ParseEnum<CraftTier>(dto.RequiredTier, $"{context}.requiredTier");
            List<RecipeIngredient> ingredients = ReadIngredients(dto.Ingredients, context);
            int resultQuantity = RequirePositive(dto.ResultQuantity, $"{context}.resultQuantity");

            return new RecipeDefinition
            {
                Id = id,
                ResultItemId = resultItemId,
                ResultQuantity = resultQuantity,
                RequiredTier = requiredTier,
                DisplayName = RequireText(dto.DisplayName, $"{context}.displayName"),
                DisplayCost = RequireText(dto.DisplayCost, $"{context}.displayCost"),
                Ingredients = new ReadOnlyCollection<RecipeIngredient>(ingredients)
            };
        }

        private static List<RecipeIngredient> ReadIngredients(IReadOnlyList<RecipeIngredientDto> ingredients, string context)
        {
            if (ingredients == null || ingredients.Count == 0)
                throw new InvalidOperationException($"{context}.ingredients must contain at least one ingredient.");

            List<RecipeIngredient> result = new();
            for (int i = 0; i < ingredients.Count; i++)
            {
                RecipeIngredientDto ingredient = ingredients[i];
                string field = $"{context}.ingredients[{i}]";
                ItemId itemId = ParseEnum<ItemId>(ingredient.Item, $"{field}.item");
                if (!ItemDefinitions.TryGet(itemId, out _))
                    throw new InvalidOperationException($"{field}.item references undefined item '{itemId}'.");

                result.Add(new RecipeIngredient
                {
                    ItemId = itemId,
                    Quantity = RequirePositive(ingredient.Quantity, $"{field}.quantity")
                });
            }

            return result;
        }

        private static TEnum ParseEnum<TEnum>(string value, string field) where TEnum : struct
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
            {
                throw new InvalidOperationException($"{field} has invalid value '{value}'.");
            }

            return parsed;
        }

        private static string RequireText(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{field} cannot be empty.");

            return value;
        }

        private static int RequirePositive(int value, string field)
        {
            if (value <= 0)
                throw new InvalidOperationException($"{field} must be positive.");

            return value;
        }

        public sealed class RecipeDefinitionDto
        {
            public string Id { get; init; }
            public string Result { get; init; }
            public int ResultQuantity { get; init; }
            public string RequiredTier { get; init; }
            public string DisplayName { get; init; }
            public string DisplayCost { get; init; }
            public List<RecipeIngredientDto> Ingredients { get; init; }
        }

        public sealed class RecipeIngredientDto
        {
            public string Item { get; init; }
            public int Quantity { get; init; }
        }
    }
}
