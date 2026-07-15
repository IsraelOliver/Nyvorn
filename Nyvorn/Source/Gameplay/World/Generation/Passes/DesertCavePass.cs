using System;
using System.Collections.Generic;
using Nyvorn.Source.World.Generation.Biomes;

namespace Nyvorn.Source.World.Generation.Passes
{
    public sealed class DesertCavePass : IWorldGenPass
    {
        /*
         * DesertCavePass v7 - Bolsoes Fosseis por Interseccao (Sandstone Fossil Pods)
         *
         * Nota v7 (correcao de borda organica):
         * A versao anterior media "distancia ate a borda" pelo INDICE da coluna
         * dentro da lista (distancia ate a largura maxima do deserto). Isso e a
         * distancia ate um RETANGULO, nao ate a areia de verdade. Como o corpo
         * do deserto afunila muito com a profundidade (ver GetBodyHalfWidth em
         * DesertCirclePass, que vai de ~1.20x o raio no topo a ~0.10x perto do
         * fundo), nas partes fundas a areia real ja tinha acabado bem antes do
         * "indice de borda" ficar pequeno o suficiente para suavizar. Resultado:
         * a suavizacao praticamente nao agia no cone, e a caverna esbarrava na
         * areia de verdade sem aviso -> corte reto.
         *
         * Agora a distancia lateral e medida tile a tile, escaneando a propria
         * malha (Sand x nao-Sand) na mesma linha Y. Isso acompanha o contorno
         * organico exatamente como ele foi esculpido (duna, ramificacoes,
         * afunilamento), sem precisar duplicar a logica de ruido/branches do
         * DesertCirclePass. O campo antigo baseado no indice da coluna e no
         * HalfWidth global foi removido.
         *
         * Objetivo:
         * - Gerar cavernas do deserto com identidade propria de Nyvorn.
         * - As cavernas globais (cinza/marrom) usam um campo ridged somado e
         *   continuo -> rede fina de fissuras. Isso ja esta correto e nao muda aqui.
         * - As cavernas do deserto NAO devem parecer essa mesma rede, nem parecer
         *   os tuneis vermiformes do Terraria.
         *
         * Tecnica (adaptada do artigo classico da Accidental Noise Library sobre
         * geracao estilo Minecraft/Terraria - accidentalnoise.sourceforge.net):
         *
         *   O artigo usa DOIS campos ridged multifractal independentes (fontes/
         *   seeds diferentes). Cada campo, sozinho, e binarizado (Select: acima do
         *   threshold = "aberto", abaixo = "solido"). Depois os DOIS resultados
         *   binarios sao multiplicados entre si (AND). A caverna final so existe
         *   onde os dois campos concordam que ali e "aberto".
         *
         *   Por que isso muda a forma da caverna:
         *   - Um unico campo ridged (ou uma soma de varios) e uma superficie
         *     continua cortada por UM corte -> sempre produz cristas/fissuras
         *     conectadas de ponta a ponta (o que voce ve no print 1).
         *   - A interseccao de DOIS campos independentes quebra essa
         *     continuidade: a fissura de um campo raramente coincide de ponta a
         *     ponta com a fissura do outro. O que sobra sao os pontos de
         *     encontro -> bolsoes arredondados, isolados, com formato organico
         *     (o que voce ve no print 2).
         *
         * Linguagem visual do deserto (diferente da caverna global e do
         * Terraria):
         * - Bolsoes arredondados e horizontais, nao fissuras finas.
         * - Bolsoes cortados em "lentes" sedimentares pelo campo de estratos,
         *   como camaras fosseis presas entre camadas de arenito.
         * - Densidade regional variavel (macro): zonas com muitos bolsoes
         *   conectados, zonas quase solidas, sem uniformidade.
         * - Bordas dos bolsoes levemente porosas (micro-warp), lembrando
         *   erosao granular do arenito, nao uma parede lisa.
         * - Massa solida preservada por costelas e veios minerais.
         * - Cavernas morrendo antes da borda do bioma e antes do topo/fundo.
         */

        public string Name => "DesertCave";

        // Protecao estrutural.
        private const int MinTilesBelowPixelSand = 16;
        private const int BottomCoreProtectionTiles = 22;

