using System;
using Nyvorn.Source.World.Generation.Biomes;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class DesertCavePass : IWorldGenPass
    {
        // AJUSTES DAS CAVERNAS DO DESERTO
        private const int MinTilesBelowPixelSand = 10;
        private const int BottomCoreProtectionTiles = 18;
        private const float BottomFadeStart = 0.70f;
        private const float BottomFadeThresholdBoost = 0.75f;
        private const float TopFadeTiles = 14f;
        private const float TopFadeThresholdBoost = 0.28f;
        private const float EdgeFadeStart = 0.70f;
        private const float EdgeThresholdBoost = 0.24f;
        private const float BaseCarveThreshold = 0.54f;
        private const float DryWarpFrequency = 0.032f;
        private const float DryWarpStrength = 15f;
        private const float PocketFrequencyX = 0.030f;
        private const float PocketFrequencyY = 0.046f;
        private const float PocketWeight = 0.72f;
        private const float MacroVoidFrequencyX = 0.010f;
        private const float MacroVoidFrequencyY = 0.014f;
        private const float MacroVoidWeight = 0.34f;
        private const float StrataFrequencyX = 0.052f;
        private const float StrataFrequencyY = 0.014f;
        private const float StrataWeight = 0.42f;
        private const float FissureFrequencyX = 0.010f;
        private const float FissureFrequencyY = 0.110f;
        private const float FissureGateFrequencyX = 0.006f;
        private const float FissureGateFrequencyY = 0.018f;
        private const float FissureGateThreshold = 0.34f;
        private const float FissureCoreStart = 0.72f;
        private const float FissureWeight = 0.58f;
        private const float MineralRibFrequencyX = 0.075f;
        private const float MineralRibFrequencyY = 0.024f;
        private const float MineralRibPreserveBelow = 0.24f;
        private const float MineralRibPenalty = 0.34f;

        public string Name => "DesertCave";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Cavando cavernas secas do deserto");

            DesertRegionProfile profile = context.DesertRegion;
            if (profile == null || profile.Columns == null || profile.Columns.Count == 0)
            {
                context.ProgressReporter?.Complete(Name, "Sem deserto para cavernas secas");
                return;
            }

            int seed = SeedHash.ToIntSeed(SeedHash.Derive(context.Seeds.CaveSeed, "desert-caves"));
            OpenSimplexNoise pocketNoise = new(seed + 1000);
            OpenSimplexNoise macroNoise = new(seed + 2000);
            OpenSimplexNoise strataNoise = new(seed + 3000);
            OpenSimplexNoise fissureNoise = new(seed + 4000);
            OpenSimplexNoise ribNoise = new(seed + 5000);
            OpenSimplexNoise warpNoise = new(seed + 6000);

            int carvedTiles = 0;
            int eligibleTiles = 0;
            int skippedByDepth = 0;
            int skippedByCore = 0;

            for (int i = 0; i < profile.Columns.Count; i++)
            {
                DesertRegionColumn column = profile.Columns[i];
                int startY = Math.Max(0, column.PixelSandBaseY + MinTilesBelowPixelSand);
                int hardEndY = Math.Min(
                    context.WorldMap.Height - context.Config.BorderThickness - 2,
                    column.BottomY - BottomCoreProtectionTiles);

                if (startY > column.BottomY)
                {
                    skippedByDepth++;
                    continue;
                }

                if (hardEndY < startY)
                {
                    skippedByCore++;
                    continue;
                }

                for (int y = startY; y <= hardEndY; y++)
                {
                    if (context.WorldMap.GetTile(column.X, y) != TileType.Sand)
                        continue;

                    eligibleTiles++;
                    if (!ShouldCarveDesertCave(
                        context,
                        profile,
                        column,
                        i,
                        startY,
                        hardEndY,
                        pocketNoise,
                        macroNoise,
                        strataNoise,
                        fissureNoise,
                        ribNoise,
                        warpNoise,
                        y))
                    {
                        continue;
                    }

                    context.WorldMap.SetTile(column.X, y, TileType.Empty);
                    carvedTiles++;
                }

                if ((i & 15) == 0 || i == profile.Columns.Count - 1)
                {
                    context.ProgressReporter?.Report(
                        Name,
                        (i + 1) / (float)profile.Columns.Count,
                        "Cavando cavernas secas do deserto");
                }
            }

            context.DebugStats["DesertCave.EligibleTiles"] = eligibleTiles.ToString();
            context.DebugStats["DesertCave.CarvedTiles"] = carvedTiles.ToString();
            context.DebugStats["DesertCave.MinTilesBelowPixelSand"] = MinTilesBelowPixelSand.ToString();
            context.DebugStats["DesertCave.BottomCoreProtectionTiles"] = BottomCoreProtectionTiles.ToString();
            context.DebugStats["DesertCave.ColumnsSkippedByDepth"] = skippedByDepth.ToString();
            context.DebugStats["DesertCave.ColumnsSkippedByCore"] = skippedByCore.ToString();
            context.ProgressReporter?.Complete(Name, "Cavernas secas do deserto esculpidas");
        }

        private static bool ShouldCarveDesertCave(
            WorldGenContext context,
            DesertRegionProfile profile,
            DesertRegionColumn column,
            int columnIndex,
            int startY,
            int hardEndY,
            OpenSimplexNoise pocketNoise,
            OpenSimplexNoise macroNoise,
            OpenSimplexNoise strataNoise,
            OpenSimplexNoise fissureNoise,
            OpenSimplexNoise ribNoise,
            OpenSimplexNoise warpNoise,
            int y)
        {
            int x = column.X;
            float bodyDepthT = Math.Clamp((y - column.TopY) / (float)Math.Max(1, column.BottomY - column.TopY), 0f, 1f);
            float caveDepthT = Math.Clamp((y - startY) / (float)Math.Max(1, hardEndY - startY), 0f, 1f);
            float sideT = GetSideProtectionT(profile, column, columnIndex);

            float warpX = WorldFieldSampler.Fractal(context, warpNoise, x, y, DryWarpFrequency, DryWarpFrequency, 1700f, 500f) * DryWarpStrength;
            float warpY = WorldFieldSampler.Fractal(context, warpNoise, x, y, DryWarpFrequency, DryWarpFrequency, 700f, 2300f) * DryWarpStrength;

            float pocket = WorldFieldSampler.Fractal(context, pocketNoise, x, y, PocketFrequencyX, PocketFrequencyY, warpX, warpY);
            float macroVoid = WorldFieldSampler.SampleSeamedNoise(context, macroNoise, x, y, MacroVoidFrequencyX, MacroVoidFrequencyY, warpX * 0.22f, warpY * 0.22f);
            float strata = WorldFieldSampler.SampleSeamedNoise(context, strataNoise, x, y, StrataFrequencyX, StrataFrequencyY, warpX * 0.35f, warpY * 0.12f);

            float fissureRaw = 1f - MathF.Abs(WorldFieldSampler.SampleSeamedNoise(context, fissureNoise, x, y, FissureFrequencyX, FissureFrequencyY, warpX * 0.16f, warpY * 0.30f));
            float fissureGate = WorldFieldSampler.SampleSeamedNoise(context, fissureNoise, x, y, FissureGateFrequencyX, FissureGateFrequencyY, 2800f, 900f);
            float fissure = fissureGate > FissureGateThreshold
                ? SmoothStep(FissureCoreStart, 1f, fissureRaw)
                : 0f;

            float rib = MathF.Abs(WorldFieldSampler.SampleSeamedNoise(context, ribNoise, x, y, MineralRibFrequencyX, MineralRibFrequencyY, warpX * 0.18f, warpY * 0.08f));
            float ribPenalty = (1f - SmoothStep(MineralRibPreserveBelow, 0.72f, rib)) * MineralRibPenalty;

            float field =
                (pocket * PocketWeight) +
                (macroVoid * MacroVoidWeight) +
                (strata * StrataWeight) +
                (fissure * FissureWeight) -
                ribPenalty;

            float topFade = 1f - SmoothStep(0f, TopFadeTiles, y - startY);
            float edgeFade = SmoothStep(EdgeFadeStart, 1f, sideT);
            float bottomFade = SmoothStep(BottomFadeStart, 1f, bodyDepthT);
            float deepOpeningBias = Lerp(0.08f, -0.04f, caveDepthT);

            float threshold =
                BaseCarveThreshold +
                (topFade * TopFadeThresholdBoost) +
                (edgeFade * EdgeThresholdBoost) +
                (bottomFade * BottomFadeThresholdBoost) +
                deepOpeningBias;

            return field > threshold;
        }

        private static float GetSideProtectionT(DesertRegionProfile profile, DesertRegionColumn column, int columnIndex)
        {
            float localT = Math.Clamp(MathF.Abs(column.LocalX) / Math.Max(1f, profile.HalfWidth), 0f, 1f);
            int count = profile.Columns.Count;
            if (count <= 1)
                return localT;

            float edgeDistance = Math.Min(columnIndex, (count - 1) - columnIndex);
            float columnT = 1f - Math.Clamp(edgeDistance / Math.Max(1f, profile.BaseRadius * 0.22f), 0f, 1f);
            return Math.Max(localT, columnT);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * Math.Clamp(t, 0f, 1f));
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
