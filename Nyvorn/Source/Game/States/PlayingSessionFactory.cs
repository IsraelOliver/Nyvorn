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

            public void SetResult(PlayingSession session)
            {
                Result = session;
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
            BuildContext build = new();

            BuildOperation operation = null;
            operation = new BuildOperation(new[]
            {
                new BuildOperation.BuildStep("Carregando blocos e configuracoes", () => LoadWorldAssets(build, planetMetadata)),
                new BuildOperation.BuildStep("Gerando terreno base", () => build.WorldGenerator.Generate(build.WorldMap, build.WorldGenConfig)),
                new BuildOperation.BuildStep("Preparando mundo para jogo", () => PrepareWorld(build, tileChanges)),
                new BuildOperation.BuildStep("Gerando tecido do planeta", () => build.TissueNetwork = new TissueGenerator(build.WorldGenConfig.Seed).Generate(build.WorldMap)),
                new BuildOperation.BuildStep("Carregando entidades e interface", () => LoadGameplayAssets(build)),
                new BuildOperation.BuildStep("Posicionando spawns e finalizando sessao", () => operation.SetResult(CreateSession(build, planetMetadata)))
            });

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
        }

        private static void PrepareWorld(BuildContext build, IReadOnlyCollection<WorldTileChange> tileChanges)
        {
            build.PlayerSpawnTileX = build.WorldGenConfig.SpawnApproximateTileX;
            build.ItemSpawnTileX = build.PlayerSpawnTileX + 5;
            build.EnemySpawnTileX = build.PlayerSpawnTileX + 16;

            build.WorldMap.ResetTrackedTileChanges();
            build.WorldMap.ApplyPersistentTileChanges(tileChanges);
            build.WorldMap.InitializeGrassSimulation();
            build.WorldMap.BeginTileChangeTracking();
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
            build.TissueRevealEffect = content.Load<Effect>("shaders/TissueReveal");
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
            Vector2 playerSpawn = build.WorldGenerator.GetSurfaceSpawnPosition(build.WorldMap, build.PlayerSpawnTileX);
            Vector2 enemySpawn = build.WorldGenerator.GetSurfaceSpawnPosition(build.WorldMap, build.EnemySpawnTileX);
            Vector2 shortStickSpawn = build.WorldGenerator.GetSurfaceSpawnPosition(build.WorldMap, build.ItemSpawnTileX, 2);
            Vector2 pickaxeSpawn = build.WorldGenerator.GetSurfaceSpawnPosition(build.WorldMap, build.ItemSpawnTileX + 3, 2);

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

            List<WorldItem> worldItems = new()
            {
                new WorldItem(ItemDefinitions.Get(ItemId.ShortStick), build.ShortStickTexture, shortStickSpawn),
                new WorldItem(ItemDefinitions.Get(ItemId.Pickaxe), build.PickaxeTexture, pickaxeSpawn)
            };

            Hotbar hotbar = new(2);
            Inventory inventory = new(10);

            return new PlayingSession
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
                TissueNetwork = build.TissueNetwork,
                TissueMaskRenderer = new TissueMaskRenderer(graphicsDevice, build.TissueRevealEffect),
                TissueRevealController = new TissueRevealController(build.WorldMap.TileSize * 28f, fadeDuration: 0.16f, activeDuration: 4.2f),
                TissueDebugRenderer = new TissueDebugRenderer(graphicsDevice)
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
            public Effect TissueRevealEffect { get; set; }
            public SpriteFont UiFont { get; set; }
            public WorldGenConfig WorldGenConfig { get; set; }
            public WorldMap WorldMap { get; set; }
            public WorldGenerator WorldGenerator { get; set; }
            public int PlayerSpawnTileX { get; set; }
            public int ItemSpawnTileX { get; set; }
            public int EnemySpawnTileX { get; set; }
            public TissueNetwork TissueNetwork { get; set; }
            public Dictionary<ItemId, Texture2D> ItemTextures { get; set; }
            public Dictionary<ItemId, Weapon> Weapons { get; set; }
            public PlayerConfig PlayerConfig { get; } = PlayerConfig.Default;
            public EnemyConfig EnemyConfig { get; } = EnemyConfig.Default;
        }
    }
}
