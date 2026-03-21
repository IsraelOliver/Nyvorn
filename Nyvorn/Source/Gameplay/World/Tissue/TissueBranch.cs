using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueBranch
    {
        public TissueBranch(int id, int startNodeId, int endNodeId, bool isPrimary, float thickness, IReadOnlyList<Vector2> points)
        {
            Id = id;
            StartNodeId = startNodeId;
            EndNodeId = endNodeId;
            IsPrimary = isPrimary;
            Thickness = thickness;
            Points = points;
        }

        public int Id { get; }
        public int StartNodeId { get; }
        public int EndNodeId { get; }
        public bool IsPrimary { get; }
        public float Thickness { get; }
        public IReadOnlyList<Vector2> Points { get; }
    }
}
