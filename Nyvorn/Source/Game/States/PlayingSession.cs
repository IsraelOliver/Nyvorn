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
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;
using Nyvorn.Source.World.Tissue;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSession
    {
        public required PlanetWorldMetadata PlanetMetadata { get; init; }
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
        public required WorldMinimapRenderer WorldMinimapRenderer { get; init; }
        public required ElyraSkyRenderer ElyraSkyRenderer { get; init; }
        public required WorldTilePreviewRenderer TilePreviewRenderer { get; init; }
        public required CombatSystem CombatSystem { get; init; }
        public required TissueNetwork TissueNetwork { get; init; }
        public required TissueMaskRenderer TissueMaskRenderer { get; init; }
        public required TissueRevealController TissueRevealController { get; init; }
        public required TissueFieldDebugRenderer TissueDebugRenderer { get; init; }
        private const float BlockPlaceInterval = 0.08f;
        private const float ItemPullRangeInTiles = 1f;
        private const float ItemPullStrength = 900f;
        private const float EntitySimulationRangeInTiles = 56f;
        private const float EntityDrawPaddingPixels = 48f;
        private const float TissueBackgroundOpacity = 0.34f;
        private const float TissueForegroundOpacity = 1.00f;
        private const float TissueMaskCullPaddingPixels = 96f;
        private const float GrassSpreadInterval = 0.24f;
        private const float AmbientTissueRadiusInTiles = 7f;
        private const float AmbientLinkPresence = 0.045f;
        private const float AmbientHubPresence = 0.085f;
        public int SelectedHotbarIndex { get; private set; }
        private int lastBlockBreakAttackSequence = -1;
        private Point lastBrokenBlockTile = new Point(int.MinValue, int.MinValue);
        private float blockPlaceCooldownTimer;
        private float grassSpreadTimer;
        private float tissueTime;
        private Rectangle hoveredTileBounds;
        private WorldTilePreviewState hoveredTileState = WorldTilePreviewState.Hidden;
        public Effect TissueCompositeEffect => TissueMaskRenderer.CompositeEffect;

        public void Update(float dt, InputState input, Vector2 mouseWorld)
        {
            tissueTime += dt;

            if (blockPlaceCooldownTimer > 0f)
                blockPlaceCooldownTimer -= dt;

            grassSpreadTimer -= dt;
            if (grassSpreadTimer <= 0f)
            {
                WorldMap.UpdateGrassSpread();
                grassSpreadTimer = GrassSpreadInterval;
            }

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
                    false,
                    input.PlacePressed,
                    input.OpenInventoryPressed,
                    input.TissueRevealPressed,
                    input.ToggleMinimapPressed,
                    input.HotbarSelectionIndex,
                    input.DodgePressed,
                    input.DodgeDir,
                    input.MouseScreenPosition,
                    input.MouseWheelDelta);
            }

            SyncEquippedWeapon();
            UpdateTilePreview(mouseWorld);
            TryPlaceSelectedBlock(worldInput, mouseWorld);
            Player.Update(dt, WorldMap, worldInput, mouseWorld);
            mouseWorld = NormalizeLoopingWorld(mouseWorld);
            TissueRevealController.Update(dt, input, Player.Position);
            TryBreakTargetBlock(mouseWorld);

            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                if (IsWithinSimulationRange(Enemies[i].Position))
                    Enemies[i].Update(dt, WorldMap);
            }

            for (int i = WorldItems.Count - 1; i >= 0; i--)
            {
                WorldItem worldItem = WorldItems[i];
                bool shouldSimulate = !worldItem.CanBePickedUp || IsWithinSimulationRange(worldItem.Position);

                if (shouldSimulate)
                {
                    PullNearbyWorldItem(worldItem, dt);
                    worldItem.Update(dt, WorldMap);
                    TryCollectWorldItem(i);
                }
            }

            CombatSystem.Resolve(Player, Enemies);
            EnemyRespawnController.Update(dt, Enemies);
        }

        public void DrawTerrain(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            const int tilePadding = 2;
            float viewWidth = screenWidth / Camera.Zoom;
            float viewHeight = screenHeight / Camera.Zoom;
            float localLeft = Camera.Position.X - worldOffsetX;
            float localTop = Camera.Position.Y;
            float localRight = localLeft + viewWidth;
            float localBottom = localTop + viewHeight;

            int startTileX = (int)MathF.Floor(localLeft / WorldMap.TileSize) - tilePadding;
            int endTileX = (int)MathF.Ceiling(localRight / WorldMap.TileSize) + tilePadding;
            int startTileY = (int)MathF.Floor(localTop / WorldMap.TileSize) - tilePadding;
            int endTileY = (int)MathF.Ceiling(localBottom / WorldMap.TileSize) + tilePadding;

            WorldMap.Draw(spriteBatch, startTileX, endTileX, startTileY, endTileY);
            TilePreviewRenderer.Draw(spriteBatch, hoveredTileBounds, hoveredTileState);
        }

        public void DrawEntities(SpriteBatch spriteBatch)
        {
            Player.Draw(spriteBatch);
        }

        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            float viewWidth = screenWidth / Camera.Zoom;
            float viewHeight = screenHeight / Camera.Zoom;
            float localLeft = Camera.Position.X - worldOffsetX - EntityDrawPaddingPixels;
            float localTop = Camera.Position.Y - EntityDrawPaddingPixels;
            float localRight = localLeft + viewWidth + (EntityDrawPaddingPixels * 2f);
            float localBottom = localTop + viewHeight + (EntityDrawPaddingPixels * 2f);

            foreach (Enemy enemy in Enemies)
            {
                if (!IntersectsVisibleArea(enemy.Hurtbox, localLeft, localTop, localRight, localBottom))
                    continue;

                enemy.Draw(spriteBatch);
                HealthBarRenderer.Draw(spriteBatch, enemy.Position + new Vector2(0f, -30f), enemy.Health, enemy.MaxHealth, 22, 3);
            }

            foreach (WorldItem worldItem in WorldItems)
            {
                if (!IntersectsVisibleArea(worldItem.WorldBounds, localLeft, localTop, localRight, localBottom))
                    continue;

                worldItem.Draw(spriteBatch);
            }
        }

        public void RenderTissueMask(GraphicsDevice graphicsDevice, SpriteBatch spriteBatch)
        {
            (float revealStrength, float revealRadius, float waveProgress) = GetEffectiveTissueVisualState();
            if (revealStrength <= 0.001f)
                return;

            TissueMaskRenderer.EnsureTarget(
                graphicsDevice,
                graphicsDevice.PresentationParameters.BackBufferWidth,
                graphicsDevice.PresentationParameters.BackBufferHeight);

            RenderTargetBinding[] previousTargets = graphicsDevice.GetRenderTargets();
            graphicsDevice.SetRenderTarget((RenderTarget2D)TissueMaskRenderer.MaskTexture);
            graphicsDevice.Clear(Color.Transparent);

            float worldWidthPixels = WorldMap.PixelWidth;
            int centerLoop = (int)MathF.Floor(TissueRevealController.FocusPosition.X / worldWidthPixels);
            int minLoopOffset = -1;
            int maxLoopOffset = 1;
            float leftThreshold = TissueRevealController.RevealRadius + TissueMaskCullPaddingPixels;
            float rightThreshold = worldWidthPixels - leftThreshold;
            float wrappedFocusX = TissueRevealController.FocusPosition.X;

            while (wrappedFocusX < 0f)
                wrappedFocusX += worldWidthPixels;
            while (wrappedFocusX >= worldWidthPixels)
                wrappedFocusX -= worldWidthPixels;

            if (wrappedFocusX > leftThreshold && wrappedFocusX < rightThreshold)
            {
                minLoopOffset = 0;
                maxLoopOffset = 0;
            }

            for (int loopOffset = minLoopOffset; loopOffset <= maxLoopOffset; loopOffset++)
            {
                float worldOffset = (centerLoop + loopOffset) * worldWidthPixels;
                Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * Camera.GetViewMatrix();
                Rectangle cullBounds = GetTissueWorldCullBounds(worldOffset);

                spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
                if (WorldMap.TissueField != null)
                {
                    TissueMaskRenderer.DrawMask(
                        spriteBatch,
                        WorldMap,
                        WorldMap.TissueField,
                        revealStrength,
                        TissueRevealController.FocusPosition,
                        revealRadius,
                        cullBounds);
                }
                else
                {
                    TissueMaskRenderer.DrawMask(
                        spriteBatch,
                        TissueNetwork,
                        revealStrength,
                        TissueRevealController.FocusPosition,
                        revealRadius,
                        cullBounds);
                }
                spriteBatch.End();
            }

            graphicsDevice.SetRenderTargets(previousTargets);
        }

        public void DrawTissueBackground(SpriteBatch spriteBatch)
        {
            (float revealStrength, float revealRadius, float waveProgress) = GetEffectiveTissueVisualState();
            if (revealStrength <= 0.001f)
                return;

            Vector2 focusScreenPosition = Camera.WorldToScreen(TissueRevealController.FocusPosition);
            float revealRadiusPixels = revealRadius * Camera.Zoom;
            TissueMaskRenderer.DrawComposite(
                spriteBatch,
                revealStrength,
                tissueTime,
                focusScreenPosition,
                revealRadiusPixels,
                waveProgress,
                layerMode: 0f,
                layerOpacity: TissueBackgroundOpacity);
        }

        public void DrawTissueOverlay(SpriteBatch spriteBatch)
        {
            (float revealStrength, float revealRadius, float waveProgress) = GetEffectiveTissueVisualState();
            if (revealStrength <= 0.001f)
                return;

            Vector2 focusScreenPosition = Camera.WorldToScreen(TissueRevealController.FocusPosition);
            float revealRadiusPixels = revealRadius * Camera.Zoom;
            TissueMaskRenderer.DrawComposite(
                spriteBatch,
                revealStrength,
                tissueTime,
                focusScreenPosition,
                revealRadiusPixels,
                waveProgress,
                layerMode: 1f,
                layerOpacity: TissueForegroundOpacity);
        }

        public void DrawSky(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ElyraSkyRenderer.Draw(spriteBatch, screenWidth, screenHeight);
        }


        public void DrawTissueDebug(SpriteBatch spriteBatch)
        {
        }

        public void DrawHud(SpriteBatch spriteBatch, int screenWidth)
        {
            HudRenderer.Draw(spriteBatch, Hotbar, SelectedHotbarIndex, Player.Health, Player.MaxHealth, screenWidth);
        }

        public void DrawMinimap(SpriteBatch spriteBatch, int screenWidth, int screenHeight, bool tissueMode)
        {
            WorldMinimapRenderer.Draw(spriteBatch, WorldMap, TissueNetwork, Camera, Player.Position, screenWidth, screenHeight, tissueMode);
        }

        public WorldMinimapInteractionResult UpdateMinimapInteraction(InputState input, int screenWidth, int screenHeight, bool tissueMode)
        {
            return WorldMinimapRenderer.HandleInput(
                WorldMap,
                Player.Position,
                screenWidth,
                screenHeight,
                input.MouseScreenPosition,
                input.MouseWheelDelta,
                input.AttackPressed,
                input.AttackJustPressed,
                tissueMode);
        }

        public void DrawInventory(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            HudRenderer.DrawInventoryPanel(spriteBatch, Hotbar, Inventory, SelectedHotbarIndex, screenWidth, screenHeight);
        }

        private (float Strength, float Radius, float WaveProgress) GetEffectiveTissueVisualState()
        {
            if (TissueRevealController.CurrentStrength > 0.001f)
            {
                return (
                    TissueRevealController.CurrentStrength,
                    TissueRevealController.RevealRadius,
                    TissueRevealController.WaveProgress);
            }

            float ambientPresence = GetAmbientTissuePresence();
            if (ambientPresence <= 0.001f)
                return (0f, TissueRevealController.RevealRadius, 1f);

            float ambientRadius = AmbientTissueRadiusInTiles * WorldMap.TileSize;
            return (ambientPresence, ambientRadius, 1f);
        }

        private float GetAmbientTissuePresence()
        {
            TissueAnalysisResult analysis = WorldMap.GetOrCreateTissueAnalysis();
            TissueField tissueField = WorldMap.TissueField;
            if (analysis == null || tissueField == null)
                return 0f;

            Point centerTile = WorldMap.WorldToTile(Player.Position);
            int radiusTiles = System.Math.Max(2, (int)System.MathF.Round(AmbientTissueRadiusInTiles));
            float bestLinkSignal = 0f;
            float bestHubSignal = 0f;

            for (int y = centerTile.Y - radiusTiles; y <= centerTile.Y + radiusTiles; y++)
            {
                if (y < 0 || y >= WorldMap.Height)
                    continue;

                for (int x = centerTile.X - radiusTiles; x <= centerTile.X + radiusTiles; x++)
                {
                    if (!tissueField.HasTissue(x, y))
                        continue;

                    Vector2 tileCenter = WorldMap.GetTileCenter(x, y);
                    float distance = Vector2.Distance(tileCenter, Player.Position);
                    float normalized = 1f - MathHelper.Clamp(distance / (radiusTiles * WorldMap.TileSize), 0f, 1f);
                    bestLinkSignal = System.MathF.Max(bestLinkSignal, normalized);
                }
            }

            for (int i = 0; i < analysis.Hubs.Count; i++)
            {
                TissueHub hub = analysis.Hubs[i];
                float distance = Vector2.Distance(hub.WorldPosition, Player.Position);
                float normalized = 1f - MathHelper.Clamp(distance / (radiusTiles * WorldMap.TileSize), 0f, 1f);
                if (normalized <= 0f)
                    continue;

                float hubWeight = hub.IsIsolated ? 0.55f : hub.IsTerminal ? 0.78f : 1f;
                bestHubSignal = System.MathF.Max(bestHubSignal, normalized * hubWeight);
            }

            float linkPresence = bestLinkSignal * AmbientLinkPresence;
            float hubPresence = bestHubSignal * AmbientHubPresence;
            return System.MathF.Max(linkPresence, hubPresence);
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

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (Player.AttackSequence == lastBlockBreakAttackSequence && tile == lastBrokenBlockTile)
                return;

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
            lastBrokenBlockTile = tile;
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

        private bool IsWithinSimulationRange(Vector2 worldPosition)
        {
            float maxDistance = WorldMap.TileSize * EntitySimulationRangeInTiles;
            return GetLoopAwareDistance(worldPosition, Player.Position) <= maxDistance;
        }

        private float GetLoopAwareDistance(Vector2 a, Vector2 b)
        {
            float worldWidth = WorldMap.PixelWidth;
            float deltaX = a.X - b.X;

            if (deltaX > worldWidth * 0.5f)
                deltaX -= worldWidth;
            else if (deltaX < -worldWidth * 0.5f)
                deltaX += worldWidth;

            float deltaY = a.Y - b.Y;
            return MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        }

        private static bool IntersectsVisibleArea(Rectangle bounds, float left, float top, float right, float bottom)
        {
            return bounds.Right >= left &&
                   bounds.Left <= right &&
                   bounds.Bottom >= top &&
                   bounds.Top <= bottom;
        }

        private Rectangle GetTissueWorldCullBounds(float worldOffsetX)
        {
            int screenW = TissueMaskRenderer.MaskTexture?.Width ?? 0;
            int screenH = TissueMaskRenderer.MaskTexture?.Height ?? 0;
            float viewWidth = screenW / Camera.Zoom;
            float viewHeight = screenH / Camera.Zoom;
            float extraPadding = TissueRevealController.RevealRadius + TissueMaskCullPaddingPixels;
            float left = Camera.Position.X - worldOffsetX - extraPadding;
            float top = Camera.Position.Y - extraPadding;
            float right = left + viewWidth + (extraPadding * 2f);
            float bottom = top + viewHeight + (extraPadding * 2f);

            int x = (int)MathF.Floor(left);
            int y = (int)MathF.Floor(top);
            int width = (int)MathF.Ceiling(right - left);
            int height = (int)MathF.Ceiling(bottom - top);
            return new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
        }

        private Vector2 NormalizeLoopingWorld(Vector2 mouseWorld)
        {
            float worldWidth = WorldMap.PixelWidth;
            float wrapDelta = 0f;

            if (Player.Position.X < 0f)
                wrapDelta = worldWidth;
            else if (Player.Position.X >= worldWidth)
                wrapDelta = -worldWidth;

            if (wrapDelta == 0f)
                return mouseWorld;

            Player.ShiftX(wrapDelta);
            Camera.ShiftX(wrapDelta);

            return new Vector2(mouseWorld.X + wrapDelta, mouseWorld.Y);
        }
    }
}
