using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Nyvorn.Source.Engine.Input
{
    public class InputService
    {
        private MouseState _prevMouse;

        public InputState Update()
        {
            KeyboardState keyboard = Keyboard.GetState();
            MouseState mouse = Mouse.GetState();

            int moveDir = 0;
            if (keyboard.IsKeyDown(Keys.D)) moveDir = 1;
            else if (keyboard.IsKeyDown(Keys.A)) moveDir = -1;

            bool jumpPressed = keyboard.IsKeyDown(Keys.Space);
            bool attackPressed = mouse.LeftButton == ButtonState.Pressed &&
                                _prevMouse.LeftButton == ButtonState.Released;

            _prevMouse = mouse;

            return new InputState(
                moveDir,
                jumpPressed,
                attackPressed,
                new Vector2(mouse.X, mouse.Y));
        }
    }
}
