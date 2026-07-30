using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Gameplay.Crafting;

namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Adapter that bridges Lighting V2 to the existing TorchRuntimeSystem.
    /// Exposes torch positions as the primary local light source.
    /// </summary>
    public sealed class LocalLightAdapter : ILocalLightRegistry
    {
        private readonly TorchRuntimeSystem torchRuntimeSystem;

        public LocalLightAdapter(TorchRuntimeSystem torchRuntimeSystem)
        {
            this.torchRuntimeSystem = torchRuntimeSystem ?? throw new System.ArgumentNullException(nameof(torchRuntimeSystem));
        }

        public IEnumerable<Vector2> GetLightSourcePositions()
        {
            return torchRuntimeSystem.GetLightSourcePositions();
        }
    }
}
