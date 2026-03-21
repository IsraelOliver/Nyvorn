using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Input
{
    public readonly struct InputState
    {
        public int MoveDir { get; }
        public bool JumpPressed { get; }
        public bool AttackPressed { get; }
        public bool PlacePressed { get; }
        public bool OpenInventoryPressed { get; }
        public bool TissueRevealPressed { get; }
        public int HotbarSelectionIndex { get; }
        public bool DodgePressed { get; }
        public int DodgeDir { get; }
        public Vector2 MouseScreenPosition { get; }

        public InputState(int moveDir, bool jumpPressed, bool attackPressed, bool placePressed, bool openInventoryPressed, bool tissueRevealPressed, int hotbarSelectionIndex, bool dodgePressed, int dodgeDir, Vector2 mouseScreenPosition)
        {
            MoveDir = moveDir;
            JumpPressed = jumpPressed;
            AttackPressed = attackPressed;
            PlacePressed = placePressed;
            OpenInventoryPressed = openInventoryPressed;
            TissueRevealPressed = tissueRevealPressed;
            HotbarSelectionIndex = hotbarSelectionIndex;
            DodgePressed = dodgePressed;
            DodgeDir = dodgeDir;
            MouseScreenPosition = mouseScreenPosition;
        }

        public InputState ConsumeWorldMouseInput()
        {
            return new InputState(MoveDir, JumpPressed, false, false, OpenInventoryPressed, TissueRevealPressed, HotbarSelectionIndex, DodgePressed, DodgeDir, MouseScreenPosition);
        }
    }
}
