using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class SandRegionPass : IWorldGenPass
    {
        public string Name => "SandRegion";

        public void Apply(WorldGenContext context)
        {
            if (context.WorldMap.Width <= 0 || context.SurfaceHeights.Length == 0)
                return;

            int regionWidth = context.Random.Next(
                Math.Min(context.Config.SandRegionMinWidth, context.Config.SandRegionMaxWidth),
                Math.Max(context.Config.SandRegionMinWidth, context.Config.SandRegionMaxWidth) + 1);

            int centerX = ChooseRegionCenter(context, regionWidth);
            int halfWidth = Math.Max(1, regionWidth / 2);
            int startX = Math.Max(0, centerX - halfWidth);
            int endX = Math.Min(context.WorldMap.Width - 1, centerX + halfWidth);
            int sandTiles = 0;

            for (int x = startX; x <= endX; x++)
            {
                float edgeFactor = GetEdgeFactor(x, centerX, halfWidth);
                int localDepth = Math.Max(1, (int)MathF.Round(context.Config.SandRegionMaxDepth * edgeFactor));
                int surfaceY = context.SurfaceHeights[x];

                for (int y = surfaceY; y <= Math.Min(context.WorldMap.Height - 1, surfaceY + localDepth); y++)
                {
                    TileType currentTile = context.WorldMap.GetTile(x, y);
                    if (currentTile != TileType.Dirt && currentTile != TileType.Grass)
                        continue;

                    context.WorldMap.SetTile(x, y, TileType.Sand);
                    sandTiles++;
                }
            }

            context.DebugStats["SandRegion.CenterX"] = centerX.ToString();
            context.DebugStats["SandRegion.StartX"] = startX.ToString();
            context.DebugStats["SandRegion.EndX"] = endX.ToString();
            context.DebugStats["SandRegion.Width"] = (endX - startX + 1).ToString();
            context.DebugStats["SandRegion.TileCount"] = sandTiles.ToString();
        }

        private int ChooseRegionCenter(WorldGenContext context, int regionWidth)
        {
            int worldWidth = context.WorldMap.Width;
            int spawnCenter = Math.Clamp(context.Config.SpawnApproximateTileX, 0, worldWidth - 1);
            int spawnExclusionRadius = Math.Max(regionWidth / 2, context.Config.SurfaceSpawnFlattenHalfWidth + context.Config.SurfaceSpawnFlattenBlendWidth);
            int leftMin = Math.Max(regionWidth / 2, 0);
            int leftMax = Math.Max(leftMin, spawnCenter - spawnExclusionRadius);
            int rightMin = Math.Min(worldWidth - 1 - (regionWidth / 2), spawnCenter + spawnExclusionRadius);
            int rightMax = Math.Max(rightMin, worldWidth - 1 - (regionWidth / 2));

            bool canUseLeft = leftMax > leftMin;
            bool canUseRight = rightMax > rightMin;

            if (canUseLeft && canUseRight)
            {
                return context.Random.Next(0, 2) == 0
                    ? context.Random.Next(leftMin, leftMax + 1)
                    : context.Random.Next(rightMin, rightMax + 1);
            }

            if (canUseLeft)
                return context.Random.Next(leftMin, leftMax + 1);

            if (canUseRight)
                return context.Random.Next(rightMin, rightMax + 1);

            return Math.Clamp(worldWidth / 2, 0, worldWidth - 1);
        }

        private float GetEdgeFactor(int x, int centerX, int halfWidth)
        {
            if (halfWidth <= 0)
                return 1f;

            float distance = MathF.Abs(x - centerX) / halfWidth;
            float clamped = Math.Clamp(distance, 0f, 1f);
            float inverted = 1f - clamped;
            return inverted * inverted * (3f - (2f * inverted));
        }
    }
}
