using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Physics.Liquids;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Gameplay.Combat.Interfaces;
using Nyvorn.Source.Gameplay.Combat.Weapons;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public class Player : IDamageable, IHitSource
    {
        private const float DebugFlySpeed = 520f;
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
        public int MiningPower => combat.MiningPower;
        public float MiningSpeed => combat.MiningSpeed;
        public bool DebugFlyEnabled { get; private set; }
        public bool IsInWater { get; private set; }
        public bool IsAtWaterSurface { get; private set; }

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

        public void Update(float dt, WorldMap worldMap, SandSystem sandSystem, LiquidSystem liquidSystem, InputState input, Vector2 mouseWorld)
        {
            combat.Tick(dt);

            ApplyInput(input, mouseWorld);
            combat.UpdateDodge(dt);
            bool facingRight = playerAnimator.FacingRight;
            combat.UpdateAttack(dt, moveDir, ref facingRight);
            playerAnimator.SetFacing(facingRight);

            if (DebugFlyEnabled)
            {
                IsInWater = false;
                IsAtWaterSurface = false;
                motor.UpdateDebugFly(dt, worldMap, new Vector2(input.MoveDir, input.VerticalMoveDir), DebugFlySpeed);
            }
            else
            {
                IsInWater = IsTouchingWater(liquidSystem);
                float waterSurfaceY = 0f;
                bool hasWaterSurface = IsInWater && TryGetWaterSurfaceY(liquidSystem, out waterSurfaceY);
                IsAtWaterSurface = IsInWater && IsTouchingWaterSurface(liquidSystem);
                Vector2 waterMoveInput = IsInWater ? GetWaterMoveInput(input) : Vector2.Zero;
                float horizontalVelocity = combat.IsDodging ? combat.DodgeDirection * config.DodgeSpeed : moveDir * config.MoveSpeed;
                motor.Update(dt, worldMap, sandSystem, horizontalVelocity, combat.IsDodging, IsInWater, waterMoveInput, hasWaterSurface, waterSurfaceY);

                if (!IsInWater)
                    ApplyFallDamage(motor.LastLandingImpactVelocity);

                if (!IsInWater && motor.IsGrounded && jumpPressed)
                    motor.TryJump();
            }

            bool useUpperAttackPose = combat.IsAttacking && combat.UsesPlayerAttackUpperPose;
            playerAnimator.Update(dt, motor.Velocity, moveDir, motor.IsGrounded, useUpperAttackPose);
            bool useWeaponWalkAnchor = combat.HasVisibleWeaponEquipped && combat.UsesAttackHandPose && moveDir != 0 && playerAnimator.IsVisuallyGrounded && !combat.IsAttacking;
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
            else if (!playerAnimator.IsVisuallyGrounded && motor.Velocity.Y > 0)
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

                if (combat.EquippedWeapon.DrawsPlayerUpperBodyOverlay &&
                    combat.EquippedWeapon.PlayerUpperBodyOverlayTexture != null)
                {
                    playerAnimator.DrawLayer(
                        spriteBatch,
                        combat.EquippedWeapon.PlayerUpperBodyOverlayTexture,
                        weaponUpperFrame,
                        playerAnimator.MovementFrame,
                        drawPos,
                        playerAnimator.Effects,
                        combat.EquippedWeapon.FrameWidth,
                        combat.EquippedWeapon.FrameHeight);
                }
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

        public void TryStartToolUseAnimation(Vector2 mouseWorld)
        {
            if (combat.TryStartVisualAttack(Position, mouseWorld, out bool attackFacingRight))
                playerAnimator.SetFacing(attackFacingRight);
        }

        private void ApplyFallDamage(float landingVelocity)
        {
            if (landingVelocity <= config.FallDamageSafeVelocity || config.FallDamageMax <= 0)
                return;

            float damageRange = System.MathF.Max(1f, config.FallDamageFatalVelocity - config.FallDamageSafeVelocity);
            float damageRatio = MathHelper.Clamp((landingVelocity - config.FallDamageSafeVelocity) / damageRange, 0f, 1f);
            int damage = (int)System.MathF.Round(damageRatio * config.FallDamageMax);
            combat.TryReceiveFallDamage(damage);
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

        public void SetDebugFly(bool enabled)
        {
            if (DebugFlyEnabled == enabled)
                return;

            DebugFlyEnabled = enabled;
            motor.TeleportTo(Position);
        }

        public void RespawnAt(Vector2 targetPosition)
        {
            combat.Respawn();
            motor.TeleportTo(targetPosition);
            moveDir = 0;
            jumpPressed = false;
            IsInWater = false;
            IsAtWaterSurface = false;
        }

        void IHitSource.OnHitConnected()
        {
        }

        private void ApplyInput(InputState input, Vector2 mouseWorld)
        {
            moveDir = input.MoveDir;
            jumpPressed = !combat.IsDodging && input.JumpPressed;

            if (!DebugFlyEnabled && input.DodgePressed && combat.TryStartDodge(motor.IsGrounded, input.DodgeDir, playerAnimator.FacingRight, out bool dodgeFacingRight))
                playerAnimator.SetFacing(dodgeFacingRight);

            if (input.AttackPressed && combat.TryStartAttack(Position, mouseWorld, out bool attackFacingRight))
                playerAnimator.SetFacing(attackFacingRight);
        }

        private Vector2 GetWaterMoveInput(InputState input)
        {
            int verticalDir = 0;
            if (jumpPressed)
                verticalDir = -1;
            else if (input.VerticalMoveDir > 0)
                verticalDir = 1;

            return new Vector2(moveDir, verticalDir);
        }

        private bool IsTouchingWater(LiquidSystem liquidSystem)
        {
            if (liquidSystem == null)
                return false;

            Rectangle hurtbox = motor.Hurtbox;
            int probeHeight = System.Math.Max(4, (int)System.MathF.Ceiling(hurtbox.Height * 0.65f));
            int probeY = hurtbox.Bottom - probeHeight;

            return liquidSystem.HasLiquidInRectangle(hurtbox.X, probeY, hurtbox.Width, probeHeight);
        }

        private bool IsTouchingWaterSurface(LiquidSystem liquidSystem)
        {
            if (liquidSystem == null)
                return false;

            Rectangle hurtbox = motor.Hurtbox;
            int headProbeHeight = System.Math.Max(4, (int)System.MathF.Ceiling(hurtbox.Height * 0.35f));
            return !liquidSystem.HasLiquidInRectangle(hurtbox.X, hurtbox.Y, hurtbox.Width, headProbeHeight);
        }

        private bool TryGetWaterSurfaceY(LiquidSystem liquidSystem, out float surfaceY)
        {
            surfaceY = 0f;
            if (liquidSystem == null)
                return false;

            Rectangle hurtbox = motor.Hurtbox;
            for (int y = hurtbox.Y; y < hurtbox.Bottom; y++)
            {
                if (liquidSystem.HasLiquidInRectangle(hurtbox.X, y, hurtbox.Width, 1))
                {
                    surfaceY = y;
                    return true;
                }
            }

            return false;
        }

        private void DrawDodge(SpriteBatch spriteBatch)
        {
            Vector2 drawPos = playerAnimator.GetDrawPosition(VisualPosition);
            playerAnimator.DrawLowerBody(spriteBatch, lowerBody, drawPos);
            playerAnimator.DrawUpperBody(spriteBatch, upperBody, drawPos);
        }
    }
}
