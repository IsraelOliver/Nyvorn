using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.World;
using Nyvorn.Source.Gameplay.Combat.Weapons;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public class Player
    {
        public Vector2 Position; //é o pivot do pé
        private Vector2 Velocity;

        private bool isGrounded;
        private bool isAttacking;
        private float attackTimer;
        private MouseState prevMouse;
        private const float AttackDuration = 0.3f;
        private Weapon equippedWeapon;

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

        private int moveDir;
        private bool jumpPressed;
        const float VisualFootSink = 1f;

        // Animação
        private bool facingRight = true;

        private AnimationState animState = AnimationState.Idle;

        private readonly Animator anim;
        private readonly Animator animAttack;

        private readonly Texture2D _attackHandBack;
        private readonly Texture2D _attackHandFront;
        private readonly Texture2D _attackBody;
        private readonly Texture2D _legs;
        private readonly Texture2D _handFront_weaponRun;

        private Vector2 frameTopLeft;
        private Vector2 handLocal;
        private Vector2 handWorld;

        Texture2D debugPixel;

        public Player(Vector2 startPositionPivotFoot, Texture2D sheet, Texture2D handBack_base, Texture2D handFront_base, Texture2D handBack_attack, Texture2D handFront_attack, Texture2D body_attack, Texture2D legs, Texture2D stickTexture, Texture2D handFront_weaponRun)
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

            equippedWeapon = new ShortStick(stickTexture);
            _handFront_weaponRun = handFront_weaponRun;

            debugPixel = new Texture2D(sheet.GraphicsDevice, 1, 1);
            debugPixel.SetData(new[] { Color.Red });

            anim = new Animator(PlayerAnimations.CreateBase(), AnimationState.Idle);
            animAttack = new Animator(PlayerAnimations.CreateAttackShortSword(), AnimationState.Attack);
        }

        private float HitLeft => Position.X - (HitW * 0.5f);
        private float HitRight => HitLeft + HitW - 1;
        private float HitBottom => Position.Y;
        private float HitTop => HitBottom - HitH + 1;

        public void Update(float dt, WorldMap worldMap, int screenW, int screenH, Vector2 mouseWorld)
        {
            float prevHitBottom = HitBottom;
            float prevHitTop = HitTop;

            ReadInput(mouseWorld);

            Attack(dt);

            // Horizontal
            Velocity.X = moveDir * moveSpeed;
            Position.X += Velocity.X * dt;
            ResolveWorldCollisionsX(worldMap);

            // Vertical
            ApplyGravity(dt);
            Position.Y += Velocity.Y * dt;
            ResolveWorldCollisionsY(worldMap, prevHitBottom, prevHitTop);

            UpdateAnimationState();

            anim.Play(animState);
            anim.Update(dt);

            ConvertToWorld();
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Rectangle src = anim.CurrentFrame;
            Rectangle attackSrc = animAttack.CurrentFrame;
            var fx = facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            var drawPos = new Vector2(
                (float)System.Math.Round(Position.X),
                (float)System.Math.Round(Position.Y + VisualFootSink)
            );

            bool weaponEquipped = equippedWeapon != null;
            bool isMoving = moveDir != 0;
            bool weaponRunPose = weaponEquipped && isMoving && isGrounded && !isAttacking;

            var origin = new Vector2(16f, 32f);

            spriteBatch.Draw(_legs, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);

            if (!isAttacking)
            {
                spriteBatch.Draw(_handBack, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(_body, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
                if (weaponRunPose)
                {
                    equippedWeapon.SetWalk();
                    equippedWeapon.Draw(spriteBatch, handWorld, facingRight);
                    spriteBatch.Draw(_handFront_weaponRun, drawPos, new Rectangle(0, 0, 32, 32), Color.White, 0f, origin, 1f, fx, 0f);
                }
                else
                {
                    if (!isGrounded && Velocity.Y > 0)
                    {
                        equippedWeapon.SetAttackFrame(0);
                    }
                    else if (moveDir != 0)
                    {
                        equippedWeapon.SetWalk();
                    }
                    else
                    {
                        equippedWeapon.SetIdle();
                    }
                    equippedWeapon.Draw(spriteBatch, handWorld, facingRight);
                    spriteBatch.Draw(_handFront, drawPos, src, Color.White, 0f, origin, 1f, fx, 0f);
                }
                spriteBatch.Draw(debugPixel, handWorld, Color.White);
            }
            else
            {
                spriteBatch.Draw(_attackHandBack, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(_attackBody, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
                equippedWeapon.SetAttackFrame(animAttack.FrameIndex);
                equippedWeapon.Draw(spriteBatch, handWorld, facingRight);
                spriteBatch.Draw(_attackHandFront, drawPos, attackSrc, Color.White, 0f, origin, 1f, fx, 0f);
                spriteBatch.Draw(debugPixel, handWorld, Color.White);
            }
        }

        private void ReadInput(Vector2 mouseWorld)
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
                StartAttack(mouseWorld);
            }

            prevMouse = mouse;
        }

        private void UpdateAnimationState()
        {
            if (!isAttacking)
            {
                if (moveDir > 0) facingRight = true;
                else if (moveDir < 0) facingRight = false;
            }

            if (isGrounded && jumpPressed)
            {
                Velocity.Y = -jumpSpeed;
                isGrounded = false;
            }

            const float apexThreshold = 5f;

            if (!isGrounded)
            {
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

            attackTimer -= dt;
            animAttack.Update(dt);

            if (attackTimer <= 0f)
            {
                isAttacking = false;
                if (moveDir != 0)
                    facingRight = moveDir > 0;
            }
        }

        private void StartAttack(Vector2 mouseWorld)
        {
            isAttacking = true;
            facingRight = mouseWorld.X >= Position.X;
            attackTimer = AttackDuration;

            animAttack.Reset();
            animAttack.Play(AnimationState.Attack);
        }

        private void ConvertToWorld()
        {
            var drawPos = new Vector2(
                (float)System.Math.Round(Position.X),
                (float)System.Math.Round(Position.Y + VisualFootSink)
            );
            var origin = new Vector2(16f, 32f);

            frameTopLeft = drawPos - origin;

            Animator a = isAttacking ? animAttack : anim;
            handLocal = PlayerAnimations.GetHandAnchor(a);

            if (!facingRight)
                handLocal.X = 31 - handLocal.X;


            handWorld = frameTopLeft + handLocal;
        }

        private void ApplyGravity(float dt)
        {
            Velocity.Y += gravity * dt;
        }

        private void ResolveWorldCollisionsX(WorldMap worldMap)
        {
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
        }

        private void ResolveWorldCollisionsY(WorldMap worldMap, float prevHitBottom, float prevHitTop)
        {
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
                        float tileBottom = y * ts + ts;
                        float newHitTop = tileBottom;

                        Position.Y = newHitTop + HitH - 1;

                        Velocity.Y = 0;
                        return;
                    }
                }
            }
        }
    }
}
