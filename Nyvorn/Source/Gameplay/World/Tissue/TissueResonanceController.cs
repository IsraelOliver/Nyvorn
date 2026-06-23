using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.World.Tissue
{
    public readonly record struct TissueResonanceState(
        bool IsActive,
        TissueNodeInfo OriginNode,
        TissuePropagationMap Propagation,
        float ResponseStrength,
        float VisualStrength,
        float ElapsedTime,
        float PulseFront,
        float PulseBack,
        float MaximumDistance);

    public sealed class TissueResonanceController
    {
        private readonly ITissueQueryService tissueQueries;
        private readonly TissueEnvironmentSensor environmentSensor;
        private TissueNodeInfo activeNode;
        private TissuePropagationMap propagation;
        private float responseStrength;
        private float elapsed;
        private float configuredMaximumDistance = TissueConfig.Resonance.DefaultMaxPropagationDistance;
        private float activeMaximumDistance;

        public TissueResonanceController(
            ITissueQueryService tissueQueries,
            TissueEnvironmentSensor environmentSensor)
        {
            this.tissueQueries = tissueQueries ?? throw new ArgumentNullException(nameof(tissueQueries));
            this.environmentSensor = environmentSensor ?? throw new ArgumentNullException(nameof(environmentSensor));
        }

        public TissueResonanceState CurrentState { get; private set; }

        public void SetViewport(float viewWidth, float viewHeight)
        {
            if (float.IsNaN(viewWidth) || float.IsInfinity(viewWidth) ||
                float.IsNaN(viewHeight) || float.IsInfinity(viewHeight) ||
                viewWidth <= 0f || viewHeight <= 0f)
            {
                configuredMaximumDistance = TissueConfig.Resonance.DefaultMaxPropagationDistance;
                return;
            }

            float diagonal = MathF.Sqrt((viewWidth * viewWidth) + (viewHeight * viewHeight));
            configuredMaximumDistance = MathF.Max(
                TissueConfig.Resonance.MinimumPropagationDistance,
                diagonal * TissueConfig.Resonance.ViewportDistanceScale);
        }

        public bool Trigger(Vector2 worldPosition)
        {
            environmentSensor.Refresh(worldPosition);
            TissueEnvironmentState environment = environmentSensor.CurrentState;
            if (!environment.HasTissue ||
                !tissueQueries.TryFindNearestConnectedNode(
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

            if (!tissueQueries.TryBuildPropagation(
                    node.Id,
                    configuredMaximumDistance,
                    out TissuePropagationMap propagationMap))
            {
                Clear();
                return false;
            }

            activeNode = node;
            propagation = propagationMap;
            activeMaximumDistance = configuredMaximumDistance;
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
            float pulseBack = (elapsed * TissueConfig.Resonance.PulseSpeed) - TissueConfig.Resonance.TrailLength;
            if (pulseBack >= activeMaximumDistance)
            {
                Clear();
                return;
            }

            UpdateState();
        }

        public void Clear()
        {
            activeNode = default;
            propagation = null;
            activeMaximumDistance = 0f;
            responseStrength = 0f;
            elapsed = 0f;
            CurrentState = default;
        }

        private void UpdateState()
        {
            float visualStrength = MathHelper.Clamp(
                responseStrength * TissueConfig.Resonance.VisualGain,
                0f,
                1f);
            float pulseFront = elapsed * TissueConfig.Resonance.PulseSpeed;
            CurrentState = new TissueResonanceState(
                true,
                activeNode,
                propagation,
                responseStrength,
                visualStrength,
                elapsed,
                pulseFront,
                pulseFront - TissueConfig.Resonance.TrailLength,
                activeMaximumDistance);
        }
    }
}
