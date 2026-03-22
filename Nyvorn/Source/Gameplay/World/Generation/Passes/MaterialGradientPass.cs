using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class MaterialGradientPass : IWorldGenPass
    {
        public string Name => "MaterialGradient";

        public void Apply(WorldGenContext context)
        {
            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                int surfaceY = context.SurfaceHeights[x];
                bool useSand = context.SandColumns[x];

                for (int y = surfaceY; y < context.WorldMap.Height; y++)
                {
                    TileType tileType = ResolveBaseTileType(context, surfaceY, y, useSand);
                    context.WorldMap.SetTile(x, y, tileType);
                }
            }
        }

        private TileType ResolveBaseTileType(WorldGenContext context, int surfaceY, int y, bool useSand)
        {
            if (y == surfaceY)
                return useSand ? TileType.Sand : TileType.Grass;

            int depth = y - surfaceY;
            if (depth <= context.Settings.SurfaceTopsoilDepth)
                return useSand ? TileType.Sand : TileType.Dirt;

            if (useSand && depth <= context.Settings.SurfaceTopsoilDepth + 1)
                return TileType.Sand;

            float stoneChance = GetStoneChanceByDepth(context.Settings, depth);
            float localVariation = (context.MaterialNoise.GetNoise(y * 0.85f, surfaceY * 0.65f) + 1f) * 0.5f;
            return localVariation < stoneChance ? TileType.Stone : TileType.Dirt;
        }

        private float GetStoneChanceByDepth(WorldGenSettings settings, int depth)
        {
            if (depth <= settings.StoneTransitionStartDepth)
                return settings.SurfaceStoneChance;

            if (depth >= settings.StoneTransitionEndDepth)
                return settings.DeepStoneChance;

            float normalized = (depth - settings.StoneTransitionStartDepth) /
                               (float)(settings.StoneTransitionEndDepth - settings.StoneTransitionStartDepth);
            normalized = MathHelper.Clamp(normalized, 0f, 1f);
            float curved = normalized * normalized * (3f - (2f * normalized));
            return MathHelper.Lerp(settings.SurfaceStoneChance, settings.DeepStoneChance, curved);
        }
    }
}
