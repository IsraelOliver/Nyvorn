using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI
{
    public struct Perception
    {
        public bool SeesTarget;
        public Vector2 TargetOffset;
        public bool OnGround;
        public bool BlockedHorizontally;
    }
}
