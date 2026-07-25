using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Physics
{
    public readonly struct CollisionRaycast
    {
        public bool Intersects { get; }
        public float TParameter { get; }  // 0 = start, 1 = end of movement
        public Vector2 IntersectionPoint { get; }
        public Vector2 SurfaceNormal { get; }  // Direction to push out

        private CollisionRaycast(bool intersects, float t, Vector2 intersection, Vector2 normal)
        {
            Intersects = intersects;
            TParameter = t;
            IntersectionPoint = intersection;
            SurfaceNormal = normal;
        }

        /// <summary>
        /// Test if a movement ray intersects a rectangle (AABB).
        /// </summary>
        public static CollisionRaycast TestMovement(
            Vector2 previousPosition,
            Vector2 currentPosition,
            Rectangle targetBounds)
        {
            Vector2 movement = currentPosition - previousPosition;
            if (movement.LengthSquared() < 0.001f)
                return new CollisionRaycast(false, 0f, Vector2.Zero, Vector2.Zero);

            // Expand rect by player size to do circle vs rect
            const int PlayerHalfWidth = 7;  // Half of 13px hurtbox width
            Rectangle expandedBounds = new Rectangle(
                targetBounds.X - PlayerHalfWidth,
                targetBounds.Y - 12,  // Player height ~23px
                targetBounds.Width + (PlayerHalfWidth * 2),
                targetBounds.Height + 24);

            // Test ray against expanded rectangle
            if (TryRayVsAABB(previousPosition, movement, expandedBounds, out float t, out Vector2 normal))
            {
                Vector2 collisionPoint = previousPosition + (movement * t);
                return new CollisionRaycast(true, t, collisionPoint, normal);
            }

            return new CollisionRaycast(false, 0f, Vector2.Zero, Vector2.Zero);
        }

        /// <summary>
        /// Ray vs AABB collision detection (based on slab method).
        /// Returns the parameter t (0-1) where collision occurs, 0 = start, 1 = end.
        /// </summary>
        private static bool TryRayVsAABB(
            Vector2 rayOrigin,
            Vector2 rayDirection,
            Rectangle aabb,
            out float tCollision,
            out Vector2 normalOut)
        {
            tCollision = 0f;
            normalOut = Vector2.Zero;

            float tMin = float.MinValue;
            float tMax = float.MaxValue;
            Vector2 normalMin = Vector2.Zero;

            // Check X axis
            if (System.Math.Abs(rayDirection.X) > 0.001f)
            {
                float t1 = (aabb.Left - rayOrigin.X) / rayDirection.X;
                float t2 = (aabb.Right - rayOrigin.X) / rayDirection.X;

                if (t1 > t2)
                {
                    var temp = t1;
                    t1 = t2;
                    t2 = temp;
                }

                tMin = System.Math.Max(tMin, t1);
                tMax = System.Math.Min(tMax, t2);

                if (rayDirection.X > 0)
                    normalMin = new Vector2(-1, 0);  // Hit left side
                else
                    normalMin = new Vector2(1, 0);   // Hit right side
            }
            else if (rayOrigin.X < aabb.Left || rayOrigin.X > aabb.Right)
                return false;

            // Check Y axis
            if (System.Math.Abs(rayDirection.Y) > 0.001f)
            {
                float t1 = (aabb.Top - rayOrigin.Y) / rayDirection.Y;
                float t2 = (aabb.Bottom - rayOrigin.Y) / rayDirection.Y;

                if (t1 > t2)
                {
                    var temp = t1;
                    t1 = t2;
                    t2 = temp;
                }

                if (t1 > tMin)
                {
                    tMin = t1;
                    if (rayDirection.Y > 0)
                        normalMin = new Vector2(0, -1);  // Hit top
                    else
                        normalMin = new Vector2(0, 1);   // Hit bottom
                }

                tMax = System.Math.Min(tMax, t2);
            }
            else if (rayOrigin.Y < aabb.Top || rayOrigin.Y > aabb.Bottom)
                return false;

            if (tMin > tMax || tMin < 0f)
                return false;

            tCollision = System.Math.Max(0f, System.Math.Min(1f, tMin));
            normalOut = normalMin;
            return true;
        }
    }
}
