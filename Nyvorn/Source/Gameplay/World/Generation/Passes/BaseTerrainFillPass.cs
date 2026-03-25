namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class BaseTerrainFillPass : IWorldGenPass
    {
        public string Name => "BaseTerrainFill";

        public void Apply(WorldGenContext context)
        {
            int airCount = 0;
            int dirtCount = 0;
            int stoneCount = 0;

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = context.SurfaceHeights[x];

                for (int y = 0; y < context.WorldMap.Height; y++)
                {
                    if (y < surfaceY)
                    {
                        context.WorldMap.SetTile(x, y, TileType.Empty);
                        airCount++;
                        continue;
                    }

                    TileType tile = GetBaseSolidTile(context, x, y, surfaceY);
                    context.WorldMap.SetTile(x, y, tile);

                    if (tile == TileType.Stone)
                        stoneCount++;
                    else
                        dirtCount++;
                }
            }

            context.DebugStats["BaseTerrain.AirTiles"] = airCount.ToString();
            context.DebugStats["BaseTerrain.DirtTiles"] = dirtCount.ToString();
            context.DebugStats["BaseTerrain.StoneTiles"] = stoneCount.ToString();
        }

        private TileType GetBaseSolidTile(WorldGenContext context, int x, int y, int surfaceY)
        {
            int depthBelowSurface = y - surfaceY;
            if (depthBelowSurface <= context.Config.DirtDepthBelowSurface)
                return TileType.Dirt;

            if (depthBelowSurface < context.Config.StoneStartDepth)
                return TileType.Dirt;

            if (depthBelowSurface >= context.Config.StoneFullDepth)
                return TileType.Stone;

            float stoneChance = GetStoneChance(context, x, y, depthBelowSurface);
            float materialNoise = (context.MaterialNoise.GetNoise(x, y) + 1f) * 0.5f;
            return materialNoise <= stoneChance ? TileType.Stone : TileType.Dirt;
        }

        private float GetStoneChance(WorldGenContext context, int x, int y, int depthBelowSurface)
        {
            int stoneRange = System.Math.Max(1, context.Config.StoneFullDepth - context.Config.StoneStartDepth);
            float normalizedDepth = (depthBelowSurface - context.Config.StoneStartDepth) / (float)stoneRange;
            normalizedDepth = System.Math.Clamp(normalizedDepth, 0f, 1f);

            WorldLayerDefinition layer = context.GetLayerDefinitionAtY(y);
            float layerBias = layer.LayerType switch
            {
                WorldLayerType.Surface => 0f,
                WorldLayerType.ShallowUnderground => 0.08f,
                WorldLayerType.Cavern => 0.16f,
                WorldLayerType.DeepCavern => 0.22f,
                _ => 0f
            };

            float chance = normalizedDepth + layerBias;
            return System.Math.Clamp(chance, 0f, 1f);
        }
    }
}
