using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueAnalysisResult
    {
        private readonly byte[] neighborCounts;
        private readonly float[] opennessScores;
        private readonly TissueLocalType[] localTypes;
        public List<TissueLink> Links { get; } = new();

        public TissueAnalysisResult(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            neighborCounts = new byte[width * height];
            opennessScores = new float[width * height];
            localTypes = new TissueLocalType[width * height];
        }

        public int Width { get; }
        public int Height { get; }

        public List<TissueHub> Hubs { get; } = new();

        public byte GetNeighborCount(int x, int y)
        {
            return IsInBounds(x, y)
                ? neighborCounts[(y * Width) + x]
                : (byte)0;
        }

        public float GetOpennessScore(int x, int y)
        {
            return IsInBounds(x, y)
                ? opennessScores[(y * Width) + x]
                : 0f;
        }

        public TissueLocalType GetLocalType(int x, int y)
        {
            return IsInBounds(x, y)
                ? localTypes[(y * Width) + x]
                : TissueLocalType.None;
        }

        public void SetNeighborCount(int x, int y, byte value)
        {
            if (!IsInBounds(x, y))
                return;

            neighborCounts[(y * Width) + x] = value;
        }

        public void SetOpennessScore(int x, int y, float value)
        {
            if (!IsInBounds(x, y))
                return;

            opennessScores[(y * Width) + x] = Math.Clamp(value, 0f, 1f);
        }

        public void SetLocalType(int x, int y, TissueLocalType value)
        {
            if (!IsInBounds(x, y))
                return;

            localTypes[(y * Width) + x] = value;
        }

        private bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < Width &&
                   y >= 0 && y < Height;
        }
    }
}