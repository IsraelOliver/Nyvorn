using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenContext
    {
        public required WorldMap WorldMap { get; init; }
        public required WorldGenConfig Config { get; init; }
        public required Random Random { get; init; }

        public int[] SurfaceHeights { get; set; } = Array.Empty<int>();
        public WorldLayerDefinition[] LayerDefinitions { get; set; } = Array.Empty<WorldLayerDefinition>();
        public Point SpawnTile { get; set; }
        public Dictionary<string, string> DebugStats { get; } = new();
        public TissueField TissueField { get; set; }
        public WorldGenProgressReporter ProgressReporter { get; set; }

        public WorldLayerType GetLayerAtY(int y)
        {
            return GetLayerDefinitionAtY(y).LayerType;
        }

        public WorldLayerDefinition GetLayerDefinitionAtY(int y)
        {
            if (LayerDefinitions.Length == 0)
                return new WorldLayerDefinition(WorldLayerType.DeepCavern, 0, System.Math.Max(0, WorldMap.Height - 1));

            int clampedY = System.Math.Clamp(y, 0, WorldMap.Height - 1);
            for (int i = 0; i < LayerDefinitions.Length; i++)
            {
                if (LayerDefinitions[i].Contains(clampedY))
                    return LayerDefinitions[i];
            }

            return LayerDefinitions[^1];
        }

        public WorldLayerDefinition GetLayerDefinition(WorldLayerType layerType)
        {
            for (int i = 0; i < LayerDefinitions.Length; i++)
            {
                if (LayerDefinitions[i].LayerType == layerType)
                    return LayerDefinitions[i];
            }

            return GetLayerDefinitionAtY(WorldMap.Height - 1);
        }

        public float GetNormalizedDepthInLayer(int y)
        {
            return GetLayerDefinitionAtY(y).GetNormalizedDepth(y);
        }

        public float SampleTerrain1D(OpenSimplexNoise noise, int x, float frequency, float seedOffset)
        {
            return WorldNoiseSampler.SampleTerrain1D(
                noise,
                x,
                WorldMap.Width,
                frequency,
                seedOffset,
                Config.WrapHorizontally);
        }
    }
}
