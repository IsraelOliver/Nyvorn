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

        public static Dictionary<AnimationState, Rectangle[]> CreateDodge()
        {
            const int frameW = 32;
            const int frameH = 32;

            return new Dictionary<AnimationState, Rectangle[]>
            {
                {
                    AnimationState.Dodge,
                    new[]
                    {
                        new Rectangle(0 * frameW, 0, frameW, frameH),
                        new Rectangle(1 * frameW, 0, frameW, frameH),
                        new Rectangle(2 * frameW, 0, frameW, frameH),
                        new Rectangle(3 * frameW, 0, frameW, frameH),
                        new Rectangle(4 * frameW, 0, frameW, frameH),
                        new Rectangle(5 * frameW, 0, frameW, frameH),
                        new Rectangle(6 * frameW, 0, frameW, frameH)
                    }
                }
            };
        }

        public static Dictionary<AnimationState, float[]> CreateDodgeFrameTimes()
        {
            return new Dictionary<AnimationState, float[]>
            {
                {
                    AnimationState.Dodge,
                    new[]
                    {
                        0.06f,
                        0.06f,
                        0.06f,
                        0.05f,
                        0.05f,
                        0.05f,
                        0.05f
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
            new Vector2(10, 23),
            new Vector2(12, 22),
            new Vector2(15, 20),
            new Vector2(12, 22),
            new Vector2(8, 23)
        };

        static readonly Vector2[] WalkWeaponHandAnchors =
        {
            new Vector2(7, 21),
            new Vector2(7, 21),
            new Vector2(7, 21),
            new Vector2(7, 21),
            new Vector2(7, 21),
            new Vector2(7, 21)
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

        public static Vector2 GetHandAnchor(AnimationState state, int frameIndex, bool useWeaponWalkAnchor)
        {
            switch (state)
            {
                case AnimationState.Attack:
                    {
                        int i = Math.Clamp(frameIndex, 0, AttackHandAnchors.Length - 1);
                        return AttackHandAnchors[i];
                    }

                case AnimationState.Dodge:
                    {
                        int i = Math.Clamp(0, 0, IdleHandAnchors.Length - 1);
                        return IdleHandAnchors[i];
                    }

                case AnimationState.Walk:
                    {
                        if (useWeaponWalkAnchor)
                        {
                            int weaponIndex = Math.Clamp(frameIndex, 0, WalkWeaponHandAnchors.Length - 1);
                            return WalkWeaponHandAnchors[weaponIndex];
                        }

                        int walkIndex = Math.Clamp(frameIndex, 0, WalkHandAnchors.Length - 1);
                        return WalkHandAnchors[walkIndex];
                    }

                case AnimationState.Jump:
                    {
                        int i = Math.Clamp(frameIndex, 0, JumpHandAnchors.Length - 1);
                        return JumpHandAnchors[i];
                    }

                case AnimationState.Fall:
                    {
                        int i = Math.Clamp(frameIndex, 0, FallHandAnchors.Length - 1);
                        return FallHandAnchors[i];
                    }

                case AnimationState.Idle:
                default:
                    {
                        int i = Math.Clamp(frameIndex, 0, IdleHandAnchors.Length - 1);
                        return IdleHandAnchors[i];
                    }
            }
        }

        public static Vector2 GetHandAnchor(Animator animator, bool useWeaponWalkAnchor)
        {
            return GetHandAnchor(animator.CurrentState, animator.FrameIndex, useWeaponWalkAnchor);
        }
    }
}
