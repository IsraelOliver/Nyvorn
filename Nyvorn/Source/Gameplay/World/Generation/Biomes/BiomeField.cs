using System;

namespace Nyvorn.Source.World.Generation.Biomes
{
    public sealed class BiomeField
    {
        private readonly BiomeType[] biomes;

        public BiomeField(int width)
        {
            Width = Math.Max(1, width);
            biomes = new BiomeType[Width];
        }

        public int Width { get; }

        public BiomeType GetBiome(int x)
        {
            return biomes[WrapX(x)];
        }

        public void SetBiome(int x, BiomeType biome)
        {
            biomes[WrapX(x)] = biome;
        }

        public BiomeSample Sample(int x, int blendRadius = 24)
        {
            BiomeType primary = GetBiome(x);
            if (blendRadius <= 0)
                return new BiomeSample(primary, primary, 1f);

            int nearestDistance = blendRadius + 1;
            BiomeType secondary = primary;
            for (int offset = 1; offset <= blendRadius; offset++)
            {
                BiomeType left = GetBiome(x - offset);
                if (left != primary)
                {
                    nearestDistance = offset;
                    secondary = left;
                    break;
                }

                BiomeType right = GetBiome(x + offset);
                if (right != primary)
                {
                    nearestDistance = offset;
                    secondary = right;
                    break;
                }
            }

            if (secondary == primary)
                return new BiomeSample(primary, primary, 1f);

            float distanceT = Math.Clamp(nearestDistance / (float)blendRadius, 0f, 1f);
            float easedDistance = distanceT * distanceT * (3f - (2f * distanceT));
            float blend = 0.5f + (easedDistance * 0.5f);
            return new BiomeSample(primary, secondary, blend);
        }

        private int WrapX(int x)
        {
            int wrapped = x % Width;
            return wrapped < 0 ? wrapped + Width : wrapped;
        }
    }
}
