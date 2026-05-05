using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public sealed class PlayerAnimator
    {
        private const float VisualFootSink = 0f;
        private const float GroundedAnimationGrace = 0.08f;
        private const float WalkSlopeFallVelocityThreshold = 45f;

        private readonly Dictionary<AnimationState, Animation> movementAnimations;
        private readonly Dictionary<AnimationState, Animation> upperMovementAnimations;
        private readonly Dictionary<AnimationState, Animation> upperCombatAnimations;
        private Animation movementAnimation;
        private Animation upperAnimation;
        private bool facingRight = true;
        private AnimationState movementState = AnimationState.Idle;
        private AnimationState upperState = AnimationState.Idle;
        private float groundedAnimationGraceTimer;
        private bool isVisuallyGrounded;

        public PlayerAnimator()
        {
            movementAnimations = PlayerAnimations.CreateLocomotion();
            upperMovementAnimations = PlayerAnimations.CreateLocomotion();
            upperCombatAnimations = PlayerAnimations.CreateUpperCombat();
            movementAnimation = movementAnimations[AnimationState.Idle];
            upperAnimation = upperMovementAnimations[AnimationState.Idle];
        }

        public bool FacingRight => facingRight;
        public AnimationState CurrentState => movementState;
        public AnimFrame MovementFrame => movementAnimation.GetCurrentFrame();
        public AnimFrame UpperFrame => upperAnimation.GetCurrentFrame();
        public int MovementFrameIndex => movementAnimation.CurrentFrameIndex;
        public int UpperFrameIndex => upperAnimation.CurrentFrameIndex;
        public SpriteEffects Effects => facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
        public bool IsVisuallyGrounded => isVisuallyGrounded;

        public void SetFacing(bool value)
        {
            facingRight = value;
        }

        public void Update(float dt, Vector2 velocity, int moveDir, bool isGrounded, bool isAttacking)
        {
            if (!isAttacking)
            {
                if (moveDir > 0)
                    facingRight = true;
                else if (moveDir < 0)
                    facingRight = false;
            }

            const float apexThreshold = 5f;
            bool groundedForAnimation = isGrounded;

            if (isGrounded)
                groundedAnimationGraceTimer = GroundedAnimationGrace;
            else
            {
                groundedAnimationGraceTimer = System.Math.Max(0f, groundedAnimationGraceTimer - dt);
                groundedForAnimation = groundedAnimationGraceTimer > 0f && velocity.Y > -apexThreshold;
            }

            if (!groundedForAnimation &&
                moveDir != 0 &&
                velocity.Y >= 0f &&
                velocity.Y <= WalkSlopeFallVelocityThreshold)
            {
                groundedForAnimation = true;
            }

            isVisuallyGrounded = groundedForAnimation;

            if (!groundedForAnimation)
            {
                if (velocity.Y < -apexThreshold)
                    PlayMovement(AnimationState.Jump);
                else if (velocity.Y > apexThreshold)
                    PlayMovement(AnimationState.Fall);
                else
                    PlayMovement(AnimationState.Jump);
            }
            else
            {
                PlayMovement(moveDir != 0 ? AnimationState.Walk : AnimationState.Idle);
            }

            movementAnimation.Update(dt);

            AnimationState nextUpperState = isAttacking ? AnimationState.Attack : movementState;
            PlayUpper(nextUpperState);
            upperAnimation.Update(dt);
        }

        public Vector2 GetDrawPosition(Vector2 position)
        {
            return new Vector2(
                (float)System.Math.Round(position.X),
                (float)System.Math.Round(position.Y + VisualFootSink));
        }

        public void DrawLowerBody(SpriteBatch spriteBatch, Texture2D texture, Vector2 playerRootPosition)
        {
            DrawLayer(spriteBatch, texture, MovementFrame, MovementFrame, playerRootPosition, Effects);
        }

        public void DrawUpperBody(SpriteBatch spriteBatch, Texture2D texture, Vector2 playerRootPosition)
        {
            // The upper body may use another animation, but it still receives the
            // movement offset so walk bounce keeps both body layers together.
            DrawLayer(spriteBatch, texture, UpperFrame, MovementFrame, playerRootPosition, Effects);
        }

        public void DrawLayer(SpriteBatch spriteBatch, Texture2D texture, AnimFrame layerFrame, AnimFrame movementFrame, Vector2 playerRootPosition, SpriteEffects flip)
        {
            Rectangle source = layerFrame.GetSourceRectangle(PlayerAnimations.FrameW, PlayerAnimations.FrameH);
            Vector2 drawPosition = GetLayerDrawPosition(playerRootPosition, layerFrame, movementFrame);
            spriteBatch.Draw(texture, drawPosition, source, Color.White, 0f, Vector2.Zero, 1f, flip, 0f);
        }

        public Vector2 GetHandWorld(Vector2 position, bool useWeaponWalkAnchor)
        {
            Vector2 root = GetDrawPosition(position);
            AnimFrame movementFrame = MovementFrame;
            AnimFrame upperFrame = UpperFrame;
            Vector2 frameTopLeft = GetLayerDrawPosition(root, upperFrame, movementFrame);
            Vector2 handLocal = PlayerAnimations.GetHandAnchor(upperState, UpperFrameIndex, useWeaponWalkAnchor && upperState != AnimationState.Attack);

            if (!facingRight)
                handLocal.X = 31 - handLocal.X;

            return frameTopLeft + handLocal;
        }

        private Vector2 GetLayerDrawPosition(Vector2 playerRootPosition, AnimFrame layerFrame, AnimFrame movementFrame)
        {
            // Player position is the foot root. Sprite top-left is derived from it.
            // OffsetX belongs to the drawn layer; OffsetY comes from locomotion so
            // lower body, upper body, and weapon sheets share the same ground bounce.
            return new Vector2(
                playerRootPosition.X - PlayerAnimations.PivotX + layerFrame.OffsetX,
                playerRootPosition.Y - PlayerAnimations.PivotY + movementFrame.OffsetY);
        }

        private void PlayMovement(AnimationState state)
        {
            if (state == movementState && movementAnimation != null)
                return;

            movementState = state;
            movementAnimation = movementAnimations[state];
            movementAnimation.Reset();
        }

        private void PlayUpper(AnimationState state)
        {
            if (state == upperState && upperAnimation != null)
                return;

            upperState = state;
            if (upperCombatAnimations.TryGetValue(state, out Animation combatAnimation))
                upperAnimation = combatAnimation;
            else
                upperAnimation = upperMovementAnimations[state];

            upperAnimation.Reset();
        }
    }
}
