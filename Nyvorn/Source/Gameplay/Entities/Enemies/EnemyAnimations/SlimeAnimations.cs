using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.EnemyAnimations
{
    // Placeholder set to keep the project ready for future enemy types.
    public static class SlimeAnimations
    {
        public static Dictionary<EnemyAnimState, EnemyAnimationClip> Create()
        {
            Rectangle fallback = new Rectangle(0, 0, 32, 32);
            var clip = new EnemyAnimationClip(new[] { fallback }, 0.12f, true);

            return new Dictionary<EnemyAnimState, EnemyAnimationClip>
            {
                [EnemyAnimState.Idle] = clip,
                [EnemyAnimState.Move] = clip,
                [EnemyAnimState.Attack] = clip,
                [EnemyAnimState.Hurt] = clip,
                [EnemyAnimState.Dead] = clip,
            };
        }
    }
}
