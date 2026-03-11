using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Combat.Weapons;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public sealed class PlayerCombat
    {
        private const float AttackDuration = 0.3f;
        private const int DodgeFrames = 7;
        private const float DodgeFrameTime = 0.05f;
        private const float DodgeDuration = DodgeFrames * DodgeFrameTime;
        private const float DodgeCooldown = 0.30f;
        private const float HurtCooldown = 0.35f;
        private const int MaxHealthValue = 100;

        private Weapon equippedWeapon;
        private readonly Animator attackAnimator;
        private readonly Animator dodgeAnimator;

        private bool isAttacking;
        private bool isDodging;
        private float attackTimer;
        private float dodgeTimer;
        private float dodgeCooldownTimer;
        private float hurtCooldownTimer;
        private int attackSequence;
        private int dodgeDir;
        private int health;
        private Rectangle attackHitbox;

        public PlayerCombat(Weapon equippedWeapon)
        {
            this.equippedWeapon = equippedWeapon;
            attackAnimator = new Animator(PlayerAnimations.CreateAttackShortSword(), AnimationState.Attack);
            dodgeAnimator = new Animator(PlayerAnimations.CreateDodge(), PlayerAnimations.CreateDodgeFrameTimes(), AnimationState.Dodge)
            {
                FrameTime = DodgeFrameTime
            };

            attackSequence = 0;
            dodgeDir = 1;
            health = MaxHealthValue;
            attackHitbox = Rectangle.Empty;
        }

        public Weapon EquippedWeapon => equippedWeapon;
        public Animator AttackAnimator => attackAnimator;
        public Animator DodgeAnimator => dodgeAnimator;
        public bool IsAttacking => isAttacking;
        public bool IsDodging => isDodging;
        public bool IsInvincible => isDodging;
        public int DodgeDirection => dodgeDir;
        public bool HasVisibleWeaponEquipped => equippedWeapon != null && equippedWeapon.IsVisibleInHand;
        public bool UsesAttackHandPose => equippedWeapon != null && equippedWeapon.UsesAttackHandPose;
        public bool HasActiveAttackHitbox => !attackHitbox.IsEmpty;
        public Rectangle AttackHitbox => attackHitbox;
        public int AttackSequence => attackSequence;
        public int Health => health;
        public int MaxHealth => MaxHealthValue;
        public bool IsAlive => health > 0;

        public void Tick(float dt)
        {
            if (hurtCooldownTimer > 0f)
                hurtCooldownTimer -= dt;
            if (dodgeCooldownTimer > 0f)
                dodgeCooldownTimer -= dt;
        }

        public bool TryStartAttack(Vector2 playerPosition, Vector2 mouseWorld, out bool attackFacingRight)
        {
            attackFacingRight = mouseWorld.X >= playerPosition.X;

            if (equippedWeapon == null || !equippedWeapon.CanAttack)
                return false;

            if (isDodging || isAttacking)
                return false;

            isAttacking = true;
            attackTimer = AttackDuration;
            attackSequence++;

            attackAnimator.Reset();
            attackAnimator.Play(AnimationState.Attack);
            return true;
        }

        public void SetEquippedWeapon(Weapon weapon)
        {
            equippedWeapon = weapon;
        }

        public bool TryStartDodge(bool isGrounded, int inputDodgeDir, bool currentFacingRight, out bool dodgeFacingRight)
        {
            dodgeFacingRight = currentFacingRight;

            if (!isGrounded)
                return false;

            if (isDodging || isAttacking)
                return false;

            if (dodgeCooldownTimer > 0f)
                return false;

            dodgeDir = inputDodgeDir != 0 ? inputDodgeDir : (currentFacingRight ? 1 : -1);
            dodgeFacingRight = dodgeDir > 0;
            isDodging = true;
            dodgeTimer = DodgeDuration;
            dodgeCooldownTimer = DodgeCooldown;
            dodgeAnimator.Reset();
            dodgeAnimator.Play(AnimationState.Dodge);
            return true;
        }

        public void UpdateAttack(float dt, int moveDir, ref bool facingRight)
        {
            if (isDodging || !isAttacking)
                return;

            attackTimer -= dt;
            attackAnimator.Update(dt);

            if (attackTimer > 0f)
                return;

            isAttacking = false;
            if (moveDir != 0)
                facingRight = moveDir > 0;
        }

        public void UpdateDodge(float dt)
        {
            if (!isDodging)
                return;

            dodgeTimer -= dt;
            dodgeAnimator.Play(AnimationState.Dodge);
            dodgeAnimator.Update(dt);

            if (dodgeTimer > 0f)
                return;

            isDodging = false;
            dodgeTimer = 0f;
        }

        public void UpdateAttackHitbox(Vector2 handWorld, bool facingRight)
        {
            attackHitbox = Rectangle.Empty;

            if (isDodging || !isAttacking)
                return;

            if (!equippedWeapon.IsActiveFrame(attackAnimator.FrameIndex))
                return;

            attackHitbox = equippedWeapon.GetAttackHitbox(handWorld, facingRight);
        }

        public bool TryReceiveHit(Rectangle hurtbox, Rectangle hitbox, int damage)
        {
            if (!hitbox.Intersects(hurtbox))
                return false;

            return TryReceiveDamage(damage);
        }

        public bool TryReceiveDamage(int damage)
        {
            if (!IsAlive)
                return false;

            if (IsInvincible)
                return false;

            if (hurtCooldownTimer > 0f)
                return false;

            health = System.Math.Max(0, health - damage);
            hurtCooldownTimer = HurtCooldown;
            return true;
        }
    }
}
