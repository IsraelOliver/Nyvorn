using Microsoft.Xna.Framework;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    public static class ActiveSimulationChunkSelector
    {
        public static void Collect(
            WorldMap worldMap,
            Rectangle visibleTileBounds,
            int borderChunks,
            List<WorldChunkCoord> destination)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();
            if (worldMap.Width <= 0 || worldMap.Height <= 0)
                return;

            int minTileX = visibleTileBounds.Left;
            int maxTileX = visibleTileBounds.Right - 1;
            int minTileY = Math.Clamp(visibleTileBounds.Top, 0, worldMap.Height - 1);
            int maxTileY = Math.Clamp(visibleTileBounds.Bottom - 1, 0, worldMap.Height - 1);
            if (minTileY > maxTileY)
                return;

            int startChunkX = (int)MathF.Floor(minTileX / (float)worldMap.ChunkTileSize) - borderChunks;
            int endChunkX = (int)MathF.Floor(maxTileX / (float)worldMap.ChunkTileSize) + borderChunks;
            int startChunkY = Math.Clamp((minTileY / worldMap.ChunkTileSize) - borderChunks, 0, worldMap.ChunkCountY - 1);
            int endChunkY = Math.Clamp((maxTileY / worldMap.ChunkTileSize) + borderChunks, 0, worldMap.ChunkCountY - 1);

            HashSet<WorldChunkCoord> seen = new();
            for (int chunkY = startChunkY; chunkY <= endChunkY; chunkY++)
            {
                for (int chunkX = startChunkX; chunkX <= endChunkX; chunkX++)
                {
                    WorldChunkCoord chunkCoord = new(worldMap.WrapChunkX(chunkX), chunkY);
                    if (seen.Add(chunkCoord))
                        destination.Add(chunkCoord);
                }
            }
        }
    }
}
