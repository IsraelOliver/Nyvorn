using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics;
using Nyvorn.Source.Gameplay.Combat;
using Nyvorn.Source.Gameplay.Combat.Weapons;
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionFactory
    {
        private const int EnemySpawnOffsetTilesFromPlayer = 8;

        private readonly GraphicsDevice graphicsDevice;
        private readonly ContentManager content;
        private readonly PlayerSaveService playerSaveService = new();

        public PlayingSessionFactory(GraphicsDevice graphicsDevice, ContentManager content)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
        }

        public sealed class BuildOperation
        {
            private readonly IReadOnlyList<BuildStep> steps;
            private int completedSteps;
            private Task runningTask;
            private Exception stepException;
            private int currentStepIndex;
            private readonly float totalWeight;

            public BuildOperation(IReadOnlyList<BuildStep> steps)
            {
                this.steps = steps;
                totalWeight = CalculateTotalWeight(steps);
                currentStepIndex = steps.Count > 0 ? 0 : -1;
            }

            public string StatusText
            {
                get
                {
                    if (IsCompleted)
                        return "Concluido";
                    if (currentStepIndex < 0 || currentStepIndex >= steps.Count)
                        return steps.Count > 0 ? steps[^1].Label : "Concluido";

                    BuildStep step = steps[currentStepIndex];
                    if (step.StatusProvider != null)
                    {
                        string status = step.StatusProvider();
                        if (!string.IsNullOrWhiteSpace(status))
                            return status;
                    }

                    return step.Label;
                }
            }
            public string CurrentPhaseLabel
            {
                get
                {
                    if (IsCompleted)
                        return "Concluido";
                    if (currentStepIndex < 0 || currentStepIndex >= steps.Count)
                        return steps.Count > 0 ? steps[^1].Label : "Concluido";

                    return steps[currentStepIndex].Label;
                }
            }
            public float Progress
            {
                get
                {
                    if (steps.Count == 0 || totalWeight <= 0f)
                        return 1f;

                    return Math.Clamp((GetCompletedWeight() + GetCurrentStepWeightProgress()) / totalWeight, 0f, 1f);
                }
            }
            public bool IsCompleted => completedSteps >= steps.Count;
            public bool IsBusy => runningTask != null;
            public PlayingSession Result { get; private set; }

            public void Advance()
            {
                if (IsCompleted)
                    return;
                if (stepException != null)
                    throw new InvalidOperationException($"Falha durante a etapa '{StatusText}'.", stepException);

                if (runningTask != null)
                {
                    if (!runningTask.IsCompleted)
                        return;

                    if (runningTask.IsFaulted)
                    {
                        stepException = runningTask.Exception?.GetBaseException() ?? runningTask.Exception;
                        runningTask = null;
                        throw new InvalidOperationException($"Falha durante a etapa '{StatusText}'.", stepException);
                    }

                    runningTask = null;
                    completedSteps++;
                    currentStepIndex = IsCompleted ? -1 : completedSteps;

                    return;
                }

                currentStepIndex = completedSteps;
                BuildStep step = steps[currentStepIndex];

                if (step.RunInBackground)
                {
                    runningTask = Task.Run(step.Action);
                    return;
                }

                step.Action();
                completedSteps++;
                currentStepIndex = IsCompleted ? -1 : completedSteps;
            }

            public void SetResult(PlayingSession session)
            {
                Result = session;
            }

            private float GetCompletedWeight()
            {
                float weight = 0f;
                for (int i = 0; i < completedSteps && i < steps.Count; i++)
                    weight += Math.Max(0f, steps[i].Weight);

                return weight;
            }

            private float GetCurrentStepWeightProgress()
            {
                if (currentStepIndex < 0 || currentStepIndex >= steps.Count)
                    return 0f;

                BuildStep step = steps[currentStepIndex];
                float weight = Math.Max(0f, step.Weight);
                float stepProgress = 0f;
                if (runningTask != null)
                {
                    stepProgress = step.ProgressProvider?.Invoke() ?? 0f;
                }
                else if (completedSteps > currentStepIndex)
                {
                    stepProgress = 1f;
                }

                return weight * Math.Clamp(stepProgress, 0f, 1f);
            }

            private static float CalculateTotalWeight(IReadOnlyList<BuildStep> steps)
            {
                float total = 0f;
                for (int i = 0; i < steps.Count; i++)
                    total += Math.Max(0f, steps[i].Weight);

                return total <= 0f ? 1f : total;
            }

            public sealed class BuildStep
            {
                public BuildStep(
                    string label,
                    Action action,
                    float weight = 1f,
                    bool runInBackground = false,
                    Func<float> progressProvider = null,
                    Func<string> statusProvider = null)
                {
                    Label = label;
                    Action = action;
                    Weight = weight;
                    RunInBackground = runInBackground;
                    ProgressProvider = progressProvider;
                    StatusProvider = statusProvider;
                }

                public string Label { get; }
                public Action Action { get; }
                public float Weight { get; }
                public bool RunInBackground { get; }
                public Func<float> ProgressProvider { get; }
                public Func<string> StatusProvider { get; }
            }
        }

        public PlayingSession Create()
        {
            string playerId = playerSaveService.CreatePlayer("Jogador").PlayerId;
            return Create(playerId, "Elyra", WorldSizePreset.Medium, WorldSeedSet.CreateRandom().SeedText);
        }

        public PlayingSession Create(string playerId, string planetName, WorldSizePreset sizePreset, string seedText)
        {
            return CompleteBuild(CreateBuildOperation(playerId, planetName, sizePreset, seedText));
        }

        public BuildOperation CreateBuildOperation(string playerId, string planetName, WorldSizePreset sizePreset, string seedText)
        {
            WorldGenConfig worldGenConfig = WorldGenConfig.CreatePreset(sizePreset, seedText);
            PlanetWorldMetadata planetMetadata = PlanetWorldMetadata.Create(planetName, worldGenConfig);
            return CreateBuildOperation(playerId, planetMetadata, saveData: null);
        }

        public BuildOperation CreateBuildOperation(string playerId, PlanetSaveData saveData)
        {
            if (saveData == null)
                return CreateBuildOperation(playerId, "Elyra", WorldSizePreset.Medium, WorldSeedSet.CreateRandom().SeedText);

            return CreateBuildOperationFromSaveData(playerId, saveData);
        }

        private PlayingSession CompleteBuild(BuildOperation operation)
        {
            while (!operation.IsCompleted)
                operation.Advance();

            return operation.Result;
        }

        private BuildOperation CreateBuildOperationFromSaveData(string playerId, PlanetSaveData saveData)
        {
            return CreateBuildOperation(playerId, saveData.Metadata, saveData);
        }

        private BuildOperation CreateBuildOperation(string playerId, PlanetWorldMetadata planetMetadata, PlanetSaveData saveData)
        {
            planetMetadata = planetMetadata.WithSeedMetadataDefaults();
            BuildContext build = new();
            build.PlayerId = playerId;
            build.SavedPlayerPositionX = saveData != null && saveData.Version >= 18 ? saveData.LastPlayerPositionX : null;
            build.SavedPlayerPositionY = saveData != null && saveData.Version >= 18 ? saveData.LastPlayerPositionY : null;
            build.SavedActivatedTissueHubKeys = saveData != null && saveData.Version >= 18 && saveData.ActivatedTissueHubKeys != null
                ? new List<int>(saveData.ActivatedTissueHubKeys)
                : null;
            build.ApplyGeneratedLiquidPlacements = saveData == null;
            build.ApplyGeneratedSandPlacements = saveData == null;
            bool hasWorldSnapshot = saveData?.WorldTileSnapshot != null && saveData.WorldTileSnapshot.Length > 0;
            build.SavedSandSnapshot = saveData?.SandSnapshot;
            build.SavedLiquidSnapshot = saveData != null && saveData.Version >= 15
                ? saveData.LiquidSnapshot
                : null;
            build.SavedTissueFieldDeltaSnapshot = saveData != null && saveData.Version >= 11
                ? saveData.TissueFieldDeltaSnapshot
                : null;
            build.SavedConsoleCommandHistory = saveData != null &&
                                                saveData.Version >= 12 &&
                                                saveData.ConsoleCommandHistory != null
                ? saveData.ConsoleCommandHistory
                    .Where(command => !string.IsNullOrWhiteSpace(command))
                    .Select(command => command.Trim())
                    .Select(command => command.StartsWith('/') ? command : "/" + command)
                    .TakeLast(100)
                    .ToList()
                : new List<string>();
            build.SavedTimeOfDay01 = saveData != null && saveData.Version >= 13
                ? saveData.TimeOfDay01
                : WorldDayNightCycle.DefaultStartTimeOfDay01;
            build.SavedCycleIndex = saveData != null && saveData.Version >= 14
                ? saveData.CycleIndex
                : 0;
            build.SavedWorldEnvironment = saveData != null && saveData.Version >= 14
                ? saveData.Environment
                : null;
            build.SavedBackgroundTileSnapshot = saveData != null && saveData.Version >= 10
                ? saveData.BackgroundTileSnapshot
                : null;
            build.SavedWorldItems = saveData != null && saveData.Version >= 5 && saveData.WorldItems != null
                ? new List<WorldItemSaveData>(saveData.WorldItems)
                : null;
            build.SavedWorkbenches = saveData != null && saveData.Version >= 8 && saveData.Workbenches != null
                ? new List<WorkbenchSaveData>(saveData.Workbenches)
                : null;
            build.SavedFurnaces = saveData != null && saveData.Version >= 17 && saveData.Furnaces != null
                ? new List<FurnaceSaveData>(saveData.Furnaces)
                : null;
            build.SavedTorches = saveData != null && saveData.Version >= 19 && saveData.Torches != null
                ? new List<TorchSaveData>(saveData.Torches)
                : null;
            build.SavedDoors = saveData != null && saveData.Version >= 9 && saveData.Doors != null
                ? new List<DoorSaveData>(saveData.Doors)
                : null;
            build.SavedChairs = saveData != null && saveData.Version >= 20 && saveData.Chairs != null
                ? new List<ChairSaveData>(saveData.Chairs)
                : null;
            build.SavedTables = saveData != null && saveData.Version >= 20 && saveData.Tables != null
                ? new List<TableSaveData>(saveData.Tables)
                : null;
            build.SavedPlatforms = saveData != null && saveData.Version >= 20 && saveData.Platforms != null
                ? new List<PlatformSaveData>(saveData.Platforms)
                : null;

            BuildOperation operation = null;
            List<BuildOperation.BuildStep> steps = new()
            {
                new BuildOperation.BuildStep("Carregando blocos e configuracoes", () => LoadWorldAssets(build, planetMetadata), weight: 5f),
                new BuildOperation.BuildStep("Carregando dados do jogador", () => LoadPlayerProgress(build, planetMetadata), weight: 1f)
            };

            if (hasWorldSnapshot)
            {
                steps.Add(new BuildOperation.BuildStep(
                    "Carregando snapshot do mundo",
                    () => LoadWorldSnapshot(build, saveData),
                    weight: 85f,
                    runInBackground: true));
            }
            else
            {
                IReadOnlyList<WorldGenPhaseDefinition> generationPasses = WorldGenerator.GetOrderedPasses();
                for (int i = 0; i < generationPasses.Count; i++)
                {
                    WorldGenPhaseDefinition generationPass = generationPasses[i];
                    if (saveData != null && generationPass.Name == "Hydrology")
                        continue;
                    if (saveData != null && generationPass.Name == "DesertPixelSand")
                        continue;
                    if (saveData != null && generationPass.Name == "DesertCave")
                        continue;

                    string generationPassName = generationPass.Name;
                    steps.Add(new BuildOperation.BuildStep(generationPass.Label, () =>
                    {
                        build.WorldGenerator.ApplyPassByName(build.GenerationContext, generationPassName);
                    },
                    weight: generationPass.Weight,
                    runInBackground: true,
                    progressProvider: () => build.GenerationProgress?.GetPhaseProgress(generationPassName) ?? 0f,
                    statusProvider: () => build.GenerationProgress?.GetPhaseMessage(generationPassName, generationPass.Label)));
                }
            }

            steps.Add(new BuildOperation.BuildStep("Preparando mundo para jogo", () =>
            {
                PrepareWorld(build, hasWorldSnapshot ? null : saveData?.TileChanges);
                if (build.GenerationContext?.TissueGeneration != null)
                {
                    build.TissueGeneration = build.GenerationContext.TissueGeneration;
                    build.TissueNetwork = build.TissueGeneration.Network;
                }
                if (saveData != null)
                    build.WorldMap.MarkPersisted();
            }, weight: 5f, runInBackground: true));

            steps.Add(new BuildOperation.BuildStep("Preparando tecido cosmico", () =>
            {
                if (build.TissueGeneration == null)
                {
                    build.TissueGeneration = new TissueGenerator(SeedHash.ToIntSeed(build.WorldGenConfig.SeedSet.TissueSeed)).Generate(build.WorldMap);
                    build.TissueNetwork = build.TissueGeneration.Network;
                }

                TissueField generatedField = build.TissueGeneration.RasterizedField;
                if (!ReferenceEquals(build.WorldMap.TissueField, generatedField))
                    build.WorldMap.SetTissueField(generatedField);

                if (build.GenerationContext != null)
                    build.GenerationContext.TissueField = generatedField;

                if (build.SavedTissueFieldDeltaSnapshot != null && build.SavedTissueFieldDeltaSnapshot.Length > 0)
                {
                    TissueFieldDeltaCodec.Import(
                        build.SavedTissueFieldDeltaSnapshot,
                        generatedField,
                        build.WorldMap,
                        SeedHash.ToIntSeed(build.WorldGenConfig.SeedSet.TissueSeed),
                        build.TissueGeneration.Stats.DeterministicHash,
                        TissueGenerator.AlgorithmVersion);
                }

                generatedField.MarkPersisted();
            }, weight: 8f, runInBackground: true));

            steps.Add(new BuildOperation.BuildStep("Carregando entidades e interface", () => LoadGameplayAssets(build), weight: 4f));
            steps.Add(new BuildOperation.BuildStep("Posicionando spawns e finalizando sessao", () => operation.SetResult(CreateSession(build, planetMetadata)), weight: 1f));

            operation = new BuildOperation(steps);

            return operation;
        }

        private void LoadWorldAssets(BuildContext build, PlanetWorldMetadata planetMetadata)
        {
            build.DirtTexture = content.Load<Texture2D>("tiles/dirt_spritesheet");
            build.GrassTexture = content.Load<Texture2D>("tiles/grass_spritesheet");
            build.SandTexture = content.Load<Texture2D>("tiles/sand_spritesheet");
            build.StoneTexture = content.Load<Texture2D>("tiles/stone_spritesheet");
            build.WoodTexture = content.Load<Texture2D>("tiles/wood_spritesheet");
            build.IronOreTexture = content.Load<Texture2D>("tiles/iron-ore_spritesheet");
            build.TreeTexture = content.Load<Texture2D>("trees/tree_modular_spritesheet");
            build.MushroomTexture = content.Load<Texture2D>("trees/mushroom");

            build.WorldGenConfig = WorldGenConfig.CreatePreset(planetMetadata.SizePreset, planetMetadata.CreateSeedSet());
            build.WorldMap = new WorldMap(build.WorldGenConfig.WorldWidth, build.WorldGenConfig.WorldHeight, build.WorldGenConfig.TileSize);
            build.WorldMap.SetTextures(build.DirtTexture, build.GrassTexture, build.SandTexture, build.StoneTexture, build.WoodTexture, build.IronOreTexture);
            build.WorldMap.SetTreeTexture(build.TreeTexture);
            build.WorldMap.SetSurfaceDecorationTexture(build.MushroomTexture);
            build.WorldGenerator = new WorldGenerator();
            build.GenerationContext = build.WorldGenerator.CreateGenerationContext(build.WorldMap, build.WorldGenConfig);
            build.GenerationProgress = new WorldGenProgressReporter(WorldGenerator.GetOrderedPasses());
            build.GenerationContext.ProgressReporter = build.GenerationProgress;
        }

        private static void PrepareWorld(BuildContext build, IReadOnlyCollection<WorldTileChange> tileChanges)
        {
            int worldCenterTileX = build.WorldMap.Width / 2;
            build.PlayerSpawnTileX = worldCenterTileX;
            build.ItemSpawnTileX = WrapTileX(build.PlayerSpawnTileX + 5, build.WorldMap.Width);

            build.WorldMap.ResetTrackedTileChanges();
            build.WorldMap.ApplyPersistentTileChanges(tileChanges);
            build.WorldMap.BeginTileChangeTracking();
        }

        private void LoadPlayerProgress(BuildContext build, PlanetWorldMetadata planetMetadata)
        {
            build.PlayerSaveData = playerSaveService.Load(build.PlayerId);
        }

        private static void LoadWorldSnapshot(BuildContext build, PlanetSaveData saveData)
        {
            build.WorldMap.ImportTileSnapshot(saveData.WorldTileSnapshot);
            build.WorldMap.ImportBackgroundTileSnapshot(build.SavedBackgroundTileSnapshot);
            RestoreTrees(build, saveData.Trees);
            RestoreSurfaceDecorations(build, saveData.SurfaceDecorations);
        }

        private static int WrapTileX(int tileX, int worldWidth)
        {
            if (worldWidth <= 0)
                return 0;

            int wrapped = tileX % worldWidth;
            return wrapped < 0 ? wrapped + worldWidth : wrapped;
        }

        private static void RestoreTrees(BuildContext build, IReadOnlyList<TreeSaveData> savedTrees)
        {
            if (savedTrees == null || savedTrees.Count == 0)
                return;

            List<TreeInstance> trees = new(savedTrees.Count);
            for (int i = 0; i < savedTrees.Count; i++)
            {
                TreeSaveData savedTree = savedTrees[i];
                if (savedTree != null)
                    trees.Add(MigrateLegacyTreeBase(build.WorldMap, savedTree, savedTree.ToTree()));
            }

            build.WorldMap.SetTrees(trees);
        }

        private static void RestoreSurfaceDecorations(BuildContext build, IReadOnlyList<SurfaceDecorationSaveData> savedDecorations)
        {
            if (savedDecorations == null || savedDecorations.Count == 0)
                return;

            List<SurfaceDecorationInstance> decorations = new(savedDecorations.Count);
            for (int i = 0; i < savedDecorations.Count; i++)
            {
                if (savedDecorations[i] != null)
                    decorations.Add(savedDecorations[i].ToDecoration());
            }

            build.WorldMap.SetSurfaceDecorations(decorations);
        }

        private static TreeInstance MigrateLegacyTreeBase(WorldMap worldMap, TreeSaveData savedTree, TreeInstance tree)
        {
            bool hasLegacyCanopyOffset = savedTree.Canopy == null ||
                (savedTree.Canopy.OffsetX == -2 && savedTree.Canopy.OffsetY == -tree.Height - 2);
            if (!hasLegacyCanopyOffset)
                return tree;

            if (worldMap.GetTile(tree.BaseTile.X, tree.BaseTile.Y) != TileType.Grass)
                return tree;

            int migratedBaseY = tree.BaseTile.Y - 1;
            if (!worldMap.InBounds(tree.BaseTile.X, migratedBaseY) || worldMap.IsSolidAt(tree.BaseTile.X, migratedBaseY))
                return tree;

            return new TreeInstance
            {
                BaseTile = new Point(tree.BaseTile.X, migratedBaseY),
                Height = tree.Height,
                Variant = tree.Variant,
                RootStyleRow = tree.RootStyleRow,
                BranchHeight = tree.BranchHeight,
                BranchDirection = tree.BranchDirection,
                Seed = tree.Seed,
                Parts = tree.Parts,
                Canopy = tree.Canopy.PartType == TreePartType.Canopy
                    ? new TreePartPlacement(TreePartType.Canopy, new Point(-2, -tree.Height - 4))
                    : tree.Canopy
            };
        }

        private void LoadGameplayAssets(BuildContext build)
        {
            build.PlayerDownTexture = content.Load<Texture2D>("entities/player/playerDown_sheet");
            build.PlayerUpTexture = content.Load<Texture2D>("entities/player/playerUp_sheet");
            build.PlayerPickaxeMovesetTexture = content.Load<Texture2D>(
                "entities/player/movesets/player_moveset_pickaxe-Sheet");
            build.WorkbenchTexture = content.Load<Texture2D>("objects/worktable-sheet");
            build.FurnaceTexture = content.Load<Texture2D>("objects/furnace-Sheet");
            build.TorchPoleTexture = content.Load<Texture2D>("furniture/torch-Sheet");
            build.TorchFlameTexture = content.Load<Texture2D>("furniture/torch-animation-Sheet-Sheet");
            build.DoorTexture = content.Load<Texture2D>("objects/wood_door");
            build.ChairTexture = content.Load<Texture2D>("furniture/wood_chair");
            build.TableTexture = content.Load<Texture2D>("furniture/wood_table");
            build.PlatformTexture = content.Load<Texture2D>("tiles/wood_platform");
            build.ToolbarTexture = content.Load<Texture2D>("ui/toolbar");
            build.TissueRevealIconTexture = content.Load<Texture2D>("ui/tissue_reveal-Sheet");
            build.BackgroundFarTexture = content.Load<Texture2D>("ui/background_parallax/background1");
            build.BackgroundMidTexture = content.Load<Texture2D>("ui/background_parallax/background2");
            build.BackgroundNearTexture = content.Load<Texture2D>("ui/background_parallax/background3");
            build.LifeBarTexture = content.Load<Texture2D>("ui/lifebar");
            build.UiFont = content.Load<SpriteFont>("ui/UIFont");
            build.EnemyTexture = content.Load<Texture2D>("entities/enemy/enemy_test");
            build.SunRaysEffect = content.Load<Effect>("effects/SunRays");
            build.MoonPhaseEffect = content.Load<Effect>("effects/MoonPhase");
            build.ComposeLightingEffect = content.Load<Effect>("effects/ComposeLighting");

            build.ItemTextures = LoadItemTextures();
            build.Weapons = CreateWeapons(build.ItemTextures, build.PlayerPickaxeMovesetTexture);
        }

        private Dictionary<ItemId, Texture2D> LoadItemTextures()
        {
            Dictionary<ItemId, Texture2D> itemTextures = new();
            foreach (ItemDefinition definition in ItemDefinitions.GetAll())
                itemTextures[definition.Id] = content.Load<Texture2D>(definition.TexturePath);

            return itemTextures;
        }

        private Dictionary<ItemId, Weapon> CreateWeapons(
            IReadOnlyDictionary<ItemId, Texture2D> itemTextures,
            Texture2D playerPickaxeMovesetTexture)
        {
            Texture2D nullWeaponTexture = new Texture2D(graphicsDevice, 1, 1);
            nullWeaponTexture.SetData(new[] { Color.Transparent });

            Dictionary<ItemId, Weapon> weapons = new()
            {
                [ItemId.None] = new HandWeapon(nullWeaponTexture)
            };

            foreach (ItemDefinition definition in ItemDefinitions.GetAll())
            {
                if (definition.EquipmentKind == EquipmentKind.None)
                    continue;

                if (!itemTextures.TryGetValue(definition.Id, out Texture2D texture))
                    throw new System.InvalidOperationException($"Missing texture for equipment item '{definition.Id}'.");

                weapons[definition.Id] = definition.EquipmentKind switch
                {
                    EquipmentKind.Pickaxe => new Pickaxe(
                        texture,
                        playerPickaxeMovesetTexture,
                        miningPower: definition.MiningPower.Value,
                        miningSpeed: definition.MiningSpeed.Value,
                        powerTier: definition.PowerTier.Value,
                        hitDamage: definition.HitDamage.Value,
                        hitKnockbackX: definition.HitKnockbackX.Value,
                        hitKnockbackY: definition.HitKnockbackY.Value,
                        useSpeed: definition.UseSpeed),

                    EquipmentKind.Axe => new Axe(
                        texture,
                        playerPickaxeMovesetTexture,
                        powerTier: definition.PowerTier.Value,
                        hitDamage: definition.HitDamage.Value,
                        hitKnockbackX: definition.HitKnockbackX.Value,
                        hitKnockbackY: definition.HitKnockbackY.Value,
                        useSpeed: definition.UseSpeed ?? 1f),

                    _ => throw new System.InvalidOperationException($"Unsupported equipment kind '{definition.EquipmentKind}' for item '{definition.Id}'.")
                };
            }

            return weapons;
        }

        private PlayingSession CreateSession(BuildContext build, PlanetWorldMetadata planetMetadata)
        {
            Vector2 defaultPlayerSpawn = build.WorldGenerator.GetSurfaceSpawnPosition(
                build.WorldMap,
                build.PlayerSpawnTileX,
                tilesAboveSurface: 2);
            Vector2 playerSpawn = ResolvePlayerSpawn(build, defaultPlayerSpawn);
            Vector2 pickaxeSpawn = build.WorldGenerator.GetLayerSpawnPosition(
                build.WorldMap,
                build.WorldGenConfig,
                WorldLayerType.DeepCavern,
                build.ItemSpawnTileX,
                tilesAboveGround: 2);

            Player player = new(
                playerSpawn,
                build.PlayerDownTexture,
                build.PlayerUpTexture,
                build.PlayerConfig);
            ApplyPlayerHealth(build.PlayerSaveData, player);

            List<Enemy> enemies = new();
            EnemyRespawnController enemyRespawnController = new(
                build.EnemyTexture,
                () => ResolveEnemySpawnNearPlayer(build.WorldMap, player.Position),
                build.EnemyConfig,
                spawningEnabled: false);

            Hotbar hotbar = new(9);
            Inventory inventory = new(10);
            int selectedHotbarIndex = 0;
            ApplyPlayerInventory(build.PlayerSaveData, hotbar, inventory, ref selectedHotbarIndex);
            GiveAndEquipStarterWoodAxe(hotbar, inventory, ref selectedHotbarIndex);
            List<WorldItem> worldItems = CreateWorldItems(build, pickaxeSpawn);
            Camera2D camera = CreateCamera();
            SessionRuntimeContext runtimeContext = new SessionRuntimeContext
            {
                WorldMap = build.WorldMap,
                Player = player,
                Enemies = enemies,
                WorldItems = worldItems,
                Hotbar = hotbar,
                Inventory = inventory,
                Camera = camera
            };
            WorldItemRuntimeSystem worldItemRuntimeSystem = new WorldItemRuntimeSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                WorldItems = worldItems,
                Hotbar = hotbar,
                Inventory = inventory,
                ItemTextures = build.ItemTextures
            };
            PlayingSessionEntityRuntimeSystem entityRuntimeSystem = new PlayingSessionEntityRuntimeSystem
            {
                RuntimeContext = runtimeContext,
                WorldItemRuntimeSystem = worldItemRuntimeSystem,
                EnemyRespawnController = enemyRespawnController
            };
            TissueNetwork tissueNetwork = build.TissueNetwork ?? CreateEmptyTissueNetwork(build.WorldMap, SeedHash.ToIntSeed(build.WorldGenConfig.SeedSet.TissueSeed));
            if (build.TissueGeneration == null ||
                !ReferenceEquals(build.WorldMap.TissueField, build.TissueGeneration.RasterizedField))
            {
                throw new InvalidOperationException("TissueField da sessao nao corresponde ao campo gerado.");
            }
            ITissueQueryService tissueQueries = new TissueQueryService(
                build.WorldMap,
                build.WorldMap.TissueField,
                tissueNetwork);
            ITissueMutationService tissueMutations = new TissueMutationService(
                build.WorldMap,
                tissueQueries);
            ITissuePropagationService tissuePropagation = new TissuePropagationService(
                build.WorldMap,
                build.WorldMap.TissueField,
                tissueNetwork);
            TissueEnvironmentSensor tissueEnvironmentSensor = new(build.WorldMap, tissueQueries);
            TissueResonanceController tissueResonanceController = new(
                tissueQueries,
                tissuePropagation,
                tissueEnvironmentSensor);
            HashSet<int> activatedTissueHubKeys = CreateActivatedTissueHubSet(build.SavedActivatedTissueHubKeys);
            PlayingSessionTissueSystem tissueSystem = new PlayingSessionTissueSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                TissueNetwork = tissueNetwork,
                TissueQueries = tissueQueries,
                EnvironmentSensor = tissueEnvironmentSensor,
                ResonanceController = tissueResonanceController,
                TissueRevealController = new TissueRevealController(build.WorldMap.TileSize * 28f, fadeDuration: 0.16f, activeDuration: 4.2f),
                TissueDebugRenderer = new TissueFieldDebugRenderer(graphicsDevice),
                ActivatedTissueHubKeys = activatedTissueHubKeys
            };
            PlayerPowerSystem powerSystem = new PlayerPowerSystem();
            powerSystem.AddPower(new TissueRevealPower(tissueSystem, build.TissueRevealIconTexture));
            PlayingSessionBlockInteractionSystem blockInteractionSystem = new PlayingSessionBlockInteractionSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                Hotbar = hotbar,
                WorldItemRuntimeSystem = worldItemRuntimeSystem
            };
            WorkbenchRuntimeSystem workbenchRuntimeSystem = new WorkbenchRuntimeSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                Hotbar = hotbar,
                Texture = build.WorkbenchTexture
            };
            workbenchRuntimeSystem.Restore(build.SavedWorkbenches);
            FurnaceRuntimeSystem furnaceRuntimeSystem = new FurnaceRuntimeSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                Hotbar = hotbar,
                Texture = build.FurnaceTexture
            };
            furnaceRuntimeSystem.Restore(build.SavedFurnaces);
            TorchRuntimeSystem torchRuntimeSystem = new TorchRuntimeSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                Hotbar = hotbar,
                PoleTexture = build.TorchPoleTexture,
                FlameTexture = build.TorchFlameTexture
            };
            torchRuntimeSystem.Restore(build.SavedTorches);
            DoorRuntimeSystem doorRuntimeSystem = new DoorRuntimeSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                Hotbar = hotbar,
                Texture = build.DoorTexture
            };
            doorRuntimeSystem.Restore(build.SavedDoors);
            ChairRuntimeSystem chairRuntimeSystem = new ChairRuntimeSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                Hotbar = hotbar,
                Texture = build.ChairTexture
            };
            chairRuntimeSystem.Restore(build.SavedChairs);
            TableRuntimeSystem tableRuntimeSystem = new TableRuntimeSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                Hotbar = hotbar,
                Texture = build.TableTexture
            };
            tableRuntimeSystem.Restore(build.SavedTables);
            PlatformRuntimeSystem platformRuntimeSystem = new PlatformRuntimeSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                Hotbar = hotbar,
                Texture = build.PlatformTexture
            };
            platformRuntimeSystem.Restore(build.SavedPlatforms);
            WorldObjectRegistry worldObjectRegistry = new();
            worldObjectRegistry.Register(workbenchRuntimeSystem);
            worldObjectRegistry.Register(furnaceRuntimeSystem);
            worldObjectRegistry.Register(torchRuntimeSystem);
            worldObjectRegistry.Register(doorRuntimeSystem);
            worldObjectRegistry.Register(chairRuntimeSystem);
            worldObjectRegistry.Register(tableRuntimeSystem);
            worldObjectRegistry.Register(platformRuntimeSystem);
            blockInteractionSystem.WorldObjectRegistry = worldObjectRegistry;
            build.WorldMap.SetObjectCollisionQueries(
                worldObjectRegistry.IsObjectOccupyingTile,
                worldObjectRegistry.IsMovementBlockingTile);
            FurnitureCollisionSystem furnitureCollisionSystem = new();
            furnitureCollisionSystem.Register(tableRuntimeSystem);
            furnitureCollisionSystem.Register(platformRuntimeSystem);
            InteriorFocusSystem interiorFocusSystem = new InteriorFocusSystem
            {
                WorldMap = build.WorldMap,
                Player = player,
                DoorRuntimeSystem = doorRuntimeSystem
            };
            BlockParticleSystem blockParticleSystem = new BlockParticleSystem
            {
                WorldMap = build.WorldMap
            };
            blockInteractionSystem.BlockParticleSystem = blockParticleSystem;
            DamageNumberSystem damageNumberSystem = new DamageNumberSystem();
            PlayingSessionInputRouter inputRouter = new PlayingSessionInputRouter
            {
                Hotbar = hotbar
            };
            PlayingSessionWorldWrapSystem worldWrapSystem = new PlayingSessionWorldWrapSystem
            {
                RuntimeContext = runtimeContext
            };
            PlayingSessionViewCoordinator viewCoordinator = new PlayingSessionViewCoordinator
            {
                WorldMap = build.WorldMap,
                Player = player,
                Enemies = enemies,
                WorldItems = worldItems,
                Camera = runtimeContext.Camera,
                DebugPixel = CreateDebugPixelTexture(),
                HealthBarRenderer = new WorldHealthBarRenderer(graphicsDevice),
                HudRenderer = new HudRenderer(graphicsDevice, build.ToolbarTexture, build.LifeBarTexture, build.UiFont, build.ItemTextures),
                WorldMinimapRenderer = new WorldMinimapRenderer(graphicsDevice),
                ElyraSkyRenderer = new ElyraSkyRenderer(
                    graphicsDevice,
                    build.SunRaysEffect,
                    build.MoonPhaseEffect,
                    build.BackgroundFarTexture,
                    build.BackgroundMidTexture,
                    build.BackgroundNearTexture),
                ComposeLightingEffect = build.ComposeLightingEffect,
                TilePreviewRenderer = new WorldTilePreviewRenderer(graphicsDevice),
                PowerHUD = new PowerHUD(graphicsDevice, build.UiFont),
                TissueNetwork = tissueNetwork,
                TissueNetworkRenderer = new TissueNetworkRenderer(graphicsDevice),
                TissueFieldOverlayRenderer = new TissueFieldOverlayRenderer(graphicsDevice),
                ActivatedTissueHubKeys = activatedTissueHubKeys,
                InteriorFocusSystem = interiorFocusSystem,
                BlockParticleSystem = blockParticleSystem,
                DamageNumberSystem = damageNumberSystem,
                WorkbenchRuntimeSystem = workbenchRuntimeSystem,
                FurnaceRuntimeSystem = furnaceRuntimeSystem,
                TorchRuntimeSystem = torchRuntimeSystem,
                DoorRuntimeSystem = doorRuntimeSystem,
                ChairRuntimeSystem = chairRuntimeSystem,
                TableRuntimeSystem = tableRuntimeSystem,
                PlatformRuntimeSystem = platformRuntimeSystem,
                WorldObjectRegistry = worldObjectRegistry,
                FurnitureCollisionSystem = furnitureCollisionSystem
            };
            WorldDayNightCycle dayNightCycle = new(build.SavedTimeOfDay01, cycleIndex: build.SavedCycleIndex);
            WorldEnvironmentSystem environmentSystem = new(
                SeedHash.ToIntSeed(build.WorldGenConfig.SeedSet.EnvironmentSeed),
                build.SavedWorldEnvironment,
                dayNightCycle.CreateSnapshot());
            entityRuntimeSystem.EnvironmentSystem = environmentSystem;
            entityRuntimeSystem.DayNightCycle = dayNightCycle;
            entityRuntimeSystem.NightSurfaceSpawnSystem = new NightSurfaceSpawnSystem(build.EnemyTexture, build.EnemyConfig);
            PlayingSessionWorldTickCoordinator worldTickCoordinator = new PlayingSessionWorldTickCoordinator
            {
                WorldMap = build.WorldMap,
                ViewCoordinator = viewCoordinator,
                WorldTickSystem = new WorldTickSystem(),
                EnvironmentSystem = environmentSystem,
                DayNightCycle = dayNightCycle,
                WetnessField = new TileWetnessField(build.WorldMap)
            };
            build.WorldMap.SetWetnessField(worldTickCoordinator.WetnessField);

            PlayingSessionCombatCoordinator combatCoordinator = new PlayingSessionCombatCoordinator
            {
                RuntimeContext = runtimeContext,
                Weapons = build.Weapons,
                CombatSystem = new CombatSystem(damageNumberSystem)
            };

            PlayingSession session = new PlayingSession
            {
                PlanetMetadata = planetMetadata,
                PlayerId = build.PlayerId,
                RuntimeContext = runtimeContext,
                WorldItemRuntimeSystem = worldItemRuntimeSystem,
                EntityRuntimeSystem = entityRuntimeSystem,
                BlockInteractionSystem = blockInteractionSystem,
                ViewCoordinator = viewCoordinator,
                TissueSystem = tissueSystem,
                TissueQueries = tissueQueries,
                TissueMutations = tissueMutations,
                TissuePropagation = tissuePropagation,
                CosmicTissueGeneration = build.TissueGeneration,
                InputRouter = inputRouter,
                WorldWrapSystem = worldWrapSystem,
                WorldTickCoordinator = worldTickCoordinator,
                DayNightCycle = dayNightCycle,
                EnvironmentSystem = environmentSystem,
                CombatCoordinator = combatCoordinator,
                WorkbenchRuntimeSystem = workbenchRuntimeSystem,
                FurnaceRuntimeSystem = furnaceRuntimeSystem,
                TorchRuntimeSystem = torchRuntimeSystem,
                DoorRuntimeSystem = doorRuntimeSystem,
                ChairRuntimeSystem = chairRuntimeSystem,
                TableRuntimeSystem = tableRuntimeSystem,
                PlatformRuntimeSystem = platformRuntimeSystem,
                InteriorFocusSystem = interiorFocusSystem,
                BlockParticleSystem = blockParticleSystem,
                DamageNumberSystem = damageNumberSystem,
                PowerSystem = powerSystem,
                ConsoleCommandHistory = build.SavedConsoleCommandHistory
            };

            session.InitializeSandSystem();
            if (build.SavedSandSnapshot != null && build.SavedSandSnapshot.Length > 0)
                session.SandSystem.ImportSnapshot(build.SavedSandSnapshot);
            else if (build.ApplyGeneratedSandPlacements)
                ApplyGeneratedSandPlacements(session, build);
            if (build.SavedLiquidSnapshot != null && build.SavedLiquidSnapshot.Length > 0)
                session.LiquidSystem.ImportSnapshot(build.SavedLiquidSnapshot);
            else if (build.ApplyGeneratedLiquidPlacements)
                ApplyGeneratedLiquidPlacements(session, build);

            session.SetSelectedHotbarIndex(selectedHotbarIndex);
            session.InitializeRuntimeState();

            // P2-E-BG1: Calculate and set layer definitions (derived from world config)
            WorldLayerDefinition[] layerDefinitions = WorldGenerator.BuildLayerDefinitions(build.WorldMap, build.WorldGenConfig);
            session.SetLayerDefinitions(layerDefinitions);

            PrewarmVisibleTerrainChunks(session);
            return session;
        }

        private static void ApplyGeneratedLiquidPlacements(PlayingSession session, BuildContext build)
        {
            if (session?.LiquidSystem == null || build?.GenerationContext?.LiquidPlacements == null)
                return;

            IReadOnlyList<WorldGenLiquidPlacement> placements = build.GenerationContext.LiquidPlacements;
            for (int i = 0; i < placements.Count; i++)
            {
                WorldGenLiquidPlacement placement = placements[i];
                session.LiquidSystem.SetSettledLiquid(placement.X, placement.Y, placement.Type, placement.Amount);
            }

            session.LiquidSystem.SettleAllLiquids();
        }

        private static void ApplyGeneratedSandPlacements(PlayingSession session, BuildContext build)
        {
            if (session?.SandSystem == null || build?.GenerationContext?.PixelSandPlacements == null)
                return;

            int tileSize = build.WorldMap.TileSize;
            IReadOnlyList<WorldGenPixelSandPlacement> placements = build.GenerationContext.PixelSandPlacements;
            for (int i = 0; i < placements.Count; i++)
            {
                WorldGenPixelSandPlacement placement = placements[i];
                session.SandSystem.AddSettledSandRectangle(
                    placement.TileX * tileSize,
                    placement.TileY * tileSize,
                    placement.WidthTiles * tileSize,
                    placement.HeightTiles * tileSize);
            }
        }

        private Texture2D CreateDebugPixelTexture()
        {
            Texture2D debugPixel = new Texture2D(graphicsDevice, 1, 1);
            debugPixel.SetData(new[] { Color.White });
            return debugPixel;
        }

        private static void GiveAndEquipStarterWoodAxe(Hotbar hotbar, Inventory inventory, ref int selectedHotbarIndex)
        {
            if (hotbar == null || inventory == null)
                return;

            if (TryFindItemSlot(hotbar, ItemId.WoodAxe, out int hotbarIndex))
            {
                selectedHotbarIndex = hotbarIndex;
                return;
            }

            if (TryFindItemSlot(inventory, ItemId.WoodAxe, out int inventoryIndex) &&
                TryMoveInventorySlotToHotbar(inventory, inventoryIndex, hotbar, selectedHotbarIndex, out int movedHotbarIndex))
            {
                selectedHotbarIndex = movedHotbarIndex;
                return;
            }

            ItemDefinition axeDefinition = ItemDefinitions.Get(ItemId.WoodAxe);
            if (TryPlaceItemInHotbar(axeDefinition, hotbar, inventory, selectedHotbarIndex, out int placedHotbarIndex))
                selectedHotbarIndex = placedHotbarIndex;
            else
                inventory.TryAdd(axeDefinition, 1);
        }

        private static bool TryFindItemSlot(Inventory inventory, ItemId itemId, out int slotIndex)
        {
            for (int i = 0; i < inventory.Capacity; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (!slot.IsEmpty && slot.ItemId == itemId)
                {
                    slotIndex = i;
                    return true;
                }
            }

            slotIndex = -1;
            return false;
        }

        private static bool TryFindEmptySlot(Inventory inventory, out int slotIndex)
        {
            for (int i = 0; i < inventory.Capacity; i++)
            {
                if (inventory.GetSlot(i).IsEmpty)
                {
                    slotIndex = i;
                    return true;
                }
            }

            slotIndex = -1;
            return false;
        }

        private static bool TryMoveInventorySlotToHotbar(
            Inventory inventory,
            int inventoryIndex,
            Hotbar hotbar,
            int preferredHotbarIndex,
            out int hotbarIndex)
        {
            InventorySlot sourceSlot = inventory.GetSlot(inventoryIndex);
            if (sourceSlot.IsEmpty)
            {
                hotbarIndex = -1;
                return false;
            }

            if (!TryResolveHotbarTarget(hotbar, inventory, preferredHotbarIndex, out hotbarIndex))
                return false;

            hotbar.GetSlot(hotbarIndex).CopyFrom(sourceSlot);
            sourceSlot.Clear();
            return true;
        }

        private static bool TryPlaceItemInHotbar(
            ItemDefinition definition,
            Hotbar hotbar,
            Inventory inventory,
            int preferredHotbarIndex,
            out int hotbarIndex)
        {
            if (!TryResolveHotbarTarget(hotbar, inventory, preferredHotbarIndex, out hotbarIndex))
                return false;

            hotbar.GetSlot(hotbarIndex).Set(definition.Id, 1);
            return true;
        }

        private static bool TryResolveHotbarTarget(
            Hotbar hotbar,
            Inventory inventory,
            int preferredHotbarIndex,
            out int hotbarIndex)
        {
            int preferredIndex = Math.Clamp(preferredHotbarIndex, 0, hotbar.Capacity - 1);
            if (hotbar.GetSlot(preferredIndex).IsEmpty)
            {
                hotbarIndex = preferredIndex;
                return true;
            }

            if (TryFindEmptySlot(hotbar, out hotbarIndex))
                return true;

            InventorySlot preferredSlot = hotbar.GetSlot(preferredIndex);
            if (TryMoveSlotToInventory(preferredSlot, inventory))
            {
                hotbarIndex = preferredIndex;
                return true;
            }

            hotbarIndex = -1;
            return false;
        }

        private static bool TryMoveSlotToInventory(InventorySlot sourceSlot, Inventory inventory)
        {
            if (sourceSlot == null || sourceSlot.IsEmpty)
                return true;

            if (!ItemDefinitions.TryGet(sourceSlot.ItemId, out ItemDefinition definition))
                return false;

            if (!inventory.TryAdd(definition, sourceSlot.Quantity))
                return false;

            sourceSlot.Clear();
            return true;
        }

        private static Vector2 ResolvePlayerSpawn(BuildContext build, Vector2 fallbackPosition)
        {
            if (build.SavedPlayerPositionX == null || build.SavedPlayerPositionY == null)
                return fallbackPosition;

            float x = build.SavedPlayerPositionX.Value;
            float y = build.SavedPlayerPositionY.Value;
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
                return fallbackPosition;

            float worldWidth = build.WorldMap.PixelWidth;
            if (worldWidth > 0f)
            {
                x %= worldWidth;
                if (x < 0f)
                    x += worldWidth;
            }

            float maxY = build.WorldMap.Height * build.WorldMap.TileSize;
            y = Math.Clamp(y, build.WorldMap.TileSize, Math.Max(build.WorldMap.TileSize, maxY));
            return new Vector2(x, y);
        }

        private static void ApplyPlayerInventory(PlayerSaveData playerSaveData, Hotbar hotbar, Inventory inventory, ref int selectedHotbarIndex)
        {
            if (playerSaveData == null)
                return;

            RestoreInventory(hotbar, playerSaveData.HotbarSlots);
            RestoreInventory(inventory, playerSaveData.InventorySlots);
            selectedHotbarIndex = Math.Clamp(playerSaveData.SelectedHotbarIndex, 0, hotbar.Capacity - 1);
        }

        private static void ApplyPlayerHealth(PlayerSaveData playerSaveData, Player player)
        {
            if (playerSaveData == null || playerSaveData.CurrentHealth < 0)
                return;

            player.SetHealth(playerSaveData.CurrentHealth);
        }

        private static HashSet<int> CreateActivatedTissueHubSet(List<int> savedActivatedTissueHubKeys)
        {
            HashSet<int> activatedHubKeys = new();

            if (savedActivatedTissueHubKeys == null)
                return activatedHubKeys;

            for (int i = 0; i < savedActivatedTissueHubKeys.Count; i++)
                activatedHubKeys.Add(savedActivatedTissueHubKeys[i]);

            return activatedHubKeys;
        }

        private static void RestoreInventory(Inventory inventory, IReadOnlyList<PlayerInventorySlotSaveData> slotData)
        {
            if (inventory == null || slotData == null)
                return;

            for (int i = 0; i < slotData.Count; i++)
            {
                PlayerInventorySlotSaveData entry = slotData[i];
                if (entry == null || entry.SlotIndex < 0 || entry.SlotIndex >= inventory.Capacity)
                    continue;
                if (!ItemDefinitions.TryGet(entry.ItemId, out ItemDefinition definition))
                    continue;

                int quantity = definition.Stackable
                    ? Math.Clamp(entry.Quantity, 1, definition.MaxStack)
                    : 1;

                inventory.GetSlot(entry.SlotIndex).Set(entry.ItemId, quantity);
            }
        }

        private List<WorldItem> CreateWorldItems(BuildContext build, Vector2 pickaxeSpawn)
        {
            if (build.SavedWorldItems != null)
            {
                List<WorldItem> restoredItems = new();
                for (int i = 0; i < build.SavedWorldItems.Count; i++)
                {
                    WorldItemSaveData itemSave = build.SavedWorldItems[i];
                    if (!ItemDefinitions.TryGet(itemSave.ItemId, out ItemDefinition definition))
                        continue;
                    if (!build.ItemTextures.TryGetValue(itemSave.ItemId, out Texture2D texture))
                        continue;

                    restoredItems.Add(new WorldItem(
                        definition,
                        texture,
                        new Vector2(itemSave.PositionX, itemSave.PositionY),
                        itemSave.PickupDelayRemaining,
                        itemSave.VelocityX,
                        itemSave.VelocityY));
                }

                return restoredItems;
            }

            if (build.PlayerSaveData != null)
                return new List<WorldItem>();

            return new List<WorldItem>
            {
                new WorldItem(ItemDefinitions.Get(ItemId.IronPickaxe), build.ItemTextures[ItemId.IronPickaxe], pickaxeSpawn)
            };
        }

        private static Vector2 ResolveEnemySpawnNearPlayer(WorldMap worldMap, Vector2 playerPosition)
        {
            int playerTileX = worldMap.WrapTileX((int)MathF.Floor(playerPosition.X / worldMap.TileSize));
            int spawnTileX = worldMap.WrapTileX(playerTileX + EnemySpawnOffsetTilesFromPlayer);

            for (int offset = 0; offset <= EnemySpawnOffsetTilesFromPlayer; offset++)
            {
                int candidateTileX = worldMap.WrapTileX(spawnTileX + GetAlternatingOffset(offset));
                if (TryGetSurfaceSpawnPosition(worldMap, candidateTileX, out Vector2 spawnPosition))
                    return spawnPosition;
            }

            return new Vector2(playerPosition.X + (EnemySpawnOffsetTilesFromPlayer * worldMap.TileSize), playerPosition.Y);
        }

        private static bool TryGetSurfaceSpawnPosition(WorldMap worldMap, int tileX, out Vector2 spawnPosition)
        {
            int wrappedX = worldMap.WrapTileX(tileX);

            for (int y = 1; y < worldMap.Height; y++)
            {
                if (!worldMap.IsSolidAt(wrappedX, y) || worldMap.IsSolidAt(wrappedX, y - 1))
                    continue;

                spawnPosition = new Vector2(worldMap.GetTileCenter(wrappedX, y).X, y * worldMap.TileSize);
                return true;
            }

            spawnPosition = Vector2.Zero;
            return false;
        }

        private static int GetAlternatingOffset(int step)
        {
            if (step <= 0)
                return 0;

            int magnitude = (step + 1) / 2;
            return (step & 1) == 1 ? magnitude : -magnitude;
        }

        private static Camera2D CreateCamera()
        {
            return new Camera2D
            {
                FollowLerpX = 0f,
                FollowLerpY = 0.12f,
                FollowSnapMarginY = 28f
            };
        }

        private static TissueNetwork CreateEmptyTissueNetwork(WorldMap worldMap, int seed)
        {
            return new TissueNetwork(
                seed,
                new Rectangle(0, 0, worldMap.Width * worldMap.TileSize, worldMap.Height * worldMap.TileSize),
                Array.Empty<TissueNode>(),
                Array.Empty<TissueBranch>());
        }

        private void PrewarmVisibleTerrainChunks(PlayingSession session)
        {
            int screenWidth = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenHeight = graphicsDevice.PresentationParameters.BackBufferHeight;
            float worldWidthPixels = session.WorldMap.PixelWidth;
            int centerLoop = (int)MathF.Floor(session.Camera.Position.X / worldWidthPixels);

            for (int loopOffset = -1; loopOffset <= 1; loopOffset++)
            {
                float worldOffset = (centerLoop + loopOffset) * worldWidthPixels;
                session.PrepareTerrainRender(graphicsDevice, screenWidth, screenHeight, worldOffset);
            }
        }

        private sealed class BuildContext
        {
            public Texture2D DirtTexture { get; set; }
            public Texture2D GrassTexture { get; set; }
            public Texture2D SandTexture { get; set; }
            public Texture2D StoneTexture { get; set; }
            public Texture2D WoodTexture { get; set; }
            public Texture2D IronOreTexture { get; set; }
            public Texture2D TreeTexture { get; set; }
            public Texture2D MushroomTexture { get; set; }
            public Texture2D PlayerDownTexture { get; set; }
            public Texture2D PlayerUpTexture { get; set; }
            public Texture2D PlayerPickaxeMovesetTexture { get; set; }
            public Texture2D WorkbenchTexture { get; set; }
            public Texture2D FurnaceTexture { get; set; }
            public Texture2D TorchPoleTexture { get; set; }
            public Texture2D TorchFlameTexture { get; set; }
            public Texture2D DoorTexture { get; set; }
            public Texture2D ChairTexture { get; set; }
            public Texture2D TableTexture { get; set; }
            public Texture2D PlatformTexture { get; set; }
            public Texture2D ToolbarTexture { get; set; }
            public Texture2D LifeBarTexture { get; set; }
            public Texture2D TissueRevealIconTexture { get; set; }
            public Texture2D BackgroundFarTexture { get; set; }
            public Texture2D BackgroundMidTexture { get; set; }
            public Texture2D BackgroundNearTexture { get; set; }
            public Texture2D EnemyTexture { get; set; }
            public Effect SunRaysEffect { get; set; }
            public Effect MoonPhaseEffect { get; set; }
            public Effect ComposeLightingEffect { get; set; }
            public SpriteFont UiFont { get; set; }
            public WorldGenConfig WorldGenConfig { get; set; }
            public WorldMap WorldMap { get; set; }
            public WorldGenerator WorldGenerator { get; set; }
            public WorldGenContext GenerationContext { get; set; }
            public WorldGenProgressReporter GenerationProgress { get; set; }
            public int PlayerSpawnTileX { get; set; }
            public int ItemSpawnTileX { get; set; }
            public TissueNetwork TissueNetwork { get; set; }
            public TissueGenerationResult TissueGeneration { get; set; }
            public string PlayerId { get; set; }
            public PlayerSaveData PlayerSaveData { get; set; }
            public float? SavedPlayerPositionX { get; set; }
            public float? SavedPlayerPositionY { get; set; }
            public List<int> SavedActivatedTissueHubKeys { get; set; }
            public bool ApplyGeneratedLiquidPlacements { get; set; }
            public bool ApplyGeneratedSandPlacements { get; set; }
            public byte[] SavedSandSnapshot { get; set; }
            public byte[] SavedLiquidSnapshot { get; set; }
            public byte[] SavedTissueFieldDeltaSnapshot { get; set; }
            public List<string> SavedConsoleCommandHistory { get; set; }
            public float SavedTimeOfDay01 { get; set; } = WorldDayNightCycle.DefaultStartTimeOfDay01;
            public int SavedCycleIndex { get; set; }
            public WorldEnvironmentSaveData SavedWorldEnvironment { get; set; }
            public byte[] SavedBackgroundTileSnapshot { get; set; }
            public List<WorldItemSaveData> SavedWorldItems { get; set; }
            public List<WorkbenchSaveData> SavedWorkbenches { get; set; }
            public List<FurnaceSaveData> SavedFurnaces { get; set; }
            public List<TorchSaveData> SavedTorches { get; set; }
            public List<DoorSaveData> SavedDoors { get; set; }
            public List<ChairSaveData> SavedChairs { get; set; }
            public List<TableSaveData> SavedTables { get; set; }
            public List<PlatformSaveData> SavedPlatforms { get; set; }
            public Dictionary<ItemId, Texture2D> ItemTextures { get; set; }
            public Dictionary<ItemId, Weapon> Weapons { get; set; }
            public PlayerConfig PlayerConfig { get; } = PlayerConfig.Default;
            public EnemyConfig EnemyConfig { get; } = EnemyConfig.Default;
        }
    }
}
