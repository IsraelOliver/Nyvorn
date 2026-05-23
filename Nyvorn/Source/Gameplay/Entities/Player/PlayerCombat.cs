using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Combat.Weapons;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public sealed class PlayerCombat
    {
        private readonly PlayerConfig config;
        private readonly Animation attackAnimation;
        private Weapon equippedWeapon;

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
        private bool attackHitboxEnabled;

        public PlayerCombat(Weapon equippedWeapon, PlayerConfig config)
        {
            this.config = config;
            this.equippedWeapon = equippedWeapon;
            attackAnimation = PlayerAnimations.CreateUpperCombat()[AnimationState.Attack];

            attackSequence = 0;
            dodgeDir = 1;
            health = config.MaxHealth;
            attackHitbox = Rectangle.Empty;
            attackHitboxEnabled = false;
        }

        public Weapon EquippedWeapon => equippedWeapon;
        public Animation AttackAnimation => attackAnimation;
        public bool IsAttacking => isAttacking;
        public bool IsDodging => isDodging;
        public bool IsInvincible => isDodging;
        public int DodgeDirection => dodgeDir;
        public bool HasVisibleWeaponEquipped => equippedWeapon != null && equippedWeapon.IsVisibleInHand;
        public bool UsesAttackHandPose => equippedWeapon != null && equippedWeapon.UsesAttackHandPose;
        public bool UsesPlayerAttackUpperPose => equippedWeapon != null && equippedWeapon.UsesPlayerAttackUpperPose;
        public float? WorldBreakRangeOverride => equippedWeapon?.WorldBreakRangeOverride;
        public int MiningPower => equippedWeapon?.MiningPower ?? 0;
        public float MiningSpeed => equippedWeapon?.MiningSpeed ?? 0f;
        public int HitDamage => equippedWeapon?.HitDamage ?? 1;
        public float HitKnockbackX => equippedWeapon?.HitKnockbackX ?? 80f;
        public float HitKnockbackY => equippedWeapon?.HitKnockbackY ?? -35f;
        public bool HasActiveAttackHitbox => !attackHitbox.IsEmpty;
        public Rectangle AttackHitbox => attackHitbox;
        public int AttackSequence => attackSequence;
        public int Health => health;
        public int MaxHealth => config.MaxHealth;
        public bool IsAlive => health > 0;

        public void Tick(float dt)
        {
            TickCooldowns(dt);
        }

        public void SetEquippedWeapon(Weapon weapon)
        {
            if (weapon != null)
                equippedWeapon = weapon;
        }

        public bool TryStartAttack(Vector2 playerPosition, Vector2 mouseWorld, out bool attackFacingRight)
        {
            attackFacingRight = mouseWorld.X >= playerPosition.X;

            if (!CanStartAttack())
                return false;

            StartAttack(enableHitbox: true);
            attackSequence++;
            return true;
        }

        public bool TryStartVisualAttack(Vector2 playerPosition, Vector2 mouseWorld, out bool attackFacingRight)
        {
            attackFacingRight = mouseWorld.X >= playerPosition.X;

            if (!CanStartAttack())
                return false;

            StartAttack(enableHitbox: false);
            return true;
        }

        public void UpdateAttack(float dt, int moveDir, ref bool facingRight)
        {
            if (isDodging || !isAttacking)
                return;

            attackTimer -= dt;
            attackAnimation.Update(dt);

            if (attackTimer > 0f)
                return;

            FinishAttack(moveDir, ref facingRight);
        }

        public void UpdateAttackHitbox(Vector2 handWorld, bool facingRight)
        {
            attackHitbox = Rectangle.Empty;

            if (!CanUseAttackHitbox())
                return;

            attackHitbox = equippedWeapon.GetAttackHitbox(handWorld, facingRight);
        }

        public bool TryStartDodge(bool isGrounded, int inputDodgeDir, bool currentFacingRight, out bool dodgeFacingRight)
        {
            dodgeFacingRight = currentFacingRight;

            if (!CanStartDodge(isGrounded))
                return false;

            StartDodge(inputDodgeDir, currentFacingRight, out dodgeFacingRight);
            return true;
        }

        public void UpdateDodge(float dt)
        {
            if (!isDodging)
                return;

            dodgeTimer -= dt;
            if (dodgeTimer > 0f)
                return;

            isDodging = false;
            dodgeTimer = 0f;
        }

        public bool TryReceiveHit(Rectangle hurtbox, Rectangle hitbox, int damage)
        {
            if (!hitbox.Intersects(hurtbox))
                return false;

            return TryReceiveDamage(damage);
        }

        public bool TryReceiveDamage(int damage)
        {
            if (!CanReceiveDamage())
                return false;

            health = System.Math.Max(0, health - damage);
            hurtCooldownTimer = config.HurtCooldown;
            return true;
        }

        private void TickCooldowns(float dt)
        {
            if (hurtCooldownTimer > 0f)
                hurtCooldownTimer -= dt;
            if (dodgeCooldownTimer > 0f)
                dodgeCooldownTimer -= dt;
        }

        private bool CanStartAttack()
        {
            if (equippedWeapon == null || !equippedWeapon.CanAttack)
                return false;

            return !isDodging && !isAttacking;
        }

        private void StartAttack(bool enableHitbox)
        {
            isAttacking = true;
            attackTimer = equippedWeapon.AttackDuration;
            attackHitboxEnabled = enableHitbox;
            attackAnimation.Reset();
            attackHitbox = Rectangle.Empty;
        }

        private void FinishAttack(int moveDir, ref bool facingRight)
        {
            isAttacking = false;
            attackTimer = 0f;
            attackHitbox = Rectangle.Empty;
            attackHitboxEnabled = false;

            if (moveDir != 0)
                facingRight = moveDir > 0;
        }

        private bool CanUseAttackHitbox()
        {
            if (isDodging || !isAttacking || !attackHitboxEnabled || equippedWeapon == null)
                return false;

            return equippedWeapon.IsActiveFrame(attackAnimation.CurrentFrameIndex);
        }

        private bool CanStartDodge(bool isGrounded)
        {
            if (!isGrounded)
                return false;

            if (isDodging || isAttacking)
                return false;

            return dodgeCooldownTimer <= 0f;
        }

        private void StartDodge(int inputDodgeDir, bool currentFacingRight, out bool dodgeFacingRight)
        {
            dodgeDir = inputDodgeDir != 0 ? inputDodgeDir : (currentFacingRight ? 1 : -1);
            dodgeFacingRight = dodgeDir > 0;
            isDodging = true;
            dodgeTimer = config.DodgeDuration;
            dodgeCooldownTimer = config.DodgeCooldown;
        }

        private bool CanReceiveDamage()
        {
            if (!IsAlive)
                return false;

            if (IsInvincible)
                return false;

            return hurtCooldownTimer <= 0f;
        }
    }
}
