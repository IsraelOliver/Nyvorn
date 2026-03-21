using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public sealed class PlayerAnimator
    {
        private const float VisualFootSink = 1f;

        private readonly Animator baseAnimator;
        private bool facingRight = true;
        private AnimationState animState = AnimationState.Idle;

        public PlayerAnimator()
        {
            baseAnimator = new Animator(PlayerAnimations.CreateBase(), AnimationState.Idle);
        }

        public bool FacingRight => facingRight;
        public AnimationState CurrentState => animState;
        public Rectangle BaseFrame => baseAnimator.CurrentFrame;
        public SpriteEffects Effects => facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

        public void SetFacing(bool value)
        {
            facingRight = value;
        }

        public void Update(float dt, Vector2 velocity, int moveDir, bool isGrounded, bool isAttacking, bool isDodging)
        {
            if (isDodging)
            {
                animState = AnimationState.Dodge;
                return;
            }

            if (!isAttacking)
            {
                if (moveDir > 0)
                    facingRight = true;
                else if (moveDir < 0)
                    facingRight = false;
            }

            const float apexThreshold = 5f;

            if (!isGrounded)
            {
                if (velocity.Y < -apexThreshold)
                    animState = AnimationState.Jump;
                else if (velocity.Y > apexThreshold)
                    animState = AnimationState.Fall;
                else
                    animState = AnimationState.Jump;
            }
            else
            {
                animState = moveDir != 0 ? AnimationState.Walk : AnimationState.Idle;
            }

            baseAnimator.Play(animState);
            baseAnimator.Update(dt);
        }

        public Vector2 GetDrawPosition(Vector2 position)
        {
            return new Vector2(
                (float)System.Math.Round(position.X),
                (float)System.Math.Round(position.Y + VisualFootSink));
        }

        public Vector2 GetHandWorld(Vector2 position, bool useAttackHandPose, bool useWeaponWalkAnchor, Animator attackAnimator)
        {
            Vector2 drawPos = GetDrawPosition(position);
            Vector2 origin = new Vector2(16f, 32f);
            Vector2 frameTopLeft = drawPos - origin;
            Animator activeAnimator = useAttackHandPose ? attackAnimator : baseAnimator;
            Vector2 handLocal = PlayerAnimations.GetHandAnchor(activeAnimator, useWeaponWalkAnchor && !useAttackHandPose);

            if (!facingRight)
                handLocal.X = 31 - handLocal.X;

            return frameTopLeft + handLocal;
        }
    }
}
