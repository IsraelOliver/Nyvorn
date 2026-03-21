using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.EnemyAnimations
{
    public static class EnemyTestAnimations
    {
        public static Dictionary<EnemyAnimState, EnemyAnimationClip> Create()
        {
            const int w = 32;
            const int h = 32;

            Rectangle[] moveFrames =
            {
                new Rectangle(0 * w, 0 * h, w, h),
                new Rectangle(1 * w, 0 * h, w, h),
                new Rectangle(2 * w, 0 * h, w, h),
                new Rectangle(3 * w, 0 * h, w, h),
                new Rectangle(4 * w, 0 * h, w, h),
                new Rectangle(5 * w, 0 * h, w, h),
            };

            Rectangle idle = new Rectangle(0 * w, 1 * h, w, h);
            Rectangle jump = new Rectangle(1 * w, 1 * h, w, h);
            Rectangle fall = new Rectangle(2 * w, 1 * h, w, h);

            return new Dictionary<EnemyAnimState, EnemyAnimationClip>
            {
                [EnemyAnimState.Idle] = new EnemyAnimationClip(new[] { idle }, 0.12f, true),
                [EnemyAnimState.Move] = new EnemyAnimationClip(moveFrames, 0.09f, true),
                [EnemyAnimState.Attack] = new EnemyAnimationClip(new[] { jump }, 0.08f, false),
                [EnemyAnimState.Hurt] = new EnemyAnimationClip(new[] { fall }, 0.08f, false),
                [EnemyAnimState.Dead] = new EnemyAnimationClip(new[] { fall }, 0.12f, false),
            };
        }
    }
}
