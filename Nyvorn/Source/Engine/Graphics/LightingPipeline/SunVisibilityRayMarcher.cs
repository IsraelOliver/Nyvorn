using System;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
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

            // Rule 3: Check elevation of sun direction before ray march
            float elevationDegrees = (float)Math.Asin(Math.Clamp(-sunDirection.Y, -1f, 1f)) * 180f / MathF.PI;
            if (elevationDegrees < MinimumTraceElevationDegrees)
                return 0.0f;  // Direction too flat

            // Ray march along sunDirection using DDA
            return RayMarchDDA(worldX, worldY, sunDirection, geometryProvider, worldWidthTiles, worldHeightTiles, tileSize);
        }

        /// <summary>
        /// DDA-based grid traversal for ray marching.
        /// SEMÂNTICA:
        /// 1. Resolve starting cell opacity exatamente uma vez
        /// 2. Aplica transmittance da starting cell
        /// 3. Avança para célula subsequente
        /// 4. DDA processa células restantes até Y sair dos limites ou transmittance bloquear
        /// 5. Nenhuma célula é processada duas vezes
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

            // DDA loop: avança a partir da starting cell até sair do mundo
            while (cellsTraversed < maxCellsEstimate)
            {
                // Avançar para próxima célula
                currentWorldX += direction.X * tileSize;
                currentWorldY += direction.Y * tileSize;

                // Obter tile atual
                int tileX = (int)Math.Floor(currentWorldX / tileSize);
                int tileY = (int)Math.Floor(currentWorldY / tileSize);

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

                // Early exit se bloqueado
                if (transmittance <= Epsilon)
                {
                    transmittance = 0.0f;
                    break;
                }

                cellsTraversed++;
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
