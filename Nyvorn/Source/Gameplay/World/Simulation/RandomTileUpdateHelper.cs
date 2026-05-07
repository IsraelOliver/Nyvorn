using Microsoft.Xna.Framework;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public static class RandomTileUpdateHelper
    {
        public static int VisitRandomTiles(
            WorldMap worldMap,
            IReadOnlyList<WorldChunkCoord> activeChunks,
            int samplesPerChunk,
            int maxSamples,
            Random random,
            Action<Point> visitTile)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));
            if (activeChunks == null || activeChunks.Count == 0 || samplesPerChunk <= 0 || maxSamples <= 0)
                return 0;
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (visitTile == null)
                throw new ArgumentNullException(nameof(visitTile));

            int targetSamples = Math.Min(maxSamples, activeChunks.Count * samplesPerChunk);
            int visited = 0;

            for (int i = 0; i < targetSamples; i++)
            {
                WorldChunkCoord chunkCoord = activeChunks[random.Next(activeChunks.Count)];
                Rectangle tileBounds = worldMap.GetChunkTileBounds(chunkCoord);
                if (tileBounds.Width <= 0 || tileBounds.Height <= 0)
                    continue;

                int tileX = tileBounds.X + random.Next(tileBounds.Width);
                int tileY = tileBounds.Y + random.Next(tileBounds.Height);
                visitTile(new Point(worldMap.WrapTileX(tileX), tileY));
                visited++;
            }

            return visited;
        }
    }
}
