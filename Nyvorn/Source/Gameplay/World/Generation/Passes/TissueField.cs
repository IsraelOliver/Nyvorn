using System;

namespace Nyvorn.Source.World.Generation
{
    public sealed class TissueField
    {
        private readonly TissueCellState[] cells;

        public TissueField(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            cells = new TissueCellState[width * height];
        }

        public int Width { get; }
        public int Height { get; }

        // Compatibilidade temporaria com os sistemas atuais. Novas regras devem
        // ler GetState e decidir usando Presence/Vitality/Corruption/etc.
        public bool HasTissue(int x, int y)
        {
            return GetState(x, y).HasBiologicalPresence;
        }

        // Compatibilidade com snapshots legados: true vira presenca viva basica.
        public void SetTissue(int x, int y, bool hasTissue)
        {
            SetState(x, y, TissueCellState.FromLegacyPresence(hasTissue));
        }

        public TissueCellState GetState(int x, int y)
        {
            if (!IsInBounds(x, y))
                return TissueCellState.Neutral;

            return cells[(y * Width) + x];
        }

        public void SetState(int x, int y, TissueCellState state)
        {
            if (!IsInBounds(x, y))
                return;

            cells[(y * Width) + x] = state;
        }

        public int CountActiveTiles()
        {
            int count = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].HasBiologicalPresence)
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
