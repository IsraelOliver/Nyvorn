using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Engine.Physics.Liquids;
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
        private const int MaxSavedConsoleCommands = 100;
        private int consoleCommandHistoryRevision;
        private int persistedConsoleCommandHistoryRevision;

        public required PlanetWorldMetadata PlanetMetadata { get; init; }
        public required string PlayerId { get; init; }
        public required SessionRuntimeContext RuntimeContext { get; init; }
        public SandSystem SandSystem { get; private set; }
        public LiquidSystem LiquidSystem { get; private set; }
        public required WorldItemRuntimeSystem WorldItemRuntimeSystem { get; init; }
        public required PlayingSessionEntityRuntimeSystem EntityRuntimeSystem { get; init; }
        public required PlayingSessionBlockInteractionSystem BlockInteractionSystem { get; init; }
        public required PlayingSessionViewCoordinator ViewCoordinator { get; init; }
        public required PlayingSessionTissueSystem TissueSystem { get; init; }
        public required ITissueQueryService TissueQueries { get; init; }
        public required ITissueMutationService TissueMutations { get; init; }
        public required ITissuePropagationService TissuePropagation { get; init; }
        public required TissueGenerationResult CosmicTissueGeneration { get; init; }
        public required PlayingSessionInputRouter InputRouter { get; init; }
        public required PlayingSessionWorldWrapSystem WorldWrapSystem { get; init; }
        public required PlayingSessionWorldTickCoordinator WorldTickCoordinator { get; init; }
        public required WorldLightingSystem LightingSystem { get; init; }
        public required WorldDayNightCycle DayNightCycle { get; init; }
        public required WorldEnvironmentSystem EnvironmentSystem { get; init; }
        public required PlayingSessionCombatCoordinator CombatCoordinator { get; init; }
        public required WorkbenchRuntimeSystem WorkbenchRuntimeSystem { get; init; }
        public required FurnaceRuntimeSystem FurnaceRuntimeSystem { get; init; }
        public required TorchRuntimeSystem TorchRuntimeSystem { get; init; }
        public required DoorRuntimeSystem DoorRuntimeSystem { get; init; }
        public required InteriorFocusSystem InteriorFocusSystem { get; init; }
        public required BlockParticleSystem BlockParticleSystem { get; init; }
        public required PlayerPowerSystem PowerSystem { get; init; }
        public required List<string> ConsoleCommandHistory { get; init; }
        public int SelectedHotbarIndex => InputRouter.SelectedHotbarIndex;
        public IReadOnlyList<WorldChunkCoord> ActiveSimulationChunks => ViewCoordinator.ActiveSimulationChunks;
        public TileWetnessField WetnessField => WorldTickCoordinator.WetnessField;
        public int LastRandomTileSampleCount => WorldTickCoordinator.LastRandomTileSampleCount;
        public int LastGrassGrowthCount => WorldTickCoordinator.LastGrassGrowthCount;
        public float WorldTickTimeScale => WorldTickCoordinator.WorldTickTimeScale;
        public bool WorldTicksPaused => WorldTickCoordinator.WorldTicksPaused;
        public long FastTickCount => WorldTickCoordinator.FastTickCount;
        public long MediumTickCount => WorldTickCoordinator.MediumTickCount;
        public long SlowTickCount => WorldTickCoordinator.SlowTickCount;
        public WorldMap WorldMap => RuntimeContext.WorldMap;
        public bool HasUnsavedWorldChanges => WorldMap.HasUnsavedChanges ||
                                              DayNightCycle.HasUnsavedChanges ||
                                              EnvironmentSystem.HasUnsavedChanges ||
                                              LiquidSystem?.HasUnsavedChanges == true ||
                                              WorkbenchRuntimeSystem.HasUnsavedChanges ||
                                              FurnaceRuntimeSystem.HasUnsavedChanges ||
                                              TorchRuntimeSystem.HasUnsavedChanges ||
                                              DoorRuntimeSystem.HasUnsavedChanges ||
                                              consoleCommandHistoryRevision != persistedConsoleCommandHistoryRevision;
        public Player Player => RuntimeContext.Player;
        public List<Enemy> Enemies => RuntimeContext.Enemies;
        public List<WorldItem> WorldItems => RuntimeContext.WorldItems;
        public Hotbar Hotbar => RuntimeContext.Hotbar;
        public Inventory Inventory => RuntimeContext.Inventory;
        public Camera2D Camera => RuntimeContext.Camera;
        public HudRenderer HudRenderer => ViewCoordinator.HudRenderer;
        public WorldMinimapRenderer WorldMinimapRenderer => ViewCoordinator.WorldMinimapRenderer;
        public TissueNetwork TissueNetwork => TissueSystem.TissueNetwork;
        public TissueField TissueField => WorldMap.TissueField;
        public TissueGenerationStats CosmicTissueStats => CosmicTissueGeneration.Stats;
        public TissueEnvironmentState TissueEnvironment => TissueSystem.EnvironmentState;
        public TissueResonanceState TissueResonance => TissueSystem.ResonanceState;
        public IReadOnlySet<int> ActivatedTissueHubKeys => TissueSystem.ActivatedTissueHubKeys;
        public bool IsConstructionMode { get; private set; }
        public bool TissueVisualEnabled { get; private set; }
        public bool TissueFieldVisualEnabled { get; private set; }
        public bool DebugFlyEnabled => Player.DebugFlyEnabled;
        public float TimeOfDay01 => DayNightCycle.TimeOfDay01;
        public float WorldTimeCyclePercent => DayNightCycle.CyclePercent;
        public float WorldNightStrength => DayNightCycle.NightStrength;
        public string WorldClockText24h => DayNightCycle.ClockText24h;
        public int WorldCycleIndex => DayNightCycle.CycleIndex;
        public WorldTimePhase WorldTimePhase => DayNightCycle.CurrentPhase;
        public WeatherState WeatherState => EnvironmentSystem.WeatherState;
        public TissueCycleState TissueCycleState => EnvironmentSystem.TissueCycleState;
        public string WorldEnvironmentStatusText => EnvironmentSystem.GetStatusText();

        public void InitializeRuntimeState()
        {
            TissueSystem.InitializeRuntimeState();
        }

        public void AddConsoleCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            string normalized = command.Trim();
            if (ConsoleCommandHistory.Count > 0 &&
                string.Equals(ConsoleCommandHistory[^1], normalized, System.StringComparison.Ordinal))
            {
                return;
            }

            ConsoleCommandHistory.Add(normalized);
            while (ConsoleCommandHistory.Count > MaxSavedConsoleCommands)
                ConsoleCommandHistory.RemoveAt(0);
            consoleCommandHistoryRevision++;
        }

        public void MarkConsoleCommandHistoryPersisted()
        {
            persistedConsoleCommandHistoryRevision = consoleCommandHistoryRevision;
        }

        public void MarkDayNightCyclePersisted()
        {
            DayNightCycle.MarkPersisted();
        }

        public void MarkWorldEnvironmentPersisted()
        {
            EnvironmentSystem.MarkPersisted();
        }

        public void MarkLiquidSystemPersisted()
        {
            LiquidSystem?.MarkPersisted();
        }

        public WorldEnvironmentSaveData CreateWorldEnvironmentSaveData()
        {
            return EnvironmentSystem.CreateSaveData(DayNightCycle.CycleIndex);
        }

        public void SetWorldTimeOfDay(float timeOfDay01)
        {
            DayNightCycle.SetTimeOfDay01(timeOfDay01);
            RefreshWorldEnvironment();
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

        public int PlaceWaterAtMouse(Vector2 worldPosition, int radiusTiles)
        {
            if (LiquidSystem == null)
                return 0;

            int radiusPixels = ResolveWaterRadiusPixels(radiusTiles);
            int centerPixelX = WrapPixelX((int)System.MathF.Floor(worldPosition.X));
            int centerPixelY = (int)System.MathF.Floor(worldPosition.Y);
            int placed = 0;

            for (int y = centerPixelY - radiusPixels; y <= centerPixelY + radiusPixels; y += LiquidSystem.CellSize)
            {
                if (y < 0 || y >= LiquidSystem.Height)
                    continue;

                for (int rawX = centerPixelX - radiusPixels; rawX <= centerPixelX + radiusPixels; rawX += LiquidSystem.CellSize)
                {
                    int dx = rawX - centerPixelX;
                    int dy = y - centerPixelY;
                    if ((dx * dx) + (dy * dy) > radiusPixels * radiusPixels)
                        continue;

                    if (LiquidSystem.SetLiquidAt(WrapPixelX(rawX), y, LiquidType.Water, true))
                        placed++;
                }
            }

            return placed;
        }

        public int DrainWaterAtMouse(Vector2 worldPosition, int radiusTiles)
        {
            if (LiquidSystem == null)
                return 0;

            int radiusPixels = ResolveWaterRadiusPixels(radiusTiles);
            int centerPixelX = WrapPixelX((int)System.MathF.Floor(worldPosition.X));
            int centerPixelY = (int)System.MathF.Floor(worldPosition.Y);
            int removed = 0;

            for (int y = centerPixelY - radiusPixels; y <= centerPixelY + radiusPixels; y += LiquidSystem.CellSize)
            {
                if (y < 0 || y >= LiquidSystem.Height)
                    continue;

                for (int rawX = centerPixelX - radiusPixels; rawX <= centerPixelX + radiusPixels; rawX += LiquidSystem.CellSize)
                {
                    int dx = rawX - centerPixelX;
                    int dy = y - centerPixelY;
                    if ((dx * dx) + (dy * dy) > radiusPixels * radiusPixels)
                        continue;

                    if (LiquidSystem.SetLiquidAt(WrapPixelX(rawX), y, LiquidType.Water, false))
                        removed++;
                }
            }

            return removed;
        }

        public int ClearWater()
        {
            return LiquidSystem?.Clear() ?? 0;
        }

        public string GetWaterStatusText()
        {
            if (LiquidSystem == null)
                return "Water: unavailable";

            LiquidSimulationStats stats = LiquidSystem.LastStats;
            return $"Water cells:{LiquidSystem.CellCount} active:{LiquidSystem.ActiveCellCount} " +
                   $"volume:{LiquidSystem.TotalTileVolume:0.##} processed:{stats.ProcessedCells} " +
                   $"moves:{stats.Transfers} chunks:{stats.WokenChunks}";
        }

        public string GetWaterTuningText()
        {
            if (LiquidSystem == null)
                return "Water tune: unavailable";

            LiquidRules rules = LiquidSystem.Rules;
            return $"Water tune tps:{rules.LiquidTicksPerSecond} fall:{rules.MaxFlowPerTick} " +
                   $"side:{rules.MaxLateralFlowPerTick} search:{rules.MaxLateralSearchTiles} " +
                   $"cells:{rules.MaxLiquidCellsPerTick}";
        }

        public bool TryApplyWaterTuningPreset(string preset, out string message)
        {
            message = "Water tune: unavailable";
            if (LiquidSystem == null)
                return false;

            LiquidRules rules = LiquidSystem.Rules;
            if (preset.Equals("slow", System.StringComparison.OrdinalIgnoreCase))
                rules.ApplySlowPreset();
            else if (preset.Equals("balanced", System.StringComparison.OrdinalIgnoreCase) ||
                     preset.Equals("normal", System.StringComparison.OrdinalIgnoreCase) ||
                     preset.Equals("default", System.StringComparison.OrdinalIgnoreCase) ||
                     preset.Equals("reset", System.StringComparison.OrdinalIgnoreCase))
                rules.ApplyBalancedPreset();
            else if (preset.Equals("fast", System.StringComparison.OrdinalIgnoreCase))
                rules.ApplyFastPreset();
            else
            {
                message = "Uso: /water tune slow|balanced|fast";
                return false;
            }

            LiquidSystem.WakeAllLiquids();
            message = GetWaterTuningText();
            return true;
        }

        public bool TrySetWaterTuningValue(string parameter, int value, out string message)
        {
            message = "Water tune: unavailable";
            if (LiquidSystem == null)
                return false;

            LiquidRules rules = LiquidSystem.Rules;
            if (parameter.Equals("tps", System.StringComparison.OrdinalIgnoreCase) ||
                parameter.Equals("rate", System.StringComparison.OrdinalIgnoreCase))
            {
                rules.LiquidTicksPerSecond = System.Math.Clamp(value, 1, 120);
            }
            else if (parameter.Equals("fall", System.StringComparison.OrdinalIgnoreCase) ||
                     parameter.Equals("down", System.StringComparison.OrdinalIgnoreCase))
            {
                rules.MaxFlowPerTick = System.Math.Clamp(value, 1, rules.MaxLiquidAmount);
            }
            else if (parameter.Equals("side", System.StringComparison.OrdinalIgnoreCase) ||
                     parameter.Equals("lateral", System.StringComparison.OrdinalIgnoreCase))
            {
                rules.MaxLateralFlowPerTick = System.Math.Clamp(value, 1, rules.MaxLiquidAmount);
            }
            else if (parameter.Equals("search", System.StringComparison.OrdinalIgnoreCase) ||
                     parameter.Equals("reach", System.StringComparison.OrdinalIgnoreCase))
            {
                rules.MaxLateralSearchTiles = System.Math.Clamp(value, 1, 64);
            }
            else if (parameter.Equals("cells", System.StringComparison.OrdinalIgnoreCase) ||
                     parameter.Equals("budget", System.StringComparison.OrdinalIgnoreCase))
            {
                rules.MaxLiquidCellsPerTick = System.Math.Clamp(value, 256, 50000);
            }
            else
            {
                message = "Uso: /water tune <tps|fall|side|search|cells> <valor>";
                return false;
            }

            LiquidSystem.WakeAllLiquids();
            message = GetWaterTuningText();
            return true;
        }

        public void StepWorldTicks(int cycles)
        {
            WorldTickCoordinator.StepWorldTicks(cycles);
        }

        public void ToggleConstructionMode()
        {
            IsConstructionMode = !IsConstructionMode;
        }

        public void SetTissueVisualEnabled(bool enabled)
        {
            TissueVisualEnabled = enabled;
        }

        public void SetTissueFieldVisualEnabled(bool enabled)
        {
            TissueFieldVisualEnabled = enabled;
        }

        public void SetDebugFly(bool enabled)
        {
            Player.SetDebugFly(enabled);
        }

        public bool ToggleDebugFly()
        {
            SetDebugFly(!DebugFlyEnabled);
            return DebugFlyEnabled;
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

        public void Update(float dt, InputState input, Vector2 mouseWorld, int screenWidth, int screenHeight)
        {
            AdvanceDayNightCycle(dt);
            RefreshWorldEnvironment(dt);
            UpdateFrame(dt, input, mouseWorld, screenWidth, screenHeight);
            AdvanceWorldTicks(dt);
        }

        public void UpdateSimulationViewport(int screenWidth, int screenHeight)
        {
            ViewCoordinator.UpdateSimulationViewport(screenWidth, screenHeight);
            TissueSystem.SetPropagationViewport(
                screenWidth / Camera.Zoom,
                screenHeight / Camera.Zoom);
        }

        private void UpdateFrame(float dt, InputState input, Vector2 mouseWorld, int screenWidth, int screenHeight)
        {
            BlockInteractionSystem.Update(dt);
            WorkbenchRuntimeSystem.UpdateHover(mouseWorld);
            FurnaceRuntimeSystem.UpdateHover(mouseWorld);
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
                FurnaceRuntimeSystem.TryPlaceSelectedFurnace(worldInput, SelectedHotbarIndex, mouseWorld) ||
                TorchRuntimeSystem.TryPlaceSelectedTorch(worldInput, SelectedHotbarIndex, mouseWorld) ||
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

            Player.Update(dt, WorldMap, SandSystem, LiquidSystem, worldInput, mouseWorld, WetnessField);
            mouseWorld = WorldWrapSystem.NormalizePlayerAndMouse(mouseWorld);
            InteriorFocusSystem.Update(dt, IsConstructionMode);
            TissueSystem.SetCorrectionPulseBoost(EnvironmentSystem.TissueCycleState.PulseBoost);
            TissueSystem.Update(dt, input);
            PowerSystem.Update(dt);
            BlockInteractionSystem.TryBreakTargetBlock(dt, worldInput, mouseWorld, SelectedHotbarIndex);
            EntityRuntimeSystem.Update(dt, screenWidth, screenHeight);
            LightingSystem.SetPointLights(TorchRuntimeSystem.GetLightSourcePositions());
            LightingSystem.Update(dt, Camera.Position, Camera.Zoom, screenWidth, screenHeight, EnvironmentSystem.SkyState.AmbientLight);
            BlockParticleSystem.Update(dt);

            CombatCoordinator.ResolveCombat();
        }

        private void AdvanceWorldTicks(float dt)
        {
            WorldTickCoordinator.Advance(dt);
        }

        private void AdvanceDayNightCycle(float dt)
        {
            DayNightCycle.Advance(dt, WorldTickTimeScale, WorldTicksPaused);
        }

        private void RefreshWorldEnvironment(float dt = 0f)
        {
            EnvironmentSystem.Update(dt, WorldTickTimeScale, WorldTicksPaused, DayNightCycle.CreateSnapshot());
        }

        public void DrawTerrainBase(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.DrawTerrainBase(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawWater(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.DrawWater(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawTerrainOverlay(SpriteBatch spriteBatch)
        {
            ViewCoordinator.DrawTerrainOverlay(
                spriteBatch,
                BlockInteractionSystem.HoveredTileBounds,
                BlockInteractionSystem.HoveredTileState);
        }

        public void DrawWetnessOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.DrawWetnessOverlay(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public void PrepareWorldLighting(GraphicsDevice graphicsDevice)
        {
            ViewCoordinator.PrepareWorldLighting(graphicsDevice, LightingSystem);
        }

        public void DrawWorldLighting(SpriteBatch spriteBatch, float worldOffsetX)
        {
            ViewCoordinator.DrawWorldLighting(spriteBatch, worldOffsetX);
        }

        public void PrepareTorchGlow(GraphicsDevice graphicsDevice)
        {
            ViewCoordinator.PrepareTorchGlow(graphicsDevice, LightingSystem);
        }

        public void DrawTorchGlow(SpriteBatch spriteBatch, float worldOffsetX)
        {
            ViewCoordinator.DrawTorchGlow(spriteBatch, worldOffsetX);
        }

        public void DrawTreeDecorations(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX, TreeRenderLayer layer)
        {
            ViewCoordinator.DrawTreeDecorations(spriteBatch, screenWidth, screenHeight, worldOffsetX, layer, EnvironmentSystem.SkyState.AmbientLight);
        }

        public void PrepareTerrainRender(GraphicsDevice graphicsDevice, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.PrepareTerrainRender(graphicsDevice, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawEntities(SpriteBatch spriteBatch)
        {
            ViewCoordinator.DrawEntities(spriteBatch, LightingSystem);
        }

        public void DrawTissueHalo(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            if (TissueVisualEnabled)
                ViewCoordinator.DrawTissueHalo(spriteBatch, screenWidth, screenHeight, worldOffsetX);

            ViewCoordinator.DrawTissueResonanceHalo(
                spriteBatch,
                screenWidth,
                screenHeight,
                worldOffsetX,
                TissueSystem.ResonanceState);
        }

        public void DrawTissueCore(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            if (TissueVisualEnabled)
                ViewCoordinator.DrawTissueCore(spriteBatch, screenWidth, screenHeight, worldOffsetX);

            ViewCoordinator.DrawTissueResonanceCore(
                spriteBatch,
                screenWidth,
                screenHeight,
                worldOffsetX,
                TissueSystem.ResonanceState);
        }

        public void DrawTissueFieldOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            if (TissueFieldVisualEnabled)
                ViewCoordinator.DrawTissueFieldOverlay(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawBackgroundWalls(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.DrawBackgroundWalls(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawLoopedWorldEntities(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.DrawLoopedWorldEntities(spriteBatch, screenWidth, screenHeight, worldOffsetX, LightingSystem);
        }

        public void DrawInteriorFocusOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight, float worldOffsetX)
        {
            ViewCoordinator.DrawInteriorFocusOverlay(spriteBatch, screenWidth, screenHeight, worldOffsetX);
        }

        public void DrawSky(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ViewCoordinator.DrawSky(spriteBatch, screenWidth, screenHeight, EnvironmentSystem.SkyState);
        }

        public void DrawNightOverlay(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            Color tint = EnvironmentSystem.SkyState.NightOverlayTint;

            // Soften the darkening while it's raining and the player has a roof over them -
            // WorldMap stays player-agnostic (IsWeatherAudioMuffled/HasOpenSkyAbove take any
            // tile), the player-awareness lives here at the draw call site instead.
            if (EnvironmentSystem.SkyState.RainIntensity > 0.05f)
            {
                Point playerTile = WorldMap.WorldToTile(Player.Position);
                if (WorldMap.IsWeatherAudioMuffled(playerTile.X, playerTile.Y))
                    tint *= 0.6f;
            }

            ViewCoordinator.DrawNightOverlay(spriteBatch, screenWidth, screenHeight, tint);
        }

        public void DrawRainFront(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ViewCoordinator.DrawRainFront(spriteBatch, screenWidth, screenHeight, EnvironmentSystem.SkyState);
        }

        public void DrawSunGlow(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ViewCoordinator.DrawSunGlow(spriteBatch, screenWidth, screenHeight, EnvironmentSystem.SkyState);
        }

        public void DrawMoons(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ViewCoordinator.DrawMoons(spriteBatch, screenWidth, screenHeight, EnvironmentSystem.SkyState);
        }

        public void DrawParallaxMountains(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ViewCoordinator.DrawParallaxMountains(spriteBatch, screenWidth, screenHeight, EnvironmentSystem.SkyState);
        }

        public void DrawTissueDebug(SpriteBatch spriteBatch)
        {
            TissueSystem.DrawDebug(spriteBatch);
        }

        public void DrawHud(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            ViewCoordinator.DrawHud(spriteBatch, Hotbar, SelectedHotbarIndex, screenWidth, screenHeight, WorldClockText24h);
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

        public int StoreItem(ItemId itemId, int quantity, bool preferInventory)
        {
            return WorldItemRuntimeSystem.StoreItem(itemId, quantity, preferInventory);
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
            LiquidSystem = new LiquidSystem(WorldMap)
            {
                SandSystem = SandSystem
            };
            SandSystem.LiquidSystem = LiquidSystem;
            BlockInteractionSystem.SandSystem = SandSystem;
            BlockInteractionSystem.LiquidSystem = LiquidSystem;
            ViewCoordinator.SandSystem = SandSystem;
            ViewCoordinator.LiquidSystem = LiquidSystem;
            WorldTickCoordinator.SandSystem = SandSystem;
            WorldTickCoordinator.LiquidSystem = LiquidSystem;
            EntityRuntimeSystem.SandSystem = SandSystem;
            LightingSystem.SandSystem = SandSystem;
        }

        private int ResolveWaterRadiusPixels(int radiusTiles)
        {
            int safeRadiusTiles = System.Math.Clamp(radiusTiles, 0, 16);
            return System.Math.Max(LiquidSystem.CellSize, safeRadiusTiles * WorldMap.TileSize);
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
