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
            bool attackPressed = mouse.LeftButton == ButtonState.Pressed;
            bool attackJustPressed = mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton != ButtonState.Pressed;
            bool placePressed = mouse.LeftButton == ButtonState.Pressed;
            bool openInventoryPressed = keyboard.IsKeyDown(Keys.E) && !_prevKeyboard.IsKeyDown(Keys.E);
            bool tissueRevealPressed = keyboard.IsKeyDown(Keys.F) && !_prevKeyboard.IsKeyDown(Keys.F);
            bool toggleMinimapPressed = keyboard.IsKeyDown(Keys.M) && !_prevKeyboard.IsKeyDown(Keys.M);
            int mouseWheelDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
            int hotbarSelectionIndex = GetHotbarSelectionIndex(keyboard);
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
                attackJustPressed,
                placePressed,
                openInventoryPressed,
                tissueRevealPressed,
                toggleMinimapPressed,
                hotbarSelectionIndex,
                dodgePressed,
                dodgeDir,
                new Vector2(mouse.X, mouse.Y),
                mouseWheelDelta);
        }

        private int GetHotbarSelectionIndex(KeyboardState keyboard)
        {
            Keys[] numberKeys =
            {
                Keys.D1,
                Keys.D2,
                Keys.D3,
                Keys.D4,
                Keys.D5,
                Keys.D6
            };

            Keys[] numPadKeys =
            {
                Keys.NumPad1,
                Keys.NumPad2,
                Keys.NumPad3,
                Keys.NumPad4,
                Keys.NumPad5,
                Keys.NumPad6
            };

            for (int i = 0; i < numberKeys.Length; i++)
            {
                if ((keyboard.IsKeyDown(numberKeys[i]) && !_prevKeyboard.IsKeyDown(numberKeys[i])) ||
                    (keyboard.IsKeyDown(numPadKeys[i]) && !_prevKeyboard.IsKeyDown(numPadKeys[i])))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
