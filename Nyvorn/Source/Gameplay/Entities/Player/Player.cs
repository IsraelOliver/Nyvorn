using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Gameplay.Combat.Interfaces;
using Nyvorn.Source.Gameplay.Combat.Weapons;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public class Player : IDamageable, IHitSource
    {
        private readonly PlayerCombat combat;
        private readonly PlayerMotor motor;
        private readonly PlayerAnimator playerAnimator;

        public Vector2 Position => motor.Position;
        Vector2 IDamageable.Position => Position;
        public bool HasActiveAttackHitbox => combat.HasActiveAttackHitbox;
        public Rectangle AttackHitbox => combat.AttackHitbox;
        public int AttackSequence => combat.AttackSequence;
        bool IHitSource.HasActiveHitbox => HasActiveAttackHitbox;
        Rectangle IHitSource.ActiveHitbox => AttackHitbox;
        int IHitSource.HitSequence => AttackSequence;
        public int Health => combat.Health;
        public int MaxHealth => combat.MaxHealth;
        public bool IsAlive => combat.IsAlive;
        public bool IsInvincible => combat.IsInvincible;

        private const float DodgeSpeed = 230f;

        public const int SpriteW = 32;
        public const int SpriteH = 32;
        public const int HitW = 10;
        public const int HitH = 23;

        private const float MoveSpeed = 90f;

        private int moveDir;
        private bool jumpPressed;

        private readonly Texture2D body;
        private readonly Texture2D handBack;
        private readonly Texture2D handFront;
        private readonly Texture2D attackHandBack;
        private readonly Texture2D attackHandFront;
        private readonly Texture2D attackBody;
        private readonly Texture2D legs;
        private readonly Texture2D handFrontWeaponRun;
        private readonly Texture2D dodgeTexture;
        private readonly Texture2D debugPixel;
        private Vector2 handWorld;

        public Player(
            Vector2 startPositionPivotFoot,
            Texture2D sheet,
            Texture2D handBackBase,
            Texture2D handFrontBase,
            Texture2D handBackAttack,
            Texture2D handFrontAttack,
            Texture2D bodyAttack,
            Texture2D legs,
            Texture2D handFrontWeaponRun,
            Texture2D dodgeTexture)
        {
            body = sheet;
            handBack = handBackBase;
            handFront = handFrontBase;
            this.legs = legs;
            attackHandBack = handBackAttack;
            attackHandFront = handFrontAttack;
            attackBody = bodyAttack;
            this.handFrontWeaponRun = handFrontWeaponRun;
            this.dodgeTexture = dodgeTexture;

            motor = new PlayerMotor(startPositionPivotFoot);
            Texture2D emptyWeaponTexture = new Texture2D(sheet.GraphicsDevice, 1, 1);
            emptyWeaponTexture.SetData(new[] { Color.Transparent });
            combat = new PlayerCombat(new NullWeapon(emptyWeaponTexture));
            playerAnimator = new PlayerAnimator();
            debugPixel = new Texture2D(sheet.GraphicsDevice, 1, 1);
            debugPixel.SetData(new[] { Color.Red });
        }

        public void Update(float dt, WorldMap worldMap, InputState input, Vector2 mouseWorld)
        {
            combat.Tick(dt);

            ApplyInput(input, mouseWorld);
            combat.UpdateDodge(dt);
            bool facingRight = playerAnimator.FacingRight;
            combat.UpdateAttack(dt, moveDir, ref facingRight);
            playerAnimator.SetFacing(facingRight);

            float horizontalVelocity = combat.IsDodging ? combat.DodgeDirection * DodgeSpeed : moveDir * MoveSpeed;
            motor.Update(dt, worldMap, horizontalVelocity);

            if (motor.IsGrounded && jumpPressed)
                motor.TryJump();

            playerAnimator.Update(dt, motor.Velocity, moveDir, motor.IsGrounded, combat.IsAttacking, combat.IsDodging);
            bool useAttackHandPose = combat.IsAttacking && combat.UsesAttackHandPose;
            bool useWeaponWalkAnchor = combat.HasVisibleWeaponEquipped && combat.UsesAttackHandPose && moveDir != 0 && motor.IsGrounded && !combat.IsAttacking;
            handWorld = playerAnimator.GetHandWorld(Position, useAttackHandPose, useWeaponWalkAnchor, combat.AttackAnimator);
            combat.UpdateAttackHitbox(handWorld, playerAnimator.FacingRight);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (combat.IsDodging)
            {
                DrawDodge(spriteBatch);
                return;
            }

            Rectangle src = playerAnimator.BaseFrame;
            Rectangle attackSrc = combat.AttackAnimator.CurrentFrame;
            SpriteEffects fx = playerAnimator.Effects;
            Vector2 drawPos = playerAnimator.GetDrawPosition(Position);
            bool isMoving = moveDir != 0;
            bool hasVisibleWeaponEquipped = combat.HasVisibleWeaponEquipped;
            bool useAttackHandPose = combat.IsAttacking && combat.UsesAttackHandPose;
            bool weaponRunPose = hasVisibleWeaponEquipped && combat.UsesAttackHandPose && isMoving && motor.IsGrounded && !combat.IsAttacking;
            Vector2 origin = new Vector2(16f, 32f);

            spriteBatch.Draw(legs, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);

            if (!useAttackHandPose)
            {
                spriteBatch.Draw(handBack, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(body, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
                if (weaponRunPose)
                {
                    combat.EquippedWeapon.SetWalk();
                    combat.EquippedWeapon.Draw(spriteBatch, handWorld, playerAnimator.FacingRight);
                    spriteBatch.Draw(handFrontWeaponRun, drawPos, new Rectangle(0, 0, 32, 32), Color.White, 0f, origin, 1f, fx, 0f);
                }
                else
                {
                    if (combat.IsAttacking)
                        combat.EquippedWeapon.SetAttackFrame(combat.AttackAnimator.FrameIndex);
                    else if (!motor.IsGrounded && motor.Velocity.Y > 0)
                        combat.EquippedWeapon.SetAttackFrame(0);
                    else if (moveDir != 0)
                        combat.EquippedWeapon.SetWalk();
                    else
                        combat.EquippedWeapon.SetIdle();

                    combat.EquippedWeapon.Draw(spriteBatch, handWorld, playerAnimator.FacingRight);
                    spriteBatch.Draw(handFront, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
                }

                if (hasVisibleWeaponEquipped)
                    spriteBatch.Draw(debugPixel, handWorld, Color.White);
                return;
            }

            spriteBatch.Draw(attackHandBack, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
            spriteBatch.Draw(attackBody, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
            combat.EquippedWeapon.SetAttackFrame(combat.AttackAnimator.FrameIndex);
            combat.EquippedWeapon.Draw(spriteBatch, handWorld, playerAnimator.FacingRight);
            spriteBatch.Draw(attackHandFront, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
            if (hasVisibleWeaponEquipped)
                spriteBatch.Draw(debugPixel, handWorld, Color.White);
        }

        public Rectangle Hurtbox => new Rectangle((int)HitLeft, (int)HitTop, HitW, HitH);
        private float HitLeft => Position.X - (HitW * 0.5f);
        private float HitTop => Position.Y - HitH + 1f;

        bool IDamageable.TryReceiveHit(Rectangle hitbox, int hitSequence, int damage)
        {
            return combat.TryReceiveHit(Hurtbox, hitbox, damage);
        }

        public bool TryReceiveDamage(int damage)
        {
            return combat.TryReceiveDamage(damage);
        }

        public void SetEquippedWeapon(Weapon weapon)
        {
            combat.SetEquippedWeapon(weapon);
        }

        public void ApplyKnockback(float forceX, float forceY = -60f)
        {
            motor.ApplyKnockback(forceX, forceY);
        }

        void IHitSource.OnHitConnected()
        {
        }

        private void ApplyInput(InputState input, Vector2 mouseWorld)
        {
            moveDir = input.MoveDir;
            jumpPressed = !combat.IsDodging && input.JumpPressed;

            if (input.DodgePressed && combat.TryStartDodge(motor.IsGrounded, input.DodgeDir, playerAnimator.FacingRight, out bool dodgeFacingRight))
                playerAnimator.SetFacing(dodgeFacingRight);

            if (input.AttackPressed && combat.TryStartAttack(Position, mouseWorld, out bool attackFacingRight))
                playerAnimator.SetFacing(attackFacingRight);
        }

        private void DrawDodge(SpriteBatch spriteBatch)
        {
            Rectangle src = combat.DodgeAnimator.CurrentFrame;
            SpriteEffects fx = playerAnimator.Effects;
            Vector2 drawPos = playerAnimator.GetDrawPosition(Position);
            Vector2 origin = new Vector2(16f, 32f);
            spriteBatch.Draw(dodgeTexture, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
        }
    }
}