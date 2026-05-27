using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Input
{
    public readonly struct InputState
    {
        public int MoveDir { get; }
        public bool JumpPressed { get; }
        public bool AttackPressed { get; }
        public bool AttackJustPressed { get; }
        public bool PlacePressed { get; }
        public bool ActivePowerPressed { get; }
        public bool ActivePowerJustPressed { get; }
        public bool TogglePlayerHubPressed { get; }
        public bool ToggleMapPressed { get; }
        public bool ToggleConstructionModePressed { get; }
        public bool InteractPressed { get; }
        public bool CyclePowerPressed { get; }
        public bool ToggleDebugPressed { get; }
        public bool CancelPressed { get; }
        public int HotbarSelectionIndex { get; }
        public bool DodgePressed { get; }
        public int DodgeDir { get; }
        public Vector2 MouseScreenPosition { get; }
        public int MouseWheelDelta { get; }

        public InputState(
            int moveDir,
            bool jumpPressed,
            bool attackPressed,
            bool attackJustPressed,
            bool placePressed,
            bool activePowerPressed,
            bool activePowerJustPressed,
            bool togglePlayerHubPressed,
            bool toggleMapPressed,
            bool toggleConstructionModePressed,
            bool interactPressed,
            bool cyclePowerPressed,
            bool toggleDebugPressed,
            bool cancelPressed,
            int hotbarSelectionIndex,
            bool dodgePressed,
            int dodgeDir,
            Vector2 mouseScreenPosition,
            int mouseWheelDelta)
        {
            MoveDir = moveDir;
            JumpPressed = jumpPressed;
            AttackPressed = attackPressed;
            AttackJustPressed = attackJustPressed;
            PlacePressed = placePressed;
            ActivePowerPressed = activePowerPressed;
            ActivePowerJustPressed = activePowerJustPressed;
            TogglePlayerHubPressed = togglePlayerHubPressed;
            ToggleMapPressed = toggleMapPressed;
            ToggleConstructionModePressed = toggleConstructionModePressed;
            InteractPressed = interactPressed;
            CyclePowerPressed = cyclePowerPressed;
            ToggleDebugPressed = toggleDebugPressed;
            CancelPressed = cancelPressed;
            HotbarSelectionIndex = hotbarSelectionIndex;
            DodgePressed = dodgePressed;
            DodgeDir = dodgeDir;
            MouseScreenPosition = mouseScreenPosition;
            MouseWheelDelta = mouseWheelDelta;
        }

        public InputState ConsumeWorldMouseInput()
        {
            return new InputState(
                MoveDir,
                JumpPressed,
                false,
                false,
                false,
                false,
                false,
                TogglePlayerHubPressed,
                ToggleMapPressed,
                ToggleConstructionModePressed,
                InteractPressed,
                CyclePowerPressed,
                ToggleDebugPressed,
                CancelPressed,
                HotbarSelectionIndex,
                DodgePressed,
                DodgeDir,
                MouseScreenPosition,
                0);
        }

        public InputState ConsumeGameplayInput()
        {
            return new InputState(
                0,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                false,
                -1,
                false,
                0,
                MouseScreenPosition,
                0);
        }
    }
}
