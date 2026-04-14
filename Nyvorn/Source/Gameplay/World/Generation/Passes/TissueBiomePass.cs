using Microsoft.Xna.Framework;
using Nyvorn.Source.World.Tissue;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class TissueBiomePass : IWorldGenPass
    {
        private readonly OpenSimplexNoise shapeNoise;
        private readonly OpenSimplexNoise detailNoise;

        private readonly record struct BiomeCandidate(
            Rectangle Bounds,
            Point CenterTile,
            float SolidRatio,
            float TissueRatio,
            float Score);

        public TissueBiomePass(int seed = 1337)
        {
            shapeNoise = new OpenSimplexNoise(seed + 5201);
            detailNoise = new OpenSimplexNoise(seed + 5202);
        }

        public string Name => "TissueBiome";

        public void Apply(WorldGenContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.ProgressReporter?.Begin(Name, "Definindo bioma denso do tecido");

            WorldMap worldMap = context.WorldMap;
            TissueField field = worldMap.TissueField;
            if (field == null)
            {
                worldMap.SetTissueBiomeField(null);
                context.ProgressReporter?.Complete(Name, "Sem malha-base para bioma");
                return;
            }

            WorldLayerDefinition cavernLayer = context.GetLayerDefinition(WorldLayerType.Cavern);
            WorldLayerDefinition deepLayer = context.GetLayerDefinition(WorldLayerType.DeepCavern);

            TissueBiomeField biomeField = new(field.Width, field.Height);
            List<BiomeCandidate> candidates = BuildCandidates(context, field, cavernLayer, deepLayer);
            context.DebugStats["TissueBiome.Candidates"] = candidates.Count.ToString();

            BiomeCandidate selected = SelectCandidate(context, candidates, cavernLayer, deepLayer);
            TissueBiomeRegion region = new(
                regionId: 1,
                biomeType: TissueBiomeType.DenseCavern,
                tileBounds: selected.Bounds,
                centerTile: selected.CenterTile,
                anchorLayer: WorldLayerType.Cavern);

            biomeField.Regions.Add(region);
            int injectedTiles = ApplyRegion(context, field, biomeField, region);

            worldMap.SetTissueBiomeField(biomeField);
            worldMap.MarkTissueDirty();
            worldMap.RebuildTissueAnalysis();

            context.DebugStats["TissueBiome.RegionCount"] = biomeField.Regions.Count.ToString();
            context.DebugStats["TissueBiome.InjectedTiles"] = injectedTiles.ToString();
            context.ProgressReporter?.Complete(Name, "Bioma denso consolidado");
        }

        private List<BiomeCandidate> BuildCandidates(
            WorldGenContext context,
            TissueField field,
            WorldLayerDefinition cavernLayer,
            WorldLayerDefinition deepLayer)
        {
            List<BiomeCandidate> candidates = new();
            int minWidth = 80;
            int maxWidth = Math.Min(130, Math.Max(80, context.WorldMap.Width / 6));
            int minHeight = 50;
            int maxHeight = Math.Min(80, Math.Max(50, cavernLayer.Height - 6));
            int sampleSpacing = Math.Max(92, minWidth - 4);
            int borderPadding = Math.Max(18, maxWidth / 2);

            for (int sampleCenterX = borderPadding; sampleCenterX < context.WorldMap.Width - borderPadding; sampleCenterX += sampleSpacing)
            {
                Rectangle bounds = BuildRegionBounds(context, cavernLayer, deepLayer, sampleCenterX, minWidth, maxWidth, minHeight, maxHeight);
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    continue;

                BiomeCandidate? candidate = EvaluateCandidate(context.WorldMap, field, bounds);
                if (candidate.HasValue)
                    candidates.Add(candidate.Value);
            }

            return candidates;
        }

        private Rectangle BuildRegionBounds(
            WorldGenContext context,
            WorldLayerDefinition cavernLayer,
            WorldLayerDefinition deepLayer,
            int sampleCenterX,
            int minWidth,
            int maxWidth,
            int minHeight,
            int maxHeight)
        {
            float widthNoise = Normalize01(shapeNoise.Evaluate((sampleCenterX * 0.0135) + 17.2, 91.7));
            float heightNoise = Normalize01(shapeNoise.Evaluate((sampleCenterX * 0.0105) - 44.9, -18.4));
            int regionWidth = (int)MathF.Round(MathHelper.Lerp(minWidth, maxWidth, widthNoise));
            int regionHeight = (int)MathF.Round(MathHelper.Lerp(minHeight, maxHeight, heightNoise));

            int halfWidth = Math.Max(1, regionWidth / 2);
            int halfHeight = Math.Max(1, regionHeight / 2);
            int preferredCenterY = deepLayer.StartY - Math.Max(10, regionHeight / 3);
            int minCenterY = cavernLayer.StartY + halfHeight + 2;
            int maxCenterY = Math.Min(cavernLayer.EndY - halfHeight - 2, deepLayer.StartY - Math.Max(8, regionHeight / 5));
            if (maxCenterY < minCenterY)
                maxCenterY = minCenterY;

            float verticalNoise = Normalize01(shapeNoise.Evaluate((sampleCenterX * 0.0095) + 144.0, 63.5));
            int verticalOffset = (int)MathF.Round(MathHelper.Lerp(-12f, 10f, verticalNoise));
            int centerY = Math.Clamp(preferredCenterY + verticalOffset, minCenterY, maxCenterY);
            int left = Math.Clamp(sampleCenterX - halfWidth, 0, Math.Max(0, context.WorldMap.Width - regionWidth));
            int top = Math.Clamp(centerY - halfHeight, cavernLayer.StartY + 1, Math.Max(cavernLayer.StartY + 1, cavernLayer.EndY - regionHeight));

            return new Rectangle(left, top, regionWidth, regionHeight);
        }

        private BiomeCandidate? EvaluateCandidate(WorldMap worldMap, TissueField field, Rectangle bounds)
        {
            int total = 0;
            int solidCount = 0;
            int tissueCount = 0;
            Point centerTile = new(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

            for (int y = bounds.Top; y < bounds.Bottom; y += 2)
            {
                for (int x = bounds.Left; x < bounds.Right; x += 2)
                {
                    total++;
                    if (worldMap.IsSolidAt(x, y))
                        solidCount++;
                    if (field.HasTissue(x, y))
                        tissueCount++;
                }
            }

            if (total <= 0)
                return null;

            float solidRatio = solidCount / (float)total;
            float tissueRatio = tissueCount / (float)total;
            if (solidRatio < 0.28f || solidRatio > 0.92f)
                return null;
            if (tissueRatio < 0.02f)
                return null;

            float solidBalance = 1f - MathF.Min(1f, MathF.Abs(solidRatio - 0.58f) / 0.58f);
            float score = (tissueRatio * 0.55f) + (solidBalance * 0.45f);

            return new BiomeCandidate(bounds, centerTile, solidRatio, tissueRatio, score);
        }

        private BiomeCandidate SelectCandidate(
            WorldGenContext context,
            List<BiomeCandidate> candidates,
            WorldLayerDefinition cavernLayer,
            WorldLayerDefinition deepLayer)
        {
            if (candidates.Count == 0)
            {
                int fallbackCenterX = context.WorldMap.Width / 2;
                Rectangle fallbackBounds = BuildRegionBounds(context, cavernLayer, deepLayer, fallbackCenterX, 92, 116, 56, 72);
                return new BiomeCandidate(
                    fallbackBounds,
                    new Point(fallbackBounds.X + (fallbackBounds.Width / 2), fallbackBounds.Y + (fallbackBounds.Height / 2)),
                    0.5f,
                    0.12f,
                    0.12f);
            }

            Random random = new(context.Config.Seed + 5203);
            List<BiomeCandidate> shortlist = candidates
                .OrderByDescending(candidate => candidate.Score)
                .Take(Math.Min(6, candidates.Count))
                .ToList();

            return shortlist[random.Next(shortlist.Count)];
        }

        private int ApplyRegion(WorldGenContext context, TissueField field, TissueBiomeField biomeField, TissueBiomeRegion region)
        {
            int injectedTiles = 0;
            Rectangle bounds = region.TileBounds;
            float centerX = region.CenterTile.X + 0.5f;
            float centerY = region.CenterTile.Y + 0.5f;
            float radiusX = Math.Max(6f, bounds.Width * 0.5f);
            float radiusY = Math.Max(6f, bounds.Height * 0.5f);

            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    float dx = ((x + 0.5f) - centerX) / radiusX;
                    float dy = ((y + 0.5f) - centerY) / radiusY;
                    float ellipse = (dx * dx) + (dy * dy);

                    float edgeNoise = Normalize01(shapeNoise.Evaluate((x * 0.072) + 311.7, (y * 0.072) - 142.4));
                    float edgeThreshold = MathHelper.Lerp(0.86f, 1.05f, edgeNoise);
                    if (ellipse > edgeThreshold)
                        continue;

                    biomeField.SetBiomeType(x, y, region.BiomeType);

                    bool hasTissue = field.HasTissue(x, y);
                    bool solid = context.WorldMap.IsSolidAt(x, y);
                    int nearbyTissue = CountNearbyTissue(field, x, y, radius: 2);
                    float localShape = 1f - MathHelper.Clamp(ellipse / edgeThreshold, 0f, 1f);
                    float macro = Normalize01(shapeNoise.Evaluate((x * 0.048) + 91.2, (y * 0.048) - 73.4));
                    float filament = 1f - MathF.Abs((float)detailNoise.Evaluate((x * 0.116) - 210.5, (y * 0.116) + 164.3));
                    filament = MathF.Pow(MathHelper.Clamp(filament, 0f, 1f), 1.35f);

                    float density =
                        (localShape * 0.44f) +
                        (macro * 0.24f) +
                        (filament * 0.18f);

                    if (hasTissue)
                        density += 0.22f;
                    if (solid)
                        density += 0.10f;
                    if (nearbyTissue >= 3)
                        density += 0.10f;

                    bool shouldInject = density >= 0.54f &&
                                        (solid || hasTissue || nearbyTissue >= 4 || localShape >= 0.72f);

                    if (!shouldInject || hasTissue)
                        continue;

                    field.SetTissue(x, y, true);
                    injectedTiles++;
                }
            }

            return injectedTiles;
        }

        private static int CountNearbyTissue(TissueField field, int centerX, int centerY, int radius)
        {
            int count = 0;

            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x == centerX && y == centerY)
                        continue;

                    if (!field.HasTissue(x, y))
                        continue;

                    count++;
                }
            }

            return count;
        }

        private static float Normalize01(double value)
        {
            return (float)((value + 1.0) * 0.5);
        }
    }
}
