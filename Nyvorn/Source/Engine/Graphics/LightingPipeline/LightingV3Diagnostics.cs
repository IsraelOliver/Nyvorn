namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Central configuration for Phase 3 lighting diagnostics.
    /// Controls whether diagnostic code paths execute (harness, logging, capture).
    /// When disabled: zero diagnostic overhead.
    /// When enabled: may allocate for diagnostics (collections, strings, logs).
    /// </summary>
    public static class LightingV3Diagnostics
    {
        /// <summary>
        /// Enable Phase 3.1 runtime validation harness.
        /// When false: no capture, no marker detection, no dumps, no logs.
        /// </summary>
        public static readonly bool EnablePhase31RuntimeValidation = false;
    }
}
