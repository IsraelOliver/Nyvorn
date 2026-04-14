using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
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
using System.Threading.Tasks;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionFactory
    {
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
            return Create("Elyra", WorldSizePreset.Medium, 1337);
        }

        public PlayingSession Create(string planetName, WorldSizePreset sizePreset, int seed)
        {
            return CompleteBuild(CreateBuildOperation(planetName, sizePreset, seed));
        }

        public PlayingSession Create(PlanetSaveData saveData)
        {
            if (saveData == null)
                return Create();

            return CompleteBuild(CreateBuildOperation(saveData));
        }

        public BuildOperation CreateBuildOperation(string planetName, WorldSizePreset sizePreset, int seed)
        {
            WorldGenConfig worldGenConfig = WorldGenConfig.CreatePreset(sizePreset, seed);
            PlanetWorldMetadata planetMetadata = PlanetWorldMetadata.Create(planetName, worldGenConfig);
            return CreateBuildOperation(planetMetadata, saveData: null);
        }

        public BuildOperation CreateBuildOperation(PlanetSaveData saveData)
        {
            if (saveData == null)
                return CreateBuildOperation("Elyra", WorldSizePreset.Medium, 1337);

            return CreateBuildOperationFromSaveData(saveData);
        }

        private PlayingSession CompleteBuild(BuildOperation operation)
        {
            while (!operation.IsCompleted)
                operation.Advance();

            return operation.Result;
        }

        private BuildOperation CreateBuildOperationFromSaveData(PlanetSaveData saveData)
        {
            return CreateBuildOperation(saveData.Metadata, saveData);
        }

        private BuildOperation CreateBuildOperation(PlanetWorldMetadata planetMetadata, PlanetSaveData saveData)
        {
            BuildContext build = new();
            bool hasWorldSnapshot = saveData?.WorldTileSnapshot != null && saveData.WorldTileSnapshot.Length > 0;
            bool hasTissueSnapshot = saveData?.TissueFieldSnapshot != null && saveData.TissueFieldSnapshot.Length > 0;
            build.SavedWorldItems = saveData != null && saveData.Version >= 5 && saveData.WorldItems != null
                ? new List<WorldItemSaveData>(saveData.WorldItems)
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
                if (saveData != null)
                    build.WorldMap.MarkPersisted();
            }, weight: 5f, runInBackground: true));

            if (!hasTissueSnapshot)
            {
                steps.Add(new BuildOperation.BuildStep("Preparando tecido do planeta", () =>
                {
                    if (build.WorldMap.TissueField == null)
                        build.TissueNetwork = new TissueGenerator(build.WorldGenConfig.Seed).Generate(build.WorldMap);
                }, weight: 8f, runInBackground: true));
            }

            steps.Add(new BuildOperation.BuildStep("Carregando entidades e interface", () => LoadGameplayAssets(build), weight: 4f));
            steps.Add(new BuildOperation.BuildStep("Posicionando spawns e finalizando sessao", () => operation.SetResult(CreateSession(build, planetMetadata)), weight: 1f));

            operation = new BuildOperation(steps);

            return operation;
        }

        private void LoadWorldAssets(BuildContext build, PlanetWorldMetadata planetMetadata)
        {
            build.DirtTexture = content.Load<Texture2D>("blocks/dirt_spritesheet");
            build.GrassTexture = content.Load<Texture2D>("blocks/grass_spritesheet");
            build.SandTexture = content.Load<Texture2D>("blocks/sand_spritesheet");
            build.StoneTexture = content.Load<Texture2D>("blocks/stone_spritesheet");

            build.WorldGenConfig = WorldGenConfig.CreatePreset(planetMetadata.SizePreset, planetMetadata.Seed);
            build.WorldMap = new WorldMap(build.WorldGenConfig.WorldWidth, build.WorldGenConfig.WorldHeight, build.WorldGenConfig.TileSize);
            build.WorldMap.SetTextures(build.DirtTexture, build.GrassTexture, build.SandTexture, build.StoneTexture);
            build.WorldGenerator = new WorldGenerator();
            build.GenerationContext = build.WorldGenerator.CreateGenerationContext(build.WorldMap, build.WorldGenConfig);
            build.GenerationProgress = new WorldGenProgressReporter(WorldGenerator.GetOrderedPasses());
        }

        private static void PrepareWorld(BuildContext build, IReadOnlyCollection<WorldTileChange> tileChanges)
        {
            int worldCenterTileX = build.WorldMap.Width / 2;
            build.PlayerSpawnTileX = worldCenterTileX;
            build.ItemSpawnTileX = WrapTileX(build.PlayerSpawnTileX + 5, build.WorldMap.Width);
            build.EnemySpawnTileX = WrapTileX(build.PlayerSpawnTileX + 16, build.WorldMap.Width);

            build.WorldMap.ResetTrackedTileChanges();
            build.WorldMap.ApplyPersistentTileChanges(tileChanges);
            build.WorldMap.InitializeGrassSimulation();
            build.WorldMap.BeginTileChangeTracking();
        }

        private void LoadPlayerProgress(BuildContext build, PlanetWorldMetadata planetMetadata)
        {
            build.PlayerSaveData = playerSaveService.Load(planetMetadata.WorldId);
        }

        private static void LoadWorldSnapshot(BuildContext build, PlanetSaveData saveData)
        {
            build.WorldMap.ImportTileSnapshot(saveData.WorldTileSnapshot);
            build.WorldMap.ImportTissueSnapshot(saveData.TissueFieldSnapshot);
            build.WorldMap.ImportTissueBiomeSnapshot(saveData.TissueBiomeSnapshot);

            if (build.WorldMap.TissueField != null)
            {
                if (saveData.TissueAnalysisSnapshot != null && saveData.TissueAnalysisSnapshot.Length > 0)
                    build.WorldMap.ImportTissueAnalysisSnapshot(saveData.TissueAnalysisSnapshot);
                else
                    build.WorldMap.RebuildTissueAnalysis();
            }
        }

        private static int WrapTileX(int tileX, int worldWidth)
        {
            if (worldWidth <= 0)
                return 0;

            int wrapped = tileX % worldWidth;
            return wrapped < 0 ? wrapped + worldWidth : wrapped;
        }

        private void LoadGameplayAssets(BuildContext build)
        {
            build.BackHandTexture = content.Load<Texture2D>("entities/player/handBackTexture_base");
            build.BodyTexture = content.Load<Texture2D>("entities/player/bodyTexture_base");
            build.LegsTexture = content.Load<Texture2D>("entities/player/legsTexture_base");
            build.FrontHandTexture = content.Load<Texture2D>("entities/player/handFrontTexture_base");
            build.ShortStickTexture = content.Load<Texture2D>("weapons/shortStick");
            build.PickaxeTexture = content.Load<Texture2D>("weapons/Pickaxe-Sheet");
            build.HandFrontWeaponRunTexture = content.Load<Texture2D>("entities/player/handFront_weaponRun");
            build.PlayerDodgeTexture = content.Load<Texture2D>("entities/player/player_dodge");
            build.AttackHandBackTexture = content.Load<Texture2D>("entities/player/handBackShortSword_attack");
            build.AttackHandFrontTexture = content.Load<Texture2D>("entities/player/handFrontShortSword_attack");
            build.AttackBodyTexture = content.Load<Texture2D>("entities/player/bodyShortSword_attack");
            build.UiFont = content.Load<SpriteFont>("ui/UIFont");
            build.EnemyTexture = content.Load<Texture2D>("entities/enemy/enemy_test");

            build.ItemTextures = LoadItemTextures();
            build.Weapons = CreateWeapons(build.ShortStickTexture, build.PickaxeTexture);
        }

        private Dictionary<ItemId, Texture2D> LoadItemTextures()
        {
            Dictionary<ItemId, Texture2D> itemTextures = new();
            foreach (ItemDefinition definition in ItemDefinitions.GetAll())
                itemTextures[definition.Id] = content.Load<Texture2D>(definition.TexturePath);

            return itemTextures;
        }

        private Dictionary<ItemId, Weapon> CreateWeapons(Texture2D shortStickTexture, Texture2D pickaxeTexture)
        {
            Texture2D nullWeaponTexture = new Texture2D(graphicsDevice, 1, 1);
            nullWeaponTexture.SetData(new[] { Color.Transparent });

            return new Dictionary<ItemId, Weapon>
            {
                [ItemId.None] = new HandWeapon(nullWeaponTexture),
                [ItemId.ShortStick] = new ShortStick(shortStickTexture),
                [ItemId.Pickaxe] = new Pickaxe(pickaxeTexture)
            };
        }

        private PlayingSession CreateSession(BuildContext build, PlanetWorldMetadata planetMetadata)
        {
            Vector2 defaultPlayerSpawn = build.WorldGenerator.GetLayerSpawnPosition(
                build.WorldMap,
                build.WorldGenConfig,
                WorldLayerType.DeepCavern,
                build.PlayerSpawnTileX);
            Vector2 playerSpawn = ResolvePlayerSpawn(build, defaultPlayerSpawn);
            Vector2 enemySpawn = build.WorldGenerator.GetLayerSpawnPosition(
                build.WorldMap,
                build.WorldGenConfig,
                WorldLayerType.DeepCavern,
                build.EnemySpawnTileX);
            Vector2 shortStickSpawn = build.WorldGenerator.GetLayerSpawnPosition(
                build.WorldMap,
                build.WorldGenConfig,
                WorldLayerType.DeepCavern,
                build.ItemSpawnTileX,
                tilesAboveGround: 2);
            Vector2 pickaxeSpawn = build.WorldGenerator.GetLayerSpawnPosition(
                build.WorldMap,
                build.WorldGenConfig,
                WorldLayerType.DeepCavern,
                build.ItemSpawnTileX + 3,
                tilesAboveGround: 2);

            Player player = new(
                playerSpawn,
                build.BodyTexture,
                build.BackHandTexture,
                build.FrontHandTexture,
                build.AttackHandBackTexture,
                build.AttackHandFrontTexture,
                build.AttackBodyTexture,
                build.LegsTexture,
                build.HandFrontWeaponRunTexture,
                build.PlayerDodgeTexture,
                build.PlayerConfig);

            List<Enemy> enemies = new();
            EnemyRespawnController enemyRespawnController = new(build.EnemyTexture, enemySpawn, build.EnemyConfig);
            enemyRespawnController.SpawnInitial(enemies);

            Hotbar hotbar = new(2);
            Inventory inventory = new(10);
            int selectedHotbarIndex = 0;
            ApplyPlayerInventory(build.PlayerSaveData, hotbar, inventory, ref selectedHotbarIndex);
            List<WorldItem> worldItems = CreateWorldItems(build, shortStickSpawn, pickaxeSpawn);

            PlayingSession session = new PlayingSession
            {
                PlanetMetadata = planetMetadata,
                WorldMap = build.WorldMap,
                Player = player,
                Enemies = enemies,
                WorldItems = worldItems,
                Hotbar = hotbar,
                Inventory = inventory,
                ItemTextures = build.ItemTextures,
                Weapons = build.Weapons,
                EnemyRespawnController = enemyRespawnController,
                Camera = CreateCamera(),
                HealthBarRenderer = new WorldHealthBarRenderer(graphicsDevice),
                HudRenderer = new HudRenderer(graphicsDevice, build.UiFont, build.ItemTextures),
                WorldMinimapRenderer = new WorldMinimapRenderer(graphicsDevice),
                ElyraSkyRenderer = new ElyraSkyRenderer(graphicsDevice),
                TilePreviewRenderer = new WorldTilePreviewRenderer(graphicsDevice),
                CombatSystem = new CombatSystem(),
                TissueNetwork = build.TissueNetwork ?? CreateEmptyTissueNetwork(build.WorldMap, build.WorldGenConfig.Seed),
                TissueRevealController = new TissueRevealController(build.WorldMap.TileSize * 28f, fadeDuration: 0.16f, activeDuration: 4.2f),
                TissueDebugRenderer = new TissueFieldDebugRenderer(graphicsDevice)
            };

            session.SetSelectedHotbarIndex(selectedHotbarIndex);
            return session;
        }

        private static Vector2 ResolvePlayerSpawn(BuildContext build, Vector2 fallbackPosition)
        {
            PlayerSaveData playerSaveData = build.PlayerSaveData;
            if (playerSaveData == null)
                return fallbackPosition;

            float x = playerSaveData.PositionX;
            float y = playerSaveData.PositionY;
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

        private List<WorldItem> CreateWorldItems(BuildContext build, Vector2 shortStickSpawn, Vector2 pickaxeSpawn)
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
                new WorldItem(ItemDefinitions.Get(ItemId.ShortStick), build.ShortStickTexture, shortStickSpawn),
                new WorldItem(ItemDefinitions.Get(ItemId.Pickaxe), build.PickaxeTexture, pickaxeSpawn)
            };
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

        private sealed class BuildContext
        {
            public Texture2D DirtTexture { get; set; }
            public Texture2D GrassTexture { get; set; }
            public Texture2D SandTexture { get; set; }
            public Texture2D StoneTexture { get; set; }
            public Texture2D BackHandTexture { get; set; }
            public Texture2D BodyTexture { get; set; }
            public Texture2D LegsTexture { get; set; }
            public Texture2D FrontHandTexture { get; set; }
            public Texture2D ShortStickTexture { get; set; }
            public Texture2D PickaxeTexture { get; set; }
            public Texture2D HandFrontWeaponRunTexture { get; set; }
            public Texture2D PlayerDodgeTexture { get; set; }
            public Texture2D AttackHandBackTexture { get; set; }
            public Texture2D AttackHandFrontTexture { get; set; }
            public Texture2D AttackBodyTexture { get; set; }
            public Texture2D EnemyTexture { get; set; }
            public SpriteFont UiFont { get; set; }
            public WorldGenConfig WorldGenConfig { get; set; }
            public WorldMap WorldMap { get; set; }
            public WorldGenerator WorldGenerator { get; set; }
            public WorldGenContext GenerationContext { get; set; }
            public WorldGenProgressReporter GenerationProgress { get; set; }
            public int PlayerSpawnTileX { get; set; }
            public int ItemSpawnTileX { get; set; }
            public int EnemySpawnTileX { get; set; }
            public TissueNetwork TissueNetwork { get; set; }
            public PlayerSaveData PlayerSaveData { get; set; }
            public List<WorldItemSaveData> SavedWorldItems { get; set; }
            public Dictionary<ItemId, Texture2D> ItemTextures { get; set; }
            public Dictionary<ItemId, Weapon> Weapons { get; set; }
            public PlayerConfig PlayerConfig { get; } = PlayerConfig.Default;
            public EnemyConfig EnemyConfig { get; } = EnemyConfig.Default;
        }
    }
}
