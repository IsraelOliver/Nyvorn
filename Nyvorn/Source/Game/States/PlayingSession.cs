using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Gameplay.Combat;
using Nyvorn.Source.Gameplay.Combat.Weapons;
using Nyvorn.Source.Gameplay.Entities.Enemies;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.World;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSession
    {
        public required WorldMap WorldMap { get; init; }
        public required Player Player { get; init; }
        public required List<Enemy> Enemies { get; init; }
        public required List<WorldItem> WorldItems { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required Inventory Inventory { get; init; }
        public required Dictionary<ItemId, Texture2D> ItemTextures { get; init; }
        public required Dictionary<ItemId, Weapon> Weapons { get; init; }
        public required EnemyRespawnController EnemyRespawnController { get; init; }
        public required Camera2D Camera { get; init; }
        public required WorldHealthBarRenderer HealthBarRenderer { get; init; }
        public required HudRenderer HudRenderer { get; init; }
        public required CombatSystem CombatSystem { get; init; }
        public int SelectedHotbarIndex { get; private set; }

        public void Update(float dt, InputState input, Vector2 mouseWorld)
        {
            if (input.HotbarSelectionIndex >= 0 && input.HotbarSelectionIndex < Hotbar.Capacity)
                SelectedHotbarIndex = input.HotbarSelectionIndex;

            SyncEquippedWeapon();
            Player.Update(dt, WorldMap, input, mouseWorld);

            for (int i = Enemies.Count - 1; i >= 0; i--)
                Enemies[i].Update(dt, WorldMap);
            for (int i = WorldItems.Count - 1; i >= 0; i--)
            {
                WorldItems[i].Update(dt, WorldMap);
                TryCollectWorldItem(i);
            }

            CombatSystem.Resolve(Player, Enemies);
            EnemyRespawnController.Update(dt, Enemies);
        }

        public void DrawWorld(SpriteBatch spriteBatch)
        {
            WorldMap.Draw(spriteBatch);

            Player.Draw(spriteBatch);
            foreach (Enemy enemy in Enemies)
            {
                enemy.Draw(spriteBatch);
                HealthBarRenderer.Draw(spriteBatch, enemy.Position + new Vector2(0f, -30f), enemy.Health, enemy.MaxHealth, 22, 3);
            }

            foreach (WorldItem worldItem in WorldItems)
                worldItem.Draw(spriteBatch);
        }

        public void DrawHud(SpriteBatch spriteBatch, int screenWidth)
        {
            HudRenderer.Draw(spriteBatch, Hotbar, SelectedHotbarIndex, Player.Health, Player.MaxHealth, screenWidth);
        }

        public void DrawInventory(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            HudRenderer.DrawInventoryPanel(spriteBatch, Hotbar, Inventory, SelectedHotbarIndex, screenWidth, screenHeight);
        }

        public Rectangle GetInventoryPanelBounds(int screenWidth, int screenHeight)
        {
            return HudRenderer.GetInventoryPanelBounds(screenWidth, screenHeight);
        }

        public bool TryGetItemTexture(ItemId itemId, out Texture2D texture)
        {
            return ItemTextures.TryGetValue(itemId, out texture);
        }

        public bool TryDropItem(ItemId itemId)
        {
            if (!ItemDefinitions.TryGet(itemId, out ItemDefinition definition) || !TryGetItemTexture(itemId, out Texture2D texture))
                return false;

            Vector2 spawnPosition = Player.Position + new Vector2(12f, -26f);
            WorldItems.Add(new WorldItem(definition, texture, spawnPosition, pickupDelay: 0.25f));
            return true;
        }

        public bool TryStoreItem(ItemId itemId, int quantity, bool preferInventory)
        {
            if (quantity <= 0 || !ItemDefinitions.TryGet(itemId, out ItemDefinition definition))
                return false;

            if (preferInventory)
                return Inventory.TryAdd(definition, quantity) || Hotbar.TryAdd(definition, quantity);

            return Hotbar.TryAdd(definition, quantity) || Inventory.TryAdd(definition, quantity);
        }

        private void SyncEquippedWeapon()
        {
            InventorySlot selectedSlot = Hotbar.GetSlot(SelectedHotbarIndex);
            if (selectedSlot.IsEmpty || !Weapons.TryGetValue(selectedSlot.ItemId, out Weapon weapon))
                weapon = Weapons[ItemId.None];

            Player.SetEquippedWeapon(weapon);
        }

        private void TryCollectWorldItem(int index)
        {
            WorldItem worldItem = WorldItems[index];
            if (!worldItem.CanBePickedUp)
                return;

            if (!worldItem.WorldBounds.Intersects(Player.Hurtbox))
                return;

            if (Hotbar.TryAdd(worldItem.Definition) || Inventory.TryAdd(worldItem.Definition))
                WorldItems.RemoveAt(index);
        }
    }
}
