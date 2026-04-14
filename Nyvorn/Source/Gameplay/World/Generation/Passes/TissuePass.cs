using Microsoft.Xna.Framework;
using System;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class TissuePass : IWorldGenPass
    {
        private readonly OpenSimplexNoise macroNoise;
        private readonly OpenSimplexNoise filamentNoise;
        private readonly OpenSimplexNoise warpNoise;

        public TissuePass(int seed = 1337)
        {
            macroNoise = new OpenSimplexNoise(seed + 4101);
            filamentNoise = new OpenSimplexNoise(seed + 4102);
            warpNoise = new OpenSimplexNoise(seed + 4103);
        }

        public string Name => "Tissue";

        public void Apply(WorldGenContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.ProgressReporter?.Begin(Name, "Tecendo rede organica");

            int width = context.WorldMap.Width;
            int height = context.WorldMap.Height;

            TissueField field = new TissueField(width, height);

            WorldLayerDefinition surfaceLayer = context.GetLayerDefinition(WorldLayerType.Surface);
            WorldLayerDefinition shallowLayer = context.GetLayerDefinition(WorldLayerType.ShallowUnderground);
            WorldLayerDefinition cavernLayer = context.GetLayerDefinition(WorldLayerType.Cavern);
            WorldLayerDefinition deepLayer = context.GetLayerDefinition(WorldLayerType.DeepCavern);

            int attempts = 0;
            int solidCandidates = 0;
            int accepted = 0;

            for (int x = 0; x < width; x++)
            {
                // A malha agora pode subir por toda a ShallowUnderground e ainda
                // tocar a base da Surface de forma mais rara, para se aproximar
                // mais do teto das cavernas sem contaminar a superficie inteira.
                int surfaceReach = Math.Max(6, (int)MathF.Round(surfaceLayer.Height * 0.45f));
                int tissueStartY = Math.Max(0, surfaceLayer.EndY - surfaceReach);

                for (int y = tissueStartY; y < height; y++)
                {
                    attempts++;
                    solidCandidates++;

                    WorldLayerType layerType = context.GetLayerAtY(y);
                    if (layerType != WorldLayerType.Surface &&
                        layerType != WorldLayerType.ShallowUnderground &&
                        layerType != WorldLayerType.Cavern &&
                        layerType != WorldLayerType.DeepCavern)
                    {
                        continue;
                    }

                    float worldDepth01 = GetNormalized(y, surfaceLayer.StartY, height - 1);
                    float layerDepth01 = context.GetNormalizedDepthInLayer(y);

                    double warpX = warpNoise.Evaluate(x * 0.018, y * 0.018) * 9.0;
                    double warpY = warpNoise.Evaluate((x * 0.018) + 81.17, (y * 0.018) - 27.41) * 9.0;

                    double sx = x + warpX;
                    double sy = y + warpY;

                    float macro = Normalize01(macroNoise.Evaluate(sx * 0.0105, sy * 0.0105));
                    float filament = 1f - MathF.Abs((float)filamentNoise.Evaluate(sx * 0.050, sy * 0.050));
                    float detail = 1f - MathF.Abs((float)filamentNoise.Evaluate((sx * 0.095) + 173.2, (sy * 0.095) - 94.6));

                    filament = MathF.Pow(MathHelper.Clamp(filament, 0f, 1f), 2.15f);
                    detail = MathF.Pow(MathHelper.Clamp(detail, 0f, 1f), 1.70f);

                    float value =
                        (macro * 0.26f) +
                        (filament * 0.52f) +
                        (detail * 0.22f);

                    float depthBias = MathHelper.Lerp(-0.01f, 0.08f, worldDepth01);

                    float layerBias;
                    if (layerType == WorldLayerType.DeepCavern)
                    {
                        layerBias = MathHelper.Lerp(0.01f, 0.12f, MathF.Pow(layerDepth01, 1.25f));
                    }
                    else if (layerType == WorldLayerType.Cavern)
                    {
                        layerBias = MathHelper.Lerp(-0.01f, 0.04f, layerDepth01);
                    }
                    else if (layerType == WorldLayerType.ShallowUnderground)
                    {
                        // Faixa rasa: deixa o tecido subir, mas ainda mais esparso.
                        layerBias = MathHelper.Lerp(-0.10f, -0.01f, MathF.Pow(layerDepth01, 1.10f));
                    }
                    else
                    {
                        // Na Surface o tecido pode aparecer perto da base, mas
                        // com bem menos forca para evitar dominar o solo.
                        layerBias = MathHelper.Lerp(-0.24f, -0.11f, MathF.Pow(layerDepth01, 1.20f));
                    }

                    float deepRarefaction = 0f;
                    if (layerType == WorldLayerType.DeepCavern)
                    {
                        float deepFade = MathHelper.Clamp((layerDepth01 - 0.78f) / 0.22f, 0f, 1f);
                        deepRarefaction = MathHelper.Lerp(0f, 0.10f, deepFade);
                    }

                    value += depthBias;
                    value += layerBias;
                    value -= deepRarefaction;

                    float presenceWindow;
                    if (layerType == WorldLayerType.DeepCavern)
                    {
                        presenceWindow = MathHelper.Lerp(1.00f, 0.82f, layerDepth01);
                    }
                    else if (layerType == WorldLayerType.Cavern)
                    {
                        presenceWindow = MathHelper.Lerp(0.72f, 1.00f, layerDepth01);
                    }
                    else if (layerType == WorldLayerType.ShallowUnderground)
                    {
                        presenceWindow = MathHelper.Lerp(0.26f, 0.72f, MathF.Pow(layerDepth01, 0.85f));
                    }
                    else
                    {
                        presenceWindow = MathHelper.Lerp(0.08f, 0.30f, MathF.Pow(layerDepth01, 1.10f));
                    }

                    value *= presenceWindow;

                    float threshold;
                    if (layerType == WorldLayerType.DeepCavern)
                    {
                        float deepMidBoost = 1f - MathF.Abs((layerDepth01 - 0.62f) / 0.62f);
                        float deepThresholdOffset = MathHelper.Lerp(0.00f, -0.05f, MathHelper.Clamp(deepMidBoost, 0f, 1f));
                        threshold = 0.72f + deepThresholdOffset;
                    }
                    else if (layerType == WorldLayerType.Cavern)
                    {
                        threshold = MathHelper.Lerp(0.63f, 0.60f, layerDepth01);
                    }
                    else if (layerType == WorldLayerType.ShallowUnderground)
                    {
                        threshold = MathHelper.Lerp(0.80f, 0.67f, MathF.Pow(layerDepth01, 0.92f));
                    }
                    else
                    {
                        threshold = MathHelper.Lerp(0.90f, 0.79f, MathF.Pow(layerDepth01, 1.05f));
                    }

                    bool hasTissue = value >= threshold;
                    if (!hasTissue)
                        continue;

                    field.SetTissue(x, y, true);
                    accepted++;
                }

                if ((x & 15) == 0 || x == width - 1)
                {
                    float generationProgress = ((x + 1) / (float)width) * 0.72f;
                    context.ProgressReporter?.Report(Name, generationProgress, "Tecendo rede organica");
                }
            }

            field = Smooth(field, passes: 1, context);
            field = PruneFloatingNoise(field, minNeighbors: 2, context);

            context.WorldMap.SetTissueField(field);
            context.ProgressReporter?.Report(Name, 0.96f, "Consolidando a malha do tecido");
            context.WorldMap.RebuildTissueAnalysis();
            context.DebugStats["Tissue.Attempts"] = attempts.ToString();
            context.DebugStats["Tissue.SolidCandidates"] = solidCandidates.ToString();
            context.DebugStats["Tissue.AcceptedBeforePost"] = accepted.ToString();
            context.DebugStats["Tissue.ActiveTiles"] = field.CountActiveTiles().ToString();
            context.ProgressReporter?.Complete(Name, "Rede organica consolidada");
        }

        private static TissueField Smooth(TissueField source, int passes, WorldGenContext context)
        {
            TissueField current = source;

            for (int pass = 0; pass < passes; pass++)
            {
                TissueField next = new TissueField(current.Width, current.Height);

                for (int x = 0; x < current.Width; x++)
                {
                    for (int y = 0; y < current.Height; y++)
                    {
                        int neighbors = CountNeighbors(current, x, y);
                        bool self = current.HasTissue(x, y);

                        bool result = self
                            ? neighbors >= 2
                            : neighbors >= 5;

                        next.SetTissue(x, y, result);
                    }

                    if ((x & 15) == 0 || x == current.Width - 1)
                    {
                        float localProgress = (x + 1) / (float)current.Width;
                        float progress = 0.72f + (localProgress * 0.16f);
                        context.ProgressReporter?.Report("Tissue", progress, "Refinando filamentos");
                    }
                }

                current = next;
            }

            return current;
        }

        private static TissueField PruneFloatingNoise(TissueField source, int minNeighbors, WorldGenContext context)
        {
            TissueField next = new TissueField(source.Width, source.Height);

            for (int x = 0; x < source.Width; x++)
            {
                for (int y = 0; y < source.Height; y++)
                {
                    if (!source.HasTissue(x, y))
                        continue;

                    int neighbors = CountNeighbors(source, x, y);
                    if (neighbors >= minNeighbors)
                        next.SetTissue(x, y, true);
                }

                if ((x & 15) == 0 || x == source.Width - 1)
                {
                    float localProgress = (x + 1) / (float)source.Width;
                    float progress = 0.88f + (localProgress * 0.08f);
                    context.ProgressReporter?.Report("Tissue", progress, "Limpando ruido solto");
                }
            }

            return next;
        }

        private static int CountNeighbors(TissueField field, int centerX, int centerY)
        {
            int count = 0;

            for (int y = centerY - 1; y <= centerY + 1; y++)
            {
                for (int x = centerX - 1; x <= centerX + 1; x++)
                {
                    if (x == centerX && y == centerY)
                        continue;

                    if (field.HasTissue(x, y))
                        count++;
                }
            }

            return count;
        }

        private static float Normalize01(double value)
        {
            return (float)((value + 1.0) * 0.5);
        }

        private static float GetNormalized(int value, int min, int max)
        {
            if (max <= min)
                return 1f;

            return MathHelper.Clamp((value - min) / (float)(max - min), 0f, 1f);
        }
    }
}
