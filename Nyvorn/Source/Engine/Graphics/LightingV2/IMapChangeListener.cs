using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Listener for map changes that affect lighting calculations.
    /// Implement this to receive notifications of tile modifications.
    /// </summary>
    public interface IMapChangeListener
    {
        /// <summary>Called when a foreground tile is broken or placed.</summary>
        void OnForegroundTileChanged(int tileX, int tileY);

        /// <summary>Called when a background tile is broken or placed.</summary>
        void OnBackgroundTileChanged(int tileX, int tileY);
    }
}
