using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public static class PlayerAnimations
    {
        public const int FrameW = 32;
        public const int FrameH = 32;
        public const int PivotX = 16;
        public const int PivotY = 31;

        private const float WalkFrameDuration = 0.13f;
        private const float DefaultFrameDuration = 0.13f;
        private const float AttackFrameDuration = 0.075f;

        private static AnimFrame Frame(int column, int row, int offsetX = 0, int offsetY = 0, float duration = DefaultFrameDuration)
        {
            return new AnimFrame(row, column, offsetX, offsetY, duration);
        }

        public static Dictionary<AnimationState, Animation> CreateLocomotion()
        {
            return new Dictionary<AnimationState, Animation>
            {
                {
                    AnimationState.Idle,
                    new Animation(new[] { Frame(0, 1) })
                },

                {
                    AnimationState.Walk,
                    new Animation(new[]
                    {
                        Frame(0, 0, offsetY: -2, duration: WalkFrameDuration),
                        Frame(1, 0, offsetY: -1, duration: WalkFrameDuration),
                        Frame(2, 0, offsetY: 0, duration: WalkFrameDuration),
                        Frame(3, 0, offsetY: -2, duration: WalkFrameDuration),
                        Frame(4, 0, offsetY: -1, duration: WalkFrameDuration),
                        Frame(5, 0, offsetY: 0, duration: WalkFrameDuration)
                    })
                },

                {
                    AnimationState.Jump,
                    new Animation(new[] { Frame(1, 1) })
                },

                {
                    AnimationState.Fall,
                    new Animation(new[] { Frame(2, 1) })
                },
            };
        }

        public static Dictionary<AnimationState, Animation> CreateUpperCombat()
        {
            return new Dictionary<AnimationState, Animation>
            {
                {
                    AnimationState.Attack,
                    new Animation(new[]
                    {
                        Frame(0, 0, duration: AttackFrameDuration),
                        Frame(1, 0, duration: AttackFrameDuration),
                        Frame(2, 0, duration: AttackFrameDuration),
                        Frame(3, 0, duration: AttackFrameDuration)
                    }, loop: false)
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
            new Vector2(14, 24),
            new Vector2(12, 22)
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

    }
}
