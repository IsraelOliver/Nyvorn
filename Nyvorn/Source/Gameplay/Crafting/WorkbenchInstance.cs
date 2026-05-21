using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.Gameplay.Interaction;

namespace Nyvorn.Source.Gameplay.Crafting
{
    public sealed class WorkbenchInstance : IInteractable
    {
        public WorkbenchInstance(Vector2 position)
        {
            Position = position;
        }

        public Vector2 Position { get; }
        public Vector2 InteractionPosition => Bounds.Center.ToVector2();

        public Rectangle Bounds => new Rectangle(
            (int)System.MathF.Round(Position.X),
            (int)System.MathF.Round(Position.Y),
            WorkbenchRuntimeSystem.WorkbenchWidth,
            WorkbenchRuntimeSystem.WorkbenchHeight);

        public bool CanInteract(Player player)
        {
            return player != null &&
                   Vector2.Distance(player.Position, InteractionPosition) <= player.WorldInteractionRange;
        }

        public InteractionResult Interact(Player player)
        {
            return CanInteract(player)
                ? InteractionResult.OpenHub(CraftTier.Workbench)
                : InteractionResult.None;
        }
    }
}
