using System.Collections.Generic;
using Nyvorn.Source.Gameplay.Combat.Interfaces;
using Nyvorn.Source.Gameplay.Combat.Resolvers;

namespace Nyvorn.Source.Gameplay.Combat
{
    public sealed class CombatSystem
    {
        private readonly PlayerAttackResolver playerAttackResolver;
        private readonly EnemyContactResolver enemyContactResolver;

        public CombatSystem()
            : this(new CombatConfig())
        {
        }

        public CombatSystem(CombatConfig config)
        {
            playerAttackResolver = new PlayerAttackResolver(config);
            enemyContactResolver = new EnemyContactResolver(config);
        }

        public void Resolve<TPlayer, TEnemy>(TPlayer player, IList<TEnemy> enemies)
            where TPlayer : IDamageable, IHitSource
            where TEnemy : IDamageable, IHitSource
        {
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                TEnemy enemy = enemies[i];
                playerAttackResolver.Resolve(player, enemy);
                enemyContactResolver.Resolve(enemy, player);

                if (!enemy.IsAlive)
                    enemies.RemoveAt(i);
            }
        }
    }
}
