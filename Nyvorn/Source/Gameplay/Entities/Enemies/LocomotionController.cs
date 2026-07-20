using Nyvorn.Source.Gameplay.Entities.Enemies.AI;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Enemies
{
    // Translates a brain's intent into motor input and drives the resulting animation state.
    // Owns the Fighter-AI-style auto-jump: walking into something solid while grounded jumps,
    // exactly like Terraria's aiStyle 3 (no ledge/pit awareness - jumping "through" a pit bridged
    // by platforms is a known, accepted quirk of this locomotion style, not a bug to fix here).
    public sealed class LocomotionController
    {
        public void Apply(
            EnemyMotor motor,
            EnemyAnimator animator,
            EnemyCombat combat,
            in EnemyBrainDecision decision,
            in Perception perception,
            float dt,
            WorldMap worldMap)
        {
            bool wantsJump = decision.WantsJump || (perception.BlockedHorizontally && perception.OnGround);
            motor.Update(dt, worldMap, decision.MoveVelocityX, wantsJump);

            EnemyAnimState state = ResolveAnimState(motor, combat, decision.Intent);
            animator.Play(state);
            animator.Update(dt);
        }

        private static EnemyAnimState ResolveAnimState(EnemyMotor motor, EnemyCombat combat, EnemyIntent intent)
        {
            if (!combat.IsAlive)
                return EnemyAnimState.Dead;

            if (combat.HurtTimer > 0f)
                return EnemyAnimState.Hurt;

            if (combat.AttackTimer > 0f)
                return EnemyAnimState.Attack;

            if (!motor.OnGround)
                return motor.VelocityY < 0f ? EnemyAnimState.Jump : EnemyAnimState.Fall;

            bool isMoving = System.Math.Abs(motor.HorizontalVelocityX) > 8f
                || intent == EnemyIntent.Chase
                || intent == EnemyIntent.Investigate
                || intent == EnemyIntent.Retreat;

            return isMoving ? EnemyAnimState.Move : EnemyAnimState.Idle;
        }
    }
}
