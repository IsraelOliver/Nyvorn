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

            int width = context.WorldMap.Width;
            int height = context.WorldMap.Height;

            TissueField field = new TissueField(width, height);

            WorldLayerDefinition cavernLayer = context.GetLayerDefinition(WorldLayerType.Cavern);
            WorldLayerDefinition deepLayer = context.GetLayerDefinition(WorldLayerType.DeepCavern);

            int attempts = 0;
            int solidCandidates = 0;
            int accepted = 0;

            for (int x = 0; x < width; x++)
            {
                int preCavernRise = Math.Max(8, (int)MathF.Round(cavernLayer.Height * 0.18f));
                int tissueStartY = Math.Max(0, cavernLayer.StartY - preCavernRise);

                for (int y = tissueStartY; y < height; y++)
                {
                    attempts++;

                    solidCandidates++;

                    WorldLayerType layerType = context.GetLayerAtY(y);
                    if (layerType != WorldLayerType.Cavern && layerType != WorldLayerType.DeepCavern)
                        continue;

                    float worldDepth01 = GetNormalized(y, cavernLayer.StartY, height - 1);
                    float layerDepth01 = context.GetNormalizedDepthInLayer(y);

                    // domain warp leve
                    double warpX = warpNoise.Evaluate(x * 0.018, y * 0.018) * 9.0;
                    double warpY = warpNoise.Evaluate((x * 0.018) + 81.17, (y * 0.018) - 27.41) * 9.0;

                    double sx = x + warpX;
                    double sy = y + warpY;

                    // macro presença ampla
                    float macro = Normalize01(macroNoise.Evaluate(sx * 0.0105, sy * 0.0105));

                    // filamento principal
                    float filament = 1f - MathF.Abs((float)filamentNoise.Evaluate(sx * 0.050, sy * 0.050));

                    // detalhe filamentoso secundário
                    float detail = 1f - MathF.Abs((float)filamentNoise.Evaluate((sx * 0.095) + 173.2, (sy * 0.095) - 94.6));

                    // aumenta contraste dos filamentos para afinar e reduzir blobs
                    filament = MathF.Pow(MathHelper.Clamp(filament, 0f, 1f), 2.15f);
                    detail = MathF.Pow(MathHelper.Clamp(detail, 0f, 1f), 1.70f);

                    // peso novo: filamento manda, macro só organiza
                    float value =
                        (macro * 0.26f) +
                        (filament * 0.52f) +
                        (detail * 0.22f);

                    // leve favorecimento por profundidade, mas bem mais controlado
                    float depthBias = MathHelper.Lerp(-0.01f, 0.08f, worldDepth01);

                    // layer bias mais contido
                    float layerBias;
                    if (layerType == WorldLayerType.DeepCavern)
                    {
                        // começa moderada, cresce melhor no meio da deep
                        layerBias = MathHelper.Lerp(0.01f, 0.12f, MathF.Pow(layerDepth01, 1.25f));
                    }
                    else
                    {
                        // cavern sobe, mas não rouba o protagonismo
                        layerBias = MathHelper.Lerp(-0.01f, 0.04f, layerDepth01);
                    }

                    // rarefação vertical:
                    // no deep mais fundo, começa a cortar presença em vez de só aumentar tudo
                    float deepRarefaction = 0f;
                    if (layerType == WorldLayerType.DeepCavern)
                    {
                        // rarefação começa mais tarde e mais suave
                        float deepFade = MathHelper.Clamp((layerDepth01 - 0.78f) / 0.22f, 0f, 1f);
                        deepRarefaction = MathHelper.Lerp(0f, 0.10f, deepFade);
                    }

                    value += depthBias;
                    value += layerBias;
                    value -= deepRarefaction;

                    float presenceWindow = layerType == WorldLayerType.DeepCavern
                    ? MathHelper.Lerp(1.00f, 0.82f, layerDepth01)
                    : MathHelper.Lerp(0.72f, 1.00f, layerDepth01);

                    value *= presenceWindow;

                    // threshold mais alto no deep para evitar saturação
                    float threshold;
                    if (layerType == WorldLayerType.DeepCavern)
                    {
                        // cai um pouco no meio da deep para concentrar mais lá
                        float deepMidBoost = 1f - MathF.Abs((layerDepth01 - 0.62f) / 0.62f);
                        float deepThresholdOffset = MathHelper.Lerp(0.00f, -0.05f, MathHelper.Clamp(deepMidBoost, 0f, 1f));
                        threshold = 0.72f + deepThresholdOffset;
                    }
                    else
                    {
                        threshold = MathHelper.Lerp(0.63f, 0.60f, layerDepth01);
                    }

                    bool hasTissue = value >= threshold;
                    if (!hasTissue)
                        continue;

                    field.SetTissue(x, y, true);
                    accepted++;
                }
            }

            field = Smooth(field, passes: 1);
            field = PruneFloatingNoise(field, minNeighbors: 2);

            context.WorldMap.SetTissueField(field);
            context.WorldMap.RebuildTissueAnalysis();
            context.DebugStats["Tissue.Attempts"] = attempts.ToString();
            context.DebugStats["Tissue.SolidCandidates"] = solidCandidates.ToString();
            context.DebugStats["Tissue.AcceptedBeforePost"] = accepted.ToString();
            context.DebugStats["Tissue.ActiveTiles"] = field.CountActiveTiles().ToString();
        }

        private static TissueField Smooth(TissueField source, int passes)
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
                }

                current = next;
            }

            return current;
        }

        private static TissueField PruneFloatingNoise(TissueField source, int minNeighbors)
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