        // Regra pedida: nao cavar grudado nos limites laterais do deserto.
        // Medido agora tile a tile contra a areia de verdade (ver
        // GetLateralSandDistance), nao contra o retangulo do bioma.
        private const int HardBorderPaddingTiles = 5;
        private const int SoftBorderFadeTiles = 24;

        // Ate quantos tiles escanear lateralmente para achar a borda real.
        // Um pouco alem de HardBorderPaddingTiles + SoftBorderFadeTiles: depois
        // disso o fade ja saturou em zero, entao o valor exato nao importa mais.
        private const int LateralScanBuffer = 3;

        // Protecao de topo/fundo.
        private const float TopFadeTiles = 34f;
        private const float TopThresholdBoost = 0.62f;

        private const float BottomFadeStart = 0.80f;
        private const float BottomThresholdBoost = 0.48f;

        // Quanto a borda dificulta cavar. Mais suave que antes porque agora
        // essa suavizacao realmente atua ao longo de toda a borda real (antes,
        // por medir contra o retangulo, quase nunca chegava a agir no cone).
        private const float EdgeThresholdBoost = 0.70f;

        // ---- Campo A: primeiro campo ridged independente do par de interseccao ----
        // Frequencia baixa em X, mais alta em Y -> bolsoes tendem a horizontal.
        private const float BlobFieldAFrequencyX = 0.020f;
        private const float BlobFieldAFrequencyY = 0.046f;
        private const float BlobBaseThresholdA = 0.60f;

        // ---- Campo B: segundo campo ridged independente do par de interseccao ----
        // Frequencia e orientacao levemente diferentes do campo A de proposito:
        // se fossem iguais, a "interseccao" coincidiria demais e voltaria a
        // parecer uma fissura continua. A diferenca de frequencia/seed e o que
        // garante bolsoes isolados em vez de tuneis longos.
        private const float BlobFieldBFrequencyX = 0.027f;
        private const float BlobFieldBFrequencyY = 0.039f;
        private const float BlobBaseThresholdB = 0.60f;

        // Campo macro: controla densidade regional (algumas areas do deserto
        // ganham bolsoes maiores/mais conectados, outras ficam quase solidas).
        private const float MacroFrequencyX = 0.006f;
        private const float MacroFrequencyY = 0.016f;
        private const float MacroOpeningStrength = 0.16f;

        // Campo de estratos sedimentares: corta os bolsoes em lentes/camadas,
        // reforcando a leitura "fossil em arenito" em vez de "caverna generica".
        private const float StrataFrequencyX = 0.007f;
        private const float StrataFrequencyY = 0.095f;
        private const float StrataBandStrength = 0.22f;

        // Micro-warp de borda (usa o ruido de grao para distorcer o dominio de
        // cada campo de forma levemente diferente entre A e B), dando porosidade
        // as bordas dos bolsoes em vez de contorno liso.
        private const float MicroWarpFrequencyX = 0.045f;
        private const float MicroWarpFrequencyY = 0.080f;
        private const float MicroWarpStrengthX = 7f;
        private const float MicroWarpStrengthY = 5f;

        // Costelas solidas sedimentares.
        // Aumentam o threshold em algumas faixas, preservando blocos e pontes.
        private const float SolidRibFrequencyX = 0.010f;
        private const float SolidRibFrequencyY = 0.075f;
        private const float SolidRibPenalty = 0.20f;

        // Preservacao mineral mais vertical/diagonal leve.
        private const float MineralVeinFrequencyX = 0.052f;
        private const float MineralVeinFrequencyY = 0.026f;
        private const float MineralVeinPenalty = 0.12f;

        // Warp de dominio de larga escala (deforma os dois campos igualmente
        // antes da interseccao, para dar contorno organico geral).
        private const float WarpFrequencyX = 0.030f;
        private const float WarpFrequencyY = 0.036f;
        private const float WarpStrengthX = 18f;
        private const float WarpStrengthY = 10f;

        // Abre mais na parte media do corpo do deserto.
        private const float MiddleOpeningStrength = 0.16f;

