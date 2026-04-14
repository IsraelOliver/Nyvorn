using Nyvorn.Source.Gameplay.Items;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class WorldItemSaveData
    {
        public ItemId ItemId { get; init; }
        public float PositionX { get; init; }
        public float PositionY { get; init; }
        public float VelocityX { get; init; }
        public float VelocityY { get; init; }
        public float PickupDelayRemaining { get; init; }
    }
}
