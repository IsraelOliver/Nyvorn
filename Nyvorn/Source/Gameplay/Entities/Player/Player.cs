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
        int IHitSource.HitDamage => combat.HitDamage;
        float IHitSource.HitKnockbackX => combat.HitKnockbackX;
        float IHitSource.HitKnockbackY => combat.HitKnockbackY;
        public int Health => combat.Health;
        public int MaxHealth => combat.MaxHealth;
        public bool IsAlive => combat.IsAlive;
        public bool IsInvincible => combat.IsInvincible;
        public float WorldInteractionRange => config.WorldInteractionRange;
        public float WorldBreakRange => combat.WorldBreakRangeOverride ?? config.WorldInteractionRange;

        public const int SpriteW = 32;
        public const int SpriteH = 32;

        private int moveDir;
        private bool jumpPressed;

        private readonly Texture2D lowerBody;
        private readonly Texture2D upperBody;
        private Vector2 handWorld;

        public Player(
            Vector2 startPositionPivotFoot,
            Texture2D playerDown,
            Texture2D playerUp,
            PlayerConfig config = null)
        {
            this.config = config ?? PlayerConfig.Default;
            lowerBody = playerDown;
            upperBody = playerUp;

            motor = new PlayerMotor(startPositionPivotFoot, this.config);
            Texture2D emptyWeaponTexture = new Texture2D(playerUp.GraphicsDevice, 1, 1);
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
            motor.Update(dt, worldMap, horizontalVelocity, combat.IsDodging);

            if (motor.IsGrounded && jumpPressed)
                motor.TryJump();

            bool useUpperAttackPose = combat.IsAttacking && combat.UsesPlayerAttackUpperPose;
            playerAnimator.Update(dt, motor.Velocity, moveDir, motor.IsGrounded, useUpperAttackPose);
            bool useWeaponWalkAnchor = combat.HasVisibleWeaponEquipped && combat.UsesAttackHandPose && moveDir != 0 && motor.IsGrounded && !combat.IsAttacking;
            handWorld = playerAnimator.GetHandWorld(VisualPosition, useWeaponWalkAnchor);
            combat.EquippedWeapon.UpdateAim(handWorld, mouseWorld);
            combat.UpdateAttackHitbox(handWorld, playerAnimator.FacingRight);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (combat.IsDodging)
            {
                DrawDodge(spriteBatch);
                return;
            }

            Vector2 drawPos = playerAnimator.GetDrawPosition(VisualPosition);
            bool isMoving = moveDir != 0;

            playerAnimator.DrawLowerBody(spriteBatch, lowerBody, drawPos);

            if (combat.IsAttacking)
                combat.EquippedWeapon.SetAttackFrame(combat.AttackAnimation.CurrentFrameIndex);
            else if (!motor.IsGrounded && motor.Velocity.Y > 0)
                combat.EquippedWeapon.SetAttackFrame(0);
            else if (isMoving)
                combat.EquippedWeapon.SetWalk();
            else
                combat.EquippedWeapon.SetIdle();

            if (combat.EquippedWeapon.ReplacesPlayerUpperBody)
            {
                AnimFrame weaponUpperFrame = combat.EquippedWeapon.GetPlayerUpperBodyFrame(
                    playerAnimator.CurrentState,
                    playerAnimator.MovementFrameIndex,
                    combat.AttackAnimation.CurrentFrameIndex,
                    combat.IsAttacking);

                playerAnimator.DrawLayer(
                    spriteBatch,
                    combat.EquippedWeapon.PlayerUpperBodyTexture,
                    weaponUpperFrame,
                    playerAnimator.MovementFrame,
                    drawPos,
                    playerAnimator.Effects);
            }
            else
            {
                playerAnimator.DrawUpperBody(spriteBatch, upperBody, drawPos);
                if (combat.EquippedWeapon.DrawsWithPlayerRoot)
                    combat.EquippedWeapon.Draw(spriteBatch, handWorld, drawPos, playerAnimator.MovementFrame, playerAnimator.FacingRight);
                else
                    combat.EquippedWeapon.Draw(spriteBatch, handWorld, playerAnimator.FacingRight);
            }
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

        public void ShiftX(float deltaX)
        {
            motor.Position = new Vector2(motor.Position.X + deltaX, motor.Position.Y);
        }

        public void TeleportTo(Vector2 targetPosition)
        {
            motor.TeleportTo(targetPosition);
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
            Vector2 drawPos = playerAnimator.GetDrawPosition(VisualPosition);
            playerAnimator.DrawLowerBody(spriteBatch, lowerBody, drawPos);
            playerAnimator.DrawUpperBody(spriteBatch, upperBody, drawPos);
        }
    }
}
