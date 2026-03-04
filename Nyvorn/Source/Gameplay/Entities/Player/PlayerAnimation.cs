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

        static readonly Vector2[] IdleHandAnchors =
{
    new Vector2(9, 23)
};

        static readonly Vector2[] WalkHandAnchors =
        {
            new Vector2(6, 21),
        };

        static readonly Vector2[] JumpHandAnchors =
        {
            new Vector2(7, 22)
        };

        static readonly Vector2[] FallHandAnchors =
        {
            new Vector2(6, 13)
        };

        static readonly Vector2[] AttackHandAnchors =
        {
            new Vector2(7, 13),
            new Vector2(17, 16),
            new Vector2(14, 24)
        };

        public static Vector2 GetHandAnchor(Animator animator)
        {
            switch (animator.CurrentState)
            {
                case AnimationState.Attack:
                    {
                        int i = Math.Clamp(animator.FrameIndex, 0, AttackHandAnchors.Length - 1);
                        return AttackHandAnchors[i];
                    }

                case AnimationState.Walk:
                    {
                        int i = Math.Clamp(animator.FrameIndex, 0, WalkHandAnchors.Length - 1);
                        return WalkHandAnchors[i];
                    }

                case AnimationState.Jump:
                    {
                        int i = Math.Clamp(animator.FrameIndex, 0, JumpHandAnchors.Length - 1);
                        return JumpHandAnchors[i];
                    }

                case AnimationState.Fall:
                    {
                        int i = Math.Clamp(animator.FrameIndex, 0, FallHandAnchors.Length - 1);
                        return FallHandAnchors[i];
                    }

                case AnimationState.Idle:
                default:
                    {
                        int i = Math.Clamp(animator.FrameIndex, 0, IdleHandAnchors.Length - 1);
                        return IdleHandAnchors[i];
                    }
            }
        }
    }
}
