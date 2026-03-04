using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public static class PlayerAnimations
    {
        public static Dictionary<AnimationState, Rectangle[]> CreateBase()
        {
            const int frameW = 32;
            const int frameH = 32;

            return new Dictionary<AnimationState, Rectangle[]>
            {
                {
                    AnimationState.Idle,
                    new[]
                    {
                        new Rectangle(0 * frameW, 1 * frameH, frameW, frameH)
                    }
                },

                {
                    AnimationState.Walk,
                    new[]
                    {
                        new Rectangle(0 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(1 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(2 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(3 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(4 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(5 * frameW, 0 * frameH, frameW, frameH)
                    }
                },

                {
                    AnimationState.Jump,
                    new[]
                    {
                        new Rectangle(1 * frameW, 1 * frameH, frameW, frameH)
                    }
                },

                {
                    AnimationState.Fall,
                    new[]
                    {
                        new Rectangle(2 * frameW, 1 * frameH, frameW, frameH)
                    }
                },
            };
        }

        public static Dictionary<AnimationState, Rectangle[]> CreateAttackShortSword()
        {
            const int frameW = 32;
            const int frameH = 32;

            return new Dictionary<AnimationState, Rectangle[]>
            {
                {
                    AnimationState.Attack,
                    new[]
                    {
                        new Rectangle(0 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(1 * frameW, 0 * frameH, frameW, frameH),
                        new Rectangle(2 * frameW, 0 * frameH, frameW, frameH)
                    }
                }
            };
        }

        static readonly Vector2[] AttackHandAnchors =
        {
            new Vector2(7, 13), // frame 0
            new Vector2(17, 16), // frame 1
            new Vector2(14, 24)  // frame 2
        };

        public static Vector2 GetHandAnchor(Animator animator)
        {
            if (animator.CurrentState == AnimationState.Attack)
            {
                int i = Math.Clamp(animator.FrameIndex, 0, 2);
                    return AttackHandAnchors[i];
                }
            else
            {
                    return new Vector2(9, 23);
                }
        }
    }
}
