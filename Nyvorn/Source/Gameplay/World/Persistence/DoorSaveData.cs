namespace Nyvorn.Source.World.Persistence
{
    public sealed class DoorSaveData
    {
        public int TileX { get; init; }
        public int TileY { get; init; }
        public bool IsOpen { get; init; }
        public bool OpensRight { get; init; } = true;
    }
}
