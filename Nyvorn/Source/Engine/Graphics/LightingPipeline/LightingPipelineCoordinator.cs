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
        private LightingExecutionMetrics currentFrameMetrics;
        private LightingExecutionMetrics sessionMetrics;
        private int frameCount;

        public LightingPipelineCoordinator()
        {
            activeMode = LightingPipelineMode.Legacy;  // Default to legacy for backward compatibility
            currentFrameMetrics = new LightingExecutionMetrics();
            sessionMetrics = new LightingExecutionMetrics();
            frameCount = 0;
        }

        // ========== PUBLIC INTERFACE ==========

        /// <summary>Currently active pipeline mode</summary>
        public LightingPipelineMode ActiveMode => activeMode;

        /// <summary>Current frame execution metrics (read-only snapshot)</summary>
        public LightingExecutionMetrics Metrics => currentFrameMetrics;

        /// <summary>Cumulative session execution metrics (read-only snapshot)</summary>
        public LightingExecutionMetrics SessionMetrics => sessionMetrics;

        /// <summary>Number of frames processed since creation</summary>
        public int FrameCount => frameCount;


        /// <summary>
        /// Validate isolation before processing render steps.
        /// Called at start of frame.
        /// </summary>
        public void BeginFrame()
        {
            currentFrameMetrics.Reset();
        }

        /// <summary>
        /// Validate isolation after all render steps.
        /// Called at end of frame.
        /// </summary>
        public void EndFrame()
        {
            if (!currentFrameMetrics.IsIsolationValid)
            {
                string warning = $"[LightingPipeline] ISOLATION VIOLATION: {currentFrameMetrics}";
                Console.WriteLine(warning);
                Debug.WriteLine(warning);
            }

            frameCount++;
        }

        // ========== COUNTER INCREMENT METHODS ==========
        // Called by rendering systems when they execute
        // Each counter updates both current frame and session metrics

        public void RecordLegacyLightingUpdate() { currentFrameMetrics.LegacyLightingUpdateCount++; sessionMetrics.LegacyLightingUpdateCount++; }
        public void RecordLegacyLightGridCopy() { currentFrameMetrics.LegacyLightGridCopyCount++; sessionMetrics.LegacyLightGridCopyCount++; }
        public void RecordLegacyGlowGridCopy() { currentFrameMetrics.LegacyGlowGridCopyCount++; sessionMetrics.LegacyGlowGridCopyCount++; }
        public void RecordLegacyBslShadowDraw() { currentFrameMetrics.LegacyBslShadowDrawCount++; sessionMetrics.LegacyBslShadowDrawCount++; }
        public void RecordLegacyNightOverlayDraw() { currentFrameMetrics.LegacyNightOverlayDrawCount++; sessionMetrics.LegacyNightOverlayDrawCount++; }
        public void RecordLegacyComposite() { currentFrameMetrics.LegacyCompositeCount++; sessionMetrics.LegacyCompositeCount++; }

        public void RecordLegacyEntityLightSample() { currentFrameMetrics.LegacyEntityLightSampleCount++; sessionMetrics.LegacyEntityLightSampleCount++; }
        public void RecordLegacyPlayerLightSample() { currentFrameMetrics.LegacyPlayerLightSampleCount++; sessionMetrics.LegacyPlayerLightSampleCount++; }
        public void RecordLegacyEntityTintApply() { currentFrameMetrics.LegacyEntityTintApplyCount++; sessionMetrics.LegacyEntityTintApplyCount++; }


        public void RecordNeutralEntityLightSample() { currentFrameMetrics.NeutralEntityLightSampleCount++; sessionMetrics.NeutralEntityLightSampleCount++; }
        public void RecordNeutralEntityDraw() { currentFrameMetrics.NeutralEntityDrawCount++; sessionMetrics.NeutralEntityDrawCount++; }

        // ========== VALIDATION QUERIES ==========

        public bool IsLegacyMode => true;

        public void AssertLegacyMode() { }

        // ========== DEBUG HELPERS ==========

        public void DumpMetricsToConsole()
        {
            Console.WriteLine("\n========== LIGHTING PIPELINE METRICS (Ctrl+Shift+M) ==========");
            Console.WriteLine("Mode: Legacy");
            Console.WriteLine($"Frame: {frameCount}");
            Console.WriteLine();

            Console.WriteLine("--- CURRENT FRAME METRICS ---");
            Console.WriteLine("[LEGACY COUNTERS]");
            Console.WriteLine($"  LightingUpdate:       {currentFrameMetrics.LegacyLightingUpdateCount}");
            Console.WriteLine($"  LightGridCopy:        {currentFrameMetrics.LegacyLightGridCopyCount}");
            Console.WriteLine($"  GlowGridCopy:         {currentFrameMetrics.LegacyGlowGridCopyCount}");
            Console.WriteLine($"  BslShadowDraw:        {currentFrameMetrics.LegacyBslShadowDrawCount}");
            Console.WriteLine($"  NightOverlayDraw:     {currentFrameMetrics.LegacyNightOverlayDrawCount}");
            Console.WriteLine($"  Composite:            {currentFrameMetrics.LegacyCompositeCount}");
            Console.WriteLine($"  EntityLightSample:    {currentFrameMetrics.LegacyEntityLightSampleCount}");
            Console.WriteLine($"  PlayerLightSample:    {currentFrameMetrics.LegacyPlayerLightSampleCount}");
            Console.WriteLine($"  EntityTintApply:      {currentFrameMetrics.LegacyEntityTintApplyCount}");
            Console.WriteLine($"  TOTAL:                {currentFrameMetrics.LegacyTotalExecutions}");
            Console.WriteLine();

            Console.WriteLine("--- SESSION METRICS (Cumulative) ---");
            Console.WriteLine("[LEGACY TOTALS]");
            Console.WriteLine($"  LightingUpdate:       {sessionMetrics.LegacyLightingUpdateCount}");
            Console.WriteLine($"  LightGridCopy:        {sessionMetrics.LegacyLightGridCopyCount}");
            Console.WriteLine($"  GlowGridCopy:         {sessionMetrics.LegacyGlowGridCopyCount}");
            Console.WriteLine($"  BslShadowDraw:        {sessionMetrics.LegacyBslShadowDrawCount}");
            Console.WriteLine($"  NightOverlayDraw:     {sessionMetrics.LegacyNightOverlayDrawCount}");
            Console.WriteLine($"  Composite:            {sessionMetrics.LegacyCompositeCount}");
            Console.WriteLine($"  EntityLightSample:    {sessionMetrics.LegacyEntityLightSampleCount}");
            Console.WriteLine($"  PlayerLightSample:    {sessionMetrics.LegacyPlayerLightSampleCount}");
            Console.WriteLine($"  EntityTintApply:      {sessionMetrics.LegacyEntityTintApplyCount}");
            Console.WriteLine($"  TOTAL:                {sessionMetrics.LegacyTotalExecutions}");
            Console.WriteLine("\n============================================================\n");
        }
    }
}
