using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class SurfacePolishPass : IWorldGenPass
    {
        public string Name => "SurfacePolish";

        public void Apply(WorldGenContext context)
        {
            SmoothMicroSurfaceSteps(context);

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                bool sandColumn = context.SandColumns[x];
                int surfaceY = FindSurfaceTileY(context.WorldMap, x);
                if (surfaceY < 0)
                    continue;

                if (!sandColumn)
                    PolishSurfaceColumn(context, x, surfaceY);
            }
        }

        private void PolishSurfaceColumn(WorldGenContext context, int x, int surfaceY)
        {
            TileType topTile = context.WorldMap.GetTile(x, surfaceY);
            if (topTile == TileType.Dirt && IsExposedToAir(context.WorldMap, x, surfaceY))
                context.WorldMap.SetTile(x, surfaceY, TileType.Grass);
            else if (topTile == TileType.Stone && ShouldCapStoneSurface(context, x, surfaceY))
                AddSoilCap(context.WorldMap, x, surfaceY);

            int grassDepthLimit = Math.Min(
                context.WorldMap.Height - 1,
                surfaceY + context.Settings.SurfaceTopsoilDepth + 8);

            for (int y = surfaceY; y <= grassDepthLimit; y++)
            {
                TileType tile = context.WorldMap.GetTile(x, y);
                if (tile == TileType.Dirt && IsExposedToAir(context.WorldMap, x, y))
                    context.WorldMap.SetTile(x, y, TileType.Grass);
            }

            int stoneDepthLimit = Math.Min(
                context.WorldMap.Height - 1,
                surfaceY + context.Settings.SurfaceTopsoilDepth + 10);

            for (int y = surfaceY; y <= stoneDepthLimit; y++)
            {
                if (context.WorldMap.GetTile(x, y) != TileType.Stone)
                    continue;

                if (!IsExposedToAir(context.WorldMap, x, y))
                    continue;

                if (ShouldSoftenSurfaceStone(context, x, y, surfaceY))
                    context.WorldMap.SetTile(x, y, TileType.Dirt);
            }

            int maxDepth = Math.Min(context.WorldMap.Height - 1, surfaceY + context.Settings.SurfaceTopsoilDepth + 3);
            for (int y = surfaceY + 1; y <= maxDepth; y++)
            {
                TileType tile = context.WorldMap.GetTile(x, y);
                if (tile != TileType.Dirt)
                    continue;

                if (IsExposedToAir(context.WorldMap, x, y))
                    context.WorldMap.SetTile(x, y, TileType.Grass);
            }
        }

        private void SmoothMicroSurfaceSteps(WorldGenContext context)
        {
            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int currentSurfaceY = FindSurfaceTileY(context.WorldMap, x);
                if (currentSurfaceY < 0)
                    continue;

                int leftSurfaceY = FindSurfaceTileY(context.WorldMap, x - 1);
                int rightSurfaceY = FindSurfaceTileY(context.WorldMap, x + 1);
                if (leftSurfaceY < 0 || rightSurfaceY < 0)
                    continue;

                if (currentSurfaceY >= leftSurfaceY + 2 && currentSurfaceY >= rightSurfaceY + 2)
                {
                    FillSurfacePocket(context.WorldMap, x, currentSurfaceY);
                }
                else if (currentSurfaceY <= leftSurfaceY - 2 && currentSurfaceY <= rightSurfaceY - 2)
                {
                    TrimSurfaceSpike(context.WorldMap, x, currentSurfaceY);
                }
            }
        }

        private void FillSurfacePocket(WorldMap worldMap, int x, int currentSurfaceY)
        {
            int fillY = currentSurfaceY - 1;
            if (fillY < 0 || worldMap.GetTile(x, fillY) != TileType.Empty)
                return;

            worldMap.SetTile(x, fillY, TileType.Grass);

            for (int y = currentSurfaceY; y <= Math.Min(worldMap.Height - 1, currentSurfaceY + 2); y++)
            {
                if (worldMap.GetTile(x, y) == TileType.Empty)
                    worldMap.SetTile(x, y, TileType.Dirt);
            }
        }

        private void TrimSurfaceSpike(WorldMap worldMap, int x, int currentSurfaceY)
        {
            TileType tile = worldMap.GetTile(x, currentSurfaceY);
            if (tile == TileType.Empty)
                return;

            worldMap.SetTile(x, currentSurfaceY, TileType.Empty);

            int nextSurfaceY = FindSurfaceTileY(worldMap, x);
            if (nextSurfaceY >= 0 && worldMap.GetTile(x, nextSurfaceY) == TileType.Dirt)
                worldMap.SetTile(x, nextSurfaceY, TileType.Grass);
        }

        private bool ShouldCapStoneSurface(WorldGenContext context, int x, int y)
        {
            float coverNoise = (context.MaterialNoise.GetNoise(x * 0.55f, y * 0.35f) + 1f) * 0.5f;
            return coverNoise > 0.76f;
        }

        private bool ShouldSoftenSurfaceStone(WorldGenContext context, int x, int y, int surfaceY)
        {
            int depth = y - surfaceY;
            float stoneNoise = (context.MaterialNoise.GetNoise((x * 0.4f) + 100f, (y * 0.3f) - 20f) + 1f) * 0.5f;
            float retainThreshold = 0.80f - (depth * 0.04f);
            return stoneNoise < retainThreshold;
        }

        private void AddSoilCap(WorldMap worldMap, int x, int surfaceY)
        {
            worldMap.SetTile(x, surfaceY, TileType.Grass);

            int belowY = surfaceY + 1;
            if (belowY < worldMap.Height && worldMap.GetTile(x, belowY) == TileType.Stone)
                worldMap.SetTile(x, belowY, TileType.Dirt);
        }

        private int FindSurfaceTileY(WorldMap worldMap, int x)
        {
            for (int y = 0; y < worldMap.Height; y++)
            {
                if (worldMap.IsSolidAt(x, y))
                    return y;
            }

            return -1;
        }

        private bool IsExposedToAir(WorldMap worldMap, int x, int y)
        {
            return worldMap.GetTile(x, y - 1) == TileType.Empty
                || worldMap.GetTile(x - 1, y) == TileType.Empty
                || worldMap.GetTile(x + 1, y) == TileType.Empty;
        }
    }
}
