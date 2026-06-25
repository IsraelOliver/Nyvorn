using Microsoft.Xna.Framework;
using Nyvorn.Source.Data.Serialization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Nyvorn.Source.Gameplay.Items
{
    public static class ItemDefinitions
    {
        private static readonly Dictionary<ItemId, ItemDefinition> definitions = LoadDefinitions();
        private static readonly IReadOnlyCollection<ItemDefinition> allDefinitions =
            new ReadOnlyCollection<ItemDefinition>(new List<ItemDefinition>(definitions.Values));
        private static readonly Dictionary<string, ItemDefinition> commandDefinitions = BuildCommandDefinitions();

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

        public static string GetCommandId(ItemId id)
        {
            return NormalizeCommandId(id.ToString());
        }

        public static bool TryResolveCommandId(string value, out ItemDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte numericId) &&
                TryGet((ItemId)numericId, out definition))
            {
                return true;
            }

            return commandDefinitions.TryGetValue(NormalizeCommandId(value), out definition);
        }

        private static Dictionary<ItemId, ItemDefinition> LoadDefinitions()
        {
            List<ItemDefinitionDto> items = JsonLoader.LoadContentData<List<ItemDefinitionDto>>("items.json");
            if (items.Count == 0)
                throw new InvalidOperationException("items.json must contain at least one item definition.");

            Dictionary<ItemId, ItemDefinition> result = new();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDefinition definition = CreateDefinition(items[i], i);
                if (result.ContainsKey(definition.Id))
                    throw new InvalidOperationException($"items.json contains duplicate item id '{definition.Id}'.");

                result[definition.Id] = definition;
            }

            return result;
        }

        private static ItemDefinition CreateDefinition(ItemDefinitionDto dto, int index)
        {
            string context = $"items.json[{index}]";
            ItemId id = ParseEnum<ItemId>(dto.Id, $"{context}.id");
            EquipmentKind equipmentKind = string.IsNullOrWhiteSpace(dto.EquipmentKind)
                ? EquipmentKind.None
                : ParseEnum<EquipmentKind>(dto.EquipmentKind, $"{context}.equipmentKind");

            ItemDefinition definition = new()
            {
                Id = id,
                Name = RequireText(dto.Name, $"{context}.name"),
                TexturePath = RequireText(dto.TexturePath, $"{context}.texturePath"),
                Stackable = dto.Stackable,
                MaxStack = RequirePositive(dto.MaxStack, $"{context}.maxStack"),
                GravityScale = RequirePositive(dto.GravityScale, $"{context}.gravityScale"),
                WorldSize = ReadPoint(dto.WorldSize, $"{context}.worldSize"),
                WorldPivot = ReadPoint(dto.WorldPivot, $"{context}.worldPivot"),
                WorldBaseAnchor = ReadOptionalPoint(dto.WorldBaseAnchor, $"{context}.worldBaseAnchor"),
                SpriteSheetCell = ReadPoint(dto.SpriteSheetCell, $"{context}.spriteSheetCell"),
                FrameSize = ReadOptionalPoint(dto.FrameSize, $"{context}.frameSize"),
                WorldCollisionRect = ReadRectangle(dto.WorldCollisionRect, $"{context}.worldCollisionRect"),
                EquipmentKind = equipmentKind,
                MiningPower = dto.MiningPower,
                MiningSpeed = dto.MiningSpeed,
                PowerTier = dto.PowerTier,
                HitDamage = dto.HitDamage,
                HitKnockbackX = dto.HitKnockbackX,
                HitKnockbackY = dto.HitKnockbackY,
                UseSpeed = dto.UseSpeed
            };

            ValidateDefinition(definition, context);
            return definition;
        }

        private static void ValidateDefinition(ItemDefinition definition, string context)
        {
            if (!definition.Stackable && definition.MaxStack != 1)
                throw new InvalidOperationException($"{context}.maxStack must be 1 for non-stackable items.");

            if (definition.WorldCollisionRect.Width <= 0 || definition.WorldCollisionRect.Height <= 0)
                throw new InvalidOperationException($"{context}.worldCollisionRect must have positive width and height.");

            if (definition.FrameSize.HasValue &&
                (definition.FrameSize.Value.X <= 0 || definition.FrameSize.Value.Y <= 0))
            {
                throw new InvalidOperationException($"{context}.frameSize must contain positive values.");
            }

            switch (definition.EquipmentKind)
            {
                case EquipmentKind.Pickaxe:
                    RequireEquipmentValue(definition.MiningPower, $"{context}.miningPower");
                    RequireEquipmentValue(definition.MiningSpeed, $"{context}.miningSpeed");
                    RequireEquipmentValue(definition.PowerTier, $"{context}.powerTier");
                    RequireEquipmentValue(definition.HitDamage, $"{context}.hitDamage");
                    RequireEquipmentValue(definition.HitKnockbackX, $"{context}.hitKnockbackX");
                    RequireEquipmentValue(definition.HitKnockbackY, $"{context}.hitKnockbackY");
                    break;

                case EquipmentKind.Axe:
                    RequireEquipmentValue(definition.PowerTier, $"{context}.powerTier");
                    RequireEquipmentValue(definition.HitDamage, $"{context}.hitDamage");
                    RequireEquipmentValue(definition.HitKnockbackX, $"{context}.hitKnockbackX");
                    RequireEquipmentValue(definition.HitKnockbackY, $"{context}.hitKnockbackY");
                    break;
            }
        }

        private static void RequireEquipmentValue<T>(T? value, string field) where T : struct
        {
            if (!value.HasValue)
                throw new InvalidOperationException($"{field} is required for this equipment kind.");
        }

        private static Dictionary<string, ItemDefinition> BuildCommandDefinitions()
        {
            Dictionary<string, ItemDefinition> result = new();
            foreach (ItemDefinition definition in definitions.Values)
            {
                result[GetCommandId(definition.Id)] = definition;
                result[NormalizeCommandId(definition.Name)] = definition;
            }
            return result;
        }

        private static string NormalizeCommandId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            StringBuilder normalized = new(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsLetterOrDigit(character))
                    normalized.Append(char.ToLowerInvariant(character));
            }
            return normalized.ToString();
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

        private static float RequirePositive(float value, string field)
        {
            if (value <= 0f)
                throw new InvalidOperationException($"{field} must be positive.");

            return value;
        }

        private static Point ReadPoint(int[] values, string field)
        {
            if (values == null || values.Length != 2)
                throw new InvalidOperationException($"{field} must be [x, y].");

            return new Point(values[0], values[1]);
        }

        private static Point? ReadOptionalPoint(int[] values, string field)
        {
            if (values == null)
                return null;

            return ReadPoint(values, field);
        }

        private static Rectangle ReadRectangle(int[] values, string field)
        {
            if (values == null || values.Length != 4)
                throw new InvalidOperationException($"{field} must be [x, y, width, height].");

            return new Rectangle(values[0], values[1], values[2], values[3]);
        }

        public sealed class ItemDefinitionDto
        {
            public string Id { get; init; }
            public string Name { get; init; }
            public string TexturePath { get; init; }
            public bool Stackable { get; init; }
            public int MaxStack { get; init; }
            public float GravityScale { get; init; } = 1f;
            public int[] WorldSize { get; init; }
            public int[] WorldPivot { get; init; }
            public int[] WorldBaseAnchor { get; init; }
            public int[] SpriteSheetCell { get; init; }
            public int[] FrameSize { get; init; }
            public int[] WorldCollisionRect { get; init; }
            public string EquipmentKind { get; init; }
            public int? MiningPower { get; init; }
            public float? MiningSpeed { get; init; }
            public int? PowerTier { get; init; }
            public int? HitDamage { get; init; }
            public float? HitKnockbackX { get; init; }
            public float? HitKnockbackY { get; init; }
            public float? UseSpeed { get; init; }
        }
    }
}
