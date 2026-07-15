using System;
using Nyvorn.Source.World.Generation.Biomes;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class DesertPixelSandPass : IWorldGenPass
    {
        public string Name => "DesertPixelSand";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Soltando areia das dunas");
            context.PixelSandPlacements.Clear();

            DesertRegionProfile profile = context.DesertRegion;
            if (profile == null || profile.Columns == null || profile.Columns.Count == 0)
            {
                context.ProgressReporter?.Complete(Name, "Sem dunas para pixelizar");
                return;
            }

            for (int i = 0; i < profile.Columns.Count; i++)
            {
                DesertRegionColumn column = profile.Columns[i];
                int baseY = GetPixelSandBaseY(profile, column, context.WorldMap.Height);
                profile.SetPixelSandBaseY(i, baseY);

                int runStartY = -1;
                int runHeight = 0;
                for (int y = column.TopY; y <= baseY; y++)
                {
                    if (!context.WorldMap.InBounds(column.X, y) ||
                        context.WorldMap.GetTile(column.X, y) != TileType.Sand)
                    {
                        FlushRun();
                        continue;
                    }

                    context.WorldMap.SetTile(column.X, y, TileType.Empty);

                    if (runStartY < 0)
                    {
                        runStartY = y;
                        runHeight = 1;
                    }
                    else
                    {
                        runHeight++;
                    }
                }

                FlushRun();

                if ((i & 15) == 0 || i == profile.Columns.Count - 1)
                {
                    context.ProgressReporter?.Report(
                        Name,
                        (i + 1) / (float)profile.Columns.Count,
                        "Soltando areia das dunas");
                }

                void FlushRun()
                {
                    if (runStartY < 0 || runHeight <= 0)
                        return;

                    context.PixelSandPlacements.Add(new WorldGenPixelSandPlacement(column.X, runStartY, 1, runHeight));
                    runStartY = -1;
                    runHeight = 0;
                }
            }

            context.ProgressReporter?.Complete(Name, "Dunas pixelizadas");
        }

        private static int GetPixelSandBaseY(DesertRegionProfile profile, DesertRegionColumn column, int worldHeight)
        {
            float edgeT = Math.Clamp(MathF.Abs(column.LocalX) / Math.Max(1f, profile.HalfWidth), 0f, 1f);
            float centerDepth = Math.Clamp(profile.BaseRadius * 0.16f, 18f, 34f);
            float edgeBonus = Math.Clamp(profile.BaseRadius * 0.08f, 8f, 18f);
            float sideDepth = edgeBonus * SmoothStep(0.46f, 1f, edgeT);
            float bottomNoise = MathF.Sin((column.LocalX / Math.Max(1f, profile.BaseRadius)) * MathF.PI * 1.25f) * profile.BaseRadius * 0.018f;

            int depth = Math.Max(6, (int)MathF.Round(centerDepth + sideDepth + bottomNoise));
            int maxBaseY = Math.Min(worldHeight - 2, column.TopY + Math.Max(8, (int)MathF.Round(profile.MainDepth * 0.42f)));
            return Math.Clamp(column.TopY + depth, column.TopY, maxBaseY);
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            if (MathF.Abs(edge1 - edge0) < 0.0001f)
                return 0f;

            float t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - (2f * t));
        }
    }
}
