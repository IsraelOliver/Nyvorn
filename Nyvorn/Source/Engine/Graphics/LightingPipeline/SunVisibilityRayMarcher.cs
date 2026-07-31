using System;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Ray marcher for sun visibility computation using DDA (Amanatides & Woo grid traversal).
    /// Traces from world position along DirectionToSun to compute transmittance.
    /// </summary>
    public static class SunVisibilityRayMarcher
    {
        private const float Epsilon = 0.001f;
        private const float MinimumTraceElevationDegrees = 5.0f;  // Prevent near-horizontal rays

        /// <summary>
        /// Compute sun visibility for a single world position.
        /// Returns [0, 1]: 0 = fully blocked, 1 = completely free.
        /// </summary>
        public static float ComputeVisibility(
            float worldX,
            float worldY,
            Vector2 sunDirection,
            float sunIntensity,
            bool sunAboveHorizon,
            ILightingWorldGeometryProvider geometryProvider,
            int worldWidthTiles,
            int worldHeightTiles,
            int tileSize)
        {
            // Rule 1: Sun below horizon = no visibility
            if (!sunAboveHorizon)
                return 0.0f;

            // Rule 2: Low intensity = no visibility
            if (sunIntensity <= Epsilon)
                return 0.0f;

            // Rule 3: Check if starting position is in solid foreground
            int tileX = (int)Math.Floor(worldX / tileSize);
            int tileY = (int)Math.Floor(worldY / tileSize);
            if (geometryProvider.IsForegroundSolidAt(tileX, tileY))
                return 0.0f;

            // Check elevation of sun direction
            float elevationDegrees = (float)Math.Asin(Math.Clamp(-sunDirection.Y, -1f, 1f)) * 180f / MathF.PI;
            if (elevationDegrees < MinimumTraceElevationDegrees)
                return 0.0f;  // Direction too flat

            // Ray march along sunDirection using DDA
            return RayMarchDDA(worldX, worldY, sunDirection, geometryProvider, worldWidthTiles, worldHeightTiles, tileSize);
        }

        /// <summary>
        /// DDA-based grid traversal for ray marching.
        /// Returns final transmittance along ray direction.
        /// </summary>
        private static float RayMarchDDA(
            float startWorldX,
            float startWorldY,
            Vector2 direction,
            ILightingWorldGeometryProvider geometryProvider,
            int worldWidthTiles,
            int worldHeightTiles,
            int tileSize)
        {
            float transmittance = 1.0f;
            float currentWorldX = startWorldX;
            float currentWorldY = startWorldY;

            // DDA step parameters
            float stepX = direction.X > 0 ? tileSize : (direction.X < 0 ? -tileSize : 0);
            float stepY = direction.Y > 0 ? tileSize : (direction.Y < 0 ? -tileSize : 0);

            // Handle near-zero components
            float rayDirX = Math.Abs(direction.X) < Epsilon ? 0 : direction.X;
            float rayDirY = Math.Abs(direction.Y) < Epsilon ? 0 : direction.Y;

            // Number of cells to traverse (safety limit to prevent infinite loops)
            int maxCells = Math.Max(worldWidthTiles, worldHeightTiles) * 2;
            int cellsTraversed = 0;

            while (cellsTraversed < maxCells)
            {
                // Get current tile
                int tileX = (int)Math.Floor(currentWorldX / tileSize);
                int tileY = (int)Math.Floor(currentWorldY / tileSize);

                // Wrap X (world wrapping)
                int canonicalTileX = tileX;
                if (worldWidthTiles > 0)
                {
                    canonicalTileX = tileX % worldWidthTiles;
                    if (canonicalTileX < 0) canonicalTileX += worldWidthTiles;
                }

                // Check bounds Y
                if (tileY < 0 || tileY >= worldHeightTiles)
                {
                    // Ray exited top or bottom - we're free
                    break;
                }

                // Query opacity at this tile
                float opacity = ResolveSunOpacity(geometryProvider, canonicalTileX, tileY);
                transmittance *= (1.0f - opacity);

                // Early exit if fully blocked
                if (transmittance <= Epsilon)
                {
                    transmittance = 0.0f;
                    break;
                }

                // Step ray forward
                currentWorldX += direction.X * tileSize;
                currentWorldY += direction.Y * tileSize;

                cellsTraversed++;
            }

            return Math.Clamp(transmittance, 0.0f, 1.0f);
        }

        /// <summary>
        /// Resolve sun opacity for a tile using classification rules from Phase 2.
        /// Only foreground solid blocks sun.
        /// </summary>
        private static float ResolveSunOpacity(
            ILightingWorldGeometryProvider geometryProvider,
            int canonicalTileX,
            int canonicalTileY)
        {
            // Only foreground solid blocks sun
            if (geometryProvider.IsForegroundSolidAt(canonicalTileX, canonicalTileY))
                return 1.0f;

            // Background walls don't block sun
            // OpenAtmosphere doesn't block sun
            return 0.0f;
        }
    }
}
