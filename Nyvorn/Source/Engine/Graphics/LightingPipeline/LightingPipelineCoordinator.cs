using System;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Central coordinator for lighting pipeline mode selection.
    ///
    /// Single point of truth for determining which lighting system executes.
    /// Provides:
    /// - Mode switching (Legacy ↔ V3)
    /// - Execution counter tracking
    /// - Isolation validation
    /// - Log events for mode changes
    ///
    /// OWNERSHIP:
    /// - This class owns the mode state
    /// - No other class makes pipeline decisions unilaterally
    /// - Calling code checks coordinator, not internal flags
    ///
    /// USAGE:
    /// if (LightingPipelineCoordinator.Instance.ActiveMode == LightingPipelineMode.Legacy)
    ///     worldLightingSystem.Update(...)
    /// else
    ///     lightingV3System.Update(...)
    /// </summary>
    public sealed class LightingPipelineCoordinator
    {
        private static readonly Lazy<LightingPipelineCoordinator> Instance =
            new Lazy<LightingPipelineCoordinator>(() => new LightingPipelineCoordinator());

        public static LightingPipelineCoordinator I => Instance.Value;

        private LightingPipelineMode activeMode;
        private LightingExecutionMetrics metrics;
        private int frameCount;

        public LightingPipelineCoordinator()
        {
            activeMode = LightingPipelineMode.Legacy;  // Default to legacy for backward compatibility
            metrics = new LightingExecutionMetrics();
            frameCount = 0;
        }

        // ========== PUBLIC INTERFACE ==========

        /// <summary>Currently active pipeline mode</summary>
        public LightingPipelineMode ActiveMode => activeMode;

        /// <summary>Current execution metrics (read-only snapshot)</summary>
        public LightingExecutionMetrics Metrics => metrics;

        /// <summary>Number of frames processed since creation</summary>
        public int FrameCount => frameCount;

        /// <summary>
        /// Switch pipeline mode.
        /// Automatically resets metrics for new frame.
        /// Logs mode change.
        /// </summary>
        public void SetMode(LightingPipelineMode newMode)
        {
            if (newMode == activeMode)
                return;  // No change

            LightingPipelineMode previousMode = activeMode;
            activeMode = newMode;

            // Reset metrics when switching
            metrics.Reset();

            Console.WriteLine(
                $"[LightingPipeline] Mode switched: {previousMode} → {activeMode} (Frame {frameCount})");
        }

        /// <summary>
        /// Validate isolation before processing render steps.
        /// Called at start of frame.
        /// </summary>
        public void BeginFrame()
        {
            metrics.Reset();
        }

        /// <summary>
        /// Validate isolation after all render steps.
        /// Called at end of frame.
        /// </summary>
        public void EndFrame()
        {
            if (!metrics.IsIsolationValid)
            {
                string warning = $"[LightingPipeline] ISOLATION VIOLATION: {metrics}";
                Console.WriteLine(warning);
                Debug.WriteLine(warning);
            }

            frameCount++;
        }

        // ========== COUNTER INCREMENT METHODS ==========
        // Called by rendering systems when they execute

        public void RecordLegacyLightingUpdate() => metrics.LegacyLightingUpdateCount++;
        public void RecordLegacyLightGridCopy() => metrics.LegacyLightGridCopyCount++;
        public void RecordLegacyGlowGridCopy() => metrics.LegacyGlowGridCopyCount++;
        public void RecordLegacyBslShadowDraw() => metrics.LegacyBslShadowDrawCount++;
        public void RecordLegacyNightOverlayDraw() => metrics.LegacyNightOverlayDrawCount++;
        public void RecordLegacyComposite() => metrics.LegacyCompositeCount++;

        public void RecordV3Update() => metrics.V3UpdateCount++;
        public void RecordV3Composite() => metrics.V3CompositeCount++;

        // ========== VALIDATION QUERIES ==========

        public bool IsLegacyMode => activeMode == LightingPipelineMode.Legacy;
        public bool IsV3Mode => activeMode == LightingPipelineMode.V3;

        public void AssertLegacyMode()
        {
            if (!IsLegacyMode)
                throw new InvalidOperationException($"Expected Legacy mode, but active mode is {activeMode}");
        }

        public void AssertV3Mode()
        {
            if (!IsV3Mode)
                throw new InvalidOperationException($"Expected V3 mode, but active mode is {activeMode}");
        }
    }
}
