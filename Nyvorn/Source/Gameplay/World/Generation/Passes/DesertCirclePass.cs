using System;
using System.Collections.Generic;
using Nyvorn.Source.World.Generation.Biomes;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class DesertCirclePass : IWorldGenPass
    {
        private const int SmallWorldDiameterTiles = 300;
        private const float MediumScale = 1.40f;
        private const float LargeScale = 1.86f;

        public string Name => "DesertCircle";

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Criando deserto fossilizado");

            int diameter = GetDiameterTiles(context);
            int baseRadius = Math.Max(1, diameter / 2);
            int shapeHalfWidth = GetShapeHalfWidth(baseRadius);
            Random random = context.CreateRandom(SeedHash.Derive(context.Seeds.BiomeSeed, "desert-circle"));
            bool placeLeft = random.Next(0, 2) == 0;
            int centerX = PickCenterX(context, random, placeLeft, shapeHalfWidth);
            int surfaceY = GetSurfaceY(context, centerX);
            int mainDepth = GetMainDepth(context, surfaceY, baseRadius);
            DesertBranch[] branches = CreateBranches(random, baseRadius, mainDepth);
            float asymmetry = RandomRange(random, -1f, 1f);

            StampFossilizedWound(
                context,
                centerX,
                surfaceY,
                baseRadius,
                shapeHalfWidth,
                mainDepth,
                asymmetry,
                branches,
                out _,
                out _,
                out _,
                out DesertRegionProfile desertProfile);
            MarkBiomeColumns(context, centerX, shapeHalfWidth);
            context.DesertRegion = desertProfile;

            context.ProgressReporter?.Complete(Name, "Deserto fossilizado criado");
        }

        private static int GetDiameterTiles(WorldGenContext context)
        {
            float scale = context.Config.SizePreset switch
            {
                WorldSizePreset.Medium => MediumScale,
                WorldSizePreset.Large => LargeScale,
                _ => 1f
            };

            int target = (int)MathF.Round(SmallWorldDiameterTiles * scale);
            int maxDiameter = Math.Max(8, Math.Min(context.WorldMap.Width - 64, context.WorldMap.Height - 64));
            int minDiameter = Math.Min(64, maxDiameter);
            return Math.Clamp(target, minDiameter, maxDiameter);
        }

        private static int PickCenterX(WorldGenContext context, Random random, bool placeLeft, int halfWidth)
        {
            int worldWidth = context.WorldMap.Width;
            int borderPadding = Math.Max(32, context.Config.BorderThickness + 16);
            int minX;
            int maxX;

            if (placeLeft)
            {
                minX = borderPadding + halfWidth;
                maxX = (worldWidth / 2) - halfWidth;
            }
            else
            {
                minX = (worldWidth / 2) + halfWidth;
                maxX = worldWidth - borderPadding - halfWidth - 1;
            }

            if (maxX < minX)
                return placeLeft ? worldWidth / 4 : (worldWidth * 3) / 4;

            return random.Next(minX, maxX + 1);
        }

        private static int StampFossilizedWound(
            WorldGenContext context,
            int centerX,
            int surfaceY,
            int baseRadius,
            int shapeHalfWidth,
            int mainDepth,
            float asymmetry,
            DesertBranch[] branches,
            out int clearedTiles,
            out int minDuneY,
            out int maxDuneY,
            out DesertRegionProfile desertProfile)
        {
            int sandTiles = 0;
            clearedTiles = 0;
            minDuneY = int.MaxValue;
            maxDuneY = int.MinValue;
            List<DesertRegionColumn> columns = new();

            int maxDepth = mainDepth;
            for (int i = 0; i < branches.Length; i++)
                maxDepth = Math.Max(maxDepth, branches[i].StartDepth + branches[i].Length);

            int topLift = Math.Max(4, (int)MathF.Round(baseRadius * 0.18f));
            int valleyAllowance = Math.Max(4, (int)MathF.Round(baseRadius * 0.16f));
            int minY = Math.Max(0, surfaceY - topLift);
            int maxY = Math.Min(context.WorldMap.Height - context.Config.BorderThickness - 1, surfaceY + maxDepth + valleyAllowance);
            OpenSimplexNoise topNoise = new(SeedHash.ToIntSeed(SeedHash.Derive(context.Seeds.BiomeSeed, "desert-wound-top")));
            OpenSimplexNoise edgeNoise = new(SeedHash.ToIntSeed(SeedHash.Derive(context.Seeds.BiomeSeed, "desert-wound-edge")));

            for (int dx = -shapeHalfWidth; dx <= shapeHalfWidth; dx++)
            {
                int x = context.WorldMap.WrapTileX(centerX + dx);
                int localSurfaceY = GetSurfaceY(context, x);
                int topY = GetDuneTopY(topNoise, x, dx, localSurfaceY, surfaceY, baseRadius, shapeHalfWidth);
                topY = Math.Clamp(topY, 1, context.WorldMap.Height - context.Config.BorderThickness - 2);

                bool hasSurfaceApron = GetSurfaceApronDepth(dx, baseRadius, shapeHalfWidth) > 0;
                if (!hasSurfaceApron && !IsInsideMainBody(edgeNoise, dx, 0, baseRadius, mainDepth, asymmetry))
                    continue;

                minDuneY = Math.Min(minDuneY, topY);
                maxDuneY = Math.Max(maxDuneY, topY);

                clearedTiles += ClearOldSurfaceAboveDune(context, x, localSurfaceY, topY);
                SetSurfaceY(context, x, topY);
                int bottomY = topY;

                for (int y = minY; y <= maxY; y++)
                {
                    if (y < topY)
                        continue;

                    int depth = y - topY;
                    if (!IsInsideMainBody(edgeNoise, dx, depth, baseRadius, mainDepth, asymmetry) &&
                        !IsInsideAnyBranch(edgeNoise, dx, depth, baseRadius, branches) &&
                        !IsInsideSurfaceApron(dx, depth, baseRadius, shapeHalfWidth))
                    {
                        continue;
                    }

                    bottomY = Math.Max(bottomY, y);

                    if (context.WorldMap.GetTile(x, y) == TileType.Sand)
                        continue;

                    context.WorldMap.SetTile(x, y, TileType.Sand);
                    sandTiles++;
                }

                columns.Add(new DesertRegionColumn(x, dx, topY, localSurfaceY, bottomY, topY));

                if (((dx + shapeHalfWidth) & 15) == 0 || dx == shapeHalfWidth)
                {
                    float progress = (dx + shapeHalfWidth + 1) / (float)((shapeHalfWidth * 2) + 1);
                    context.ProgressReporter?.Report("DesertCircle", progress, "Criando deserto fossilizado");
                }
            }

            if (minDuneY == int.MaxValue)
                minDuneY = surfaceY;
            if (maxDuneY == int.MinValue)
                maxDuneY = surfaceY;

            desertProfile = new DesertRegionProfile(
                surfaceY,
                baseRadius,
                shapeHalfWidth,
                mainDepth,
                columns);

            return sandTiles;
        }

        private static bool IsInsideMainBody(
            OpenSimplexNoise noise,
            int dx,
            int depth,
            int baseRadius,
            int mainDepth,
            float asymmetry)
        {
            if (depth < 0 || depth > mainDepth)
                return false;

            float depthT = depth / (float)Math.Max(1, mainDepth);
            float halfWidth = GetBodyHalfWidth(baseRadius, depthT);
            float centerDrift = GetBodyCenterDrift(noise, baseRadius, depthT, asymmetry);
            float localX = dx - centerDrift;
            float edgeT = Math.Clamp(MathF.Abs(localX) / Math.Max(1f, halfWidth), 0f, 1f);
            float edgeStrength = SmoothStep(0.42f, 1f, edgeT) * Lerp(0.45f, 1f, SmoothStep(0.10f, 0.80f, depthT));
            float edgeNoise = (float)noise.Evaluate((dx + 4100f) * 0.035f, (depth + 1700f) * 0.028f);
            float edgeJitter = edgeNoise * baseRadius * 0.11f * edgeStrength;

            return MathF.Abs(localX) <= halfWidth + edgeJitter;
        }

        private static bool IsInsideSurfaceApron(int dx, int depth, int baseRadius, int shapeHalfWidth)
        {
            int apronDepth = GetSurfaceApronDepth(dx, baseRadius, shapeHalfWidth);
            return depth >= 0 && depth <= apronDepth;
        }

        private static int GetSurfaceApronDepth(int dx, int baseRadius, int shapeHalfWidth)
        {
            float edgeT = Math.Clamp(MathF.Abs(dx) / Math.Max(1f, shapeHalfWidth), 0f, 1f);
            float apronT = 1f - SmoothStep(0.72f, 1f, edgeT);
            if (apronT <= 0f)
                return 0;

            float shoulder = Lerp(baseRadius * 0.035f, baseRadius * 0.20f, apronT);
            return Math.Max(2, (int)MathF.Round(shoulder));
        }

        private static bool IsInsideAnyBranch(
            OpenSimplexNoise noise,
            int dx,
            int depth,
            int baseRadius,
            DesertBranch[] branches)
        {
            for (int i = 0; i < branches.Length; i++)
            {
                if (IsInsideBranch(noise, dx, depth, baseRadius, branches[i]))
                    return true;
            }

            return false;
        }

        private static bool IsInsideBranch(OpenSimplexNoise noise, int dx, int depth, int baseRadius, DesertBranch branch)
        {
            if (depth < branch.StartDepth || depth > branch.StartDepth + branch.Length)
                return false;

            float branchT = (depth - branch.StartDepth) / (float)Math.Max(1, branch.Length);
            float easedT = SmoothStep01(branchT);
            float centerX = branch.OriginOffsetX + (branch.DriftX * easedT);
            float wiggle = (float)noise.Evaluate(branch.NoiseOffset + (branchT * 3.2f), 2600f) * baseRadius * 0.055f;
            float halfWidth = Lerp(branch.StartHalfWidth, branch.EndHalfWidth, easedT);
            float edgeNoise = (float)noise.Evaluate((dx + branch.NoiseOffset) * 0.055f, (depth + 3200f) * 0.040f);
            float edgeJitter = edgeNoise * baseRadius * 0.030f;

            return MathF.Abs(dx - centerX - wiggle) <= halfWidth + edgeJitter;
        }

        private static int MarkBiomeColumns(WorldGenContext context, int centerX, int halfWidth)
        {
            if (context.Biomes == null)
                return 0;

            int columns = 0;
            for (int dx = -halfWidth; dx <= halfWidth; dx++)
            {
                int x = context.WorldMap.WrapTileX(centerX + dx);
                if (context.Biomes.GetBiome(x) != BiomeType.Desert)
                    columns++;

                context.Biomes.SetBiome(x, BiomeType.Desert);
            }

            return columns;
        }

        private static DesertBranch[] CreateBranches(Random random, int baseRadius, int mainDepth)
        {
            int count = random.Next(0, 100) < 65 ? 1 : 2;
            DesertBranch[] branches = new DesertBranch[count];
            for (int i = 0; i < branches.Length; i++)
            {
                float side = random.Next(0, 2) == 0 ? -1f : 1f;
                float originOffset = RandomRange(random, -0.22f, 0.22f) * baseRadius;
                int startDepth = (int)MathF.Round(RandomRange(random, 0.70f, 0.86f) * mainDepth);
                int endDepth = (int)MathF.Round(RandomRange(random, 0.94f, 1.00f) * mainDepth);
                int length = Math.Max(12, endDepth - startDepth);
                float drift = side * RandomRange(random, 0.16f, 0.34f) * baseRadius;
                float startHalfWidth = RandomRange(random, 0.075f, 0.135f) * baseRadius;
                float endHalfWidth = RandomRange(random, 0.020f, 0.040f) * baseRadius;
                float noiseOffset = RandomRange(random, 1000f, 9000f);

                branches[i] = new DesertBranch(originOffset, startDepth, length, drift, startHalfWidth, endHalfWidth, noiseOffset);
            }

            return branches;
        }

        private static int GetShapeHalfWidth(int baseRadius)
        {
            return Math.Max(1, (int)MathF.Ceiling(baseRadius * 1.24f));
        }

        private static int GetMainDepth(WorldGenContext context, int surfaceY, int baseRadius)
        {
            WorldLayerDefinition deepLayer = context.GetLayerDefinition(WorldLayerType.DeepCavern);
            int deepBuffer = Math.Max(28, (int)MathF.Round(context.WorldMap.Height * 0.035f));
            int targetBottomY = deepLayer.StartY - deepBuffer;
            int targetDepth = targetBottomY - surfaceY;
            int minimumDepth = Math.Max(24, (int)MathF.Round(baseRadius * 1.85f));
            int maximumDepth = context.WorldMap.Height - context.Config.BorderThickness - surfaceY - 10;

            if (maximumDepth < minimumDepth)
                return Math.Max(1, maximumDepth);

            return Math.Clamp(Math.Max(minimumDepth, targetDepth), minimumDepth, maximumDepth);
        }

        private static float GetBodyHalfWidth(int baseRadius, float depthT)
        {
            if (depthT < 0.16f)
                return Lerp(baseRadius * 1.20f, baseRadius * 1.08f, SmoothStep01(depthT / 0.16f));

            if (depthT < 0.62f)
                return Lerp(baseRadius * 1.08f, baseRadius * 0.70f, SmoothStep01((depthT - 0.16f) / 0.46f));

            return Lerp(baseRadius * 0.70f, baseRadius * 0.10f, SmoothStep01((depthT - 0.62f) / 0.38f));
        }

        private static float GetBodyCenterDrift(OpenSimplexNoise noise, int baseRadius, float depthT, float asymmetry)
        {
            float fossilBend = asymmetry * baseRadius * 0.22f * SmoothStep(0.12f, 1f, depthT);
            float organicWaver = (float)noise.Evaluate(depthT * 2.4f, 114f) * baseRadius * 0.08f;
            return fossilBend + organicWaver;
        }

        private static int GetDuneTopY(
            OpenSimplexNoise noise,
            int x,
            int dx,
            int normalSurfaceY,
            int anchorSurfaceY,
            int baseRadius,
            int shapeHalfWidth)
        {
            float normalizedX = dx / Math.Max(1f, baseRadius);
            float edgeT = Math.Clamp(MathF.Abs(dx) / Math.Max(1f, shapeHalfWidth), 0f, 1f);
            float edgeBlend = SmoothStep(0.56f, 0.94f, edgeT);
            float crestEnvelope = 1f - SmoothStep(0.40f, 1f, edgeT);

            float macroSwell = (float)noise.Evaluate((x + 7000f) * 0.006f, 1300f) * baseRadius * 0.085f;
            float duneWave = MathF.Sin((normalizedX * MathF.PI * 2.10f) + 0.65f) * baseRadius * 0.070f;
            float secondaryWave = MathF.Sin((normalizedX * MathF.PI * 4.35f) - 1.20f) * baseRadius * 0.025f;
            float fineRipple = (float)noise.Evaluate((x + 2300f) * 0.022f, 1800f) * baseRadius * 0.012f;
            float broadLift = -baseRadius * 0.040f * crestEnvelope;
            float duneTop = anchorSurfaceY + broadLift + macroSwell + duneWave + secondaryWave + fineRipple;

            float edgeTop = normalSurfaceY;
            int topY = (int)MathF.Round(Lerp(duneTop, edgeTop, edgeBlend));
            return Math.Clamp(topY, 1, int.MaxValue - 1);
        }

        private static int GetSurfaceY(WorldGenContext context, int x)
        {
            if (context.SurfaceHeights != null && context.SurfaceHeights.Length > 0)
                return context.SurfaceHeights[context.WorldMap.WrapTileX(x)];

            return context.Config.SurfaceBaseHeight;
        }

        private static void SetSurfaceY(WorldGenContext context, int x, int y)
        {
            if (context.SurfaceHeights == null || context.SurfaceHeights.Length == 0)
                return;

            context.SurfaceHeights[context.WorldMap.WrapTileX(x)] = Math.Clamp(y, 0, context.WorldMap.Height - 1);
        }

        private static int ClearOldSurfaceAboveDune(WorldGenContext context, int x, int oldSurfaceY, int duneTopY)
        {
            if (duneTopY <= oldSurfaceY)
                return 0;

            int cleared = 0;
            int startY = Math.Clamp(oldSurfaceY, 0, context.WorldMap.Height - 1);
            int endY = Math.Clamp(duneTopY - 1, 0, context.WorldMap.Height - 1);
            for (int y = startY; y <= endY; y++)
            {
                if (context.WorldMap.GetTile(x, y) == TileType.Empty)
                    continue;

                context.WorldMap.SetTile(x, y, TileType.Empty);
                cleared++;
            }

            return cleared;
        }

        private static float RandomRange(Random random, float min, float max)
        {
            return min + ((max - min) * (float)random.NextDouble());
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * Math.Clamp(t, 0f, 1f));
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            if (MathF.Abs(edge1 - edge0) < 0.0001f)
                return 0f;

            return SmoothStep01((value - edge0) / (edge1 - edge0));
        }

        private static float SmoothStep01(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - (2f * t));
        }

        private readonly struct DesertBranch
        {
            public DesertBranch(
                float originOffsetX,
                int startDepth,
                int length,
                float driftX,
                float startHalfWidth,
                float endHalfWidth,
                float noiseOffset)
            {
                OriginOffsetX = originOffsetX;
                StartDepth = startDepth;
                Length = length;
                DriftX = driftX;
                StartHalfWidth = startHalfWidth;
                EndHalfWidth = endHalfWidth;
                NoiseOffset = noiseOffset;
            }

            public float OriginOffsetX { get; }
            public int StartDepth { get; }
            public int Length { get; }
            public float DriftX { get; }
            public float StartHalfWidth { get; }
            public float EndHalfWidth { get; }
            public float NoiseOffset { get; }
        }
    }
}
