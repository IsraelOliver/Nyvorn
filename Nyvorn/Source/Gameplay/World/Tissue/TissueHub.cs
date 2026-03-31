using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueHub
    {
        public TissueHub(
            Point tilePosition,
            Vector2 worldPosition,
            TissueLocalType localType,
            byte neighborCount,
            float openness,
            float importanceScore)
        {
            TilePosition = tilePosition;
            WorldPosition = worldPosition;
            LocalType = localType;
            NeighborCount = neighborCount;
            Openness = openness;
            ImportanceScore = importanceScore;
            LinkCount = 0;
        }

        public Point TilePosition { get; }
        public Vector2 WorldPosition { get; }
        public TissueLocalType LocalType { get; }
        public byte NeighborCount { get; }
        public float Openness { get; }
        public float ImportanceScore { get; }

        public int LinkCount { get; private set; }
        public bool HasLinks => LinkCount > 0;
        public bool IsTerminal => LinkCount == 1;
        public bool IsIsolated => LinkCount == 0;

        public void IncrementLinkCount()
        {
            LinkCount++;
        }
    }
}