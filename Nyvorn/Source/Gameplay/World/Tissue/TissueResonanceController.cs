using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.World.Tissue
{
    public readonly record struct TissueResonanceState(
        bool IsActive,
        TissueNodeInfo Node,
        float ResponseStrength,
        float VisualStrength,
        float PulseProgress,
        float LifetimeProgress);

    public sealed class TissueResonanceController
    {
        private readonly ITissueQueryService tissueQueries;
        private readonly TissueEnvironmentSensor environmentSensor;
        private TissueNodeInfo activeNode;
        private float responseStrength;
        private float elapsed;

        public TissueResonanceController(
            ITissueQueryService tissueQueries,
            TissueEnvironmentSensor environmentSensor)
        {
            this.tissueQueries = tissueQueries ?? throw new ArgumentNullException(nameof(tissueQueries));
            this.environmentSensor = environmentSensor ?? throw new ArgumentNullException(nameof(environmentSensor));
        }

        public TissueResonanceState CurrentState { get; private set; }

        public bool Trigger(Vector2 worldPosition)
        {
            environmentSensor.Refresh(worldPosition);
            TissueEnvironmentState environment = environmentSensor.CurrentState;
            if (!environment.HasTissue ||
                !tissueQueries.TryFindNearestNode(
                    worldPosition,
                    TissueConfig.Resonance.NodeSearchDistance,
                    out TissueNodeInfo node))
            {
                Clear();
                return false;
            }

            float coverageRatio = MathHelper.Clamp(
                environment.Coverage / TissueConfig.Resonance.CoverageAtFullStrength,
                0f,
                1f);
            float coverageInfluence = MathHelper.Lerp(
                TissueConfig.Resonance.MinimumCoverageInfluence,
                1f,
                MathF.Sqrt(coverageRatio));
            float response = node.Strength *
                environment.Presence *
                environment.Vitality *
                coverageInfluence;
            if (response < TissueConfig.Resonance.MinimumResponseStrength)
            {
                Clear();
                return false;
            }

            activeNode = node;
            responseStrength = MathHelper.Clamp(response, 0f, 1f);
            elapsed = 0f;
            UpdateState();
            return true;
        }

        public void Update(float dt)
        {
            if (!CurrentState.IsActive)
                return;

            elapsed += MathF.Max(0f, dt);
            if (elapsed >= TissueConfig.Resonance.Duration)
            {
                Clear();
                return;
            }

            UpdateState();
        }

        public void Clear()
        {
            activeNode = default;
            responseStrength = 0f;
            elapsed = 0f;
            CurrentState = default;
        }

        private void UpdateState()
        {
            float lifetimeProgress = MathHelper.Clamp(
                elapsed / TissueConfig.Resonance.Duration,
                0f,
                1f);
            float pulseProgress = MathHelper.Clamp(
                elapsed / TissueConfig.Resonance.PulseDuration,
                0f,
                1f);
            float fade = MathF.Pow(1f - lifetimeProgress, TissueConfig.Resonance.FadePower);
            float visualStrength = MathHelper.Clamp(
                responseStrength * TissueConfig.Resonance.VisualGain * fade,
                0f,
                1f);
            CurrentState = new TissueResonanceState(
                true,
                activeNode,
                responseStrength,
                visualStrength,
                pulseProgress,
                lifetimeProgress);
        }
    }
}
