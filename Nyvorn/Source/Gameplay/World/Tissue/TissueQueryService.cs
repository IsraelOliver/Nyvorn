using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation;
using System;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueQueryService : ITissueQueryService
    {
        private readonly WorldMap worldMap;
        private readonly TissueField tissueField;
        private readonly TissueNetwork tissueNetwork;

        public TissueQueryService(
            WorldMap worldMap,
            TissueField tissueField,
            TissueNetwork tissueNetwork)
        {
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
            this.tissueField = tissueField ?? throw new ArgumentNullException(nameof(tissueField));
            this.tissueNetwork = tissueNetwork ?? throw new ArgumentNullException(nameof(tissueNetwork));

            if (tissueField.Width != worldMap.Width || tissueField.Height != worldMap.Height)
            {
                throw new ArgumentException(
                    "TissueField e WorldMap precisam ter as mesmas dimensoes.",
                    nameof(tissueField));
            }
        }

        public TissueCellState GetState(int tileX, int tileY)
        {
            if (tileY < 0 || tileY >= worldMap.Height)
                return TissueCellState.Neutral;

            return tissueField.GetState(worldMap.WrapTileX(tileX), tileY);
        }

        public bool HasTissue(
            int tileX,
            int tileY,
            float minimumPresence = TissueCellState.PresenceThreshold)
        {
            TissueCellState state = GetState(tileX, tileY);
            float threshold = NormalizeThreshold(minimumPresence);
            return state.HasBiologicalPresence && state.Presence >= threshold;
        }

        public TissueAreaSample SampleArea(Rectangle tileArea)
        {
            if (tileArea.Width <= 0 || tileArea.Height <= 0)
                return default;

            int sampleWidth = Math.Min(tileArea.Width, worldMap.Width);
            long requestedBottom = (long)tileArea.Y + tileArea.Height;
            int startY = Math.Max(0, tileArea.Y);
            int endY = (int)Math.Min(worldMap.Height, requestedBottom);
            if (sampleWidth <= 0 || startY >= endY)
                return default;

            int totalTileCount = sampleWidth * (endY - startY);
            int activeTileCount = 0;
            float presenceTotal = 0f;
            float maximumPresence = 0f;
            float vitalityTotal = 0f;
            float corruptionTotal = 0f;
            float memoryTotal = 0f;
            float flowTotal = 0f;
            int wrappedStartX = worldMap.WrapTileX(tileArea.X);

            for (int y = startY; y < endY; y++)
            {
                int wrappedX = wrappedStartX;
                for (int offsetX = 0; offsetX < sampleWidth; offsetX++)
                {
                    TissueCellState state = tissueField.GetState(wrappedX, y);
                    wrappedX++;
                    if (wrappedX == worldMap.Width)
                        wrappedX = 0;

                    if (!state.HasBiologicalPresence)
                        continue;

                    activeTileCount++;
                    presenceTotal += state.Presence;
                    maximumPresence = MathF.Max(maximumPresence, state.Presence);
                    vitalityTotal += state.Vitality;
                    corruptionTotal += state.Corruption;
                    memoryTotal += state.MemoryDensity;
                    flowTotal += state.Flow;
                }
            }

            float coverage = activeTileCount / (float)totalTileCount;
            float inverseActiveCount = activeTileCount > 0 ? 1f / activeTileCount : 0f;
            return new TissueAreaSample(
                activeTileCount,
                totalTileCount,
                coverage,
                presenceTotal * inverseActiveCount,
                maximumPresence,
                vitalityTotal * inverseActiveCount,
                corruptionTotal * inverseActiveCount,
                memoryTotal * inverseActiveCount,
                flowTotal * inverseActiveCount);
        }

        public bool IsDenseRegion(Rectangle tileArea, float minimumDensity)
        {
            TissueAreaSample sample = SampleArea(tileArea);
            return sample.TotalTileCount > 0 &&
                   sample.Coverage >= NormalizeThreshold(minimumDensity);
        }

        public bool TryFindNearestNode(
            Vector2 worldPosition,
            float maximumDistance,
            out TissueNodeInfo node)
        {
            node = default;
            if (!IsFinite(worldPosition.X) ||
                !IsFinite(worldPosition.Y) ||
                !IsFinite(maximumDistance) ||
                maximumDistance <= 0f)
            {
                return false;
            }

            if (!tissueNetwork.TryFindNearestNode(worldPosition, maximumDistance, out TissueNode nearest))
                return false;

            node = new TissueNodeInfo(
                nearest.Id,
                nearest.Position,
                nearest.IsPrimary,
                nearest.Strength,
                nearest.Degree,
                nearest.NestInfluence);
            return true;
        }

        public bool TryFindNearestConnectedNode(
            Vector2 worldPosition,
            float maximumDistance,
            out TissueNodeInfo node)
        {
            node = default;
            if (!IsFinite(worldPosition.X) ||
                !IsFinite(worldPosition.Y) ||
                !IsFinite(maximumDistance) ||
                maximumDistance <= 0f)
            {
                return false;
            }

            if (!tissueNetwork.TryFindNearestConnectedNode(worldPosition, maximumDistance, out TissueNode nearest))
                return false;

            node = new TissueNodeInfo(
                nearest.Id,
                nearest.Position,
                nearest.IsPrimary,
                nearest.Strength,
                nearest.Degree,
                nearest.NestInfluence);
            return true;
        }

        public bool TryBuildPropagation(
            int originNodeId,
            float maximumDistance,
            out TissuePropagationMap propagation)
        {
            propagation = null;
            if (!IsFinite(maximumDistance) || maximumDistance <= 0f)
                return false;

            return tissueNetwork.TryBuildPropagation(
                originNodeId,
                maximumDistance,
                out propagation);
        }

        private static float NormalizeThreshold(float value)
        {
            if (!IsFinite(value))
                return TissueCellState.PresenceThreshold;

            return Math.Clamp(value, 0f, 1f);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
