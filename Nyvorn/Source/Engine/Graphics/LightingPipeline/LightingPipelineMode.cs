namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Rendering pipeline mode for world lighting.
    ///
    /// Exactly one mode is active per frame.
    /// Modes are mutually exclusive - no hybrid rendering.
    ///
    /// Legacy:
    ///   - Uses WorldLightingSystem (BFS-based)
    ///   - Applies BSL shadows
    ///   - Draws night overlay
    ///   - Legacy light/glow texture composition
    ///   - Temporal backward compatibility
    ///
    /// V3:
    ///   - Separate RenderTarget-based pipeline
    ///   - New lighting architecture (isolated)
    ///   - No legacy system execution
    ///   - New composition rules
    /// </summary>
    public enum LightingPipelineMode
    {
        /// <summary>
        /// Legacy lighting system (WorldLightingSystem + BSL + night overlay).
        /// Current production mode.
        /// </summary>
        Legacy = 0,

        /// <summary>
        /// New lighting architecture (LightingV3).
        /// Experimental / development mode.
        /// </summary>
        V3 = 1
    }
}
