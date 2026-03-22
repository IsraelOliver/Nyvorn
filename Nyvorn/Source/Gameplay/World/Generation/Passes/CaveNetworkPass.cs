using System;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class CaveNetworkPass : IWorldGenPass
    {
        private const float ShallowLayerDensity = 0.16f;
        private const float CavernLayerDensity = 1.12f;
        private const float DepthsLayerDensity = 0.94f;
        private const float CavernTunnelThreshold = 0.66f;
        private const float DepthsTunnelThreshold = 0.62f;
        private const float CavernRoomThreshold = 0.79f;
        private const float DepthsRoomThreshold = 0.73f;
        private const int ConnectorCountDivisor = 520;
        private const int ConnectorHalfWidth = 1;
        private const int ConnectorHalfHeight = 2;

        public string Name => "CaveNetwork";

        public void Apply(WorldGenContext context)
        {
            // Base subterranean noise gives the cave band an organic baseline.
            CarveNoiseBase(context);

            // Deeper chambers act as larger landmarks farther below.
            CarveDepthChambers(context);

            // Vertical links stop layers from feeling disconnected.
            CarveVerticalConnectors(context);
        }

        private void CarveNoiseBase(WorldGenContext context)
        {
            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = context.LayerProfile.GetSurfaceY(x);
                int startY = Math.Clamp(surfaceY + context.Settings.CavernLayerDepth - 10, 0, context.WorldMap.Height - 1);

                for (int y = startY; y < context.WorldMap.Height - 6; y++)
                {
                    WorldDepthLayer layer = context.LayerProfile.GetLayerAt(x, y);
                    if (layer == WorldDepthLayer.Surface)
                        continue;

                    float density = GetLayerDensity(layer);
                    if (density <= 0f)
                        continue;

                    float tunnelNoise = (context.CaveNoise.GetNoise(x, y) + 1f) * 0.5f;
                    float roomNoise = (context.CaveRoomNoise.GetNoise(x, y) + 1f) * 0.5f;
                    float tunnelThreshold = layer == WorldDepthLayer.Cavern ? CavernTunnelThreshold : DepthsTunnelThreshold;
                    float roomThreshold = layer == WorldDepthLayer.Cavern ? CavernRoomThreshold : DepthsRoomThreshold;

                    if ((tunnelNoise * density) > tunnelThreshold)
                        context.WorldMap.SetTile(x, y, TileType.Empty);
                    else if ((roomNoise * density) > roomThreshold)
                        CarveAirEllipse(context.WorldMap, x, y, 2, 2, allowSurface: false);
                }
            }
        }

        private void CarveDepthChambers(WorldGenContext context)
        {
            Random random = new Random(context.Settings.Seed + 1313);
            int chamberCount = Math.Max(8, context.WorldMap.Width / 560);

            for (int chamberIndex = 0; chamberIndex < chamberCount; chamberIndex++)
            {
                int chamberX = (chamberIndex * context.WorldMap.Width) / chamberCount;
                chamberX = WrapColumn(chamberX + random.Next(-60, 61), context.WorldMap.Width);

                int surfaceY = context.LayerProfile.GetSurfaceY(chamberX);
                int minY = Math.Min(context.WorldMap.Height - 24, surfaceY + context.Settings.DepthsLayerDepth + 14);
                int maxY = context.WorldMap.Height - 18;
                if (minY >= maxY)
                    continue;

                int chamberY = random.Next(minY, maxY);
                CarveAirEllipse(
                    context.WorldMap,
                    chamberX,
                    chamberY,
                    random.Next(6, 10),
                    random.Next(4, 7),
                    allowSurface: false);
            }
        }

        private void CarveVerticalConnectors(WorldGenContext context)
        {
            Random random = new Random(context.Settings.Seed + 1409);
            int connectorCount = Math.Max(6, context.WorldMap.Width / ConnectorCountDivisor);

            for (int connectorIndex = 0; connectorIndex < connectorCount; connectorIndex++)
            {
                int startX = (connectorIndex * context.WorldMap.Width) / connectorCount;
                startX = WrapColumn(startX + random.Next(-32, 33), context.WorldMap.Width);
                int surfaceY = context.LayerProfile.GetSurfaceY(startX);
                int currentX = startX;
                int currentY = surfaceY + context.Settings.CavernLayerDepth + random.Next(8, 28);
                int steps = random.Next(20, 42);

                for (int step = 0; step < steps; step++)
                {
                    CarveAirEllipse(context.WorldMap, currentX, currentY, ConnectorHalfWidth, ConnectorHalfHeight, allowSurface: false);

                    float drift = context.CaveRoomNoise.GetNoise((currentX * 0.09f) + step, connectorIndex * 11f);
                    currentX = WrapColumn(currentX + (drift > 0.22f ? 1 : drift < -0.22f ? -1 : 0), context.WorldMap.Width);
                    currentY += 2;

                    if (currentY >= context.WorldMap.Height - 12)
                        break;
                }
            }
        }

        private float GetLayerDensity(WorldDepthLayer layer)
        {
            return layer switch
            {
                WorldDepthLayer.ShallowUnderground => ShallowLayerDensity,
                WorldDepthLayer.Cavern => CavernLayerDensity,
                WorldDepthLayer.Depths => DepthsLayerDensity,
                _ => 0f
            };
        }

        private void CarveAirEllipse(WorldMap worldMap, int centerX, int centerY, int radiusX, int radiusY, bool allowSurface)
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

                    if (!allowSurface && y <= 2)
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
