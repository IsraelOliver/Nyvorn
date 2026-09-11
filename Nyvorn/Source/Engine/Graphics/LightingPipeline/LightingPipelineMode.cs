namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Rendering pipeline mode for world lighting.
    ///
    /// Legacy:
    ///   - V6 lighting (V6LightingSystem + V6LightMapRenderer), Tile/Pixel presentation.
    /// V7:
    ///   - LightingV7System (sky + block channels, Terraria-style sweeps, overbright compose).
    /// </summary>
    public enum LightingPipelineMode
    {
        /// <summary>V6 lighting (kept until V7 is approved).</summary>
        Legacy = 0,

        /// <summary>V7 lighting (Source/Engine/Graphics/LightingV7).</summary>
        V7 = 1
    }
}
