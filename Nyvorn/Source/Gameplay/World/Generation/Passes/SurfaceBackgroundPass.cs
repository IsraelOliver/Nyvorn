using System;
using Nyvorn.Source.World.Generation.Biomes;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class SurfaceBackgroundPass : IWorldGenPass
    {
        private const int StartOffsetBelowSurface = 2;
        private const float FadeStartPercent = 0.60f;

        public string Name => "SurfaceBackground";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Gerando fundo de terra com fissuras");

            WorldLayerDefinition shallowLayer = context.GetLayerDefinition(WorldLayerType.ShallowUnderground);
            WorldLayerDefinition cavernLayer = context.GetLayerDefinition(WorldLayerType.Cavern);

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = context.SurfaceHeights[x];
                int startY = Math.Clamp(surfaceY + StartOffsetBelowSurface, shallowLayer.StartY, shallowLayer.EndY);
                int endY = shallowLayer.EndY;
                int layerHeight = Math.Max(1, endY - startY + 1);

                for (int y = startY; y <= endY; y++)
                {
                    float depthT = (y - startY) / (float)layerHeight;
                    float fadeT = Math.Max(0f, (depthT - FadeStartPercent) / (1f - FadeStartPercent));

                    if (WorldFieldSampler.SampleBackgroundFissure(context, x, y))
                        continue;

                    if (fadeT > 0f)
                    {
                        if (new Random(unchecked((int)(x * 73856093 ^ y * 19349663))).NextSingle() < fadeT)
                            continue;
                    }

                    context.WorldMap.SetBackgroundTile(x, y, TileType.Dirt);
                }
            }
        }
    }
}
