using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.World.Tissue
{
    public readonly record struct TissueResonanceState(
        bool IsActive,
        TissueNodeInfo OriginNode,
        TissuePropagationResult Propagation,
        float ResponseStrength,
        float VisualStrength,
        float MemoryStrength,
        float NodeAfterglowStrength,
        float ElapsedTime,
        float PulseFront,
        float PulseBack,
        float MaximumDistance);

    public sealed class TissueResonanceController
    {
        private readonly ITissueQueryService tissueQueries;
        private readonly ITissuePropagationService tissuePropagation;
        private readonly TissueEnvironmentSensor environmentSensor;
        private TissueNodeInfo activeNode;
        private TissuePropagationResult propagation;
        private float responseStrength;
        private float elapsed;
        private float configuredMaximumDistance = TissueConfig.Resonance.DefaultMaxPropagationDistance;
        private float activeMaximumDistance;

        public TissueResonanceController(
            ITissueQueryService tissueQueries,
            ITissuePropagationService tissuePropagation,
            TissueEnvironmentSensor environmentSensor)
        {
            this.tissueQueries = tissueQueries ?? throw new ArgumentNullException(nameof(tissueQueries));
            this.tissuePropagation = tissuePropagation ?? throw new ArgumentNullException(nameof(tissuePropagation));
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

            TissuePropagationRequest request = new(
                node.Id,
                configuredMaximumDistance,
                response,
                TissueConfig.Resonance.PulseFadePower,
                TissueConfig.Propagation.DefaultMinimumConductivity,
                TissueSignalChannel.Native);
            if (!tissuePropagation.TryPropagate(
                    request,
                    out TissuePropagationResult propagationResult))
            {
                Clear();
                return false;
            }

            activeNode = node;
            propagation = propagationResult;
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
            float pulseCompletionTime = GetPulseCompletionTime();
            float maximumAfterglowLifetime = MathF.Max(
                TissueConfig.Resonance.MemoryLifetime,
                TissueConfig.Resonance.NodeAfterglowLifetime);
            if (elapsed >= pulseCompletionTime + maximumAfterglowLifetime)
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
            float timeAfterPulse = elapsed - GetPulseCompletionTime();
            float memoryStrength = CalculateMemoryFade(
                timeAfterPulse,
                TissueConfig.Resonance.MemoryLifetime);
            float nodeAfterglowStrength = CalculateMemoryFade(
                timeAfterPulse,
                TissueConfig.Resonance.NodeAfterglowLifetime);
            CurrentState = new TissueResonanceState(
                true,
                activeNode,
                propagation,
                responseStrength,
                visualStrength,
                memoryStrength,
                nodeAfterglowStrength,
                elapsed,
                pulseFront,
                pulseFront - TissueConfig.Resonance.PulseTrailLength,
                activeMaximumDistance);
        }

        private float GetPulseCompletionTime()
        {
            return (activeMaximumDistance + TissueConfig.Resonance.PulseTrailLength) /
                TissueConfig.Resonance.PulseSpeed;
        }

        private static float CalculateMemoryFade(float elapsedAfterPulse, float lifetime)
        {
            if (elapsedAfterPulse <= 0f)
                return 1f;
            if (lifetime <= 0f)
                return 0f;

            float progress = MathHelper.Clamp(elapsedAfterPulse / lifetime, 0f, 1f);
            return MathF.Pow(1f - progress, TissueConfig.Resonance.MemoryFadeCurve);
        }
    }
}
