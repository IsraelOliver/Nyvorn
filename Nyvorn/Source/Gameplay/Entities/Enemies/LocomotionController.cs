using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Gameplay.Entities.Enemies.AI;
using Nyvorn.Source.World;
using System;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    // Translates a brain's intent into motor input and drives the resulting animation state.
    // Owns the Fighter-AI-style auto-jump: walking into something solid while grounded jumps,
    // exactly like Terraria's aiStyle 3 (no ledge/pit awareness - jumping "through" a pit bridged
    // by platforms is a known, accepted quirk of this locomotion style, not a bug to fix here).
    public sealed class LocomotionController
    {
        // If the player is airborne roughly overhead, jump to try to block them from hopping over -
        // brain-agnostic (reads Perception directly) so every enemy gets this for free regardless of
        // which brain it uses.
        private const float JumpBlockHorizontalRange = 26f;
        private const float JumpBlockMinHeightAbove = 12f;

        public void Apply(
            EnemyMotor motor,
            EnemyAnimator animator,
            EnemyCombat combat,
            in EnemyBrainDecision decision,
            in Perception perception,
            float dt,
            WorldMap worldMap,
            SandSystem sandSystem)
        {
            bool wantsJumpOverhead = perception.SeesTarget
                && perception.TargetOffset.Y <= -JumpBlockMinHeightAbove
                && System.MathF.Abs(perception.TargetOffset.X) <= JumpBlockHorizontalRange;

            bool wantsJump = decision.WantsJump || wantsJumpOverhead || (perception.BlockedHorizontally && perception.OnGround);
            motor.Update(dt, worldMap, sandSystem, decision.MoveVelocityX, wantsJump);

            UpdateFacing(animator, decision, perception);

            EnemyAnimState state = ResolveAnimState(motor, combat);
            animator.Play(state);
            animator.Update(dt);
        }

        // Faces the player only while actually chasing (so it turns to look at you during Attack,
        // not just while Chase is moving it) - Perception.SeesTarget alone isn't enough to gate this,
        // since it just tracks proximity and stays true during passive daytime wander whenever the
        // player happens to be nearby, which made the enemy always face you instead of its own
        // wander direction. Anything that isn't Chase always faces its movement direction.
        private static void UpdateFacing(EnemyAnimator animator, in EnemyBrainDecision decision, in Perception perception)
        {
            if (decision.Intent == EnemyIntent.Chase && perception.SeesTarget && MathF.Abs(perception.TargetOffset.X) > 0.01f)
            {
                animator.SetFacing(perception.TargetOffset.X >= 0f);
                return;
            }

            if (MathF.Abs(decision.MoveVelocityX) > 0.01f)
                animator.SetFacing(decision.MoveVelocityX >= 0f);
        }

        private static EnemyAnimState ResolveAnimState(EnemyMotor motor, EnemyCombat combat)
        {
            if (!combat.IsAlive)
                return EnemyAnimState.Dead;

            if (combat.HurtTimer > 0f)
                return EnemyAnimState.Hurt;

            if (combat.AttackTimer > 0f)
                return EnemyAnimState.Attack;

            if (!motor.OnGround)
                return motor.VelocityY < 0f ? EnemyAnimState.Jump : EnemyAnimState.Fall;

            // Tied to actual velocity, not intent - showing "Move" while blocked/stopped (intent
            // still Chase, but real velocity 0) is exactly what made the enemy look stuck earlier.
            bool isMoving = System.Math.Abs(motor.HorizontalVelocityX) > 8f;
            return isMoving ? EnemyAnimState.Move : EnemyAnimState.Idle;
        }
    }
}
