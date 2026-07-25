using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Objects
{
    public sealed class FurnitureCollisionSystem
    {
        private readonly List<IRaycastCollider> colliders = new();

        public void Register(IRaycastCollider collider)
        {
            if (collider != null && !colliders.Contains(collider))
                colliders.Add(collider);
        }

        public bool TryGetNearestCollision(
            Vector2 previousPosition,
            Vector2 currentPosition,
            out CollisionRaycast collision)
        {
            collision = default;
            float closestT = float.MaxValue;
            bool foundCollision = false;

            // Check all colliders and find the closest one
            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i].TryGetCollision(previousPosition, currentPosition, out CollisionRaycast col))
                {
                    if (col.TParameter < closestT)
                    {
                        closestT = col.TParameter;
                        collision = col;
                        foundCollision = true;
                    }
                }
            }

            return foundCollision;
        }
    }
}
