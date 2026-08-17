using Nyvorn.Source.World.Generation;
using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// P2-E-L1: Helper to compute sky transmission factor by world depth.
    ///
    /// Returns a factor [0.0, 1.0] that modulates natural sky contribution.
    ///
    /// Surface/ShallowUnderground: 1.0 (full sky contribution)
    /// Cavern/DeepCavern: 0.0 (no sky contribution)
    ///
    /// Per-cell, world-anchored. Respects layer boundaries.
    /// Torch and artificial lights are NOT affected by this factor.
    /// </summary>
    public static class SkyTransmissionHelper
    {
        /// <summary>
        /// Get sky transmission factor at a given world tile Y.
        /// </summary>
        public static float GetSkyTransmissionAtWorldY(int worldTileY, WorldLayerDefinition[] layerDefinitions)
        {
            if (layerDefinitions == null || layerDefinitions.Length == 0)
                return 1.0f;  // Fallback: assume sky contribution

            // Find which layer contains this worldTileY
            foreach (var layer in layerDefinitions)
            {
                if (layer.Contains(worldTileY))
                {
                    // Surface and ShallowUnderground allow full skylight
                    if (layer.LayerType == WorldLayerType.Surface ||
                        layer.LayerType == WorldLayerType.ShallowUnderground ||
                        layer.LayerType == WorldLayerType.Space)
                    {
                        return 1.0f;
                    }

                    // Cavern and DeepCavern block skylight (hard cutoff)
                    if (layer.LayerType == WorldLayerType.Cavern ||
                        layer.LayerType == WorldLayerType.DeepCavern)
                    {
                        return 0.0f;
                    }
                }
            }

            // Fallback: assume sky contribution if layer not found
            return 1.0f;
        }
    }
}
