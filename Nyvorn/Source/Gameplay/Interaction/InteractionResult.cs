using Nyvorn.Source.Gameplay.Crafting;

namespace Nyvorn.Source.Gameplay.Interaction
{
    public readonly record struct InteractionResult(bool OpenPlayerHub, CraftTier CraftTier)
    {
        public static InteractionResult None => new(false, CraftTier.Basic);
        public static InteractionResult OpenHub(CraftTier craftTier) => new(true, craftTier);
    }
}
