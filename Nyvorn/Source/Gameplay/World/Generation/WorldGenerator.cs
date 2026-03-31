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
    }
}
