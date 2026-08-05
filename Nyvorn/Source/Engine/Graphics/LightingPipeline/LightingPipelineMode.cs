namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Rendering pipeline mode for world lighting.
    ///
    /// Legacy:
    ///   - Uses WorldLightingSystem (BFS-based)
    ///   - Applies BSL shadows
    ///   - Draws night overlay
    ///   - Legacy light/glow texture composition
    /// </summary>
    public enum LightingPipelineMode
    {
        /// <summary>
        /// Legacy lighting system (WorldLightingSystem + BSL + night overlay).
        /// </summary>
        Legacy = 0
    }
}
