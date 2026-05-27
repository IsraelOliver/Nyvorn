using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.Entities.Enemies;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Items;
using Nyvorn.Source.Gameplay.Powers;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.Gameplay.World.Interiors;
using Nyvorn.Source.Gameplay.World.Objects;
using Nyvorn.Source.Gameplay.World.Particles;
using Nyvorn.Source.Gameplay.World.Simulation;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Decorations;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;
using Nyvorn.Source.World.Tissue;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSession
    {
        public required PlanetWorldMetadata PlanetMetadata { get; init; }
        public required SessionRuntimeContext RuntimeContext { get; init; }
        public SandSystem SandSystem { get; private set; }
        public required WorldItemRuntimeSystem WorldItemRuntimeSystem { get; init; }
        public required PlayingSessionEntityRuntimeSystem EntityRuntimeSystem { get; init; }
        public required PlayingSessionBlockInteractionSystem BlockInteractionSystem { get; init; }
        public required PlayingSessionViewCoordinator ViewCoordinator { get; init; }
        public required PlayingSessionTissueSystem TissueSystem { get; init; }
        public required PlayingSessionInputRouter InputRouter { get; init; }
        public required PlayingSessionWorldWrapSystem WorldWrapSystem { get; init; }
        public required PlayingSessionWorldTickCoordinator WorldTickCoordinator { get; init; }
        public required PlayingSessionCombatCoordinator CombatCoordinator { get; init; }
        public required WorkbenchRuntimeSystem WorkbenchRuntimeSystem { get; init; }
        public required DoorRuntimeSystem DoorRuntimeSystem { get; init; }
        public required InteriorFocusSystem InteriorFocusSystem { get; init; }
        public required BlockParticleSystem BlockParticleSystem { get; init; }
        public required PlayerPowerSystem PowerSystem { get; init; }
        public int SelectedHotbarIndex => InputRouter.SelectedHotbarIndex;
        public IReadOnlyList<WorldChunkCoord> ActiveSimulationChunks => ViewCoordinator.ActiveSimulationChunks;
        public int LastRandomTileSampleCount => WorldTickCoordinator.LastRandomTileSampleCount;
        public int LastGrassGrowthCount => WorldTickCoordinator.LastGrassGrowthCount;
        public float WorldTickTimeScale => WorldTickCoordinator.WorldTickTimeScale;
        public bool WorldTicksPaused => WorldTickCoordinator.WorldTicksPaused;
        public long FastTickCount => WorldTickCoordinator.FastTickCount;
        public long MediumTickCount => WorldTickCoordinator.MediumTickCount;
        public long SlowTickCount => WorldTickCoordinator.SlowTickCount;
        public WorldMap WorldMap => RuntimeContext.WorldMap;
        public bool HasUnsavedWorldChanges => WorldMap.HasUnsavedChanges || WorkbenchRuntimeSystem.HasUnsavedChanges || DoorRuntimeSystem.HasUnsavedChanges;
        public Player Player => RuntimeContext.Player;
        public List<Enemy> Enemies => RuntimeContext.Enemies;
        public List<WorldItem> WorldItems => RuntimeContext.WorldItems;
        public Hotbar Hotbar => RuntimeContext.Hotbar;
        public Inventory Inventory => RuntimeContext.Inventory;
        public Camera2D Camera => RuntimeContext.Camera;
        public HudRenderer HudRenderer => ViewCoordinator.HudRenderer;
        public WorldMinimapRenderer WorldMinimapRenderer => ViewCoordinator.WorldMinimapRenderer;
        public TissueNetwork TissueNetwork => TissueSystem.TissueNetwork;
        public IReadOnlySet<int> ActivatedTissueHubKeys => TissueSystem.ActivatedTissueHubKeys;
        public bool IsConstructionMode { get; private set; }

        public void InitializeRuntimeState()
        {
            TissueSystem.InitializeRuntimeState();
        }

        public void SetWorldTickTimeScale(float timeScale)
        {
            WorldTickCoordinator.SetWorldTickTimeScale(timeScale);
        }

        public void SetWorldTicksPaused(bool isPaused)
        {
            WorldTickCoordinator.SetWorldTicksPaused(isPaused);
        }

        public int ForceGrassGrowthSamples(int sampleCount)
        {
            return WorldTickCoordinator.ForceGrassGrowthSamples(sampleCount);
        }

        public void StepWorldTicks(int cycles)
        {
            WorldTickCoordinator.StepWorldTicks(cycles);
        }

        public void ToggleConstructionMode()
        {
            IsConstructionMode = !IsConstructionMode;
        }

        public void RespawnPlayerAtWorldCenter()
        {
            int centerTileX = WorldMap.Width / 2;
            Vector2 spawnPosition = new WorldGenerator().GetSurfaceSpawnPosition(
                WorldMap,
                centerTileX,
                tilesAboveSurface: 2);

            Player.RespawnAt(spawnPosition);
        }

        public void Update(float dt, InputState input, Vector2 mouseWorld)
        {
            UpdateFrame(dt, input, mouseWorld);
            AdvanceWorldTicks(dt);
        }

        public void UpdateSimulationViewport(int screenWidth, int screenHeight)
        {
            ViewCoordinator.UpdateSimulationViewport(screenWidth, screenHeight);
        }

        private void UpdateFrame(float dt, InputState input, Vector2 mouseWorld)
        {
            BlockInteractionSystem.Update(dt);
            WorkbenchRuntimeSystem.UpdateHover(mouseWorld);
            InputState worldInput = InputRouter.RouteFrameInput(input);

            CombatCoordinator.SyncEquippedWeapon(SelectedHotbarIndex);
            BlockInteractionSystem.UpdateTilePreview(SelectedHotbarIndex, mouseWorld, IsConstructionMode);
            bool animateConstructionPickaxe =
                IsConstructionMode &&
                worldInput.ActivePowerPressed &&
                BlockInteractionSystem.ShouldAnimateConstructionPickaxe(SelectedHotbarIndex, mouseWorld);

            if (IsConstructionMode)
                BlockInteractionSystem.TryUseConstructionModeAction(dt, worldInput, SelectedHotbarIndex, mouseWorld);

            bool objectPlacementHandled =
                WorkbenchRuntimeSystem.TryPlaceSelectedWorkbench(worldInput, SelectedHotbarIndex, mouseWorld) ||
                DoorRuntimeSystem.TryPlaceSelectedDoor(worldInput, SelectedHotbarIndex, mouseWorld);
            if (objectPlacementHandled)
            {
                worldInput = worldInput.ConsumeWorldMouseInput();
            }
            else
            {
                BlockInteractionSystem.TryPlaceSelectedBlock(worldInput, SelectedHotbarIndex, mouseWorld);
            }

            if (animateConstructionPickaxe && !worldInput.AttackPressed)
                Player.TryStartToolUseAnimation(mouseWorld);

            Player.Update(dt, WorldMap, SandSystem, worldInput, mouseWorld);
            mouseWorld = WorldWrapSystem.NormalizePlayerAndMouse(mouseWorld);
            InteriorFocusSystem.Update(dt, IsConstructionMode);
            TissueSystem.Update(dt, input);
            PowerSystem.Update(dt);
            BlockInteractionSystem.TryBreakTargetBlock(dt, worldInput, mouseWorld, SelectedHotbarIndex);
            EntityRuntimeSystem.Update(dt);
            BlockParticleSystem.Update(dt);

            CombatCoordinator.ResolveCombat();
        }

        private void AdvanceWorldTicks(float dt)
        {
            WorldTickCoordinator.Advance(dt);
        }

        public void DrawTerrain(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.DrawTerrain(
                spriteBatch,
                screenWidth,
                screenHeight,
                worldOffsetX,
                BlockInteractionSystem.HoveredTileBounds,
                BlockInteractionSystem.HoveredTileState);
        }

        public void DrawTreeDecorations(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, TreeRenderLayer layer)
        {
            ViewCoordinator.DrawTreeDecorations(spriteBatch, screenWidth, screenHeight, worldOffsetX, layer);
        }

        public void PrepareTerrainRender(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.PrepareTerrainRender(graphicsDevice, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawEntities(SpriteBatch spriteBatch)
        {
            ViewCoordinator.DrawEntities(spriteBatch);
        }

        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.DrawLoopedWorldEntities(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawInteriorFocusOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.DrawInteriorFocusOverlay(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawSky(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ViewCoordinator.DrawSky(spriteBatch, screenWidth, screenHeight);
        }


        public void DrawTissueDebug(SpriteBatch spriteBatch)
        {
            TissueSystem.DrawDebug(spriteBatch);
        }

        public void DrawHud(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ViewCoordinator.DrawHud(spriteBatch, Hotbar, SelectedHotbarIndex, screenWidth, screenHeight);
            ViewCoordinator.DrawPowerHud(spriteBatch, PowerSystem, screenWidth, screenHeight, IsConstructionMode);
        }

        public void SetSelectedHotbarIndex(int index)
        {
            InputRouter.SetSelectedHotbarIndex(index);
        }

        public void DrawMinimap(SpriteBatch spriteBatch, int screenWidth, int screenHeight, bool tissueMode)
        {
            ViewCoordinator.DrawMinimap(spriteBatch, screenWidth, screenHeight, tissueMode);
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
                TissueSystem.CanUseTissueFastTravel,
                TissueSystem.ActivatedTissueHubKeys);
        }

        public bool IsTissueRadarActive => TissueSystem.IsTissueRadarActive;

        public bool IsPlayerOnActivatedTissueHub => TissueSystem.IsPlayerOnActivatedTissueHub;

        public bool CanUseTissueFastTravel => TissueSystem.CanUseTissueFastTravel;

        public void EnsureCurrentTissueHubActivated()
        {
            TissueSystem.EnsureCurrentTissueHubActivated();
        }

        public bool TryFastTravelToTissueHub(int hubIndex)
        {
            return TissueSystem.TryFastTravelToTissueHub(hubIndex);
        }

        public void DrawInventory(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ViewCoordinator.DrawInventory(spriteBatch, Hotbar, Inventory, SelectedHotbarIndex, screenWidth, screenHeight);
        }

        public Rectangle GetInventoryPanelBounds(int screenWidth, int screenHeight)
        {
            return ViewCoordinator.GetInventoryPanelBounds(screenWidth, screenHeight);
        }

        public bool TryGetItemTexture(ItemId itemId, out Texture2D texture)
        {
            return WorldItemRuntimeSystem.TryGetItemTexture(itemId, out texture);
        }

        public bool TryDropItem(ItemId itemId)
        {
            return WorldItemRuntimeSystem.TryDropItem(itemId);
        }

        public bool TryStoreItem(ItemId itemId, int quantity, bool preferInventory)
        {
            return WorldItemRuntimeSystem.TryStoreItem(itemId, quantity, preferInventory);
        }

        public int CountItem(ItemId itemId)
        {
            return Hotbar.CountItem(itemId) + Inventory.CountItem(itemId);
        }

        public bool TryConsumeItem(ItemId itemId, int quantity)
        {
            if (quantity <= 0 || CountItem(itemId) < quantity)
                return false;

            int fromInventory = System.Math.Min(Inventory.CountItem(itemId), quantity);
            if (fromInventory > 0)
                Inventory.TryRemove(itemId, fromInventory);

            int remaining = quantity - fromInventory;
            if (remaining > 0)
                Hotbar.TryRemove(itemId, remaining);

            return true;
        }

        public void FollowCamera(float dt, int screenWidth, int screenHeight)
        {
            ViewCoordinator.FollowPlayer(dt, screenWidth, screenHeight);
        }

        public void InitializeSandSystem()
        {
            SandSystem = new SandSystem(WorldMap);
            BlockInteractionSystem.SandSystem = SandSystem;
            ViewCoordinator.SandSystem = SandSystem;
            WorldTickCoordinator.SandSystem = SandSystem;
        }
    }
}
