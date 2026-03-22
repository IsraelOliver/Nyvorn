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
using System.Linq;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayingSessionFactory
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly ContentManager content;

        public PlayingSessionFactory(GraphicsDevice graphicsDevice, ContentManager content)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
        }

        public sealed class BuildOperation
        {
            private readonly IReadOnlyList<BuildStep> steps;
            private int completedSteps;

            public BuildOperation(IReadOnlyList<BuildStep> steps)
            {
                this.steps = steps;
                StatusText = steps.Count > 0 ? steps[0].Label : "Concluido";
            }

            public string StatusText { get; private set; }
            public float Progress => steps.Count == 0 ? 1f : completedSteps / (float)steps.Count;
            public bool IsCompleted => completedSteps >= steps.Count;
            public PlayingSession Result { get; private set; }

            public void Advance()
            {
                if (IsCompleted)
                    return;

                BuildStep step = steps[completedSteps];
                StatusText = step.Label;
                step.Action();
                completedSteps++;

                if (IsCompleted)
                    StatusText = "Concluido";
            }

            public sealed class BuildStep
            {
                public BuildStep(string label, Action action)
                {
                    Label = label;
                    Action = action;
                }

                public string Label { get; }
                public Action Action { get; }
            }

            public void SetResult(PlayingSession session)
            {
                Result = session;
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
            WorldGenSettings worldGenSettings = WorldGenSettings.CreatePreset(sizePreset, seed);
            PlanetWorldMetadata planetMetadata = PlanetWorldMetadata.Create(planetName, worldGenSettings);
            return CreateBuildOperation(planetMetadata, tileChanges: null);
        }

        public BuildOperation CreateBuildOperation(PlanetSaveData saveData)
        {
            if (saveData == null)
                return CreateBuildOperation("Elyra", WorldSizePreset.Medium, 1337);

            return CreateBuildOperation(saveData.Metadata, saveData.TileChanges);
        }

        private PlayingSession CompleteBuild(BuildOperation operation)
        {
            while (!operation.IsCompleted)
                operation.Advance();

            return operation.Result;
        }

        private BuildOperation CreateBuildOperation(PlanetWorldMetadata planetMetadata, IReadOnlyCollection<WorldTileChange> tileChanges)
        {
            Texture2D dirt = null;
            Texture2D grass = null;
            Texture2D sand = null;
            Texture2D stone = null;
            Texture2D backHandTexture = null;
            Texture2D bodyTexture = null;
            Texture2D legsTexture = null;
            Texture2D frontHandTexture = null;
            Texture2D shortStickTexture = null;
            Texture2D handFrontWeaponRun = null;
            Texture2D playerDodgeTexture = null;
            Texture2D attackHandbackTexture = null;
            Texture2D attackHandfrontTexture = null;
            Texture2D attackBodyTexture = null;
            Texture2D enemyTexture = null;
            Effect tissueRevealEffect = null;
            SpriteFont uiFont = null;

            WorldGenSettings worldGenSettings = null;
            WorldMap worldMap = null;
            WorldGenerator worldGenerator = null;
            int playerSpawnTileX = 0;
            int itemSpawnTileX = 0;
            int enemySpawnTileX = 0;
            TissueNetwork tissueNetwork = null;
            Dictionary<ItemId, Texture2D> itemTextures = null;
            Dictionary<ItemId, Weapon> weapons = null;
            PlayerConfig playerConfig = PlayerConfig.Default;
            EnemyConfig enemyConfig = EnemyConfig.Default;
            Hotbar hotbar = null;
            Inventory inventory = null;
            Player player = null;
            List<Enemy> enemies = null;
            List<WorldItem> worldItems = null;
            EnemyRespawnController enemyRespawnController = null;

            BuildOperation operation = null;
            operation = new BuildOperation(new[]
            {
                new BuildOperation.BuildStep("Carregando blocos e configuracoes", () =>
                {
                    dirt = content.Load<Texture2D>("blocks/dirt_spritesheet");
                    grass = content.Load<Texture2D>("blocks/grass_spritesheet");
                    sand = content.Load<Texture2D>("blocks/sand_block");
                    stone = content.Load<Texture2D>("blocks/stone_spritesheet");

                    worldGenSettings = WorldGenSettings.CreatePreset(planetMetadata.SizePreset, planetMetadata.Seed);
                    worldMap = new WorldMap(worldGenSettings.WorldWidth, worldGenSettings.WorldHeight, worldGenSettings.TileSize);
                    worldMap.SetTextures(dirt, grass, sand, stone);
                    worldGenerator = new WorldGenerator(worldGenSettings);
                }),
                new BuildOperation.BuildStep("Gerando terreno base", () =>
                {
                    worldGenerator.Generate(worldMap);
                }),
                new BuildOperation.BuildStep("Preparando mundo para jogo", () =>
                {
                    playerSpawnTileX = worldGenSettings.SpawnApproximateTileX;
                    itemSpawnTileX = playerSpawnTileX + 5;
                    enemySpawnTileX = playerSpawnTileX + 16;
                    worldMap.ResetTrackedTileChanges();
                    worldMap.ApplyPersistentTileChanges(tileChanges);
                    worldMap.InitializeGrassSimulation();
                    worldMap.BeginTileChangeTracking();
                }),
                new BuildOperation.BuildStep("Gerando tecido do planeta", () =>
                {
                    tissueNetwork = new TissueGenerator(worldGenSettings.Seed).Generate(worldMap);
                }),
                new BuildOperation.BuildStep("Carregando entidades e interface", () =>
                {
                    backHandTexture = content.Load<Texture2D>("entities/player/handBackTexture_base");
                    bodyTexture = content.Load<Texture2D>("entities/player/bodyTexture_base");
                    legsTexture = content.Load<Texture2D>("entities/player/legsTexture_base");
                    frontHandTexture = content.Load<Texture2D>("entities/player/handFrontTexture_base");
                    shortStickTexture = content.Load<Texture2D>("weapons/shortStick");
                    handFrontWeaponRun = content.Load<Texture2D>("entities/player/handFront_weaponRun");
                    playerDodgeTexture = content.Load<Texture2D>("entities/player/player_dodge");
                    attackHandbackTexture = content.Load<Texture2D>("entities/player/handBackShortSword_attack");
                    attackHandfrontTexture = content.Load<Texture2D>("entities/player/handFrontShortSword_attack");
                    attackBodyTexture = content.Load<Texture2D>("entities/player/bodyShortSword_attack");
                    tissueRevealEffect = content.Load<Effect>("shaders/TissueReveal");
                    uiFont = content.Load<SpriteFont>("ui/UIFont");
                    enemyTexture = content.Load<Texture2D>("entities/enemy/enemy_test");

                    itemTextures = new Dictionary<ItemId, Texture2D>();
                    foreach (ItemDefinition definition in ItemDefinitions.GetAll())
                        itemTextures[definition.Id] = content.Load<Texture2D>(definition.TexturePath);

                    Texture2D nullWeaponTexture = new Texture2D(graphicsDevice, 1, 1);
                    nullWeaponTexture.SetData(new[] { Color.Transparent });
                    weapons = new Dictionary<ItemId, Weapon>
                    {
                        [ItemId.None] = new HandWeapon(nullWeaponTexture),
                        [ItemId.ShortStick] = new ShortStick(shortStickTexture)
                    };
                }),
                new BuildOperation.BuildStep("Posicionando spawns e finalizando sessao", () =>
                {
                    Vector2 playerSpawn = worldGenerator.GetSurfaceSpawnPosition(worldMap, playerSpawnTileX);
                    Vector2 enemySpawn = worldGenerator.GetSurfaceSpawnPosition(worldMap, enemySpawnTileX);
                    Vector2 shortStickSpawn = worldGenerator.GetSurfaceSpawnPosition(worldMap, itemSpawnTileX, 2);

                    player = new Player(
                        playerSpawn,
                        bodyTexture,
                        backHandTexture,
                        frontHandTexture,
                        attackHandbackTexture,
                        attackHandfrontTexture,
                        attackBodyTexture,
                        legsTexture,
                        handFrontWeaponRun,
                        playerDodgeTexture,
                        playerConfig);

                    enemies = new List<Enemy>();
                    enemyRespawnController = new EnemyRespawnController(enemyTexture, enemySpawn, enemyConfig);
                    enemyRespawnController.SpawnInitial(enemies);
                    worldItems = new List<WorldItem>
                    {
                        new WorldItem(ItemDefinitions.Get(ItemId.ShortStick), shortStickTexture, shortStickSpawn)
                    };
                    hotbar = new Hotbar(2);
                    inventory = new Inventory(10);

                    operation.SetResult(new PlayingSession
                    {
                        PlanetMetadata = planetMetadata,
                        WorldMap = worldMap,
                        Player = player,
                        Enemies = enemies,
                        WorldItems = worldItems,
                        Hotbar = hotbar,
                        Inventory = inventory,
                        ItemTextures = itemTextures,
                        Weapons = weapons,
                        EnemyRespawnController = enemyRespawnController,
                        Camera = new Camera2D
                        {
                            FollowLerpX = 0f,
                            FollowLerpY = 0.12f,
                            FollowSnapMarginY = 28f
                        },
                        HealthBarRenderer = new WorldHealthBarRenderer(graphicsDevice),
                        HudRenderer = new HudRenderer(graphicsDevice, uiFont, itemTextures),
                        WorldMinimapRenderer = new WorldMinimapRenderer(graphicsDevice),
                        ElyraSkyRenderer = new ElyraSkyRenderer(graphicsDevice),
                        TilePreviewRenderer = new WorldTilePreviewRenderer(graphicsDevice),
                        CombatSystem = new CombatSystem(),
                        TissueNetwork = tissueNetwork,
                        TissueMaskRenderer = new TissueMaskRenderer(graphicsDevice, tissueRevealEffect),
                        TissueRevealController = new TissueRevealController(worldMap.TileSize * 28f, fadeDuration: 0.16f, activeDuration: 4.2f),
                        TissueDebugRenderer = new TissueDebugRenderer(graphicsDevice)
                    });
                })
            });

            return operation;
        }

    }
}