        public void Apply(WorldGenContext context)
        {
            context.ProgressReporter?.Begin(Name, "Cavando bolsoes fosseis de arenito");

            DesertRegionProfile profile = context.DesertRegion;
            if (profile == null || profile.Columns == null || profile.Columns.Count == 0)
            {
                context.ProgressReporter?.Complete(Name, "Sem deserto para bolsoes fosseis de arenito");
                return;
            }

            int seed = SeedHash.ToIntSeed(
                SeedHash.Derive(context.Seeds.CaveSeed, "desert-caves-sandstone-fossil-v7"));

            OpenSimplexNoise fieldANoise = new(seed + 1000);
            OpenSimplexNoise fieldBNoise = new(seed + 2000);
            OpenSimplexNoise macroNoise = new(seed + 3000);
            OpenSimplexNoise strataNoise = new(seed + 4000);
            OpenSimplexNoise microWarpANoise = new(seed + 5000);
            OpenSimplexNoise microWarpBNoise = new(seed + 5500);
            OpenSimplexNoise solidRibNoise = new(seed + 6000);
            OpenSimplexNoise mineralVeinNoise = new(seed + 7000);
            OpenSimplexNoise warpNoise = new(seed + 8000);

            // Coletamos as posicoes a cavar em vez de aplicar na hora. Se
            // aplicassemos direto, uma coluna processada mais tarde poderia
            // escanear lateralmente (GetLateralSandDistance) e encontrar um
            // buraco que ESTA MESMA PASS ja cavou em uma coluna anterior --
            // interpretando isso como "borda perto" e fechando quase tudo por
            // engano. Adiando a escrita, o escaneamento sempre le a massa de
            // areia original e intacta, nao importa a ordem de processamento.
            List<(int X, int Y)> tilesToCarve = new();

            for (int columnIndex = 0; columnIndex < profile.Columns.Count; columnIndex++)
            {
                DesertRegionColumn column = profile.Columns[columnIndex];

                int startY = Math.Max(0, column.PixelSandBaseY + MinTilesBelowPixelSand);
                int endY = Math.Min(
                    context.WorldMap.Height - context.Config.BorderThickness - 2,
                    column.BottomY - BottomCoreProtectionTiles);

                if (endY <= startY)
                    continue;

                int lateralScanMax = HardBorderPaddingTiles + SoftBorderFadeTiles + LateralScanBuffer;

                for (int y = startY; y <= endY; y++)
                {
                    if (context.WorldMap.GetTile(column.X, y) != TileType.Sand)
                        continue;

                    int lateralDistance = GetLateralSandDistance(context, column.X, y, lateralScanMax);
                    if (lateralDistance < HardBorderPaddingTiles)
                        continue;

                    if (!ShouldCarveDesertCave(
                        context,
                        column,
                        startY,
                        endY,
                        lateralDistance,
                        fieldANoise,
                        fieldBNoise,
                        macroNoise,
                        strataNoise,
                        microWarpANoise,
                        microWarpBNoise,
                        solidRibNoise,
                        mineralVeinNoise,
                        warpNoise,
                        y))
                    {
                        continue;
                    }

                    tilesToCarve.Add((column.X, y));
                }

                if ((columnIndex & 15) == 0 || columnIndex == profile.Columns.Count - 1)
                {
                    context.ProgressReporter?.Report(
                        Name,
                        (columnIndex + 1) / (float)profile.Columns.Count,
                        "Cavando bolsoes fosseis de arenito");
                }
            }

            // Segunda fase: aplica todas as decisoes de uma vez, com o mapa
            // original ja totalmente lido e intocado durante a fase 1.
            for (int i = 0; i < tilesToCarve.Count; i++)
            {
                (int x, int y) = tilesToCarve[i];
                context.WorldMap.SetTile(x, y, TileType.Empty);
            }


            context.ProgressReporter?.Complete(Name, "Bolsoes fosseis de arenito esculpidos");
        }

