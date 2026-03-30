using System;

namespace Nyvorn.Source.World.Generation
{
    public sealed class TissueField
    {
        private readonly bool[] values;

        public TissueField(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            values = new bool[width * height];
        }

        public int Width { get; }
        public int Height { get; }

        public bool HasTissue(int x, int y)
        {
            if (!IsInBounds(x, y))
                return false;

            return values[(y * Width) + x];
        }

        public void SetTissue(int x, int y, bool hasTissue)
        {
            if (!IsInBounds(x, y))
                return;

            values[(y * Width) + x] = hasTissue;
        }

        public int CountActiveTiles()
        {
            int count = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                    count++;
            }

            return count;
        }

        private bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }
    }
}