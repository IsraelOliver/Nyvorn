using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Input
{
    public readonly struct InputState
    {
        public int MoveDir { get; }
        public bool JumpPressed { get; }
        public bool AttackPressed { get; }
        public bool OpenInventoryPressed { get; }
        public int HotbarSelectionIndex { get; }
        public bool DodgePressed { get; }
        public int DodgeDir { get; }
        public Vector2 MouseScreenPosition { get; }

        public InputState(int moveDir, bool jumpPressed, bool attackPressed, bool openInventoryPressed, int hotbarSelectionIndex, bool dodgePressed, int dodgeDir, Vector2 mouseScreenPosition)
        {
            MoveDir = moveDir;
            JumpPressed = jumpPressed;
            AttackPressed = attackPressed;
            OpenInventoryPressed = openInventoryPressed;
            HotbarSelectionIndex = hotbarSelectionIndex;
            DodgePressed = dodgePressed;
            DodgeDir = dodgeDir;
            MouseScreenPosition = mouseScreenPosition;
        }

        public InputState ConsumeWorldMouseInput()
        {
            return new InputState(MoveDir, JumpPressed, false, OpenInventoryPressed, HotbarSelectionIndex, DodgePressed, DodgeDir, MouseScreenPosition);
        }
    }
}
