namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Real execution counters for lighting pipeline isolation validation.
    ///
    /// Incremented when actual code paths execute.
    /// Used to prove that Legacy and V3 systems never execute simultaneously.
    ///
    /// In Legacy mode: Legacy counters > 0, V3 counters == 0
    /// In V3 mode: Legacy counters == 0, V3 counters > 0
    ///
    /// Counters reset at level load or pipeline switch.
    /// </summary>
    public struct LightingExecutionMetrics
    {
        // === LEGACY COUNTERS ===
        // Must be zero when running in V3 mode

        /// <summary>WorldLightingSystem.Update() executions</summary>
        public int LegacyLightingUpdateCount { get; set; }

        /// <summary>WorldLightingSystem.CopyLightGridTo() executions</summary>
        public int LegacyLightGridCopyCount { get; set; }

        /// <summary>WorldLightingSystem.CopyGlowGridTo() executions</summary>
        public int LegacyGlowGridCopyCount { get; set; }

        /// <summary>BSL shadow system draw executions</summary>
        public int LegacyBslShadowDrawCount { get; set; }

        /// <summary>DrawNightOverlay() executions</summary>
        public int LegacyNightOverlayDrawCount { get; set; }

        /// <summary>Legacy lighting composite/multiply executions</summary>
        public int LegacyCompositeCount { get; set; }

        // === V3 COUNTERS ===
        // Must be zero when running in Legacy mode

        /// <summary>LightingV3System.Update() executions</summary>
        public int V3UpdateCount { get; set; }

        /// <summary>LightingV3Renderer.Composite() executions</summary>
        public int V3CompositeCount { get; set; }

        // === HELPERS ===

        /// <summary>Total legacy executions this frame</summary>
        public int LegacyTotalExecutions =>
            LegacyLightingUpdateCount +
            LegacyLightGridCopyCount +
            LegacyGlowGridCopyCount +
            LegacyBslShadowDrawCount +
            LegacyNightOverlayDrawCount +
            LegacyCompositeCount;

        /// <summary>Total V3 executions this frame</summary>
        public int V3TotalExecutions =>
            V3UpdateCount +
            V3CompositeCount;

        /// <summary>Validate isolation: Legacy and V3 are mutually exclusive</summary>
        public bool IsIsolationValid =>
            (LegacyTotalExecutions == 0 && V3TotalExecutions > 0) ||  // V3 mode
            (LegacyTotalExecutions > 0 && V3TotalExecutions == 0);   // Legacy mode

        /// <summary>Reset all counters to zero</summary>
        public void Reset()
        {
            LegacyLightingUpdateCount = 0;
            LegacyLightGridCopyCount = 0;
            LegacyGlowGridCopyCount = 0;
            LegacyBslShadowDrawCount = 0;
            LegacyNightOverlayDrawCount = 0;
            LegacyCompositeCount = 0;

            V3UpdateCount = 0;
            V3CompositeCount = 0;
        }

        /// <summary>Return human-readable summary</summary>
        public override string ToString()
        {
            return $"[Metrics] Legacy={LegacyTotalExecutions} V3={V3TotalExecutions} Valid={IsIsolationValid}";
        }
    }
}
