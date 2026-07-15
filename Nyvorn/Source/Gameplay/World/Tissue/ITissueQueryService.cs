using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.World.Tissue
{
    public readonly record struct TissueAreaSample(
        int ActiveTileCount,
        int TotalTileCount,
        float Coverage,
        float AveragePresence,
        float MaximumPresence,
        float AverageVitality,
        float AverageCorruption,
        float AverageMemory,
        float AverageFlow)
    {
        public bool HasTissue => ActiveTileCount > 0;
    }

    public readonly record struct TissueNodeInfo(
        int Id,
        Vector2 Position,
        bool IsPrimary,
        float Strength,
        int Degree,
        float NestInfluence);

    public readonly record struct TissueChangedEvent(
        Point Tile,
        TissueCellState Previous,
        TissueCellState Current,
        int Revision);

    public interface ITissueQueryService
    {
        TissueCellState GetState(int tileX, int tileY);

        bool HasTissue(
            int tileX,
            int tileY,
            float minimumPresence = TissueCellState.PresenceThreshold);

        TissueAreaSample SampleArea(Rectangle tileArea);

        bool IsDenseRegion(Rectangle tileArea, float minimumDensity);

        bool TryFindNearestConnectedNode(
            Vector2 worldPosition,
            float maximumDistance,
            out TissueNodeInfo node);
    }
}