        private static bool ShouldCarveDesertCave(
            WorldGenContext context,
            DesertRegionColumn column,
            int startY,
            int endY,
            int lateralDistance,
            OpenSimplexNoise fieldANoise,
            OpenSimplexNoise fieldBNoise,
            OpenSimplexNoise macroNoise,
            OpenSimplexNoise strataNoise,
            OpenSimplexNoise microWarpANoise,
            OpenSimplexNoise microWarpBNoise,
            OpenSimplexNoise solidRibNoise,
            OpenSimplexNoise mineralVeinNoise,
            OpenSimplexNoise warpNoise,
            int y)
        {
            int x = column.X;

            float caveDepthT = Math.Clamp(
                (y - startY) / (float)Math.Max(1, endY - startY),
                0f,
                1f);

            float bodyDepthT = Math.Clamp(
                (y - column.TopY) / (float)Math.Max(1, column.BottomY - column.TopY),
                0f,
                1f);

            // Warp de larga escala, aplicado igualmente aos dois campos antes da
            // interseccao. Isso da contorno organico ao par inteiro sem
            // atrapalhar a independencia entre A e B (o que importa e a
            // diferenca de frequencia/seed entre eles, nao a ausencia de warp
            // compartilhado).
            float warpX = WorldFieldSampler.Fractal(
                context,
                warpNoise,
                x,
                y,
                WarpFrequencyX,
                WarpFrequencyY,
                500f,
                1300f) * WarpStrengthX;

            float warpY = WorldFieldSampler.Fractal(
                context,
                warpNoise,
                x,
                y,
                WarpFrequencyX,
                WarpFrequencyY,
                1700f,
                900f) * WarpStrengthY;

            // Micro-warp independente para cada campo: pequena distorcao extra
            // que difere entre A e B, quebrando qualquer coincidencia perfeita
            // de borda entre os dois campos e deixando o contorno dos bolsoes
            // poroso, como arenito erodido.
            float microWarpAX = WorldFieldSampler.Fractal(
                context,
                microWarpANoise,
                x,
                y,
                MicroWarpFrequencyX,
                MicroWarpFrequencyY,
                100f,
                200f) * MicroWarpStrengthX;

            float microWarpAY = WorldFieldSampler.Fractal(
                context,
                microWarpANoise,
                x,
                y,
                MicroWarpFrequencyX,
                MicroWarpFrequencyY,
                800f,
                300f) * MicroWarpStrengthY;

            float microWarpBX = WorldFieldSampler.Fractal(
                context,
                microWarpBNoise,
                x,
                y,
                MicroWarpFrequencyX,
                MicroWarpFrequencyY,
                150f,
                450f) * MicroWarpStrengthX;

            float microWarpBY = WorldFieldSampler.Fractal(
                context,
                microWarpBNoise,
                x,
                y,
                MicroWarpFrequencyX,
                MicroWarpFrequencyY,
                950f,
                600f) * MicroWarpStrengthY;

            // Campo A e campo B: duas fontes ridged independentes. Cada uma vai
            // ser binarizada separadamente (Select) e so depois multiplicada
            // (AND) com a outra. E essa interseccao que produz bolsoes
            // arredondados em vez de uma fissura continua.
            float fieldA = Ridged(WorldFieldSampler.SampleSeamedNoise(
                context,
                fieldANoise,
                x,
                y,
                BlobFieldAFrequencyX,
                BlobFieldAFrequencyY,
                warpX + microWarpAX,
                warpY + microWarpAY));

            float fieldB = Ridged(WorldFieldSampler.SampleSeamedNoise(
                context,
                fieldBNoise,
                x,
                y,
                BlobFieldBFrequencyX,
                BlobFieldBFrequencyY,
                (warpX * 0.85f) + microWarpBX,
                (warpY * 0.85f) + microWarpBY));

            // Macro: controla densidade regional. Em vez de somar ao campo
            // principal (o que voltaria a criar uma superficie continua), ele
            // so empurra o threshold de abertura para cima ou para baixo por
            // regiao -- algumas partes do deserto ficam com bolsoes maiores e
            // mais frequentes, outras ficam quase inteiramente solidas.
            float macro = Ridged(WorldFieldSampler.Fractal(
                context,
                macroNoise,
                x,
                y,
                MacroFrequencyX,
                MacroFrequencyY,
                warpX * 0.28f,
                warpY * 0.28f));

            float macroOpening = (macro - 0.5f) * 2f * MacroOpeningStrength;

            // Estratos: gera bandas horizontais periodicas. Usado como bias no
            // threshold para que os bolsoes fiquem presos entre camadas
            // sedimentares (lentes fosseis), em vez de bolhas soltas flutuando
            // sem relacao com o entorno.
            float strata = Ridged(WorldFieldSampler.SampleSeamedNoise(
                context,
                strataNoise,
                x,
                y,
                StrataFrequencyX,
                StrataFrequencyY,
                warpX * 0.18f,
                warpY * 0.12f));

            float strataBias = (strata - 0.5f) * 2f * StrataBandStrength;

            // Costelas solidas sedimentares: preservam pontes e blocos.
            float solidRib = Ridged(WorldFieldSampler.SampleSeamedNoise(
                context,
                solidRibNoise,
                x,
                y,
                SolidRibFrequencyX,
                SolidRibFrequencyY,
                warpX * 0.10f,
                warpY * 0.18f));

            float solidRibPenalty =
                SmoothStep(0.58f, 0.92f, solidRib) * SolidRibPenalty;

            // Veios minerais: preservam faixas mais verticais/diagonais.
            float mineralVein = Ridged(WorldFieldSampler.SampleSeamedNoise(
                context,
                mineralVeinNoise,
                x,
                y,
                MineralVeinFrequencyX,
                MineralVeinFrequencyY,
                warpX * 0.16f,
                warpY * 0.10f));

            float mineralVeinPenalty =
                SmoothStep(0.64f, 0.96f, mineralVein) * MineralVeinPenalty;

            float topFade =
                1f - SmoothStep(0f, TopFadeTiles, y - startY);

            float bottomFade =
                SmoothStep(BottomFadeStart, 1f, bodyDepthT);

            // borderFade agora usa a distancia lateral real (medida tile a
            // tile contra a areia de verdade), entao a suavizacao acompanha o
            // contorno organico do deserto -- duna, ramificacao, afunilamento
            // -- em vez de um retangulo. E isso que troca o corte reto por um
            // fechamento gradual e organico perto da borda.
            float borderFade =
                1f - SmoothStep(
                    HardBorderPaddingTiles,
                    HardBorderPaddingTiles + SoftBorderFadeTiles,
                    lateralDistance);

            // Abre mais no meio do corpo, fecha no topo e no fundo.
            float middleOpening =
                SmoothStep(0.18f, 0.46f, caveDepthT) *
                (1f - SmoothStep(0.78f, 1f, caveDepthT)) *
                MiddleOpeningStrength;

            float sharedAdjustment =
                (topFade * TopThresholdBoost) +
                (bottomFade * BottomThresholdBoost) +
                (borderFade * EdgeThresholdBoost) +
                solidRibPenalty +
                mineralVeinPenalty +
                strataBias -
                middleOpening -
                macroOpening;

            float thresholdA = BlobBaseThresholdA + sharedAdjustment;
            float thresholdB = BlobBaseThresholdB + sharedAdjustment;

            bool openA = fieldA > thresholdA;
            bool openB = fieldB > thresholdB;

            // Interseccao (AND): a caverna so existe onde os dois campos
            // concordam. Isso e o que troca "rede de fissuras" por "bolsoes
            // arredondados isolados".
            return openA && openB;
        }

