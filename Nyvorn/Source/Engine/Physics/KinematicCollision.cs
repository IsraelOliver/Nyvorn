using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Physics
{
    public readonly struct KinematicCollision
    {
        public KinematicCollision(KinematicAxis axis, int direction, Vector2 attemptedPosition, Vector2 currentPosition)
        {
            Axis = axis;
            Direction = direction;
            AttemptedPosition = attemptedPosition;
            CurrentPosition = currentPosition;
        }

        public KinematicAxis Axis { get; }
        public int Direction { get; }
        public Vector2 AttemptedPosition { get; }
        public Vector2 CurrentPosition { get; }
    }
}
