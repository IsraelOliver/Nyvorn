using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Nyvorn.Source.Engine.Input
{
    public class InputService
    {
        private MouseState _prevMouse;
        private KeyboardState _prevKeyboard;

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
            bool openInventoryPressed = keyboard.IsKeyDown(Keys.E) && !_prevKeyboard.IsKeyDown(Keys.E);
            int hotbarSelectionIndex = -1;
            if ((keyboard.IsKeyDown(Keys.D1) && !_prevKeyboard.IsKeyDown(Keys.D1)) ||
                (keyboard.IsKeyDown(Keys.NumPad1) && !_prevKeyboard.IsKeyDown(Keys.NumPad1)))
            {
                hotbarSelectionIndex = 0;
            }
            else if ((keyboard.IsKeyDown(Keys.D2) && !_prevKeyboard.IsKeyDown(Keys.D2)) ||
                     (keyboard.IsKeyDown(Keys.NumPad2) && !_prevKeyboard.IsKeyDown(Keys.NumPad2)))
            {
                hotbarSelectionIndex = 1;
            }
            bool ctrlDown = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
            bool prevCtrlDown = _prevKeyboard.IsKeyDown(Keys.LeftControl) || _prevKeyboard.IsKeyDown(Keys.RightControl);
            bool ctrlJustPressed = ctrlDown && !prevCtrlDown;
            bool dJustPressed = keyboard.IsKeyDown(Keys.D) && !_prevKeyboard.IsKeyDown(Keys.D);
            bool aJustPressed = keyboard.IsKeyDown(Keys.A) && !_prevKeyboard.IsKeyDown(Keys.A);
            bool dodgePressed = ctrlDown && (ctrlJustPressed || dJustPressed || aJustPressed);
            int dodgeDir = 0;
            if (keyboard.IsKeyDown(Keys.D)) dodgeDir = 1;
            else if (keyboard.IsKeyDown(Keys.A)) dodgeDir = -1;

            _prevMouse = mouse;
            _prevKeyboard = keyboard;

            return new InputState(
                moveDir,
                jumpPressed,
                attackPressed,
                openInventoryPressed,
                hotbarSelectionIndex,
                dodgePressed,
                dodgeDir,
                new Vector2(mouse.X, mouse.Y));
        }
    }
}
