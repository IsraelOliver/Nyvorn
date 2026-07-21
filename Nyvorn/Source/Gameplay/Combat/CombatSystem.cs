using System.Collections.Generic;
using Nyvorn.Source.Gameplay.Combat.Interfaces;
using Nyvorn.Source.Gameplay.Combat.Resolvers;

namespace Nyvorn.Source.Gameplay.Combat
{
    public sealed class CombatSystem
    {
        private readonly PlayerAttackResolver playerAttackResolver;
        private readonly EnemyContactResolver enemyContactResolver;
        private readonly DamageNumberSystem damageNumbers;

        public CombatSystem(DamageNumberSystem damageNumbers)
        {
            this.damageNumbers = damageNumbers;
            playerAttackResolver = new PlayerAttackResolver();
            enemyContactResolver = new EnemyContactResolver();
        }

        public void Resolve<TPlayer, TEnemy>(TPlayer player, IList<TEnemy> enemies)
            where TPlayer : IDamageable, IHitSource
            where TEnemy : IDamageable, IHitSource
        {
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                TEnemy enemy = enemies[i];
                playerAttackResolver.Resolve(player, enemy, damageNumbers);
                enemyContactResolver.Resolve(enemy, player, damageNumbers);

                if (!enemy.IsAlive)
                    enemies.RemoveAt(i);
            }
        }
    }
}
