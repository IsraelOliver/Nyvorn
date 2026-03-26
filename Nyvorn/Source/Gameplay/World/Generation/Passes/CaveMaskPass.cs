using System;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class CaveMaskPass : IWorldGenPass
    {
        public string Name => "CaveMask";

        public void Apply(WorldGenContext context)
        {
            int shallowAir = CarveLayer(
                context,
                WorldLayerType.ShallowUnderground,
                context.Config.CaveMaskSeedSolidChanceShallow,
                context.Config.CaveMaskSpatialScaleShallow,
                context.Config.CaveMaskSpatialScaleShallow,
                context.Config.CaveMaskSmoothPassesShallow,
                context.Config.CaveMaskBirthLimitShallow,
                context.Config.CaveMaskDeathLimitShallow,
                context.Config.CaveMaskBoundaryFadeRowsShallow);
            int cavernAir = CarveLayer(
                context,
                WorldLayerType.Cavern,
                context.Config.CaveMaskSeedSolidChanceCavern,
                context.Config.CaveMaskSpatialScaleCavern,
                context.Config.CaveMaskSpatialScaleCavern,
                context.Config.CaveMaskSmoothPassesCavern,
                context.Config.CaveMaskBirthLimitCavern,
                context.Config.CaveMaskDeathLimitCavern,
                context.Config.CaveMaskBoundaryFadeRowsCavern);
            int deepAir = CarveLayer(
                context,
                WorldLayerType.DeepCavern,
                context.Config.CaveMaskSeedSolidChanceDeep,
                context.Config.CaveMaskSpatialScaleDeepX,
                context.Config.CaveMaskSpatialScaleDeepY,
                context.Config.CaveMaskSmoothPassesDeep,
                context.Config.CaveMaskBirthLimitDeep,
                context.Config.CaveMaskDeathLimitDeep,
                context.Config.CaveMaskBoundaryFadeRowsDeep);

            context.DebugStats["CaveMask.ShallowAir"] = shallowAir.ToString();
            context.DebugStats["CaveMask.CavernAir"] = cavernAir.ToString();
            context.DebugStats["CaveMask.DeepAir"] = deepAir.ToString();
        }

        private int CarveLayer(
            WorldGenContext context,
            WorldLayerType layerType,
            float seedSolidChance,
            float spatialScaleX,
            float spatialScaleY,
            int smoothPasses,
            int birthLimit,
            int deathLimit,
            int boundaryFadeRows)
        {
            WorldLayerDefinition layer = context.GetLayerDefinition(layerType);
            float clampedScaleX = MathF.Max(1f, spatialScaleX);
            float clampedScaleY = MathF.Max(1f, spatialScaleY);
            int coarseWidth = Math.Max(1, (int)MathF.Ceiling(context.WorldMap.Width / clampedScaleX));
            int coarseHeight = Math.Max(1, (int)MathF.Ceiling(layer.Height / clampedScaleY));
            bool[] solid = new bool[coarseWidth * coarseHeight];
            bool[] next = new bool[coarseWidth * coarseHeight];

            for (int coarseY = 0; coarseY < coarseHeight; coarseY++)
            {
                int sampleY = layer.StartY + Math.Min(layer.Height - 1, (int)MathF.Floor(coarseY * clampedScaleY));
                for (int coarseX = 0; coarseX < coarseWidth; coarseX++)
                {
                    int sampleX = Math.Min(context.WorldMap.Width - 1, (int)MathF.Floor(coarseX * clampedScaleX));
                    solid[(coarseY * coarseWidth) + coarseX] = SeedSolidTile(
                        context,
                        coarseX,
                        coarseY,
                        sampleX,
                        sampleY,
                        layer,
                        seedSolidChance,
                        clampedScaleX,
                        clampedScaleY,
                        boundaryFadeRows,
                        layerType);
                }
            }

            for (int pass = 0; pass < smoothPasses; pass++)
            {
                for (int coarseY = 0; coarseY < coarseHeight; coarseY++)
                {
                    for (int coarseX = 0; coarseX < coarseWidth; coarseX++)
                    {
                        int index = (coarseY * coarseWidth) + coarseX;
                        int solidNeighbors = CountSolidNeighbors(solid, coarseWidth, coarseHeight, coarseX, coarseY);
                        bool currentSolid = solid[index];
                        next[index] = currentSolid
                            ? solidNeighbors >= deathLimit
                            : solidNeighbors > birthLimit;
                    }
                }

                Array.Copy(next, solid, solid.Length);
                Array.Clear(next, 0, next.Length);
            }

            int carved = 0;
            for (int y = layer.StartY; y <= layer.EndY; y++)
            {
                int coarseY = Math.Min(coarseHeight - 1, (int)MathF.Floor((y - layer.StartY) / clampedScaleY));
                for (int x = 0; x < context.WorldMap.Width; x++)
                {
                    int coarseX = Math.Min(coarseWidth - 1, (int)MathF.Floor(x / clampedScaleX));
                    if (solid[(coarseY * coarseWidth) + coarseX])
                        continue;

                    if (context.WorldMap.GetTile(x, y) == TileType.Empty)
                        continue;

                    context.WorldMap.SetTile(x, y, TileType.Empty);
                    carved++;
                }
            }

            return carved;
        }

        private bool SeedSolidTile(
            WorldGenContext context,
            int coarseX,
            int coarseY,
            int sampleX,
            int sampleY,
            WorldLayerDefinition layer,
            float baseSolidChance,
            float spatialScaleX,
            float spatialScaleY,
            int boundaryFadeRows,
            WorldLayerType layerType)
        {
            float random = Hash01(
                coarseX,
                coarseY,
                context.Config.Seed + (int)layerType * 97);
            float noiseX = sampleX / MathF.Max(1f, spatialScaleX);
            float noiseY = sampleY / MathF.Max(1f, spatialScaleY);
            float macroSampleX = noiseX;
            float macroSampleY = noiseY;
            float detailSampleX = noiseX;
            float detailSampleY = noiseY;

            if (layerType == WorldLayerType.DeepCavern)
            {
                macroSampleX = (noiseX * 0.78f) + (noiseY * 0.62f);
                macroSampleY = (-noiseX * 0.28f) + (noiseY * 1.08f);
                detailSampleX = (noiseX * 0.64f) + (noiseY * 0.84f);
                detailSampleY = (-noiseX * 0.42f) + (noiseY * 1.12f);
            }

            float macroNoise = ((context.CaveNoise.GetNoise(macroSampleX, macroSampleY) + 1f) * 0.5f) - 0.5f;
            float detailNoise = ((context.CaveRoomNoise.GetNoise((detailSampleX * 1.35f) + 17f, (detailSampleY * 1.35f) - 29f) + 1f) * 0.5f) - 0.5f;
            float solidChance = baseSolidChance + (macroNoise * 0.14f) + (detailNoise * 0.08f);

            float depth = context.GetNormalizedDepthInLayer(sampleY);
            if (layerType == WorldLayerType.ShallowUnderground)
                solidChance += (1f - depth) * 0.10f;
            else if (layerType == WorldLayerType.DeepCavern)
                solidChance += MathF.Abs(depth - 0.5f) * 0.06f;

            int distanceToTop = sampleY - layer.StartY;
            int distanceToBottom = layer.EndY - sampleY;
            int boundaryDistance = Math.Min(distanceToTop, distanceToBottom);
            if (boundaryDistance < boundaryFadeRows)
            {
                float fadeT = 1f - (boundaryDistance / (float)Math.Max(1, boundaryFadeRows));
                solidChance += fadeT * 0.18f;
            }

            solidChance = Math.Clamp(solidChance, 0.05f, 0.95f);
            return random < solidChance;
        }

        private int CountSolidNeighbors(bool[] mask, int width, int height, int x, int y)
        {
            int count = 0;

            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0)
                        continue;

                    int sampleX = x + offsetX;
                    int sampleY = y + offsetY;
                    if (sampleX < 0 || sampleX >= width || sampleY < 0 || sampleY >= height)
                    {
                        count++;
                        continue;
                    }

                    if (mask[(sampleY * width) + sampleX])
                        count++;
                }
            }

            return count;
        }

        private float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint hash = (uint)seed;
                hash ^= (uint)x * 374761393u;
                hash = (hash << 13) ^ hash;
                hash += (uint)y * 668265263u;
                hash = (hash ^ (hash >> 15)) * 2246822519u;
                hash ^= hash >> 13;
                return (hash & 0x00FFFFFFu) / 16777215f;
            }
        }
    }
}
