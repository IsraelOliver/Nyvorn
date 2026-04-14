using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Generation.Passes;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenerator
    {
        private static readonly WorldGenPhaseDefinition[] OrderedPasses =
        {
            new WorldGenPhaseDefinition("ClearWorld", "Limpando mapa base", 3f),
            new WorldGenPhaseDefinition("LayerBoundary", "Definindo camadas do planeta", 2f),
            new WorldGenPhaseDefinition("SurfaceProfile", "Modelando superficie", 8f),
            new WorldGenPhaseDefinition("BaseTerrainFill", "Preenchendo crosta", 12f),
            new WorldGenPhaseDefinition("Cave", "Cavando cavernas", 25f),
            new WorldGenPhaseDefinition("CaveEntrance", "Abrindo entradas naturais", 14f),
            new WorldGenPhaseDefinition("Tissue", "Tecendo rede organica", 20f),
            new WorldGenPhaseDefinition("TissueBiome", "Concentrando bioma do tecido", 10f),
            new WorldGenPhaseDefinition("WorldBounds", "Selando limites do mundo", 2f)
        };

        private readonly IWorldGenPass[] generationPasses;
        private WorldGenConfig lastConfig;

        public WorldGenerator()
        {
            generationPasses = new IWorldGenPass[]
            {
                new ClearWorldPass(),
                new LayerBoundaryPass(),
                new SurfaceProfilePass(),
                new BaseTerrainFillPass(),
                new CavePass(),
                new CaveEntrancePass(),
                new TissuePass(),
                new TissueBiomePass(),
                new WorldBoundsPass()
            };
        }

        public void Generate(WorldMap worldMap, WorldGenConfig config)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            WorldGenContext context = CreateGenerationContext(worldMap, config);

            for (int i = 0; i < generationPasses.Length; i++)
            {
                if (!config.Debug.IsEnabled(generationPasses[i].Name))
                    continue;

                generationPasses[i].Apply(context);
            }
        }

        public static IReadOnlyList<WorldGenPhaseDefinition> GetOrderedPasses()
        {
            return OrderedPasses;
        }

        public WorldGenContext CreateGenerationContext(WorldMap worldMap, WorldGenConfig config)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            lastConfig = config;

            return new WorldGenContext
            {
                WorldMap = worldMap,
                Config = config,
                Random = new Random(config.Seed)
            };
        }

        public static WorldGenPhaseDefinition GetPhaseDefinition(string passName)
        {
            for (int i = 0; i < OrderedPasses.Length; i++)
            {
                if (OrderedPasses[i].Name == passName)
                    return OrderedPasses[i];
            }

            return new WorldGenPhaseDefinition(passName, passName, 1f);
        }

        public void ApplyPassByName(WorldGenContext context, string passName)
        {
            if (string.IsNullOrWhiteSpace(passName))
                throw new ArgumentException("Pass name is required.", nameof(passName));
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (context.WorldMap == null)
                throw new ArgumentException("Generation context is missing WorldMap.", nameof(context));
            if (context.Config == null)
                throw new ArgumentException("Generation context is missing Config.", nameof(context));

            for (int i = 0; i < generationPasses.Length; i++)
            {
                if (generationPasses[i].Name != passName)
                    continue;
                if (!context.Config.Debug.IsEnabled(generationPasses[i].Name))
                    return;

                generationPasses[i].Apply(context);
                return;
            }

            throw new InvalidOperationException($"Pass de geracao desconhecida: {passName}");
        }

        public int GetSurfaceTileY(WorldMap worldMap, int tileX)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));

            int clampedX = Math.Clamp(tileX, 0, worldMap.Width - 1);
            for (int y = 0; y < worldMap.Height; y++)
            {
                if (worldMap.IsSolidAt(clampedX, y))
                    return y;
            }

            return lastConfig?.SurfaceBaseHeight ?? Math.Max(0, worldMap.Height / 3);
        }

        public Vector2 GetSurfaceSpawnPosition(WorldMap worldMap, int tileX, int tilesAboveSurface = 0)
        {
            int clampedX = Math.Clamp(tileX, 0, worldMap.Width - 1);
            int surfaceY = GetSurfaceTileY(worldMap, clampedX);
            float x = worldMap.GetTileCenter(clampedX, surfaceY).X;
            float y = (surfaceY - tilesAboveSurface) * worldMap.TileSize;
            return new Vector2(x, y);
        }

        public Vector2 GetLayerSpawnPosition(
            WorldMap worldMap,
            WorldGenConfig config,
            WorldLayerType layerType,
            int approximateTileX,
            int tilesAboveGround = 0,
            int requiredClearanceTiles = 3)
        {
            if (worldMap == null)
                throw new ArgumentNullException(nameof(worldMap));
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            WorldLayerDefinition layer = GetLayerDefinition(worldMap, config, layerType);
            int localSearchRange = Math.Min(worldMap.Width - 1, Math.Max(8, config.SpawnSearchRange));

            if (!TryFindGroundSpawnTile(worldMap, layer, approximateTileX, localSearchRange, requiredClearanceTiles, config.BorderThickness, out int spawnTileX, out int spawnTileY) &&
                !TryFindGroundSpawnTile(worldMap, layer, approximateTileX, worldMap.Width - 1, requiredClearanceTiles, config.BorderThickness, out spawnTileX, out spawnTileY))
            {
                return GetSurfaceSpawnPosition(worldMap, approximateTileX, tilesAboveGround);
            }

            float x = worldMap.GetTileCenter(spawnTileX, spawnTileY).X;
            float y = (spawnTileY - tilesAboveGround) * worldMap.TileSize;
            return new Vector2(x, y);
        }

        private static WorldLayerDefinition GetLayerDefinition(WorldMap worldMap, WorldGenConfig config, WorldLayerType layerType)
        {
            WorldLayerDefinition[] layers = BuildLayerDefinitions(worldMap, config);
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].LayerType == layerType)
                    return layers[i];
            }

            return layers[^1];
        }

        private static WorldLayerDefinition[] BuildLayerDefinitions(WorldMap worldMap, WorldGenConfig config)
        {
            int lastRow = Math.Max(0, worldMap.Height - 1);
            int spaceEnd = ClampBoundary((int)MathF.Round(worldMap.Height * config.SpaceLayerEndPercent), 0, lastRow - 4);
            int surfaceEnd = ClampBoundary((int)MathF.Round(worldMap.Height * config.SurfaceLayerEndPercent), spaceEnd + 1, lastRow - 3);
            int shallowEnd = ClampBoundary((int)MathF.Round(worldMap.Height * config.ShallowLayerEndPercent), surfaceEnd + 1, lastRow - 2);
            int cavernEnd = ClampBoundary((int)MathF.Round(worldMap.Height * config.CavernLayerEndPercent), shallowEnd + 1, lastRow - 1);

            return new[]
            {
                new WorldLayerDefinition(WorldLayerType.Space, 0, spaceEnd),
                new WorldLayerDefinition(WorldLayerType.Surface, spaceEnd + 1, surfaceEnd),
                new WorldLayerDefinition(WorldLayerType.ShallowUnderground, surfaceEnd + 1, shallowEnd),
                new WorldLayerDefinition(WorldLayerType.Cavern, shallowEnd + 1, cavernEnd),
                new WorldLayerDefinition(WorldLayerType.DeepCavern, cavernEnd + 1, lastRow)
            };
        }

        private static bool TryFindGroundSpawnTile(
            WorldMap worldMap,
            WorldLayerDefinition layer,
            int approximateTileX,
            int searchRange,
            int requiredClearanceTiles,
            int bottomBorderThickness,
            out int spawnTileX,
            out int spawnTileY)
        {
            spawnTileX = 0;
            spawnTileY = 0;

            int clampedRange = Math.Min(Math.Max(0, searchRange), worldMap.Width - 1);
            int maxFloorTileY = Math.Max(layer.StartY + requiredClearanceTiles, layer.EndY - Math.Max(1, bottomBorderThickness));

            for (int offset = 0; offset <= clampedRange; offset++)
            {
                int sampleX = worldMap.WrapTileX(approximateTileX + GetAlternatingOffset(offset));
                if (!TryFindGroundSpawnTileInColumn(worldMap, sampleX, layer.StartY, maxFloorTileY, requiredClearanceTiles, out spawnTileY))
                    continue;

                spawnTileX = sampleX;
                return true;
            }

            return false;
        }

        private static bool TryFindGroundSpawnTileInColumn(
            WorldMap worldMap,
            int tileX,
            int minFloorTileY,
            int maxFloorTileY,
            int requiredClearanceTiles,
            out int spawnTileY)
        {
            spawnTileY = 0;

            int minCandidateFloorY = Math.Max(minFloorTileY, requiredClearanceTiles);
            int maxCandidateFloorY = Math.Min(maxFloorTileY, worldMap.Height - 1);

            for (int floorTileY = maxCandidateFloorY; floorTileY >= minCandidateFloorY; floorTileY--)
            {
                if (!worldMap.IsSolidAt(tileX, floorTileY))
                    continue;

                bool hasClearance = true;
                for (int offsetY = 1; offsetY <= requiredClearanceTiles; offsetY++)
                {
                    if (worldMap.IsSolidAt(tileX, floorTileY - offsetY))
                    {
                        hasClearance = false;
                        break;
                    }
                }

                if (!hasClearance)
                    continue;

                spawnTileY = floorTileY;
                return true;
            }

            return false;
        }

        private static int GetAlternatingOffset(int step)
        {
            if (step <= 0)
                return 0;

            int magnitude = (step + 1) / 2;
            return (step & 1) == 1 ? magnitude : -magnitude;
        }

        private static int ClampBoundary(int value, int min, int max)
        {
            return Math.Clamp(value, min, max);
        }
    }
}
