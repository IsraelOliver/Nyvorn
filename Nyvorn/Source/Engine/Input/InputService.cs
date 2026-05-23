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
            bool activePowerPressed = mouse.RightButton == ButtonState.Pressed;
            bool activePowerJustPressed = mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton != ButtonState.Pressed;
            bool togglePlayerHubPressed = IsNewKeyPress(keyboard, Keys.E);
            bool toggleMapPressed = IsNewKeyPress(keyboard, Keys.M);
            bool toggleConstructionModePressed = IsNewKeyPress(keyboard, Keys.LeftAlt) || IsNewKeyPress(keyboard, Keys.RightAlt);
            bool interactPressed = IsNewKeyPress(keyboard, Keys.F);
            bool cyclePowerPressed = IsNewKeyPress(keyboard, Keys.Q);
            bool toggleDebugPressed = IsNewKeyPress(keyboard, Keys.F3);
            bool cancelPressed = IsNewKeyPress(keyboard, Keys.Escape);
            int mouseWheelDelta = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
            int hotbarSelectionIndex = GetHotbarSelectionIndex(keyboard);
            bool dodgePressed = IsNewKeyPress(keyboard, Keys.LeftControl) || IsNewKeyPress(keyboard, Keys.RightControl);
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
                activePowerPressed,
                activePowerJustPressed,
                togglePlayerHubPressed,
                toggleMapPressed,
                toggleConstructionModePressed,
                interactPressed,
                cyclePowerPressed,
                toggleDebugPressed,
                cancelPressed,
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
                Keys.D6,
                Keys.D7,
                Keys.D8,
                Keys.D9
            };

            Keys[] numPadKeys =
            {
                Keys.NumPad1,
                Keys.NumPad2,
                Keys.NumPad3,
                Keys.NumPad4,
                Keys.NumPad5,
                Keys.NumPad6,
                Keys.NumPad7,
                Keys.NumPad8,
                Keys.NumPad9
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

        private bool IsNewKeyPress(KeyboardState keyboard, Keys key)
        {
            return keyboard.IsKeyDown(key) && !_prevKeyboard.IsKeyDown(key);
        }
    }
}
