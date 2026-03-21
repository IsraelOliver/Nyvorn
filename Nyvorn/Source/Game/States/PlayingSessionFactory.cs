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
using Nyvorn.Source.World.Tissue;
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

        public PlayingSession Create()
        {
            Texture2D dirt = content.Load<Texture2D>("blocks/dirt_spritesheet");
            Texture2D sand = content.Load<Texture2D>("blocks/sand_block");
            Texture2D stone = content.Load<Texture2D>("blocks/stone_block");

            WorldMap worldMap = new WorldMap(240, 80, 8);
            worldMap.SetTextures(dirt, sand, stone);
            WorldGenSettings worldGenSettings = new WorldGenSettings
            {
                Seed = 1337,
                SurfaceAmplitude = 8,
                BaseGroundLevel = 46,
                CaveStartDepth = 6
            };
            WorldGenerator worldGenerator = new WorldGenerator(worldGenSettings);
            worldGenerator.Generate(worldMap);
            worldGenerator.CarveStarterSubterranean(worldMap, 24);
            TissueNetwork tissueNetwork = new TissueGenerator(worldGenSettings.Seed).Generate(worldMap);

            Texture2D backHandTexture = content.Load<Texture2D>("entities/player/handBackTexture_base");
            Texture2D bodyTexture = content.Load<Texture2D>("entities/player/bodyTexture_base");
            Texture2D legsTexture = content.Load<Texture2D>("entities/player/legsTexture_base");
            Texture2D frontHandTexture = content.Load<Texture2D>("entities/player/handFrontTexture_base");
            Texture2D shortStickTexture = content.Load<Texture2D>("weapons/shortStick");
            Texture2D handFrontWeaponRun = content.Load<Texture2D>("entities/player/handFront_weaponRun");
            Texture2D playerDodgeTexture = content.Load<Texture2D>("entities/player/player_dodge");
            Texture2D attackHandbackTexture = content.Load<Texture2D>("entities/player/handBackShortSword_attack");
            Texture2D attackHandfrontTexture = content.Load<Texture2D>("entities/player/handFrontShortSword_attack");
            Texture2D attackBodyTexture = content.Load<Texture2D>("entities/player/bodyShortSword_attack");
            Effect tissueRevealEffect = content.Load<Effect>("shaders/TissueReveal");
            SpriteFont uiFont = content.Load<SpriteFont>("ui/UIFont");
            PlayerConfig playerConfig = PlayerConfig.Default;
            EnemyConfig enemyConfig = EnemyConfig.Default;
             
            Texture2D enemyTexture = content.Load<Texture2D>("entities/enemy/enemy_test");
            Dictionary<ItemId, Texture2D> itemTextures = new();
            foreach (ItemDefinition definition in ItemDefinitions.GetAll())
                itemTextures[definition.Id] = content.Load<Texture2D>(definition.TexturePath);
            Texture2D nullWeaponTexture = new Texture2D(graphicsDevice, 1, 1);
            nullWeaponTexture.SetData(new[] { Color.Transparent });
            Dictionary<ItemId, Weapon> weapons = new()
            {
                [ItemId.None] = new HandWeapon(nullWeaponTexture),
                [ItemId.ShortStick] = new ShortStick(shortStickTexture)
            };

            Vector2 playerSpawn = worldGenerator.GetSurfaceSpawnPosition(worldMap, 20);
            Vector2 enemySpawn = worldGenerator.GetSurfaceSpawnPosition(worldMap, 36);
            Vector2 shortStickSpawn = worldGenerator.GetSurfaceSpawnPosition(worldMap, 28, 2);

            Player player = new Player(
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

            List<Enemy> enemies = new();
            EnemyRespawnController enemyRespawnController = new EnemyRespawnController(enemyTexture, enemySpawn, enemyConfig);
            enemyRespawnController.SpawnInitial(enemies);
            List<WorldItem> worldItems = new()
            {
                new WorldItem(ItemDefinitions.Get(ItemId.ShortStick), shortStickTexture, shortStickSpawn)
            };
            Hotbar hotbar = new Hotbar(2);
            Inventory inventory = new Inventory(10);

            return new PlayingSession
            {
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
                TilePreviewRenderer = new WorldTilePreviewRenderer(graphicsDevice),
                CombatSystem = new CombatSystem(),
                TissueNetwork = tissueNetwork,
                TissueMaskRenderer = new TissueMaskRenderer(graphicsDevice, tissueRevealEffect),
                TissueRevealController = new TissueRevealController(worldMap.TileSize * 28f, fadeDuration: 0.16f, activeDuration: 4.2f),
                TissueDebugRenderer = new TissueDebugRenderer(graphicsDevice)
            };
        }
    }
}
