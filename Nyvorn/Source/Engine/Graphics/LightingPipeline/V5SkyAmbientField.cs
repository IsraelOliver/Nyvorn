using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Computes ambient light field for background tiles.
    /// Light propagates from sky portals using a heuristic that favors continuation in the current direction
    /// and penalizes lateral spread and direction changes.
    /// This is a temporary prototype heuristic - not a final algorithm.
    /// </summary>
    public sealed class V5SkyAmbientField
    {
        private readonly WorldMap worldMap;
        private readonly V5SkyAmbientConfig config;

        // Buffers: reusable across frames
        private float[] fieldBuffer = Array.Empty<float>();
        private int bufferOriginTileX;
        private int bufferOriginTileY;
        private int bufferWidth;
        private int bufferHeight;

        // Temporary work queue
        private readonly Queue<(int X, int Y, int PrevX, int PrevY, float Intensity)> propagationQueue = new();

        public V5SkyAmbientField(WorldMap worldMap, V5SkyAmbientConfig config)
        {
            this.worldMap = worldMap;
            this.config = config;
        }

        /// <summary>
        /// Compute light field for the given window and sky portals.
        /// </summary>
        public void Compute(
            int windowOriginTileX, int windowOriginTileY,
            int windowWidth, int windowHeight,
            List<V5SkyPortal> skyPortals)
        {
            // Expand window with margin
            int marginTiles = config.CameraMarginTiles;
            bufferOriginTileX = windowOriginTileX - marginTiles;
            bufferOriginTileY = windowOriginTileY - marginTiles;
            bufferWidth = windowWidth + (marginTiles * 2);
            bufferHeight = windowHeight + (marginTiles * 2);

            int cellCount = bufferWidth * bufferHeight;
            if (fieldBuffer.Length < cellCount)
                fieldBuffer = new float[cellCount];

            Array.Clear(fieldBuffer, 0, cellCount);

            if (skyPortals.Count == 0)
                return;

            propagationQueue.Clear();

            // Seed all portals
            foreach (var portal in skyPortals)
            {
                int localX = portal.TileX - bufferOriginTileX;
                int localY = portal.TileY - bufferOriginTileY;

                if (localX >= 0 && localX < bufferWidth && localY >= 0 && localY < bufferHeight)
                {
                    int index = (localY * bufferWidth) + localX;
                    fieldBuffer[index] = config.InitialIntensity;
                    propagationQueue.Enqueue((portal.TileX, portal.TileY, -1, -1, config.InitialIntensity));
                }
            }

            // Propagate from all portals
            while (propagationQueue.Count > 0)
            {
                var (x, y, prevX, prevY, intensity) = propagationQueue.Dequeue();

                // Explore 4 neighbors
                PropagateToNeighbor(x - 1, y, x, y, prevX, prevY, intensity);
                PropagateToNeighbor(x + 1, y, x, y, prevX, prevY, intensity);
                PropagateToNeighbor(x, y - 1, x, y, prevX, prevY, intensity);
                PropagateToNeighbor(x, y + 1, x, y, prevX, prevY, intensity);
            }
        }

        private void PropagateToNeighbor(int nextX, int nextY, int currentX, int currentY, int prevX, int prevY, float currentIntensity)
        {
            // Stop if out of range
            if (currentIntensity <= 0.01f)
                return;

            // Stop if out of buffer bounds
            int localX = nextX - bufferOriginTileX;
            int localY = nextY - bufferOriginTileY;
            if (localX < 0 || localX >= bufferWidth || localY < 0 || localY >= bufferHeight)
                return;

            // Don't propagate into solid tiles
            if (worldMap.IsSolidAt(nextX, nextY))
                return;

            // Don't propagate into solid background
            if (worldMap.GetBackgroundTile(nextX, nextY) != TileType.Empty)
                return;

            // Calculate falloff based on direction
            float falloff;
            if (prevX == -1 && prevY == -1)
            {
                // First step from portal: use forward falloff
                falloff = config.ForwardFalloff;
            }
            else
            {
                // Determine direction of approach
                int dx = currentX - prevX;
                int dy = currentY - prevY;
                int nextDx = nextX - currentX;
                int nextDy = nextY - currentY;

                // Check if continuing in same direction
                bool isContinuing = (dx == nextDx && dy == nextDy);

                if (isContinuing)
                {
                    falloff = config.ForwardFalloff;
                }
                else
                {
                    // Lateral or direction change
                    bool isLateral = (Math.Abs(dx) + Math.Abs(dy) == 1) && (Math.Abs(nextDx) + Math.Abs(nextDy) == 1);
                    if (isLateral)
                    {
                        falloff = config.LateralFalloff;
                    }
                    else
                    {
                        // Full reversal or complex change
                        falloff = config.LateralFalloff + config.DirectionChangePenalty;
                    }
                }
            }

            float nextIntensity = currentIntensity - falloff;
            if (nextIntensity <= 0.01f)
                return;

            int index = (localY * bufferWidth) + localX;
            float current = fieldBuffer[index];

            // Accumulate with soft saturation
            float accumulated = current + nextIntensity;
            float saturated = ApplySoftSaturation(accumulated);

            if (saturated > current)
            {
                fieldBuffer[index] = saturated;
                propagationQueue.Enqueue((nextX, nextY, currentX, currentY, nextIntensity));
            }
        }

        private float ApplySoftSaturation(float value)
        {
            // Soft knee saturation: gradual approach to ceiling, not hard clamp
            float normalized = value / config.AccumulationCeiling;
            if (normalized >= 1f)
            {
                // Use a smooth curve: 1 - e^(-softness * (normalized - 1))
                float excess = normalized - 1f;
                return config.AccumulationCeiling * (1f - MathF.Exp(-config.SaturationSoftness * excess));
            }
            return value;
        }

        /// <summary>
        /// Sample the computed field at a tile location.
        /// </summary>
        public float SampleField(int tileX, int tileY)
        {
            int localX = tileX - bufferOriginTileX;
            int localY = tileY - bufferOriginTileY;

            if (localX < 0 || localX >= bufferWidth || localY < 0 || localY >= bufferHeight)
                return 0f;

            int index = (localY * bufferWidth) + localX;
            return fieldBuffer[index];
        }

        /// <summary>
        /// For debug visualization: get raw field buffer.
        /// </summary>
        public float[] GetFieldBuffer() => fieldBuffer;
        public int GetBufferOriginTileX() => bufferOriginTileX;
        public int GetBufferOriginTileY() => bufferOriginTileY;
        public int GetBufferWidth() => bufferWidth;
        public int GetBufferHeight() => bufferHeight;
    }
}
