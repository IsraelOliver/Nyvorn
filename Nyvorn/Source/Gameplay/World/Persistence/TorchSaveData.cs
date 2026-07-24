namespace Nyvorn.Source.World.Persistence
{
    public sealed class TorchSaveData
    {
        public float PositionX { get; init; }
        public float PositionY { get; init; }
        public int PoleFrameIndex { get; init; }
        public bool FacingLeft { get; init; }
    }
}
