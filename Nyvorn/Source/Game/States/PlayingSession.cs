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
using Nyvorn.Source.World.Tissue;
using System;
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
        public required WorldTilePreviewRenderer TilePreviewRenderer { get; init; }
        public required CombatSystem CombatSystem { get; init; }
        public required TissueNetwork TissueNetwork { get; init; }
        public required TissueMaskRenderer TissueMaskRenderer { get; init; }
        public required TissueRevealController TissueRevealController { get; init; }
        public required TissueDebugRenderer TissueDebugRenderer { get; init; }
        private const float BlockPlaceInterval = 0.08f;
        private const float ItemPullRangeInTiles = 1f;
        private const float ItemPullStrength = 900f;
        private const float TissueBackgroundOpacity = 0.34f;
        private const float TissueForegroundOpacity = 1.00f;
        public int SelectedHotbarIndex { get; private set; }
        private int lastBlockBreakAttackSequence = -1;
        private float blockPlaceCooldownTimer;
        private float tissueTime;
        private Rectangle hoveredTileBounds;
        private WorldTilePreviewState hoveredTileState = WorldTilePreviewState.Hidden;
        public Effect TissueCompositeEffect => TissueMaskRenderer.CompositeEffect;

        public void Update(float dt, InputState input, Vector2 mouseWorld)
        {
            tissueTime += dt;

            if (blockPlaceCooldownTimer > 0f)
                blockPlaceCooldownTimer -= dt;

            if (input.HotbarSelectionIndex >= 0 && input.HotbarSelectionIndex < Hotbar.Capacity)
                SelectedHotbarIndex = input.HotbarSelectionIndex;

            InputState worldInput = input;
            InventorySlot selectedSlot = Hotbar.GetSlot(SelectedHotbarIndex);
            if (!selectedSlot.IsEmpty && TileItemMapper.TryGetTileType(selectedSlot.ItemId, out _))
            {
                worldInput = new InputState(
                    input.MoveDir,
                    input.JumpPressed,
                    false,
                    input.PlacePressed,
                    input.OpenInventoryPressed,
                    input.TissueRevealPressed,
                    input.HotbarSelectionIndex,
                    input.DodgePressed,
                    input.DodgeDir,
                    input.MouseScreenPosition);
            }

            SyncEquippedWeapon();
            UpdateTilePreview(mouseWorld);
            TryPlaceSelectedBlock(worldInput, mouseWorld);
            Player.Update(dt, WorldMap, worldInput, mouseWorld);
            TissueRevealController.Update(dt, input, Player.Position);
            TryBreakTargetBlock(mouseWorld);

            for (int i = Enemies.Count - 1; i >= 0; i--)
                Enemies[i].Update(dt, WorldMap);
            for (int i = WorldItems.Count - 1; i >= 0; i--)
            {
                PullNearbyWorldItem(WorldItems[i], dt);
                WorldItems[i].Update(dt, WorldMap);
                TryCollectWorldItem(i);
            }

            CombatSystem.Resolve(Player, Enemies);
            EnemyRespawnController.Update(dt, Enemies);
        }

        public void DrawWorld(SpriteBatch spriteBatch)
        {
            WorldMap.Draw(spriteBatch);
            TilePreviewRenderer.Draw(spriteBatch, hoveredTileBounds, hoveredTileState);

            Player.Draw(spriteBatch);
            foreach (Enemy enemy in Enemies)
            {
                enemy.Draw(spriteBatch);
                HealthBarRenderer.Draw(spriteBatch, enemy.Position + new Vector2(0f, -30f), enemy.Health, enemy.MaxHealth, 22, 3);
            }

            foreach (WorldItem worldItem in WorldItems)
                worldItem.Draw(spriteBatch);
        }

        public void RenderTissueMask(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch)
        {
            TissueMaskRenderer.EnsureTarget(
                graphicsDevice,
                graphicsDevice.PresentationParameters.BackBufferWidth,
                graphicsDevice.PresentationParameters.BackBufferHeight);

            RenderTargetBinding[] previousTargets = graphicsDevice.GetRenderTargets();
            graphicsDevice.SetRenderTarget((RenderTarget2D)TissueMaskRenderer.MaskTexture);
            graphicsDevice.Clear(Color.Transparent);

            spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend, transformMatrix: Camera.GetViewMatrix());
            TissueMaskRenderer.DrawMask(
                spriteBatch,
                TissueNetwork,
                TissueRevealController.CurrentStrength,
                TissueRevealController.FocusPosition,
                TissueRevealController.RevealRadius);
            spriteBatch.End();

            graphicsDevice.SetRenderTargets(previousTargets);
        }

        public void DrawTissueBackground(SpriteBatch spriteBatch)
        {
            Vector2 focusScreenPosition = Camera.WorldToScreen(TissueRevealController.FocusPosition);
            float revealRadiusPixels = TissueRevealController.RevealRadius * Camera.Zoom;
            TissueMaskRenderer.DrawComposite(
                spriteBatch,
                TissueRevealController.CurrentStrength,
                tissueTime,
                focusScreenPosition,
                revealRadiusPixels,
                TissueRevealController.WaveProgress,
                layerMode: 0f,
                layerOpacity: TissueBackgroundOpacity);
        }

        public void DrawTissueOverlay(SpriteBatch spriteBatch)
        {
            Vector2 focusScreenPosition = Camera.WorldToScreen(TissueRevealController.FocusPosition);
            float revealRadiusPixels = TissueRevealController.RevealRadius * Camera.Zoom;
            TissueMaskRenderer.DrawComposite(
                spriteBatch,
                TissueRevealController.CurrentStrength,
                tissueTime,
                focusScreenPosition,
                revealRadiusPixels,
                TissueRevealController.WaveProgress,
                layerMode: 1f,
                layerOpacity: TissueForegroundOpacity);
        }


        public void DrawTissueDebug(SpriteBatch spriteBatch)
        {
            TissueDebugRenderer.Draw(
                spriteBatch,
                TissueNetwork,
                TissueRevealController.CurrentStrength,
                TissueRevealController.FocusPosition,
                TissueRevealController.RevealRadius);
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

            return TryStoreDefinition(definition, quantity, preferInventory);
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

            if (TryStoreDefinition(worldItem.Definition, 1, preferInventory: true))
                WorldItems.RemoveAt(index);
        }

        private void TryBreakTargetBlock(Vector2 mouseWorld)
        {
            if (!Player.HasActiveAttackHitbox)
                return;

            if (Player.AttackSequence == lastBlockBreakAttackSequence)
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            TileType targetTile = WorldMap.GetTile(tile.X, tile.Y);
            if (!Player.CanBreakTile(targetTile))
                return;

            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            if (Vector2.Distance(Player.Position, tileCenter) > Player.WorldInteractionRange)
                return;

            if (!WorldMap.TryBreakTile(tile.X, tile.Y, out TileType removedTile))
                return;

            SpawnBrokenBlockDrop(removedTile, tileCenter);
            lastBlockBreakAttackSequence = Player.AttackSequence;
        }

        private void SpawnBrokenBlockDrop(TileType removedTile, Vector2 tileCenter)
        {
            if (!TileItemMapper.TryGetItemId(removedTile, out ItemId itemId))
                return;

            if (!ItemDefinitions.TryGet(itemId, out ItemDefinition definition))
                return;

            if (!TryGetItemTexture(itemId, out Texture2D texture))
                return;

            float horizontalDirection = Random.Shared.Next(2) == 0 ? -1f : 1f;
            float horizontalVelocity = horizontalDirection * 28f;
            const float verticalVelocity = -105f;
            WorldItems.Add(new WorldItem(
                definition,
                texture,
                tileCenter,
                pickupDelay: 0.15f,
                initialVelocityX: horizontalVelocity,
                initialVelocityY: verticalVelocity));
        }

        private void TryPlaceSelectedBlock(InputState input, Vector2 mouseWorld)
        {
            if (!input.PlacePressed)
            {
                blockPlaceCooldownTimer = 0f;
                return;
            }

            if (blockPlaceCooldownTimer > 0f)
                return;

            InventorySlot selectedSlot = Hotbar.GetSlot(SelectedHotbarIndex);
            if (selectedSlot.IsEmpty)
                return;

            if (!TileItemMapper.TryGetTileType(selectedSlot.ItemId, out TileType tileType))
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            Rectangle tileBounds = WorldMap.GetTileBounds(tile.X, tile.Y);
            if (tileBounds.Intersects(Player.Hurtbox))
                return;

            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            if (Vector2.Distance(Player.Position, tileCenter) > Player.WorldInteractionRange)
                return;

            if (!WorldMap.TryPlaceTile(tile.X, tile.Y, tileType))
                return;

            selectedSlot.RemoveOne();
            blockPlaceCooldownTimer = BlockPlaceInterval;
        }

        private void UpdateTilePreview(Vector2 mouseWorld)
        {
            hoveredTileState = WorldTilePreviewState.Hidden;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return;

            hoveredTileBounds = WorldMap.GetTileBounds(tile.X, tile.Y);
            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            bool inRange = Vector2.Distance(Player.Position, tileCenter) <= Player.WorldInteractionRange;

            InventorySlot selectedSlot = Hotbar.GetSlot(SelectedHotbarIndex);
            if (!selectedSlot.IsEmpty && TileItemMapper.TryGetTileType(selectedSlot.ItemId, out TileType placeTileType))
            {
                bool canPlace = inRange &&
                                placeTileType != TileType.Empty &&
                                WorldMap.CanPlaceTile(tile.X, tile.Y, placeTileType) &&
                                !hoveredTileBounds.Intersects(Player.Hurtbox);

                hoveredTileState = canPlace ? WorldTilePreviewState.PlaceValid : WorldTilePreviewState.PlaceInvalid;
                return;
            }

            TileType targetTile = WorldMap.GetTile(tile.X, tile.Y);
            if (!WorldMap.IsSolid(targetTile))
                return;

            hoveredTileState = inRange && Player.CanBreakTile(targetTile)
                ? WorldTilePreviewState.BreakValid
                : WorldTilePreviewState.BreakInvalid;
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
