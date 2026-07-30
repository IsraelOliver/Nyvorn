using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Minimal interface for Lighting V2 to enumerate local light sources (torches, etc).
    /// Shields V2 from direct TorchRuntimeSystem coupling.
    /// </summary>
    public interface ILocalLightRegistry
    {
        /// <summary>Get all active light source positions in world space.</summary>
        IEnumerable<Vector2> GetLightSourcePositions();
    }
}