        private static float Ridged(float value)
        {
            // OpenSimplex geralmente retorna algo entre -1 e 1.
            // Isso transforma o campo em cristas/bolsoes mais grossos.
            return 1f - MathF.Abs(value);
        }

        private static int GetLateralSandDistance(WorldGenContext context, int x, int y, int maxScan)
        {
            // Distancia real (em tiles) ate a borda lateral do deserto NESTA
            // linha Y especifica, medida escaneando a propria malha para os
            // dois lados ate encontrar um tile que nao e Sand. Isso segue
            // exatamente o contorno que o DesertCirclePass esculpiu -- duna,
            // ramificacao, afunilamento -- sem precisar reconstruir a formula
            // de ruido/branches usada la.
            //
            // Pressuposto: quando esta pass roda, os tiles de areia na faixa
            // de profundidade avaliada (abaixo da pixel sand, acima do nucleo
            // protegido) ainda nao foram alterados por nenhuma outra cavernas.
            // Se no futuro outra pass cavar buracos dentro do deserto antes
            // desta rodar, um buraco isolado poderia ser lido como "borda" por
            // engano; nesse caso vale reduzir maxScan ou exigir uma corrida
            // minima de tiles solidos alem do buraco antes de confiar nele.
            int left = CountSandRun(context, x, y, -1, maxScan);
            int right = CountSandRun(context, x, y, 1, maxScan);
            return Math.Min(left, right);
        }

        private static int CountSandRun(WorldGenContext context, int x, int y, int direction, int maxScan)
        {
            int distance = maxScan;
            for (int i = 1; i <= maxScan; i++)
            {
                int scanX = context.WorldMap.WrapTileX(x + (direction * i));
                if (context.WorldMap.GetTile(scanX, y) != TileType.Sand)
                {
                    distance = i - 1;
                    break;
                }
            }

            return distance;
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
