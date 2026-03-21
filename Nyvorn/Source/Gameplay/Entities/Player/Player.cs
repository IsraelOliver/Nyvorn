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
<<<<<<< HEAD
        public Vector2 Position; //é o pivot do pé
        private Vector2 Velocity;

        private bool isGrounded;
        private bool isAttacking;
        private float attackTimer;
        private MouseState prevMouse;
        private const float AttackDuration = 0.3f;

        // Textura do player
        public const int SpriteW = 32;
        public const int SpriteH = 32;

        private readonly Texture2D _body;
        private readonly Texture2D _handBack;
        private readonly Texture2D _handFront;

        // Hitbox do player - fica envolta apenas do player
        public const int HitW = 10;
        public const int HitH = 23;

        private const float moveSpeed = 90f;
        private const float jumpSpeed = 280f;
        private const float gravity = 800f;
=======
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
>>>>>>> 06a0242ea9d5e0753e26f589eb466b0d3ef40484

        private int moveDir;
        private bool jumpPressed;

<<<<<<< HEAD
        // Animação
        private bool facingRight = true;

        private readonly Animator anim;
        private readonly Animator animAttack;

        private AnimationState animState = AnimationState.Idle;

        private readonly Texture2D _attackHandBack;
        private readonly Texture2D _attackHandFront;
        private readonly Texture2D _attackBody;
        private readonly Texture2D _legs;

        public Player(Vector2 startPositionPivotFoot, Texture2D sheet, Texture2D handBack_base, Texture2D handFront_base, Texture2D handBack_attack, Texture2D handFront_attack, Texture2D body_attack, Texture2D legs)
        {
            Position = startPositionPivotFoot;
            Velocity = Vector2.Zero;
            isGrounded = false;

            _body = sheet;
            _handBack = handBack_base;
            _handFront = handFront_base;
            _legs = legs;

            _attackHandBack = handBack_attack;
            _attackHandFront = handFront_attack;
            _attackBody = body_attack;

            anim = new Animator(PlayerAnimations.CreateBase(), AnimationState.Idle);
            animAttack = new Animator(PlayerAnimations.CreateAttackShortSword(), AnimationState.Attack);
        }

        private float HitLeft   => Position.X - (HitW * 0.5f);
        private float HitRight  => HitLeft + HitW - 1;
        private float HitBottom => Position.Y;
        private float HitTop    => HitBottom - HitH + 1;

        public void Update(float dt, WorldMap worldMap, int screenW, int screenH)
        {
            float prevHitBottom = HitBottom;
            float prevHitTop = HitTop;
            
            ReadInput();

            Attack(dt);

            // Horizontal
            Velocity.X = moveDir * moveSpeed;
            Position.X += Velocity.X * dt;
            ResolveWorldCollisionsX(worldMap);

            // Vertical
            ApplyGravity(dt);
            Position.Y += Velocity.Y * dt;
            ResolveWorldCollisionsY(worldMap, prevHitBottom, prevHitTop);
=======
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
>>>>>>> 06a0242ea9d5e0753e26f589eb466b0d3ef40484

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
<<<<<<< HEAD
            Rectangle src = anim.CurrentFrame;
            Rectangle attackSrc = animAttack.CurrentFrame;
            var fx = facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            const float VisualFootSink = 1f;

            var drawPos = new Vector2(
                (float)System.Math.Round(Position.X),
                (float)System.Math.Round(Position.Y + VisualFootSink)
            );

            var origin = new Vector2(16f, 32f);

            
            spriteBatch.Draw(_legs, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
            
            if (!isAttacking)
            {   
                spriteBatch.Draw(_handBack, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(_body, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(_handFront, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
            } else {   
                spriteBatch.Draw(_attackHandBack, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(_attackBody, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(_attackHandFront, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
            }
        }

        private void ReadInput()
        {
            KeyboardState teclado = Keyboard.GetState();
            MouseState mouse = Mouse.GetState();

            moveDir = 0;
            if (teclado.IsKeyDown(Keys.D)) moveDir = 1;
            else if (teclado.IsKeyDown(Keys.A)) moveDir = -1;

            jumpPressed = teclado.IsKeyDown(Keys.Space);

            bool click = mouse.LeftButton == ButtonState.Pressed &&
                        prevMouse.LeftButton == ButtonState.Released;

            if (!isAttacking && click)
            {
                StartAttack();
            }

            prevMouse = mouse;
        }

        private void UpdateAnimationState()
        {
            if (moveDir > 0) facingRight = true;
            else if (moveDir < 0) facingRight = false;

            if (isAttacking)
            {

            }

            if (isGrounded && jumpPressed)
=======
            if (combat.IsDodging)
>>>>>>> 06a0242ea9d5e0753e26f589eb466b0d3ef40484
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
<<<<<<< HEAD
                if (Velocity.Y < -apexThreshold) animState = AnimationState.Jump;
                else if (Velocity.Y > apexThreshold) animState = AnimationState.Fall;
                else animState = AnimationState.Jump;
            }
            else
            {
                animState = (moveDir != 0) ? AnimationState.Walk : AnimationState.Idle;
            }
        }

        private void Attack(float dt)
        {
            if (!isAttacking)
                return;

            animAttack.Update(dt);

            if (animAttack.IsFinished)
                isAttacking = false;
        }

        private void StartAttack()
        {
            isAttacking = true;
            attackTimer = AttackDuration;

            animAttack.Reset();
            animAttack.Play(AnimationState.Attack);
=======
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
>>>>>>> 06a0242ea9d5e0753e26f589eb466b0d3ef40484
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
<<<<<<< HEAD
            int ts = worldMap.TileSize;

            float top = HitTop + 1;
            float bottom = HitBottom - 1;

            int tileYTop = (int)(top / ts);
            int tileYBottom = (int)(bottom / ts);

            if (Velocity.X > 0)
            {
                float right = HitRight;
                int tileX = (int)(right / ts);

                if (worldMap.IsSolidAt(tileX, tileYTop) || worldMap.IsSolidAt(tileX, tileYBottom))
                {
                    // Encosta o lado direito da hitbox na borda esquerda do tile sólido
                    float tileLeft = tileX * ts;
                    float newHitLeft = tileLeft - HitW;

                    Position.X = newHitLeft + (HitW * 0.5f);
                    Velocity.X = 0;
                }
            }
            else if (Velocity.X < 0)
            {
                float left = HitLeft;
                int tileX = (int)(left / ts);

                if (worldMap.IsSolidAt(tileX, tileYTop) || worldMap.IsSolidAt(tileX, tileYBottom))
                {
                    // Encosta o lado esquerdo da hitbox na borda direita do tile sólido
                    float tileRight = tileX * ts + ts;
                    float newHitLeft = tileRight;

                    Position.X = newHitLeft + (HitW * 0.5f);
                    Velocity.X = 0;
                }
            }
=======
            return combat.TryReceiveDamage(damage);
>>>>>>> 06a0242ea9d5e0753e26f589eb466b0d3ef40484
        }

        public void SetEquippedWeapon(Weapon weapon)
        {
<<<<<<< HEAD
            isGrounded = false;

            int ts = worldMap.TileSize;

            float left = HitLeft + 1;
            float right = HitRight - 1;

            int tileXLeft = (int)(left / ts);
            int tileXRight = (int)(right / ts);

            if (Velocity.Y > 0)
            {
                float bottom = HitBottom;

                int fromY = (int)(prevHitBottom / ts);
                int toY = (int)(bottom / ts);

                for (int y = fromY; y <= toY; y++)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        // Encosta o chão (HitBottom) no topo do tile
                        float tileTop = y * ts;
                        Position.Y = tileTop;

                        Velocity.Y = 0;
                        isGrounded = true;
                        return;
                    }
                }
            }
            else if (Velocity.Y < 0)
            {
                float top = HitTop;

                int fromY = (int)(prevHitTop / ts);
                int toY = (int)(top / ts);

                for (int y = fromY; y >= toY; y--)
                {
                    if (worldMap.IsSolidAt(tileXLeft, y) || worldMap.IsSolidAt(tileXRight, y))
                    {
                        // Encosta o topo da hitbox no fundo do tile
                        float tileBottom = y * ts + ts;
                        float newHitTop = tileBottom;

                        // HitTop = Position.Y - HitH + 1  =>  Position.Y = HitTop + HitH - 1
                        Position.Y = newHitTop + HitH - 1;

                        Velocity.Y = 0;
                        return;
                    }
                }
            }
        }
=======
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
>>>>>>> 06a0242ea9d5e0753e26f589eb466b0d3ef40484
    }
}