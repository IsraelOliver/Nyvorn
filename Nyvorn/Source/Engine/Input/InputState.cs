using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Input
{
    public readonly struct InputState
    {
        public int MoveDir { get; }
        public bool JumpPressed { get; }
        public bool AttackPressed { get; }
        public Vector2 MouseScreenPosition { get; }

        public InputState(int moveDir, bool jumpPressed, bool attackPressed, Vector2 mouseScreenPosition)
        {
            MoveDir = moveDir;
            JumpPressed = jumpPressed;
            AttackPressed = attackPressed;
            MouseScreenPosition = mouseScreenPosition;
        }
    }
}
