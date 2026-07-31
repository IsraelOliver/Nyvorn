using Microsoft.Xna.Framework;
using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Type-safe entity lighting metrics.
    /// Replaces string-based callbacks with explicit methods.
    /// </summary>
    public interface IEntityLightMetrics
    {
        void RecordDraw();
        void RecordPlayerLightSample();
        void RecordEntityTintApply();
    }

    /// <summary>
    /// Abstraction for entity lighting sampling.
    /// Decouples entity rendering from lighting implementation.
    ///
    /// Two implementations:
    /// - LegacyEntityLightSampler: uses WorldLightingSystem
    /// - NeutralEntityLightSampler: always returns Color.White (V3 neutral mode)
    /// </summary>
    public interface IEntityLightSampler
    {
        /// <summary>Sample lighting color at world position for entity tinting.</summary>
        Color SampleLightAt(Vector2 worldPosition);

        /// <summary>Get type-safe metrics recorder for this sampler.</summary>
        IEntityLightMetrics GetMetrics();
    }

    /// <summary>
    /// Legacy entity lighting metrics - type-safe implementation.
    /// </summary>
    internal class LegacyEntityLightMetrics : IEntityLightMetrics
    {
        private readonly LightingPipelineCoordinator coordinator;

        public LegacyEntityLightMetrics(LightingPipelineCoordinator coordinator)
        {
            this.coordinator = coordinator;
        }

        public void RecordDraw() => coordinator.RecordLegacyEntityTintApply();
        public void RecordPlayerLightSample() => coordinator.RecordLegacyPlayerLightSample();
        public void RecordEntityTintApply() => coordinator.RecordLegacyEntityTintApply();
    }

    /// <summary>
    /// Legacy entity lighting: sample from WorldLightingSystem grid.
    /// </summary>
    public class LegacyEntityLightSampler : IEntityLightSampler
    {
        private readonly Gameplay.World.Simulation.WorldLightingSystem lightingSystem;
        private readonly World.WorldMap worldMap;
        private readonly LegacyEntityLightMetrics metrics;

        public LegacyEntityLightSampler(
            Gameplay.World.Simulation.WorldLightingSystem lightingSystem,
            World.WorldMap worldMap,
            LightingPipelineCoordinator coordinator)
        {
            this.lightingSystem = lightingSystem ?? throw new ArgumentNullException(nameof(lightingSystem));
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
            this.metrics = new LegacyEntityLightMetrics(coordinator ?? throw new ArgumentNullException(nameof(coordinator)));
        }

        public Color SampleLightAt(Vector2 worldPosition)
        {
            if (lightingSystem == null)
                return Color.White;

            Point tile = worldMap.WorldToTile(worldPosition);
            Color light = lightingSystem.GetLightAt(tile.X, tile.Y);

            // Recorded by sampler, not by coordinator
            metrics.RecordPlayerLightSample();

            return light;
        }

        public IEntityLightMetrics GetMetrics() => metrics;
    }

    /// <summary>
    /// Neutral entity lighting metrics - type-safe implementation.
    /// </summary>
    internal class NeutralEntityLightMetrics : IEntityLightMetrics
    {
        private readonly LightingPipelineCoordinator coordinator;

        public NeutralEntityLightMetrics(LightingPipelineCoordinator coordinator)
        {
            this.coordinator = coordinator;
        }

        public void RecordDraw() => coordinator.RecordNeutralEntityDraw();
        public void RecordPlayerLightSample() => coordinator.RecordNeutralEntityLightSample();
        public void RecordEntityTintApply() { /* No-op for Neutral */ }
    }

    /// <summary>
    /// Neutral entity lighting for V3 mode validation.
    /// Always returns Color.White (no tinting).
    /// </summary>
    public class NeutralEntityLightSampler : IEntityLightSampler
    {
        private readonly NeutralEntityLightMetrics metrics;

        public NeutralEntityLightSampler(LightingPipelineCoordinator coordinator)
        {
            this.metrics = new NeutralEntityLightMetrics(coordinator ?? throw new ArgumentNullException(nameof(coordinator)));
        }

        public Color SampleLightAt(Vector2 worldPosition)
        {
            // Recorded by metrics, not by coordinator
            metrics.RecordPlayerLightSample();
            return Color.White;
        }

        public IEntityLightMetrics GetMetrics() => metrics;
    }
}
