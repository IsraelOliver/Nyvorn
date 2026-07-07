using System;
using Nyvorn.Source.World.Generation.Biomes;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class DesertSurfaceSandPass : IWorldGenPass
    {
        private const int MinDuneHeightPixels = 12;
        private const int AverageDuneHeightPixels = 48;
        private const int MaxDuneHeightPixels = 120;
        private const int HeightQuantizationPixels = 4;

        public string Name => "DesertSurfaceSand";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Depositando dunas de areia");
            context.SandPlacements.Clear();

            if (context.SurfaceHeights.Length == 0)
            {
                WriteDebugStats(context, 0, 0);
                context.ProgressReporter?.Complete(Name, "Sem superficie para dunas");
                return;
            }

            OpenSimplexNoise duneNoise = new(SeedHash.ToIntSeed(SeedHash.Derive(context.Seeds.MaterialSeed, "desert-surface-sand")));
            int tileSize = Math.Max(1, context.WorldMap.TileSize);
            int pixelBudget = GetPixelBudget(context);
            int estimatedPixels = 0;
            int columns = 0;

            PendingPlacement pending = default;
            bool hasPending = false;

            for (int x = 0; x < context.WorldMap.Width; x++)
            {
                BiomeSample biome = context.SampleBiome(x);
                if (biome.Primary != BiomeType.Desert)
                {
                    FlushPending();
                    ReportProgress(x);
                    continue;
                }

                int surfaceY = context.SurfaceHeights[x];
                if (!context.WorldMap.InBounds(x, surfaceY) || !context.WorldMap.IsSolidAt(x, surfaceY))
                {
                    FlushPending();
                    ReportProgress(x);
                    continue;
                }

                int height = CalculateDuneHeight(context, duneNoise, biome, x);
                if (height <= 0)
                {
                    FlushPending();
                    ReportProgress(x);
                    continue;
                }

                int surfacePixelY = surfaceY * tileSize;
                int startPixelY = Math.Max(0, surfacePixelY - height);
                height = surfacePixelY - startPixelY;
                if (height <= 0)
                {
                    FlushPending();
                    ReportProgress(x);
                    continue;
                }

                int columnPixels = tileSize * height;
                if (estimatedPixels + columnPixels > pixelBudget)
                {
                    int remainingHeight = Math.Max(0, (pixelBudget - estimatedPixels) / tileSize);
                    remainingHeight = QuantizeHeight(remainingHeight);
                    if (remainingHeight <= 0)
                    {
                        FlushPending();
                        ReportProgress(x);
                        continue;
                    }

                    height = remainingHeight;
                    startPixelY = surfacePixelY - height;
                    columnPixels = tileSize * height;
                }

                int startPixelX = x * tileSize;
                if (hasPending &&
                    pending.StartPixelY == startPixelY &&
                    pending.Height == height &&
                    pending.StartPixelX + pending.Width == startPixelX)
                {
                    pending.Width += tileSize;
                }
                else
                {
                    FlushPending();
                    pending = new PendingPlacement(startPixelX, startPixelY, tileSize, height);
                    hasPending = true;
                }

                estimatedPixels += columnPixels;
                columns++;
                ReportProgress(x);
            }

            FlushPending();
            WriteDebugStats(context, columns, estimatedPixels);
            context.ProgressReporter?.Complete(Name, "Dunas de areia registradas");

            void FlushPending()
            {
                if (!hasPending)
                    return;

                context.SandPlacements.Add(new GeneratedSandPlacement(
                    pending.StartPixelX,
                    pending.StartPixelY,
                    pending.Width,
                    pending.Height));
                hasPending = false;
            }

            void ReportProgress(int x)
            {
                if ((x & 63) == 0 || x == context.WorldMap.Width - 1)
                    context.ProgressReporter?.Report(Name, (x + 1) / (float)context.WorldMap.Width, "Depositando dunas de areia");
            }
        }

        private static int CalculateDuneHeight(WorldGenContext context, OpenSimplexNoise noise, BiomeSample biome, int x)
        {
            float broad = Normalize(context.SampleTerrain1D(noise, x, 0.006f, 3400f));
            float detail = Normalize(context.SampleTerrain1D(noise, x, 0.026f, 6200f));
            float duneShape = SmoothStep01((broad * 0.74f) + (detail * 0.26f));
            float height = Lerp(MinDuneHeightPixels, AverageDuneHeightPixels * 1.65f, duneShape);

            if (broad > 0.72f)
                height += Lerp(0f, MaxDuneHeightPixels - height, SmoothStep01((broad - 0.72f) / 0.28f));

            float edgeT = biome.Secondary == biome.Primary
                ? 1f
                : SmoothStep01((biome.Blend - 0.50f) / 0.50f);
            height *= edgeT;

            int quantized = QuantizeHeight((int)MathF.Round(height));
            return Math.Clamp(quantized, 0, MaxDuneHeightPixels);
        }

        private static int GetPixelBudget(WorldGenContext context)
        {
            return Math.Clamp(context.WorldMap.Width * 160, 90_000, 850_000);
        }

        private static int QuantizeHeight(int height)
        {
            if (height < HeightQuantizationPixels)
                return 0;

            return (height / HeightQuantizationPixels) * HeightQuantizationPixels;
        }

        private static float Normalize(float value)
        {
            return Math.Clamp((value + 1f) * 0.5f, 0f, 1f);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * Math.Clamp(t, 0f, 1f));
        }

        private static float SmoothStep01(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - (2f * t));
        }

        private static void WriteDebugStats(WorldGenContext context, int columns, int estimatedPixels)
        {
            context.DebugStats["DesertSurfaceSand.Columns"] = columns.ToString();
            context.DebugStats["DesertSurfaceSand.Placements"] = context.SandPlacements.Count.ToString();
            context.DebugStats["DesertSurfaceSand.EstimatedPixels"] = estimatedPixels.ToString();
        }

        private struct PendingPlacement
        {
            public PendingPlacement(int startPixelX, int startPixelY, int width, int height)
            {
                StartPixelX = startPixelX;
                StartPixelY = startPixelY;
                Width = width;
                Height = height;
            }

            public int StartPixelX { get; }
            public int StartPixelY { get; }
            public int Width { get; set; }
            public int Height { get; }
        }
    }
}
