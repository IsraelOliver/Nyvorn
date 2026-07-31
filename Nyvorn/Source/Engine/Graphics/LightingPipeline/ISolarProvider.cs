using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Provides solar state (direction, color, intensity) for directional lighting.
    /// Decouples LightingV3Foundation from game's specific time/weather system.
    /// </summary>
    public interface ISolarProvider
    {
        /// <summary>
        /// Get current directional sun state.
        /// Should return valid state even during night (with zero intensity).
        /// </summary>
        LightingV3SunState GetSunState();
    }
}
