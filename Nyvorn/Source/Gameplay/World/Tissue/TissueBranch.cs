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
            Bounds = CreateBounds(points, thickness);
        }

        public int Id { get; }
        public int StartNodeId { get; }
        public int EndNodeId { get; }
        public bool IsPrimary { get; }
        public float Thickness { get; }
        public IReadOnlyList<Vector2> Points { get; }
        public Rectangle Bounds { get; }

        private static Rectangle CreateBounds(IReadOnlyList<Vector2> points, float thickness)
        {
            if (points == null || points.Count == 0)
                return Rectangle.Empty;

            float minX = points[0].X;
            float maxX = points[0].X;
            float minY = points[0].Y;
            float maxY = points[0].Y;

            for (int i = 1; i < points.Count; i++)
            {
                Vector2 point = points[i];
                minX = System.MathF.Min(minX, point.X);
                maxX = System.MathF.Max(maxX, point.X);
                minY = System.MathF.Min(minY, point.Y);
                maxY = System.MathF.Max(maxY, point.Y);
            }

            int padding = System.Math.Max(4, (int)System.MathF.Ceiling(thickness * 6f));
            int x = (int)System.MathF.Floor(minX) - padding;
            int y = (int)System.MathF.Floor(minY) - padding;
            int width = (int)System.MathF.Ceiling(maxX - minX) + (padding * 2);
            int height = (int)System.MathF.Ceiling(maxY - minY) + (padding * 2);
            return new Rectangle(x, y, System.Math.Max(1, width), System.Math.Max(1, height));
        }
    }
}
