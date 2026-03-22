using System;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class NaturalEntrancePass : IWorldGenPass
    {
        public string Name => "NaturalEntrances";

        public void Apply(WorldGenContext context)
        {
            for (int i = 0; i < context.NaturalEntrances.Length; i++)
            {
                int entryX = context.NaturalEntrances[i];
                int surfaceY = context.SurfaceHeights[entryX];
                int targetY = Math.Min(
                    context.WorldMap.Height - 8,
                    surfaceY + context.Settings.CavernLayerDepth + 6 +
                    (int)MathF.Round(((context.SurfaceDetailNoise.GetNoise(entryX * 0.5f, i * 7f) + 1f) * 0.5f) * 10f));
                int horizontalDir = context.SurfaceWarpNoise.GetNoise(entryX, surfaceY) >= 0f ? 1 : -1;
                int currentX = entryX;
                int currentY = surfaceY + 1;
                int mouthWidth = 3;

                CarveAirEllipse(context.WorldMap, entryX, surfaceY - 2, mouthWidth, 3);
                CarveAirEllipse(context.WorldMap, entryX, surfaceY + 1, 2, 2);

                while (currentY < targetY)
                {
                    int depthFromSurface = currentY - surfaceY;
                    int radiusX = depthFromSurface < context.Settings.ShallowUndergroundDepth * 0.35f ? 2 : 1;
                    int radiusY = depthFromSurface < context.Settings.ShallowUndergroundDepth * 0.35f ? 2 : 1;
                    CarveAirEllipse(context.WorldMap, currentX, currentY, radiusX, radiusY);

                    float driftNoise = BranchNoiseLike(context, depthFromSurface + 3, i, currentX);
                    int horizontalStep = driftNoise > 0.24f ? 1 : driftNoise < -0.24f ? -1 : 0;
                    if (MathF.Abs(driftNoise) > 0.52f)
                        horizontalStep += horizontalDir;

                    currentX = WrapColumn(currentX + horizontalStep, context.WorldMap.Width);
                    currentY += depthFromSurface < context.Settings.ShallowUndergroundDepth ? 1 : (driftNoise > 0.68f ? 2 : 1);

                    if (depthFromSurface > context.Settings.ShallowUndergroundDepth / 2 && depthFromSurface % 7 == 0)
                    {
                        int pocketOffset = horizontalDir * (2 + (int)MathF.Round(MathF.Abs(driftNoise) * 2f));
                        CarveAirEllipse(context.WorldMap, WrapColumn(currentX + pocketOffset, context.WorldMap.Width), currentY, 2, 2);
                    }
                }

                int chamberCenterY = Math.Min(context.WorldMap.Height - 6, currentY + 2);
                CarveAirEllipse(context.WorldMap, currentX, chamberCenterY, 8, 5);
                CarveAirEllipse(
                    context.WorldMap,
                    WrapColumn(currentX + horizontalDir * 6, context.WorldMap.Width),
                    chamberCenterY + 2,
                    5,
                    3);
            }
        }

        private float BranchNoiseLike(WorldGenContext context, int step, int entranceIndex, int tileX)
        {
            return context.SurfaceDetailNoise.GetNoise((tileX * 0.8f) + (step * 2.7f), entranceIndex * 19.3f);
        }

        private void CarveAirEllipse(WorldMap worldMap, int centerX, int centerY, int radiusX, int radiusY)
        {
            for (int y = centerY - radiusY; y <= centerY + radiusY; y++)
            {
                if (y < 0 || y >= worldMap.Height)
                    continue;

                for (int x = centerX - radiusX; x <= centerX + radiusX; x++)
                {
                    float normalizedX = (x - centerX) / (float)Math.Max(1, radiusX);
                    float normalizedY = (y - centerY) / (float)Math.Max(1, radiusY);
                    if ((normalizedX * normalizedX) + (normalizedY * normalizedY) > 1f)
                        continue;

                    worldMap.SetTile(x, y, TileType.Empty);
                }
            }
        }

        private static int WrapColumn(int x, int width)
        {
            int wrapped = x % width;
            return wrapped < 0 ? wrapped + width : wrapped;
        }
    }
}
