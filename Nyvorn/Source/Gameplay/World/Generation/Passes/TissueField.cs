using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Generation
{
    public readonly record struct TissueFieldCell(int X, int Y, TissueCellState State);

    internal readonly record struct TissueFieldChange(
        int X,
        int Y,
        TissueCellState Previous,
        TissueCellState Current,
        int Revision);

    public sealed class TissueField
    {
        private readonly Dictionary<int, TissueCellState> baseCells = new();
        private readonly Dictionary<int, TissueCellState> overrides = new();
        private Func<int, int, bool> canContainTissue;
        private int persistedRevision;

        internal event Action<TissueFieldChange> Changed;

        public TissueField(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }
        public int Revision { get; private set; }
        public bool HasUnsavedChanges => Revision != persistedRevision;

        // Compatibilidade temporaria com os sistemas atuais. Novas regras devem
        // ler GetState e decidir usando Presence/Vitality/Corruption/etc.
        public bool HasTissue(int x, int y)
        {
            return GetState(x, y).HasBiologicalPresence;
        }

        public TissueCellState GetState(int x, int y)
        {
            if (!IsInBounds(x, y))
                return TissueCellState.Neutral;

            int key = CreateKey(x, y);
            if (overrides.TryGetValue(key, out TissueCellState overrideState))
                return overrideState;

            return baseCells.TryGetValue(key, out TissueCellState baseState)
                ? baseState
                : TissueCellState.Neutral;
        }

        internal bool TryGetBaseState(int x, int y, out TissueCellState state)
        {
            state = TissueCellState.Neutral;
            return IsInBounds(x, y) &&
                   baseCells.TryGetValue(CreateKey(x, y), out state);
        }

        public bool SetState(int x, int y, TissueCellState state)
        {
            if (!IsInBounds(x, y))
                return false;

            if (!state.IsNeutral && canContainTissue != null && !canContainTissue(x, y))
                return false;

            int key = CreateKey(x, y);
            TissueCellState current = GetState(x, y);
            if (StatesEqual(current, state))
                return false;

            TissueCellState baseState = baseCells.TryGetValue(key, out TissueCellState storedBase)
                ? storedBase
                : TissueCellState.Neutral;
            if (StatesEqual(baseState, state))
                overrides.Remove(key);
            else
                overrides[key] = state;

            RegisterChange(x, y, current, state);
            return true;
        }

        public bool Clear(int x, int y)
        {
            return SetState(x, y, TissueCellState.Neutral);
        }

        internal void SetBaseState(int x, int y, TissueCellState state)
        {
            if (!IsInBounds(x, y))
                return;

            int key = CreateKey(x, y);
            if (state.IsNeutral)
                baseCells.Remove(key);
            else
                baseCells[key] = state;
        }

        internal void SetOccupancyValidator(Func<int, int, bool> validator)
        {
            canContainTissue = validator;
        }

        internal bool ResetOverride(int x, int y)
        {
            if (!IsInBounds(x, y))
                return false;

            int key = CreateKey(x, y);
            if (!overrides.TryGetValue(key, out TissueCellState previous))
                return false;

            overrides.Remove(key);
            TissueCellState current = baseCells.TryGetValue(key, out TissueCellState baseState)
                ? baseState
                : TissueCellState.Neutral;
            RegisterChange(x, y, previous, current);
            return true;
        }

        public void MarkPersisted()
        {
            persistedRevision = Revision;
        }

        public int CountActiveTiles()
        {
            int count = 0;
            foreach (TissueFieldCell _ in EnumerateActiveCells())
                count++;

            return count;
        }

        public IEnumerable<TissueFieldCell> EnumerateActiveCells()
        {
            foreach (KeyValuePair<int, TissueCellState> pair in baseCells)
            {
                TissueCellState state = overrides.TryGetValue(pair.Key, out TissueCellState overrideState)
                    ? overrideState
                    : pair.Value;
                if (state.HasBiologicalPresence)
                    yield return CreateCell(pair.Key, state);
            }

            foreach (KeyValuePair<int, TissueCellState> pair in overrides)
            {
                if (baseCells.ContainsKey(pair.Key) || !pair.Value.HasBiologicalPresence)
                    continue;

                yield return CreateCell(pair.Key, pair.Value);
            }
        }

        internal IEnumerable<TissueFieldCell> EnumerateOverrides()
        {
            foreach (KeyValuePair<int, TissueCellState> pair in overrides)
                yield return CreateCell(pair.Key, pair.Value);
        }

        internal bool SetOverrideFromPersistence(int x, int y, TissueCellState state)
        {
            if (!IsInBounds(x, y))
                return false;
            if (!state.IsNeutral && canContainTissue != null && !canContainTissue(x, y))
                return false;

            overrides[CreateKey(x, y)] = state;
            return true;
        }

        private bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        private int CreateKey(int x, int y)
        {
            return (y * Width) + x;
        }

        private TissueFieldCell CreateCell(int key, TissueCellState state)
        {
            return new TissueFieldCell(key % Width, key / Width, state);
        }

        private void RegisterChange(
            int x,
            int y,
            TissueCellState previous,
            TissueCellState current)
        {
            Revision++;
            Changed?.Invoke(new TissueFieldChange(x, y, previous, current, Revision));
        }

        private static bool StatesEqual(TissueCellState a, TissueCellState b)
        {
            return a.Presence == b.Presence &&
                   a.Vitality == b.Vitality &&
                   a.Corruption == b.Corruption &&
                   a.MemoryDensity == b.MemoryDensity &&
                   a.Flow == b.Flow;
        }
    }
}
