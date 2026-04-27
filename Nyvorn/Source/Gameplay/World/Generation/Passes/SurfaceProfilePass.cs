using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class SurfaceProfilePass : IWorldGenPass
    {
        public string Name => "SurfaceProfile";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Modelando superficie");

            int[] heights = new int[context.WorldMap.Width];
            WorldLayerDefinition surfaceLayer = context.GetLayerDefinition(WorldLayerType.Surface);
            int flatY = ClampSurfaceHeight(context.Config.SurfaceBaseHeight, surfaceLayer, context.WorldMap.Height);

            OpenSimplexNoise noise = new OpenSimplexNoise(context.Config.Seed);

            int minY = int.MaxValue;
            int maxY = int.MinValue;

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                float macroShape = GetMacroShape(context, noise, x);
                float terrainOffset = GetTerrainOffset(context, noise, x);
                float detail = context.SampleTerrain1D(noise, x, 0.090f, 9000f) * 3.0f;
                float centerMask = GetCenterPlainsMask(x, context.WorldMap.Width);

                float softenedTerrainOffset = Lerp(terrainOffset * 0.18f, terrainOffset, 1f - centerMask);
                float softenedDetail = Lerp(detail * 0.10f, detail, 1f - centerMask);
                float softenedMacroShape = Lerp(macroShape * 0.35f, macroShape, 1f - centerMask);

                int surfaceY = flatY + (int)MathF.Round(softenedMacroShape + softenedTerrainOffset + softenedDetail);
                surfaceY = Math.Clamp(surfaceY, 8, context.WorldMap.Height - 10);

                heights[x] = surfaceY;

                if (surfaceY < minY)
                    minY = surfaceY;

                if (surfaceY > maxY)
                    maxY = surfaceY;

                if ((x & 63) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Modelando superficie");
            }

            context.SurfaceHeights = heights;
            context.DebugStats["Surface.MinY"] = minY.ToString();
            context.DebugStats["Surface.MaxY"] = maxY.ToString();
            context.DebugStats["Surface.CenterY"] = heights[context.WorldMap.Width / 2].ToString();
            context.ProgressReporter?.Complete(Name, "Superficie pronta");
        }

        private static float GetMacroShape(WorldGenContext context, OpenSimplexNoise noise, int x)
        {
            float continental =
                context.SampleTerrain1D(noise, x, 0.0025f, 3000f) * 30f;

            float broadVariation =
                context.SampleTerrain1D(noise, x, 0.0060f, 5000f) * 12f;

            return continental + broadVariation;
        }

        private static float GetTerrainOffset(WorldGenContext context, OpenSimplexNoise noise, int x)
        {
            float region = context.SampleTerrain1D(noise, x, 0.0040f, 1000f);

            float smooth =
                context.SampleTerrain1D(noise, x, 0.0100f, 0f) * 20f +
                context.SampleTerrain1D(noise, x, 0.0250f, 200f) * 5f;

            float rolling =
                context.SampleTerrain1D(noise, x, 0.0180f, 400f) * 32f +
                context.SampleTerrain1D(noise, x, 0.0400f, 600f) * 10f;

            float rugged =
                context.SampleTerrain1D(noise, x, 0.0280f, 800f) * 52f +
                context.SampleTerrain1D(noise, x, 0.0650f, 1200f) * 16f;

            if (region < -0.33f)
            {
                float t = SmoothStep(-1.0f, -0.33f, region);
                return Lerp(smooth, rolling, t);
            }

            if (region < 0.33f)
            {
                float t = SmoothStep(-0.33f, 0.33f, region);
                return Lerp(rolling, rugged, t);
            }

            float blend = SmoothStep(0.33f, 1.0f, region);
            return Lerp(rugged, smooth, blend * 0.35f);
        }

        private static float GetCenterPlainsMask(int x, int worldWidth, float plainsWidthPercent = 0.22f)
        {
            float centerX = (worldWidth - 1) * 0.5f;
            float distanceFromCenter = MathF.Abs(x - centerX);

            float plainsHalfWidth = worldWidth * plainsWidthPercent * 0.5f;

            if (plainsHalfWidth <= 0f)
                return 0f;

            float t = 1f - (distanceFromCenter / plainsHalfWidth);
            t = Math.Clamp(t, 0f, 1f);

            // suaviza a transição
            return t * t * (3f - 2f * t);
        }

        private static int ClampSurfaceHeight(int surfaceY, WorldLayerDefinition? surfaceLayer, int worldHeight)
        {
            if (surfaceLayer.HasValue)
            {
                int min = surfaceLayer.Value.StartY;
                int max = Math.Max(min, surfaceLayer.Value.EndY);
                return Math.Clamp(surfaceY, min, max);
            }

            return Math.Clamp(surfaceY, 8, worldHeight - 10);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * t);
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (edge0 == edge1)
                return 0f;

            float t = (x - edge0) / (edge1 - edge0);
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - (2f * t));
        }
    }
}
