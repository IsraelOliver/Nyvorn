using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueBiomeField
    {
        private readonly TissueBiomeType[] biomeTypes;

        public TissueBiomeField(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            biomeTypes = new TissueBiomeType[width * height];
        }

        public int Width { get; }
        public int Height { get; }
        public List<TissueBiomeRegion> Regions { get; } = new();

        public TissueBiomeType GetBiomeType(int x, int y)
        {
            return IsInBounds(x, y)
                ? biomeTypes[(y * Width) + x]
                : TissueBiomeType.None;
        }

        public bool HasBiome(int x, int y)
        {
            return GetBiomeType(x, y) != TissueBiomeType.None;
        }

        public void SetBiomeType(int x, int y, TissueBiomeType value)
        {
            if (!IsInBounds(x, y))
                return;

            biomeTypes[(y * Width) + x] = value;
        }

        private bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < Width &&
                   y >= 0 && y < Height;
        }
    }
}
