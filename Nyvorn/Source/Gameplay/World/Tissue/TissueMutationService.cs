using Nyvorn.Source.World.Generation;
using System;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueMutationService : ITissueMutationService
    {
        private readonly WorldMap worldMap;
        private readonly ITissueQueryService tissueQueries;

        public TissueMutationService(WorldMap worldMap, ITissueQueryService tissueQueries)
        {
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
            this.tissueQueries = tissueQueries ?? throw new ArgumentNullException(nameof(tissueQueries));
        }

        public bool DamageTile(int tileX, int tileY, float amount)
        {
            if (!IsPositiveFinite(amount))
                return false;

            return MutateExistingTissue(
                tileX,
                tileY,
                state => state.With(vitality: MathF.Max(0f, state.Vitality - amount)));
        }

        public bool RestoreTile(int tileX, int tileY, float amount)
        {
            if (!IsPositiveFinite(amount))
                return false;

            return MutateExistingTissue(
                tileX,
                tileY,
                state => state.With(vitality: MathF.Min(1f, state.Vitality + amount)));
        }

        public bool AddCorruption(int tileX, int tileY, float amount)
        {
            if (!IsPositiveFinite(amount))
                return false;

            return MutateExistingTissue(
                tileX,
                tileY,
                state => state.With(corruption: MathF.Min(1f, state.Corruption + amount)));
        }

        public bool ReduceCorruption(int tileX, int tileY, float amount)
        {
            if (!IsPositiveFinite(amount))
                return false;

            return MutateExistingTissue(
                tileX,
                tileY,
                state => state.With(corruption: MathF.Max(0f, state.Corruption - amount)));
        }

        public bool AddMemory(int tileX, int tileY, float amount)
        {
            if (!IsPositiveFinite(amount))
                return false;

            return MutateExistingTissue(
                tileX,
                tileY,
                state => state.With(memoryDensity: MathF.Min(1f, state.MemoryDensity + amount)));
        }

        public bool SetFlow(int tileX, int tileY, float value)
        {
            if (!IsFinite(value) || value < 0f)
                return false;

            return MutateExistingTissue(
                tileX,
                tileY,
                state => state.With(flow: MathF.Min(1f, value)));
        }

        public bool RemoveTissue(int tileX, int tileY)
        {
            if (!TryGetExistingTissue(tileX, tileY, out int wrappedX, out _))
                return false;

            return worldMap.ClearTissueAt(wrappedX, tileY);
        }

        public bool ResetTile(int tileX, int tileY)
        {
            if (tileY < 0 || tileY >= worldMap.Height)
                return false;

            int wrappedX = worldMap.WrapTileX(tileX);
            return worldMap.ResetTissueAt(wrappedX, tileY);
        }

        private bool MutateExistingTissue(
            int tileX,
            int tileY,
            Func<TissueCellState, TissueCellState> mutation)
        {
            if (!TryGetExistingTissue(tileX, tileY, out int wrappedX, out TissueCellState current))
                return false;

            TissueCellState next = mutation(current);
            return worldMap.TrySetTissueState(wrappedX, tileY, next);
        }

        private bool TryGetExistingTissue(
            int tileX,
            int tileY,
            out int wrappedX,
            out TissueCellState state)
        {
            wrappedX = 0;
            state = TissueCellState.Neutral;
            if (tileY < 0 || tileY >= worldMap.Height)
                return false;

            wrappedX = worldMap.WrapTileX(tileX);
            if (!worldMap.IsSolidAt(wrappedX, tileY))
                return false;

            state = tissueQueries.GetState(wrappedX, tileY);
            return state.HasBiologicalPresence;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && IsFinite(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
