using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Engine.Physics.Sand;
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
        public SandSystem SandSystem { get; private set; }
        public required Player Player { get; init; }
        public required List<Enemy> Enemies { get; init; }
        public required List<WorldItem> WorldItems { get; init; }
        public required Hotbar Hotbar { get; init; }
        public required Inventory Inventory { get; init; }
        public required Dictionary<ItemId, Texture2D> ItemTextures { get; init; }
        public required Dictionary<ItemId, Weapon> Weapons { get; init; }
        public required Texture2D DebugPixel { get; init; }
        public required EnemyRespawnController EnemyRespawnController { get; init; }
        public required Camera2D Camera { get; init; }
        public required WorldHealthBarRenderer HealthBarRenderer { get; init; }
        public required HudRenderer HudRenderer { get; init; }
        public required WorldMinimapRenderer WorldMinimapRenderer { get; init; }
        public required ElyraSkyRenderer ElyraSkyRenderer { get; init; }
        public required WorldTilePreviewRenderer TilePreviewRenderer { get; init; }
        public required CombatSystem CombatSystem { get; init; }
        public required TissueNetwork TissueNetwork { get; init; }
        public required TissueRevealController TissueRevealController { get; init; }
        public required TissueFieldDebugRenderer TissueDebugRenderer { get; init; }
        public required HashSet<int> ActivatedTissueHubKeys { get; init; }
        private const float BlockPlaceInterval = 0.08f;
        private const float ItemPullRangeInTiles = 1f;
        private const float ItemPullStrength = 900f;
        private const float EntitySimulationRangeInTiles = 56f;
        private const float EntityDrawPaddingPixels = 48f;
        private const float GrassSpreadIntervalMin = 0.9f;
        private const float GrassSpreadIntervalMax = 3.2f;
        private const float AmbientTissueRadiusInTiles = 7f;
        private const float TissueHubActivationRadiusInTiles = 1.35f;
        private const float AmbientLinkPresence = 0.045f;
        private const float AmbientHubPresence = 0.085f;
        private const float AmbientTissueSampleInterval = 0.15f;
        private static readonly Color SandPixelColor = new Color(214, 196, 150);
        private static readonly Color SandTopEdgeColor = new Color(168, 145, 102);
        public int SelectedHotbarIndex { get; private set; }
        private int lastBlockBreakAttackSequence = -1;
        private Point lastBrokenBlockTile = new Point(int.MinValue, int.MinValue);
        private float blockPlaceCooldownTimer;
        private float grassSpreadTimer;
        private float ambientTissuePresenceTimer;
        private float ambientTissuePresenceCache;
        private Rectangle hoveredTileBounds;
        private WorldTilePreviewState hoveredTileState = WorldTilePreviewState.Hidden;

        public void InitializeRuntimeState()
        {
            ScheduleNextGrassSpread();
            ambientTissuePresenceTimer = AmbientTissueSampleInterval;
            ambientTissuePresenceCache = 0f;
        }

        public void Update(float dt, InputState input, Vector2 mouseWorld)
        {
            ambientTissuePresenceTimer -= dt;

            if (blockPlaceCooldownTimer > 0f)
                blockPlaceCooldownTimer -= dt;

            grassSpreadTimer -= dt;
            if (grassSpreadTimer <= 0f)
            {
                WorldMap.UpdateGrassSpread();
                ScheduleNextGrassSpread();
            }

            if (input.HotbarSelectionIndex >= 0 && input.HotbarSelectionIndex < Hotbar.Capacity)
                SelectedHotbarIndex = input.HotbarSelectionIndex;
            else if (input.MouseWheelDelta != 0)
                CycleSelectedHotbarSlot(input.MouseWheelDelta);

            InputState worldInput = input;
            InventorySlot selectedSlot = Hotbar.GetSlot(SelectedHotbarIndex);
            bool isSandPixelItem = selectedSlot.ItemId == ItemId.SandBlock;
            if (!selectedSlot.IsEmpty && (isSandPixelItem || TileItemMapper.TryGetTileType(selectedSlot.ItemId, out _)))
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
            SandSystem?.Update(dt);
            Player.Update(dt, WorldMap, SandSystem, worldInput, mouseWorld);
            mouseWorld = NormalizeLoopingWorld(mouseWorld);
            TryActivateTouchedTissueHub();
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
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);

            WorldMap.Draw(spriteBatch, startTileX, endTileX, startTileY, endTileY);
            DrawSandPixels(spriteBatch, screenWidth, screenHeight, worldOffsetX);
            TilePreviewRenderer.Draw(spriteBatch, hoveredTileBounds, hoveredTileState);
        }

        public void PrepareTerrainRender(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight, float worldOffsetX)
        {
            GetVisibleTileRange(screenWidth, screenHeight, worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY);
            WorldMap.PrepareVisibleChunkCache(graphicsDevice, startTileX, endTileX, startTileY, endTileY);
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

        public void DrawSky(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ElyraSkyRenderer.Draw(spriteBatch, screenWidth, screenHeight);
        }


        public void DrawTissueDebug(SpriteBatch spriteBatch)
        {
            (float revealStrength, float revealRadius, float waveProgress) = GetEffectiveTissueVisualState();
            if (revealStrength <= 0.001f)
                return;

            TissueDebugRenderer.Draw(
                spriteBatch,
                WorldMap,
                revealStrength,
                TissueRevealController.FocusPosition,
                revealRadius);
        }

        public void DrawHud(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            HudRenderer.Draw(spriteBatch, Hotbar, SelectedHotbarIndex, Player.Health, Player.MaxHealth, screenWidth, screenHeight);
        }

        public void SetSelectedHotbarIndex(int index)
        {
            SelectedHotbarIndex = Math.Clamp(index, 0, Hotbar.Capacity - 1);
        }

        private void CycleSelectedHotbarSlot(int mouseWheelDelta)
        {
            int direction = mouseWheelDelta > 0 ? -1 : 1;
            SelectedHotbarIndex = (SelectedHotbarIndex + direction + Hotbar.Capacity) % Hotbar.Capacity;
        }

        public void DrawMinimap(SpriteBatch spriteBatch, int screenWidth, int screenHeight, bool tissueMode)
        {
            WorldMinimapRenderer.Draw(spriteBatch, WorldMap, TissueNetwork, Camera, Player.Position, screenWidth, screenHeight, tissueMode, ActivatedTissueHubKeys);
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
                tissueMode,
                CanUseTissueFastTravel,
                ActivatedTissueHubKeys);
        }

        public bool IsTissueRadarActive => TissueRevealController.IsActive;

        public bool IsPlayerOnActivatedTissueHub => TryGetCurrentActivatedTissueHubIndex(out _);

        public bool CanUseTissueFastTravel => IsPlayerOnActivatedTissueHub;

        public void EnsureCurrentTissueHubActivated()
        {
            TryActivateTouchedTissueHub();
        }

        public bool TryFastTravelToTissueHub(int hubIndex)
        {
            if (!CanUseTissueFastTravel)
                return false;

            TissueAnalysisResult analysis = WorldMap.GetOrCreateTissueAnalysis();
            if (analysis == null || hubIndex < 0 || hubIndex >= analysis.Hubs.Count)
                return false;

            TissueHub targetHub = analysis.Hubs[hubIndex];
            if (!IsTissueHubActivated(targetHub))
                return false;

            Vector2 targetPosition = GetHubTravelPosition(targetHub);
            Player.TeleportTo(targetPosition);
            return true;
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
            if (ambientTissuePresenceTimer > 0f)
                return ambientTissuePresenceCache;

            TissueAnalysisResult analysis = WorldMap.GetOrCreateTissueAnalysis();
            TissueField tissueField = WorldMap.TissueField;
            if (analysis == null || tissueField == null)
            {
                ambientTissuePresenceCache = 0f;
                ambientTissuePresenceTimer = AmbientTissueSampleInterval;
                return 0f;
            }

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
            ambientTissuePresenceCache = System.MathF.Max(linkPresence, hubPresence);
            ambientTissuePresenceTimer = AmbientTissueSampleInterval;
            return ambientTissuePresenceCache;
        }

        private void TryActivateTouchedTissueHub()
        {
            TissueAnalysisResult analysis = WorldMap.GetOrCreateTissueAnalysis();
            if (analysis == null || analysis.Hubs.Count == 0)
                return;

            float activationRadius = WorldMap.TileSize * TissueHubActivationRadiusInTiles;
            for (int i = 0; i < analysis.Hubs.Count; i++)
            {
                TissueHub hub = analysis.Hubs[i];
                if (GetLoopAwareDistance(hub.WorldPosition, Player.Position) > activationRadius)
                    continue;

                ActivatedTissueHubKeys.Add(CreateTissueHubKey(hub.TilePosition));
                return;
            }
        }

        private bool TryGetCurrentActivatedTissueHubIndex(out int hubIndex)
        {
            hubIndex = -1;

            TissueAnalysisResult analysis = WorldMap.GetOrCreateTissueAnalysis();
            if (analysis == null || analysis.Hubs.Count == 0 || ActivatedTissueHubKeys.Count == 0)
                return false;

            float activationRadius = WorldMap.TileSize * TissueHubActivationRadiusInTiles;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < analysis.Hubs.Count; i++)
            {
                TissueHub hub = analysis.Hubs[i];
                if (!IsTissueHubActivated(hub))
                    continue;

                float distance = GetLoopAwareDistance(hub.WorldPosition, Player.Position);
                if (distance > activationRadius || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                hubIndex = i;
            }

            return hubIndex >= 0;
        }

        private bool IsTissueHubActivated(TissueHub hub)
        {
            return ActivatedTissueHubKeys.Contains(CreateTissueHubKey(hub.TilePosition));
        }

        private int CreateTissueHubKey(Point tilePosition)
        {
            int wrappedX = WorldMap.WrapTileX(tilePosition.X);
            return (tilePosition.Y * WorldMap.Width) + wrappedX;
        }

        private Vector2 GetHubTravelPosition(TissueHub hub)
        {
            float worldWidth = WorldMap.PixelWidth;
            float x = hub.WorldPosition.X;
            if (worldWidth > 0f)
            {
                x %= worldWidth;
                if (x < 0f)
                    x += worldWidth;
            }

            float y = hub.WorldPosition.Y + (WorldMap.TileSize * 0.5f);
            float maxY = WorldMap.Height * WorldMap.TileSize;
            y = Math.Clamp(y, WorldMap.TileSize, Math.Max(WorldMap.TileSize, maxY));
            return new Vector2(x, y);
        }

        private void ScheduleNextGrassSpread()
        {
            grassSpreadTimer = Random.Shared.NextSingle() switch
            {
                float roll when roll < 0.18f => MathHelper.Lerp(0.18f, 0.55f, Random.Shared.NextSingle()),
                _ => MathHelper.Lerp(GrassSpreadIntervalMin, GrassSpreadIntervalMax, Random.Shared.NextSingle())
            };
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
            SpawnWorldItem(definition, texture, spawnPosition, pickupDelay: 0.25f);
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

            if (TryStoreCollectedDefinition(worldItem.Definition, 1))
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
            if (Vector2.Distance(Player.Position, tileCenter) > Player.WorldBreakRange)
                return;

            if (!WorldMap.TryBreakTile(tile.X, tile.Y, out TileType removedTile))
                return;

            SandSystem?.WakeAreaAboveTile(tile.X, tile.Y);
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
            SpawnWorldItem(
                definition,
                texture,
                tileCenter,
                pickupDelay: 0.15f,
                initialVelocityX: horizontalVelocity,
                initialVelocityY: verticalVelocity);
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

            if (selectedSlot.ItemId == ItemId.SandBlock)
            {
                TryPlaceSandPixel(selectedSlot, mouseWorld);
                return;
            }

            if (!TileItemMapper.TryGetTileType(selectedSlot.ItemId, out TileType tileType))
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            Rectangle tileBounds = WorldMap.GetTileBounds(tile.X, tile.Y);
            if (tileBounds.Intersects(Player.Hurtbox))
                return;

            if (SandSystem != null && SandSystem.HasSandInRectangle(tileBounds.X, tileBounds.Y, tileBounds.Width, tileBounds.Height))
                return;

            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            if (Vector2.Distance(Player.Position, tileCenter) > Player.WorldInteractionRange)
                return;

            if (!WorldMap.TryPlaceTile(tile.X, tile.Y, tileType))
                return;

            selectedSlot.RemoveOne();
            blockPlaceCooldownTimer = BlockPlaceInterval;
        }

        private void TryPlaceSandPixel(InventorySlot selectedSlot, Vector2 mouseWorld)
        {
            if (blockPlaceCooldownTimer > 0f || SandSystem == null)
                return;

            int pixelX = WrapPixelX((int)MathF.Floor(mouseWorld.X));
            int pixelY = (int)MathF.Floor(mouseWorld.Y);
            if (pixelY < 0 || pixelY >= SandSystem.Height)
                return;

            Point tile = WorldMap.WorldToTile(new Vector2(pixelX, pixelY));
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return;

            if (WorldMap.IsSolidAt(tile.X, tile.Y) || SandSystem.HasSandAt(pixelX, pixelY))
                return;

            Vector2 pixelCenter = new Vector2(pixelX + 0.5f, pixelY + 0.5f);
            if (Vector2.Distance(Player.Position, pixelCenter) > Player.WorldInteractionRange)
                return;

            SandSystem.SetSandAt(pixelX, pixelY, true);
            selectedSlot.RemoveOne();
            blockPlaceCooldownTimer = BlockPlaceInterval;
        }

        private void UpdateTilePreview(Vector2 mouseWorld)
        {
            hoveredTileState = WorldTilePreviewState.Hidden;

            InventorySlot selectedSlot = Hotbar.GetSlot(SelectedHotbarIndex);
            if (!selectedSlot.IsEmpty && selectedSlot.ItemId == ItemId.SandBlock)
                return;

            Point tile = WorldMap.WorldToTile(mouseWorld);
            if (!WorldMap.InBounds(tile.X, tile.Y))
                return;

            hoveredTileBounds = WorldMap.GetTileBounds(tile.X, tile.Y);
            Vector2 tileCenter = WorldMap.GetTileCenter(tile.X, tile.Y);
            bool inRange = Vector2.Distance(Player.Position, tileCenter) <= Player.WorldInteractionRange;
            bool inBreakRange = Vector2.Distance(Player.Position, tileCenter) <= Player.WorldBreakRange;

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

            hoveredTileState = inBreakRange && Player.CanBreakTile(targetTile)
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

        private void GetVisibleTileRange(int screenWidth, int screenHeight, float worldOffsetX, out int startTileX, out int endTileX, out int startTileY, out int endTileY)
        {
            const int tilePadding = 2;
            float viewWidth = screenWidth / Camera.Zoom;
            float viewHeight = screenHeight / Camera.Zoom;
            float localLeft = Camera.Position.X - worldOffsetX;
            float localTop = Camera.Position.Y;
            float localRight = localLeft + viewWidth;
            float localBottom = localTop + viewHeight;

            startTileX = (int)MathF.Floor(localLeft / WorldMap.TileSize) - tilePadding;
            endTileX = (int)MathF.Ceiling(localRight / WorldMap.TileSize) + tilePadding;
            startTileY = (int)MathF.Floor(localTop / WorldMap.TileSize) - tilePadding;
            endTileY = (int)MathF.Ceiling(localBottom / WorldMap.TileSize) + tilePadding;
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

        public void InitializeSandSystem()
        {
            SandSystem = new SandSystem(WorldMap);
        }

        private void DrawSandPixels(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            if (SandSystem == null)
                return;

            float viewWidth = screenWidth / Camera.Zoom;
            float viewHeight = screenHeight / Camera.Zoom;
            int startPixelX = (int)MathF.Floor(Camera.Position.X - worldOffsetX);
            int endPixelX = (int)MathF.Ceiling(Camera.Position.X - worldOffsetX + viewWidth);
            int startPixelY = Math.Max(0, (int)MathF.Floor(Camera.Position.Y));
            int endPixelY = Math.Min(SandSystem.Height - 1, (int)MathF.Ceiling(Camera.Position.Y + viewHeight));

            DrawWrappedSandRange(spriteBatch, startPixelX, endPixelX, startPixelY, endPixelY, SandPixelColor, topEdgesOnly: false);
            DrawWrappedSandRange(spriteBatch, startPixelX, endPixelX, startPixelY, endPixelY, SandTopEdgeColor, topEdgesOnly: true);
        }

        private void DrawWrappedSandRange(SpriteBatch spriteBatch, int rawStartX, int rawEndX, int startPixelY, int endPixelY, Color tint, bool topEdgesOnly)
        {
            int worldWidth = SandSystem.Width;
            if (worldWidth <= 0 || rawStartX > rawEndX || startPixelY > endPixelY)
                return;

            int currentRawStartX = rawStartX;
            while (currentRawStartX <= rawEndX)
            {
                int wrappedStartX = WrapPixelX(currentRawStartX);
                int segmentMaxLength = worldWidth - wrappedStartX;
                int currentRawEndX = Math.Min(rawEndX, currentRawStartX + segmentMaxLength - 1);
                int wrappedEndX = wrappedStartX + (currentRawEndX - currentRawStartX);
                int drawOffsetX = currentRawStartX - wrappedStartX;

                IEnumerable<Rectangle> segments = topEdgesOnly
                    ? SandSystem.GetVisibleTopEdgeSegments(wrappedStartX, wrappedEndX, startPixelY, endPixelY)
                    : SandSystem.GetVisibleSegments(wrappedStartX, wrappedEndX, startPixelY, endPixelY);

                foreach (Rectangle segment in segments)
                {
                    Rectangle drawBounds = new Rectangle(segment.X + drawOffsetX, segment.Y, segment.Width, segment.Height);
                    spriteBatch.Draw(DebugPixel, drawBounds, tint);
                }

                currentRawStartX = currentRawEndX + 1;
            }
        }

        private int WrapPixelX(int pixelX)
        {
            int worldWidth = WorldMap.PixelWidth;
            if (worldWidth <= 0)
                return 0;

            int wrapped = pixelX % worldWidth;
            return wrapped < 0 ? wrapped + worldWidth : wrapped;
        }
    }
}
