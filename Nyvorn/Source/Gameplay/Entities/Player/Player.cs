using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public class Player
    {
        public Vector2 Position;
        private Vector2 Velocity;

        private bool isGrounded;

        public const int Width = 64;
        public const int Height = 64;

        private const float moveSpeed = 150f; // Velocidade do player
        private float gravity = 800; // Gravidade

        public Player(Vector2 startPosition) {
            Position = startPosition;
            Velocity = Vector2.Zero;
            isGrounded = false;
        }

        public void Update(float dt, List<Rectangle> platforms, int screenW, int screenH)
        {

            float prevBottom = Position.Y + Height;

            // chama o teclado
            KeyboardCheck(dt);

            // aplica a gravidade
            ApplyGravity(dt);
            Position.Y += Velocity.Y * dt;

            // colisão com as bordas da tela.
            ResolveFloor(screenH, prevBottom, platforms);

            Position.X = MathHelper.Clamp(Position.X, 0, screenW - Width);
        }

        private void ApplyGravity(float dt)
        {
            Velocity.Y += gravity * dt;
        }

        private void ResolveFloor(int screenH, float prevBottom, List<Rectangle> platforms)
        {
            isGrounded = false;

            Rectangle redBounds = new Rectangle
            (
                (int)Position.X,
                (int)Position.Y,
                Width,
                Height
            );

            foreach (var plat in platforms)
            {
                if (redBounds.Intersects(plat) && (int)prevBottom <= plat.Top && Velocity.Y >= 0)
                {
                    Position.Y = plat.Top - Height;
                    Velocity.Y = 0;
                    isGrounded = true;
                }
            }

            //Impede de sair para fola da tela
            float floorY = screenH - Height;
            if (Position.Y >= floorY)
            {
                Position.Y = floorY;
                Velocity.Y = 0;
                isGrounded = true;
            }
        }

        private void KeyboardCheck(float deltaTime)
        {
            KeyboardState teclado = Keyboard.GetState();
            float speed = moveSpeed * deltaTime;

            if (isGrounded && teclado.IsKeyDown(Keys.Space))
            {
                Velocity.Y -= 500f;
                isGrounded = false;
            }


            if (teclado.IsKeyDown(Keys.D))
                Position.X += speed;
            else if (teclado.IsKeyDown(Keys.A))
                Position.X -= speed;
        }
    }
}