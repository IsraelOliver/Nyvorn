using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Player;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.Interaction
{
    public static class InteractionFinder
    {
        public static bool TryGetNearest<T>(IReadOnlyList<T> interactables, Player player, out T nearest)
            where T : class, IInteractable
        {
            nearest = null;
            if (interactables == null || player == null)
                return false;

            float bestDistance = float.MaxValue;
            for (int i = 0; i < interactables.Count; i++)
            {
                T interactable = interactables[i];
                if (interactable == null || !interactable.CanInteract(player))
                    continue;

                float distance = Vector2.Distance(player.Position, interactable.InteractionPosition);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                nearest = interactable;
            }

            return nearest != null;
        }
    }
}
