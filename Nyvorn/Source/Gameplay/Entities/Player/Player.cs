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
        private readonly PlayerConfig config;
        private readonly PlayerCombat combat;
        private readonly PlayerMotor motor;
        private readonly PlayerAnimator playerAnimator;

        public Vector2 Position => motor.Position;
        private Vector2 VisualPosition => motor.VisualPosition;
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
        public float WorldInteractionRange => config.WorldInteractionRange;

        public const int SpriteW = 32;
        public const int SpriteH = 32;

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
            Texture2D dodgeTexture,
            PlayerConfig config = null)
        {
            this.config = config ?? PlayerConfig.Default;
            body = sheet;
            handBack = handBackBase;
            handFront = handFrontBase;
            this.legs = legs;
            attackHandBack = handBackAttack;
            attackHandFront = handFrontAttack;
            attackBody = bodyAttack;
            this.handFrontWeaponRun = handFrontWeaponRun;
            this.dodgeTexture = dodgeTexture;

            motor = new PlayerMotor(startPositionPivotFoot, this.config);
            Texture2D emptyWeaponTexture = new Texture2D(sheet.GraphicsDevice, 1, 1);
            emptyWeaponTexture.SetData(new[] { Color.Transparent });
            combat = new PlayerCombat(new HandWeapon(emptyWeaponTexture), this.config);
            playerAnimator = new PlayerAnimator();
        }

        public void Update(float dt, WorldMap worldMap, InputState input, Vector2 mouseWorld)
        {
            combat.Tick(dt);

            ApplyInput(input, mouseWorld);
            combat.UpdateDodge(dt);
            bool facingRight = playerAnimator.FacingRight;
            combat.UpdateAttack(dt, moveDir, ref facingRight);
            playerAnimator.SetFacing(facingRight);

            float horizontalVelocity = combat.IsDodging ? combat.DodgeDirection * config.DodgeSpeed : moveDir * config.MoveSpeed;
            motor.Update(dt, worldMap, horizontalVelocity);

            if (motor.IsGrounded && jumpPressed)
                motor.TryJump();

            playerAnimator.Update(dt, motor.Velocity, moveDir, motor.IsGrounded, combat.IsAttacking, combat.IsDodging);
            bool useAttackHandPose = combat.IsAttacking && combat.UsesAttackHandPose;
            bool useWeaponWalkAnchor = combat.HasVisibleWeaponEquipped && combat.UsesAttackHandPose && moveDir != 0 && motor.IsGrounded && !combat.IsAttacking;
            handWorld = playerAnimator.GetHandWorld(VisualPosition, useAttackHandPose, useWeaponWalkAnchor, combat.AttackAnimator);
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
            Vector2 drawPos = playerAnimator.GetDrawPosition(VisualPosition);
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

                return;
            }

            spriteBatch.Draw(attackHandBack, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
            spriteBatch.Draw(attackBody, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
            combat.EquippedWeapon.SetAttackFrame(combat.AttackAnimator.FrameIndex);
            combat.EquippedWeapon.Draw(spriteBatch, handWorld, playerAnimator.FacingRight);
            spriteBatch.Draw(attackHandFront, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
        }

        public Rectangle Hurtbox => motor.Hurtbox;

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

        public bool CanBreakTile(TileType tileType)
        {
            return combat.EquippedWeapon.CanBreakTile(tileType);
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
            Vector2 drawPos = playerAnimator.GetDrawPosition(VisualPosition);
            Vector2 origin = new Vector2(16f, 32f);
            spriteBatch.Draw(dodgeTexture, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
        }
    }
}
