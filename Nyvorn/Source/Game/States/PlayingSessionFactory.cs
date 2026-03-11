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
            Texture2D dirt = content.Load<Texture2D>("blocks/dirt_block");
            Texture2D sand = content.Load<Texture2D>("blocks/sand_block");
            Texture2D stone = content.Load<Texture2D>("blocks/stone_block");

            WorldMap worldMap = new WorldMap(100, 50, 8);
            worldMap.SetTextures(dirt, sand, stone);
            worldMap.GenerateTest();

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
            SpriteFont uiFont = content.Load<SpriteFont>("ui/UIFont");
             
            Texture2D enemyTexture = content.Load<Texture2D>("entities/enemy/enemy_test");
            Dictionary<ItemId, Texture2D> itemTextures = new();
            foreach (ItemDefinition definition in ItemDefinitions.GetAll())
                itemTextures[definition.Id] = content.Load<Texture2D>(definition.TexturePath);
            Texture2D nullWeaponTexture = new Texture2D(graphicsDevice, 1, 1);
            nullWeaponTexture.SetData(new[] { Color.Transparent });
            Dictionary<ItemId, Weapon> weapons = new()
            {
                [ItemId.None] = new NullWeapon(nullWeaponTexture),
                [ItemId.ShortStick] = new ShortStick(shortStickTexture)
            };

            Player player = new Player(
                new Vector2(90, 50),
                bodyTexture,
                backHandTexture,
                frontHandTexture,
                attackHandbackTexture,
                attackHandfrontTexture,
                attackBodyTexture,
                legsTexture,
                handFrontWeaponRun,
                playerDodgeTexture);

            List<Enemy> enemies = new();
            EnemyRespawnController enemyRespawnController = new EnemyRespawnController(enemyTexture, new Vector2(118, 50));
            enemyRespawnController.SpawnInitial(enemies);
            List<WorldItem> worldItems = new()
            {
                new WorldItem(ItemDefinitions.Get(ItemId.ShortStick), shortStickTexture, new Vector2(140, 30))
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
                Camera = new Camera2D(),
                HealthBarRenderer = new WorldHealthBarRenderer(graphicsDevice),
                HudRenderer = new HudRenderer(graphicsDevice, uiFont, itemTextures),
                CombatSystem = new CombatSystem()
            };
        }
    }
}
