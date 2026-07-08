using System.Collections.Generic;

namespace Nyvorn.Source.World.Generation.Biomes
{
    public sealed class DesertRegionProfile
    {
        private readonly List<DesertRegionColumn> columns;

        public DesertRegionProfile(
            int centerX,
            int surfaceY,
            int baseRadius,
            int halfWidth,
            int mainDepth,
            IReadOnlyList<DesertRegionColumn> columns)
        {
            CenterX = centerX;
            SurfaceY = surfaceY;
            BaseRadius = baseRadius;
            HalfWidth = halfWidth;
            MainDepth = mainDepth;
            this.columns = columns == null
                ? new List<DesertRegionColumn>()
                : new List<DesertRegionColumn>(columns);
        }

        public int CenterX { get; }
        public int SurfaceY { get; }
        public int BaseRadius { get; }
        public int HalfWidth { get; }
        public int MainDepth { get; }
        public IReadOnlyList<DesertRegionColumn> Columns => columns;

        public void SetPixelSandBaseY(int columnIndex, int pixelSandBaseY)
        {
            if (columnIndex < 0 || columnIndex >= columns.Count)
                return;

            columns[columnIndex] = columns[columnIndex] with { PixelSandBaseY = pixelSandBaseY };
        }
    }

    public readonly record struct DesertRegionColumn(
        int X,
        int LocalX,
        int TopY,
        int NormalSurfaceY,
        int BottomY,
        int PixelSandBaseY);
}
