using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Items
{
    public sealed class WorldItemRuntimeSystem
    {
        private const float ItemPullRangeInTiles = 1f;
        private const float ItemPullStrength = 900f;

        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required List<WorldItem> WorldItems { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required Inventory Inventory { get; init; }
        public required Dictionary<ItemId, Texture2D> ItemTextures { get; init; }

        public void Update(float dt, Func<Vector2, bool> shouldSimulatePosition)
        {
            if (shouldSimulatePosition == null)
                throw new ArgumentNullException(nameof(shouldSimulatePosition));

            for (int i = WorldItems.Count - 1; i >= 0; i--)
            {
                WorldItem worldItem = WorldItems[i];
                bool shouldSimulate = !worldItem.CanBePickedUp || shouldSimulatePosition(worldItem.Position);

                if (!shouldSimulate)
                    continue;

                PullNearbyWorldItem(worldItem, dt);
                worldItem.Update(dt, WorldMap);
                TryCollectWorldItem(i);
            }
        }

        public bool TryDropItem(ItemId itemId)
        {
            if (!ItemDefinitions.TryGet(itemId, out ItemDefinition definition) || !TryGetItemTexture(itemId, out Texture2D texture))
                return false;

            Vector2 spawnPosition = Player.Position + new Vector2(12f, -26f);
            SpawnWorldItem(definition, texture, spawnPosition, pickupDelay: 0.25f);
            return true;
        }

        public bool TryStoreItem(ItemId itemId, int quantity, bool preferInventory)
        {
            if (quantity <= 0 || !ItemDefinitions.TryGet(itemId, out ItemDefinition definition))
                return false;

            return TryStoreDefinition(definition, quantity, preferInventory);
        }

        public void SpawnBrokenBlockDrop(TileType removedTile, Vector2 tileCenter)
        {
            if (!TileItemMapper.TryGetItemId(removedTile, out ItemId itemId))
                return;

            SpawnItemDrops(itemId, 1, tileCenter);
        }

        public void SpawnItemDrops(ItemId itemId, int quantity, Vector2 position)
        {
            if (quantity <= 0)
                return;

            if (!ItemDefinitions.TryGet(itemId, out ItemDefinition definition))
                return;

            if (!TryGetItemTexture(itemId, out Texture2D texture))
                return;

            for (int i = 0; i < quantity; i++)
            {
                float horizontalDirection = Random.Shared.Next(2) == 0 ? -1f : 1f;
                float horizontalSpeed = 22f + Random.Shared.NextSingle() * 22f;
                float horizontalVelocity = horizontalDirection * horizontalSpeed;
                float verticalVelocity = -95f - Random.Shared.NextSingle() * 35f;
                Vector2 spawnPosition = position + new Vector2((i - ((quantity - 1) * 0.5f)) * 2f, 0f);

                SpawnWorldItem(
                    definition,
                    texture,
                    spawnPosition,
                    pickupDelay: 0.15f,
                    initialVelocityX: horizontalVelocity,
                    initialVelocityY: verticalVelocity);
            }
        }

        public bool TryGetItemTexture(ItemId itemId, out Texture2D texture)
        {
            return ItemTextures.TryGetValue(itemId, out texture);
        }

        private void TryCollectWorldItem(int index)
        {
            WorldItem worldItem = WorldItems[index];
            if (!worldItem.CanBePickedUp)
                return;

            if (!worldItem.WorldBounds.Intersects(Player.Hurtbox))
                return;

            if (TryStoreCollectedDefinition(worldItem.Definition, 1))
                WorldItems.RemoveAt(index);
        }

        private bool TryStoreDefinition(ItemDefinition definition, int quantity, bool preferInventory)
        {
            if (definition == null || quantity <= 0)
                return false;

            Inventory primary = preferInventory ? Inventory : Hotbar;
            Inventory secondary = preferInventory ? Hotbar : Inventory;
            int remaining = quantity;

            remaining -= primary.AddToExistingStacks(definition, remaining);
            remaining -= secondary.AddToExistingStacks(definition, remaining);
            remaining -= primary.AddToEmptySlots(definition, remaining);
            remaining -= secondary.AddToEmptySlots(definition, remaining);

            return remaining == 0;
        }

        private void SpawnWorldItem(
            ItemDefinition definition,
            Texture2D texture,
            Vector2 position,
            float pickupDelay,
            float initialVelocityX = 0f,
            float initialVelocityY = 0f)
        {
            WorldItems.Add(new WorldItem(
                definition,
                texture,
                position,
                pickupDelay,
                initialVelocityX,
                initialVelocityY));
        }

        private bool TryStoreCollectedDefinition(ItemDefinition definition, int quantity)
        {
            if (definition == null || quantity <= 0)
                return false;

            bool inventoryAlreadyHasItem = Inventory.ContainsItem(definition.Id);
            int remaining = quantity;

            remaining -= Inventory.AddToExistingStacks(definition, remaining);
            remaining -= Hotbar.AddToExistingStacks(definition, remaining);

            if (inventoryAlreadyHasItem)
            {
                remaining -= Inventory.AddToEmptySlots(definition, remaining);
                remaining -= Hotbar.AddToEmptySlots(definition, remaining);
            }
            else
            {
                remaining -= Hotbar.AddToEmptySlots(definition, remaining);
                remaining -= Inventory.AddToEmptySlots(definition, remaining);
            }

            return remaining == 0;
        }

        private void PullNearbyWorldItem(WorldItem worldItem, float dt)
        {
            if (!worldItem.CanBePickedUp)
                return;

            float maxDistance = WorldMap.TileSize * ItemPullRangeInTiles;
            if (Vector2.Distance(worldItem.Position, Player.Position) > maxDistance)
                return;

            worldItem.PullToward(Player.Position, dt, ItemPullStrength);
        }
    }
}
