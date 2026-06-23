using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.World.Tissue
{
    public readonly record struct TissueEnvironmentState(
        bool HasTissue,
        float Coverage,
        float Presence,
        float Vitality,
        float Corruption,
        float Memory,
        float Flow,
        float DistanceToNearestNode,
        int Revision);

    public sealed class TissueEnvironmentSensor
    {
        private readonly WorldMap worldMap;
        private readonly ITissueQueryService tissueQueries;
        private float sampleTimer;

        public TissueEnvironmentSensor(WorldMap worldMap, ITissueQueryService tissueQueries)
        {
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
            this.tissueQueries = tissueQueries ?? throw new ArgumentNullException(nameof(tissueQueries));
        }

        public TissueEnvironmentState CurrentState { get; private set; }

        public void Initialize(Vector2 worldPosition)
        {
            sampleTimer = 0f;
            Refresh(worldPosition);
        }

        public void Update(float dt, Vector2 worldPosition)
        {
            sampleTimer -= MathF.Max(0f, dt);
            if (sampleTimer > 0f && CurrentState.Revision == worldMap.TissueRevision)
                return;

            Refresh(worldPosition);
        }

        public void Refresh(Vector2 worldPosition)
        {
            Point centerTile = worldMap.WorldToTile(worldPosition);
            int diameter = TissueConfig.EnvironmentSensor.SampleDiameterTiles;
            int radius = diameter / 2;
            Rectangle sampleBounds = new(
                centerTile.X - radius,
                centerTile.Y - radius,
                diameter,
                diameter);
            TissueAreaSample sample = tissueQueries.SampleArea(sampleBounds);

            float distanceToNearestNode = float.PositiveInfinity;
            if (tissueQueries.TryFindNearestConnectedNode(
                    worldPosition,
                    TissueConfig.EnvironmentSensor.NearestNodeDistance,
                    out TissueNodeInfo nearestNode))
            {
                distanceToNearestNode = GetLoopAwareDistance(
                    worldPosition,
                    nearestNode.Position,
                    worldMap.PixelWidth);
            }

            CurrentState = new TissueEnvironmentState(
                sample.HasTissue,
                sample.Coverage,
                sample.AveragePresence,
                sample.AverageVitality,
                sample.AverageCorruption,
                sample.AverageMemory,
                sample.AverageFlow,
                distanceToNearestNode,
                worldMap.TissueRevision);
            sampleTimer = TissueConfig.EnvironmentSensor.SampleInterval;
        }

        private static float GetLoopAwareDistance(Vector2 a, Vector2 b, float worldWidth)
        {
            float deltaX = MathF.Abs(a.X - b.X);
            deltaX %= worldWidth;
            deltaX = MathF.Min(deltaX, worldWidth - deltaX);
            float deltaY = a.Y - b.Y;
            return MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        }
    }
}
