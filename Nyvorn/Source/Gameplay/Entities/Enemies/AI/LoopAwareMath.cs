using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Enemies.AI
{
    public static class LoopAwareMath
    {
        public static Vector2 GetOffset(Vector2 from, Vector2 to, float worldWidth)
        {
            float deltaX = to.X - from.X;
            if (worldWidth > 0f)
            {
                if (deltaX > worldWidth * 0.5f)
                    deltaX -= worldWidth;
                else if (deltaX < -worldWidth * 0.5f)
                    deltaX += worldWidth;
            }

            return new Vector2(deltaX, to.Y - from.Y);
        }
    }
}
