using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class DirtToStoneTransitionPass : IWorldGenPass
    {
        private const int DirtPureDepthMin = 18;

        public string Name => "DirtToStoneTransition";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Misturando terra e pedra");

            int dirtCount = 0;
            int stoneCount = 0;

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = context.SurfaceHeights[x];
                int stoneStartY = GetStoneStartY(context, surfaceY);

                for (int y = surfaceY; y < context.WorldMap.Height; y++)
                {
                    TileType currentTile = context.WorldMap.GetTile(x, y);
                    if (currentTile != TileType.Dirt && currentTile != TileType.Stone)
                        continue;

                    TileType nextTile = y >= stoneStartY ? TileType.Stone : TileType.Dirt;
                    if (nextTile != currentTile)
                        context.WorldMap.SetTile(x, y, nextTile);

                    if (nextTile == TileType.Stone)
                        stoneCount++;
                    else
                        dirtCount++;
                }

                if ((x & 31) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Misturando terra e pedra");
            }

            context.DebugStats["DirtToStoneTransition.DirtTiles"] = dirtCount.ToString();
            context.DebugStats["DirtToStoneTransition.StoneTiles"] = stoneCount.ToString();
            context.ProgressReporter?.Complete(Name, "Transicao terra-pedra pronta");
        }

        private static int GetStoneStartY(WorldGenContext context, int surfaceY)
        {
            return Math.Clamp(surfaceY + DirtPureDepthMin, surfaceY + 1, context.WorldMap.Height - 1);
        }
    }
}
