using Nyvorn.Source.World.Decorations;

namespace Nyvorn.Source.World.Persistence
{
    public sealed class TreePartSaveData
    {
        public TreePartType PartType { get; init; }
        public int OffsetX { get; init; }
        public int OffsetY { get; init; }
    }
}
