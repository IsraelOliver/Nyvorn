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
        public bool OpenInventoryPressed { get; }
        public bool TissueRevealPressed { get; }
        public bool ToggleMinimapPressed { get; }
        public int HotbarSelectionIndex { get; }
        public bool DodgePressed { get; }
        public int DodgeDir { get; }
        public Vector2 MouseScreenPosition { get; }
        public int MouseWheelDelta { get; }

        public InputState(int moveDir, bool jumpPressed, bool attackPressed, bool attackJustPressed, bool placePressed, bool openInventoryPressed, bool tissueRevealPressed, bool toggleMinimapPressed, int hotbarSelectionIndex, bool dodgePressed, int dodgeDir, Vector2 mouseScreenPosition, int mouseWheelDelta)
        {
            MoveDir = moveDir;
            JumpPressed = jumpPressed;
            AttackPressed = attackPressed;
            AttackJustPressed = attackJustPressed;
            PlacePressed = placePressed;
            OpenInventoryPressed = openInventoryPressed;
            TissueRevealPressed = tissueRevealPressed;
            ToggleMinimapPressed = toggleMinimapPressed;
            HotbarSelectionIndex = hotbarSelectionIndex;
            DodgePressed = dodgePressed;
            DodgeDir = dodgeDir;
            MouseScreenPosition = mouseScreenPosition;
            MouseWheelDelta = mouseWheelDelta;
        }

        public InputState ConsumeWorldMouseInput()
        {
            return new InputState(MoveDir, JumpPressed, false, false, false, OpenInventoryPressed, TissueRevealPressed, ToggleMinimapPressed, HotbarSelectionIndex, DodgePressed, DodgeDir, MouseScreenPosition, MouseWheelDelta);
        }
    }
}
