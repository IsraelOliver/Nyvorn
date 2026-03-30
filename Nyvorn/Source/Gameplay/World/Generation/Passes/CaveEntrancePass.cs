using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class CaveEntrancePass : IWorldGenPass
    {
        public string Name => "CaveEntrance";

        public void Apply(WorldGenContext context)
        {
            int entranceCount = GetEntranceCount(context.Config.SizePreset);
            if (entranceCount <= 0)
                return;

            WorldLayerDefinition cavernLayer = context.GetLayerDefinition(WorldLayerType.Cavern);
            int worldWidth = context.WorldMap.Width;
            int margin = Math.Max(20, worldWidth / 12);

            for (int i = 0; i < entranceCount; i++)
            {
                float t = (i + 1) / (float)(entranceCount + 1);
                int bandMinX = (int)(t * worldWidth) - worldWidth / (entranceCount * 4);
                int bandMaxX = (int)(t * worldWidth) + worldWidth / (entranceCount * 4);

                bandMinX = Math.Clamp(bandMinX, margin, worldWidth - 1 - margin);
                bandMaxX = Math.Clamp(bandMaxX, margin, worldWidth - 1 - margin);

                int startX = context.Random.Next(bandMinX, Math.Max(bandMinX + 1, bandMaxX + 1));
                int startY = context.SurfaceHeights[startX];
                int targetY = cavernLayer.StartY + (cavernLayer.Height / 2);

                OpenSimplexNoise entranceNoise = new OpenSimplexNoise(context.Config.Seed + 5000);

                CarveNaturalEntrance(context, startX, startY, targetY, 2, entranceNoise, i * 1000f);
                CarveMouth(context, startX, startY);
            }
        }

        private static void CarveNaturalEntrance(
            WorldGenContext context,
            int startX,
            int startY,
            int targetY,
            int baseRadius,
            OpenSimplexNoise noise,
            float noiseOffset)
        {
            int worldWidth = context.WorldMap.Width;
            float currentX = startX;
            float currentY = startY;
            float driftX = 0f;

            while (currentY < targetY)
            {
                float pathT = (currentY - startY) / MathF.Max(1f, targetY - startY);
                float turnSample = (float)noise.Evaluate(currentY * 0.020f, noiseOffset);

                driftX += turnSample * 0.20f;
                driftX = Math.Clamp(driftX, -0.8f, 0.8f);
                driftX *= 0.92f;

                currentY += 1f;
                currentX += driftX;

                int carveX = WrapX((int)MathF.Round(currentX), worldWidth);
                int carveY = (int)MathF.Round(currentY);

                float radiusNoise = (float)noise.Evaluate(currentY * 0.06f, noiseOffset + 500f);
                int radius = baseRadius;
                if (radiusNoise > 0.20f)
                    radius += 1;
                if (radiusNoise > 0.55f)
                    radius += 1;

                CarveCircle(context, carveX, carveY, radius);

                float chamberNoise = (float)noise.Evaluate(currentY * 0.03f, noiseOffset + 1000f);
                if (chamberNoise > 0.60f && pathT > 0.2f)
                {
                    int sideOffset = driftX >= 0f ? 2 : -2;
                    CarveCircle(context, WrapX(carveX + sideOffset, worldWidth), carveY, radius + 2);
                }
            }
        }

        private static int GetEntranceCount(WorldSizePreset sizePreset)
        {
            return sizePreset switch
            {
                WorldSizePreset.Small => 1,
                WorldSizePreset.Medium => 2,
                _ => 3
            };
        }

        private static void CarveMouth(WorldGenContext context, int centerX, int surfaceY)
        {
            int width = 3 + context.Random.Next(0, 2);

            for (int dx = -width; dx <= width; dx++)
            {
                int x = WrapX(centerX + dx, context.WorldMap.Width);
                int extraDepth = 2 - Math.Abs(dx) / 2 + context.Random.Next(0, 2);

                for (int y = surfaceY - 1; y <= surfaceY + extraDepth; y++)
                {
                    if (y < 0 || y >= context.WorldMap.Height)
                        continue;

                    context.WorldMap.SetTile(x, y, TileType.Empty);
                }
            }
        }

        private static void CarveCircle(WorldGenContext context, int centerX, int centerY, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if ((dx * dx) + (dy * dy) > radius * radius)
                        continue;

                    int x = WrapX(centerX + dx, context.WorldMap.Width);
                    int y = centerY + dy;

                    if (y < 0 || y >= context.WorldMap.Height)
                        continue;

                    context.WorldMap.SetTile(x, y, TileType.Empty);
                }
            }
        }

        private static int WrapX(int x, int width)
        {
            if (width <= 0)
                return 0;

            int wrapped = x % width;
            if (wrapped < 0)
                wrapped += width;

            return wrapped;
        }
    }
}
