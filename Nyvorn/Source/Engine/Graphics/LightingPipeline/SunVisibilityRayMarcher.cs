using System;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Skip reasons for ray traversal (for diagnostics).
    /// </summary>
    public enum SunVisibilitySkipReason
    {
        None = 0,
        BelowHorizon = 1,
        LowElevation = 2,
        ZeroIntensity = 3,
    }

    /// <summary>
    /// Statistics for a single ray traversal (no allocation).
    /// Passed by ref to accumulate metrics.
    /// </summary>
    public struct SunVisibilityRayStats
    {
        public int CellsVisited;
        public bool EarlyOut;
        public bool WasFree;
        public bool WasBlocked;
        public bool WasSkipped;
        public SunVisibilitySkipReason SkipReason;
        public float FinalTransmittance;
        public bool GuardLimitHit;
    }

    /// <summary>
    /// Ray marcher for sun visibility computation using DDA (Amanatides & Woo grid traversal).
    /// Traces from world position along DirectionToSun to compute transmittance.
    ///
    /// SEMÂNTICA CRÍTICA:
    /// - Uma única autoridade: ResolveSunOpacity (nunca duplicar)
    /// - Starting cell contribui exatamente uma vez (opacidade parcial ou total)
    /// - Após starting cell, DDA avança para célula subsequente
    /// - Nenhuma célula é processada duas vezes
    /// - Y < 0 marca saída pelo topo (raio livre)
    /// </summary>
    public static class SunVisibilityRayMarcher
    {
        private const float Epsilon = 0.001f;
        private const float MinimumTraceElevationDegrees = 5.0f;

        /// <summary>
        /// Compute sun visibility for a single world position.
        /// Returns [0, 1]: 0 = fully blocked, 1 = completely free.
        /// Stats are accumulated in the ref parameter (no allocation).
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
            int tileSize,
            ref SunVisibilityRayStats stats)
        {
            stats.CellsVisited = 0;
            stats.EarlyOut = false;
            stats.WasFree = false;
            stats.WasBlocked = false;
            stats.WasSkipped = false;
            stats.SkipReason = SunVisibilitySkipReason.None;
            stats.GuardLimitHit = false;

            // Rule 1: Sun below horizon = no visibility
            if (!sunAboveHorizon)
            {
                stats.WasSkipped = true;
                stats.SkipReason = SunVisibilitySkipReason.BelowHorizon;
                return 0.0f;
            }

            // Rule 2: Low intensity = no visibility
            if (sunIntensity <= Epsilon)
            {
                stats.WasSkipped = true;
                stats.SkipReason = SunVisibilitySkipReason.ZeroIntensity;
                return 0.0f;
            }

            // Rule 3: Check elevation of sun direction before ray march
            float elevationDegrees = (float)Math.Asin(Math.Clamp(-sunDirection.Y, -1f, 1f)) * 180f / MathF.PI;
            if (elevationDegrees < MinimumTraceElevationDegrees)
            {
                stats.WasSkipped = true;
                stats.SkipReason = SunVisibilitySkipReason.LowElevation;
                return 0.0f;
            }

            // Ray march along sunDirection using DDA
            float result = RayMarchDDA(worldX, worldY, sunDirection, geometryProvider, worldWidthTiles, worldHeightTiles, tileSize, ref stats);
            stats.FinalTransmittance = result;
            stats.WasFree = result > 0.5f;
            stats.WasBlocked = result < 0.1f;
            return result;
        }

        /// <summary>
        /// Legacy overload for backward compatibility (without stats tracking).
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
            var stats = default(SunVisibilityRayStats);
            return ComputeVisibility(worldX, worldY, sunDirection, sunIntensity, sunAboveHorizon, geometryProvider, worldWidthTiles, worldHeightTiles, tileSize, ref stats);
        }

        /// <summary>
        /// DDA-based grid traversal for ray marching.
        /// SEMÂNTICA:
        /// 1. Resolve starting cell opacity exatamente uma vez
        /// 2. Aplica transmittance da starting cell
        /// 3. Avança para célula subsequente
        /// 4. DDA processa células restantes até Y sair dos limites ou transmittance bloquear
        /// 5. Nenhuma célula é processada duas vezes
        ///
        /// Stats are accumulated in the ref parameter (no allocation).
        /// </summary>
        private static float RayMarchDDA(
            float startWorldX,
            float startWorldY,
            Vector2 direction,
            ILightingWorldGeometryProvider geometryProvider,
            int worldWidthTiles,
            int worldHeightTiles,
            int tileSize,
            ref SunVisibilityRayStats stats)
        {
            // Obter tile da starting cell
            int startTileX = (int)Math.Floor(startWorldX / tileSize);
            int startTileY = (int)Math.Floor(startWorldY / tileSize);

            // Processar starting cell exatamente uma vez via ResolveSunOpacity (única autoridade)
            int canonicalStartX = WrapTileX(startTileX, worldWidthTiles);
            float startOpacity = Math.Clamp(ResolveSunOpacity(geometryProvider, canonicalStartX, startTileY), 0f, 1f);
            float transmittance = 1.0f - startOpacity;

            // Early exit se starting cell bloqueia completamente
            if (transmittance <= Epsilon)
                return 0.0f;

            // Inicializar posição na starting cell
            float currentWorldX = startWorldX;
            float currentWorldY = startWorldY;

            // Calcular máximo de passos derivado da geometria
            // verticalCells = startTileY + 1 (distância até Y < 0)
            // Para raio diagonal, adicionar cruzamentos horizontais estimados
            int verticalStepsToTop = startTileY + 1;
            int maxCellsEstimate = verticalStepsToTop + (int)Math.Ceiling(Math.Abs(verticalStepsToTop * direction.X / direction.Y)) + 10;
            int cellsTraversed = 0;
            int lastTileY = startTileY;

            // DDA loop: avança a partir da starting cell até sair do mundo
            while (cellsTraversed < maxCellsEstimate)
            {
                // Avançar para próxima célula
                currentWorldX += direction.X * tileSize;
                currentWorldY += direction.Y * tileSize;

                // Obter tile atual
                int tileX = (int)Math.Floor(currentWorldX / tileSize);
                int tileY = (int)Math.Floor(currentWorldY / tileSize);
                lastTileY = tileY;

                // Verificar se saiu pelo topo (raio livre)
                if (tileY < 0)
                    break;

                // Verificar se saiu pelas laterais (wrap X ou fora de Y)
                if (tileY >= worldHeightTiles)
                    break;

                // Resolver opacidade na célula atual (única autoridade)
                int canonicalTileX = WrapTileX(tileX, worldWidthTiles);
                float opacity = Math.Clamp(ResolveSunOpacity(geometryProvider, canonicalTileX, tileY), 0f, 1f);

                // Aplicar transmittance uma única vez
                transmittance *= (1.0f - opacity);

                stats.CellsVisited++;

                // Early exit se bloqueado
                if (transmittance <= Epsilon)
                {
                    transmittance = 0.0f;
                    stats.EarlyOut = true;
                    break;
                }

                cellsTraversed++;
            }

            // Detectar se o guard limite foi atingido
            if (cellsTraversed >= maxCellsEstimate && lastTileY >= 0 && lastTileY < worldHeightTiles)
            {
                // Use resultado conservador (bloqueado)
                transmittance = 0.0f;
                stats.GuardLimitHit = true;
            }

            return Math.Clamp(transmittance, 0.0f, 1.0f);
        }

        /// <summary>
        /// Wrap tile X coordinate using positive modulo (world wrapping).
        /// </summary>
        private static int WrapTileX(int tileX, int worldWidthTiles)
        {
            if (worldWidthTiles <= 0)
                return tileX;

            int wrapped = tileX % worldWidthTiles;
            return wrapped < 0 ? wrapped + worldWidthTiles : wrapped;
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
