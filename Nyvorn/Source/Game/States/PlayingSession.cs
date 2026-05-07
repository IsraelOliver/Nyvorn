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
using Nyvorn.Source.Gameplay.World.Simulation;
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
        public required SessionRuntimeContext RuntimeContext { get; init; }
        public SandSystem SandSystem { get; private set; }
        public required WorldItemRuntimeSystem WorldItemRuntimeSystem { get; init; }
        public required PlayingSessionEntityRuntimeSystem EntityRuntimeSystem { get; init; }
        public required PlayingSessionBlockInteractionSystem BlockInteractionSystem { get; init; }
        public required PlayingSessionViewCoordinator ViewCoordinator { get; init; }
        public required PlayingSessionTissueSystem TissueSystem { get; init; }
        public required Dictionary<ItemId, Weapon> Weapons { get; init; }
        public required CombatSystem CombatSystem { get; init; }
        public required WorldTickSystem WorldTickSystem { get; init; }
        private const int RandomTileSamplesPerChunk = 2;
        private const int MaxRandomTileSamplesPerTick = 128;
        private readonly Random randomTileUpdateRandom = new();
        public int SelectedHotbarIndex { get; private set; }
        public IReadOnlyList<WorldChunkCoord> ActiveSimulationChunks => ViewCoordinator.ActiveSimulationChunks;
        public int LastRandomTileSampleCount { get; private set; }
        public int LastGrassGrowthCount { get; private set; }
        public float WorldTickTimeScale => WorldTickSystem.TimeScale;
        public bool WorldTicksPaused => WorldTickSystem.IsPaused;
        public long FastTickCount => WorldTickSystem.FastTickCount;
        public long MediumTickCount => WorldTickSystem.MediumTickCount;
        public long SlowTickCount => WorldTickSystem.SlowTickCount;
        public WorldMap WorldMap => RuntimeContext.WorldMap;
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

        public void InitializeRuntimeState()
        {
            TissueSystem.InitializeRuntimeState();
        }

        public void SetWorldTickTimeScale(float timeScale)
        {
            WorldTickSystem.SetTimeScale(MathHelper.Clamp(timeScale, 0.1f, 16f));
        }

        public void SetWorldTicksPaused(bool isPaused)
        {
            WorldTickSystem.SetPaused(isPaused);
        }

        public int ForceGrassGrowthSamples(int sampleCount)
        {
            int grassGrowthCount = 0;
            LastRandomTileSampleCount = RandomTileUpdateHelper.VisitRandomTiles(
                WorldMap,
                ViewCoordinator.ActiveSimulationChunks,
                Math.Max(1, sampleCount),
                Math.Max(1, sampleCount),
                randomTileUpdateRandom,
                tile =>
                {
                    if (GrassSimulation.TryRandomUpdate(WorldMap, tile.X, tile.Y, randomTileUpdateRandom))
                        grassGrowthCount++;
                });

            LastGrassGrowthCount = grassGrowthCount;
            return grassGrowthCount;
        }

        public void StepWorldTicks(int cycles)
        {
            int safeCycles = Math.Clamp(cycles, 1, 600);
            for (int i = 0; i < safeCycles; i++)
            {
                OnFastTick();
                OnMediumTick();
                OnSlowTick();
            }

            WorldTickSystem.RecordManualDispatch(new WorldTickDispatch(
                safeCycles,
                safeCycles,
                safeCycles,
                FastOverflowed: false,
                MediumOverflowed: false,
                SlowOverflowed: false));
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
            BlockInteractionSystem.UpdateTilePreview(SelectedHotbarIndex, mouseWorld);
            BlockInteractionSystem.TryPlaceSelectedBlock(worldInput, SelectedHotbarIndex, mouseWorld);
            Player.Update(dt, WorldMap, SandSystem, worldInput, mouseWorld);
            mouseWorld = NormalizeLoopingWorld(mouseWorld);
            TissueSystem.Update(dt, input);
            BlockInteractionSystem.TryBreakTargetBlock(mouseWorld);
            EntityRuntimeSystem.Update(dt);

            CombatSystem.Resolve(Player, Enemies);
        }

        private void AdvanceWorldTicks(float dt)
        {
            WorldTickDispatch dispatch = WorldTickSystem.Advance(dt);

            for (int i = 0; i < dispatch.FastTicks; i++)
                OnFastTick();

            for (int i = 0; i < dispatch.MediumTicks; i++)
                OnMediumTick();

            for (int i = 0; i < dispatch.SlowTicks; i++)
                OnSlowTick();
        }

        private void OnFastTick()
        {
            SandSystem?.TickFast();
        }

        private void OnMediumTick()
        {
            int grassGrowthCount = 0;
            LastRandomTileSampleCount = RandomTileUpdateHelper.VisitRandomTiles(
                WorldMap,
                ViewCoordinator.ActiveSimulationChunks,
                RandomTileSamplesPerChunk,
                MaxRandomTileSamplesPerTick,
                randomTileUpdateRandom,
                tile =>
                {
                    if (GrassSimulation.TryRandomUpdate(WorldMap, tile.X, tile.Y, randomTileUpdateRandom))
                        grassGrowthCount++;
                });
            LastGrassGrowthCount = grassGrowthCount;
        }

        private void OnSlowTick()
        {
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

        public void FollowCamera(int screenWidth, int screenHeight)
        {
            ViewCoordinator.FollowPlayer(screenWidth, screenHeight);
        }

        private void SyncEquippedWeapon()
        {
            InventorySlot selectedSlot = Hotbar.GetSlot(SelectedHotbarIndex);
            if (selectedSlot.IsEmpty || !Weapons.TryGetValue(selectedSlot.ItemId, out Weapon weapon))
                weapon = Weapons[ItemId.None];

            Player.SetEquippedWeapon(weapon);
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
            BlockInteractionSystem.SandSystem = SandSystem;
            ViewCoordinator.SandSystem = SandSystem;
        }
    }
}
