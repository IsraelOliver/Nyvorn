using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Player;

namespace Nyvorn.Source.Gameplay.Interaction
{
    public interface IInteractable
    {
        Rectangle Bounds { get; }
        Vector2 InteractionPosition { get; }
        bool CanInteract(Player player);
        InteractionResult Interact(Player player);
    }
}
