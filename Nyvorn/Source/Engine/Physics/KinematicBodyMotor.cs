using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.Engine.Physics
{
    public sealed class KinematicBodyMotor
    {
        private float remainderX;
        private float remainderY;

        public KinematicBodyMotor(Vector2 startPosition)
        {
            Position = startPosition;
        }

        public Vector2 Position { get; set; }

        public void Reset(Vector2 position)
        {
            Position = position;
            remainderX = 0f;
            remainderY = 0f;
        }

        public int MoveX(
            float amount,
            Func<Vector2, KinematicAxis, int, bool> collidesAt,
            Func<KinematicCollision, bool> onCollide = null)
        {
            return MoveAxis(
                amount,
                KinematicAxis.Horizontal,
                collidesAt,
                onCollide,
                ref remainderX);
        }

        public int MoveY(
            float amount,
            Func<Vector2, KinematicAxis, int, bool> collidesAt,
            Func<KinematicCollision, bool> onCollide = null)
        {
            return MoveAxis(
                amount,
                KinematicAxis.Vertical,
                collidesAt,
                onCollide,
                ref remainderY);
        }

        public void ClearRemainderX()
        {
            remainderX = 0f;
        }

        public void ClearRemainderY()
        {
            remainderY = 0f;
        }

        private int MoveAxis(
            float amount,
            KinematicAxis axis,
            Func<Vector2, KinematicAxis, int, bool> collidesAt,
            Func<KinematicCollision, bool> onCollide,
            ref float remainder)
        {
            remainder += amount;
            int move = (int)MathF.Round(remainder);
            if (move == 0)
                return 0;

            remainder -= move;

            int direction = Math.Sign(move);
            int moved = 0;

            while (move != 0)
            {
                Vector2 attemptedPosition = axis == KinematicAxis.Horizontal
                    ? new Vector2(Position.X + direction, Position.Y)
                    : new Vector2(Position.X, Position.Y + direction);

                if (collidesAt(attemptedPosition, axis, direction))
                {
                    Vector2 beforeCollisionCallback = Position;
                    KinematicCollision collision = new(axis, direction, attemptedPosition, Position);
                    bool continueAfterCallback = onCollide?.Invoke(collision) == true;

                    if (continueAfterCallback && Position != beforeCollisionCallback)
                        continue;

                    remainder = 0f;
                    return moved;
                }

                Position = attemptedPosition;
                moved += direction;
                move -= direction;
            }

            return moved;
        }
    }
}
