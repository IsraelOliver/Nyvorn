> **Continuação (24/09/2026):** o céu atrás do fundo do Shallow e o halo frio em volta das suas aberturas estão em [V9_SHALLOW_SKY_GLOW_RESULTADOS.md](V9_SHALLOW_SKY_GLOW_RESULTADOS.md). O que está abaixo continua valendo; o glow é um termo natural a mais, somado por máximo antes das tochas.

# Correção da luz natural do probe: exposição + aberturas (23/09/2026, entrega seguinte)

**Resultado.** A luz natural deixou de nascer de lâmpadas pontuais acima do chão. Agora ela entra por onde o mundo está exposto ao céu e pelas aberturas reais: bocas, fendas e aberturas laterais. A geometria continua dando a direção. As tochas não mudaram: o buffer só de tochas é idêntico bit a bit nos 22 estados, e os estados de caverna têm Energy e `_final.png` idênticos. O modelo antigo continua disponível só como diagnóstico, em `--v9-probe-sky-points`. Nada foi promovido e não há commit. Falta a avaliação visual do usuário.

Etiquetas: [FATO] código/dados; [MEDIÇÃO] número medido; [OBSERVAÇÃO VISUAL] o que vi nas imagens (a avaliação do usuário está pendente); [HIPÓTESE] explicação não provada.

## N1. Problema observado

- [OBSERVAÇÃO VISUAL, do usuário] No diagnóstico de irradiância, a luz natural nascia de pontos artificiais acima da superfície.
  - Cavernas e fendas só recebiam luz com linha de visada até esses pontos.
  - Regiões expostas ficavam escuras sem essa linha.
  - Apareciam feixes estranhos, tipo holofote.
- [MEDIÇÃO] Na captura `surface` do defeito (`final-capture/`), o topo da janela, com as copas, fica preto. O pico natural é 15,25, colado à linha de pontos. O corpo do jogador, 12 px acima do chão, recebe 32,18 (soma RGB), ou seja 14,97 no canal azul.

## N2. Causa (no código)

- [FATO] `V9ProbeSky.Collect` criava uma `V9Light` com os valores de céu do lab (1,4 / 196 px / RGB linear (0,46; 0,69; 1)) a cada 12 px, 20 px acima do envelope exterior `e(c)`.
  - Essas luzes entravam na mesma lista das tochas, como fontes comuns: 108–110 pontos por janela.
- [FATO] Consequências diretas dessa origem:
  1. **Alcance finito de 196 px.** Tudo a mais de ~176 px acima da linha de pontos ficava em zero exato (copas, céu alto).
  2. **Linha de visada até um ponto.** Dentro de uma abertura, só recebia luz quem enxergava algum ponto pelo DDA. Cada ponto projeta um cone pela abertura, e daí vêm os feixes discretos.
  3. **Falso "céu aberto".** A irradiância é alta junto à linha (~13,8 no chão, 15,25 de pico) e cai longe dela.
  4. **O fundo era ignorado.** [MEDIÇÃO] No modo legado, murar o fundo sobre a boca muda 0 de 465.920 texels (`entrance-bg-closed`).
- [FATO] As tochas não participam do defeito: o caminho delas é outro (seção N6).

## N3. Semântica real dos dados (investigada antes de implementar)

- [FATO] Ordem dos passes: ClearWorld, LayerBoundary, BiomeField, SurfaceProfile, BaseTerrainFill, DirtToStoneTransition, Cave, DesertCircle, CaveEntrance, Hydrology, DesertPixelSand, DesertCave, IronOreVein, Tissue, TreeGeneration, SurfaceDecoration, WorldBounds.
- [FATO] `BaseTerrainFillPass` preenche o fundo por camada:
  - Shallow: Dirt cheio;
  - Cavern e Deep: Stone cheio;
  - Space e Surface: nunca recebem fundo, nem acima nem abaixo do chão.
- [FATO] `CavePass.CarveBackgroundFissures` abre buracos de fundo (Empty) por ruído, só na Shallow.
- [FATO] `SurfaceBackgroundPass` existe, mas não está na lista de passes.
- [FATO] Não existe marcação de "exterior" nem do que se vê atrás de um buraco de fundo. `WorldMap.HasOpenSkyAbove`, usado pelo clima, é só "coluna aberta no foreground".
- [FATO] O jogador põe e tira fundo (`TryPlaceBackgroundTile` / `TryBreakBackgroundTile`), e isso incrementa `TileRevision`.
- [FATO] O V8 decide por convenção de camada que buraco de fundo na Shallow é céu (`SkyWeight` = 1, com fade só nos últimos 6 tiles). Isso não foi copiado.
- **Conclusão.**
  - Na superfície, "fundo vazio" não informa nada, porque ali nunca há fundo.
  - Na Shallow, não se sabe o que existe atrás de uma fissura.
  - Portanto **fundo vazio sozinho não é céu**.

## N4. Nova representação (uma abordagem)

`região exposta O → termo de céu aberto + aberturas na fronteira de O → DDA V9 intacto → soma natural → + tochas na ordem → receptor V9`

- **Região exposta O** [FATO, `V9ProbeExposure`]. É o lugar onde o céu entra. Uma célula está em O quando é:
  - ar (não sólida pela colisão);
  - **sem parede de fundo**;
  - acima do envelope exterior da coluna (`linha < e(c)`). O envelope é o mesmo de antes: abertura morfológica de 17 colunas do piso de céu. Agora ele serve de limite de região, não de linha de lâmpadas.
- **Papel do fundo e do foreground.**
  - Fundo fechado numa célula a tira de O: ela deixa de ser entrada de céu.
  - Fundo aberto permite O, desde que a célula esteja exposta pelo envelope (conectividade com o exterior).
  - O foreground molda tudo: define o envelope e oclui todo o transporte.
- **Termo de céu aberto** [FATO].
  - O ar em O recebe exatamente `E_aberto × RGB do céu`.
  - Um pixel sólido com vizinho-4 em O recebe essa luz pela **seção terminal V9 intacta**: `V9LightMath.Visibility` a partir do centro da célula exposta (profundidade ≤ 6 px, absorção 2,8 px, incidência). Vale o melhor vizinho.
  - Todo o resto recebe 0.
  - `E_aberto = 5,2806554` é a irradiância natural do próprio lab no plano da sua abertura, sob a amostra central: 5 amostras em (86+12i, 28), ponto (110, 48). **É a única constante nova.**
- **Aberturas (entradas)** [FATO].
  - Uma abertura é toda célula de O com vizinho-4 de ar fora de O: bocas, fendas, aberturas laterais, ar sob fundo fechado.
  - Cada célula vira uma `V9Light` branca no centro, com potência `1,4 × 8/12 = 0,9333` (a densidade linear de céu do lab) e raio 196.
  - É avaliada pelo `V9LightMath.Evaluate` intacto, só para receptores fora de O.
  - A soma das aberturas tem **teto `E_aberto`**: um interior nunca fica mais claro que o céu aberto.
- **Natural** = RGB do céu × max(céu aberto, min(Σ aberturas, `E_aberto`)).
- **Composição** [FATO]. `Energy` = natural; depois as tochas, na ordem da lista, do mesmo cache por fonte. Onde o natural é zero, a sequência de somas é a mesma de antes, bit a bit.
- **Caches** [FATO].
  - As aberturas ficam em cache por fonte, ancorado no mundo (escalar float no quadrado do raio).
  - Uma abertura é invalidada quando muda um sólido **ou a exposição** dentro do seu quadrado.
  - A máscara O é reconstruída a cada `TileRevision`, com diff.
  - O termo de céu aberto é recalculado só em 3×3 células em volta do que mudou, ou quando a janela é recolocada. Ele é decidido por célula, e só as faces sólidas vão pixel a pixel.
  - Wrap é relabel, sem recomputar.
- **Diagnóstico** [FATO].
  - `NaturalEnergy` e `ArtificialEnergy` são preenchidos sob demanda na captura, não por quadro.
  - `_natural-diagnostic.png`, `_artificial-diagnostic.png` e `_sky-entries.png` mostram onde o céu entra (legenda no `_metrics.json`).
  - As posições das aberturas ficam em `skyEntries` no `_metrics.json`.
- **Não reutilizado do V8** [FATO]: campo ambiente, transportes, curvas, parâmetros, divisão direta/local/céu, face lighting, tint e regras de fundo.
- **Reaproveitado do próprio probe** (infraestrutura neutra): envelope, ocupação, cache por fonte, janela, recenter e wrap.

## N5. Casos testados

Captura roteirizada, 22 estados: os 14 de antes, na mesma ordem, e mais 8. Resultado em `natural-exposure/capture/checks.txt`: **147 PASS, 0 FAIL, 1 SKIP**.

Em todos os estados, verificação bit a bit:

- `Energy` contra uma referência do zero (exposição, aberturas e céu aberto recalculados com a solidez do mundo, depois as tochas);
- os buffers natural e artificial;
- a máscara O e a lista de aberturas;
- os quadrados por fonte;
- a ocupação.

E ainda: todo ar exposto recebe exatamente o céu aberto, e nenhum texel fora de O passa do teto.

- **A — superfície aberta** (`surface`).
  - [MEDIÇÃO] Os 272.384 texels de ar exposto da janela valem exatamente `E_aberto`. O jogador recebe 11,35 (soma RGB, céu aberto); antes, 32,18.
  - [OBSERVAÇÃO VISUAL] Copas e troncos altos agora iluminados (antes pretos); nenhuma região exposta escura.
- **B — fenda vertical, as capturas problemáticas** (`entrance-rim/inside/deep`).
  - [MEDIÇÃO] 9 aberturas, exatamente na boca: faixa amarela em `_sky-entries.png`. Antes eram 108 pontos vermelhos.
  - [MEDIÇÃO] Energia média do ar do túnel por distância de caminho: **9,96 → 2,22 → 0,064**. Antes: 15,09 → 1,82 → 0,013.
  - [OBSERVAÇÃO VISUAL] O feixe mantém a direção inclinada dada pela geometria da fenda, agora nascendo da boca, sem a faixa de pontos acima.
- **C — caverna com fundo aberto.**
  - [FATO] O túnel da entrada está na camada Surface, sem parede de fundo.
  - [MEDIÇÃO] Ele recebe luz pelas aberturas da boca, sem linha reta até o topo do mundo. Jogador dentro: 3,01 (antes 3,22). Mais fundo (`entrance-deep`): 0,097 (antes 0,0086).
- **D — a mesma abertura fechada.**
  - [MEDIÇÃO] Vedada com 9 blocos (`entrance-sealed`): **0 de 19.456 pixels** do túnel com luz e 0 aberturas.
  - Reabrir (`entrance-reopened`) restaura bit a bit: 0 de 383.680 texels.
  - **Variante de fundo** (`entrance-bg-closed`): paredes de fundo nas 110 células expostas sobre a boca (uma "varanda" de 6 linhas, 6 colunas além de cada lado).
    - [MEDIÇÃO] Nenhuma dessas células continua exposta ou é abertura; houve 110 mudanças de exposição e 0 de foreground.
    - [MEDIÇÃO] A entrada migra para a borda da varanda (33 aberturas), e a luz natural no túnel cai **19%** (489,1 → 394,3, soma sobre 123 células).
    - Remover as paredes restaura bit a bit: 0 de 465.920.
    - [FATO] No modelo antigo, a mesma parede não muda nada.
- **E — sala selada.**
  - [MEDIÇÃO] Caverna fechada (`cave-dark`): 0 de 42.880 pixels e 0 texels naturais na janela inteira, sem piso.
  - [MEDIÇÃO] Abrigo construído e selado **sob céu aberto** (`shelter-sealed`): 0 de 3.072 pixels internos, enquanto o topo do teto recebe 6,61.
- **F — abertura lateral.**
  - [FATO] Não existe nenhuma no mundo pelo critério usado: célula exposta com vizinho lateral de ar não exposto sob chão da própria coluna, levando a ≥ 30 células, buscada no mundo inteiro.
  - Foi testada com um abrigo construído (`shelter-open`): teto de pedra a 5 linhas do chão sobre 14 colunas, parede no fundo e o lado esquerdo aberto.
  - [MEDIÇÃO] 4 aberturas, todas laterais. Natural médio de **6,43 → 3,99 → 2,04**, da boca para o fundo.
- **G — uma tocha em caverna fechada** (`cave-torch-1`) e **H — cinco tochas** (`cave-torch-2`, `cave-torch-5`, `cave-block-placed/removed`): seção N6.
- **Recenter perto de uma abertura** (`entrance-recentre-a/b/c`).
  - [MEDIÇÃO] Três recolocações com a câmera andando 240 px. A cada passo, 0 de 371.840 texels compartilhados diferem: tudo é definido em coordenadas de mundo.
- **Wrap** (`wrap-seam`, `wrap-seam-shifted`).
  - [MEDIÇÃO] Relabel: 0 aberturas recalculadas, exposição não reconstruída, 0 texels diferentes. O céu aberto é contínuo através de x=0.
- **Geometria.** Fechado → aberto → a luz entra; aberto → fechado → a luz some (`entrance-sealed/reopened`, `entrance-bg-closed/reopened`, `shelter-open/sealed`). Tudo em sessão transitória.

## N6. Controle negativo das tochas

- [MEDIÇÃO] Mesmo binário, com o modelo trocado por flag e captura determinística (`capture/` × `legacy-point-samples/`):
  - albedo pixel-idêntico nos 22 estados: a cena é a mesma;
  - **buffer só de tochas idêntico bit a bit (SHA-256 dos floats) nos 22 estados**;
  - nos 6 estados de caverna, **`Energy` total idêntico bit a bit e `_final.png` idêntico pixel a pixel**.
- [MEDIÇÃO] Contra `final-capture/`, que veio do binário anterior:
  - os 14 `_irradiance-diagnostic.png` são pixel-idênticos;
  - `_final` e `_albedo` diferem só em caixas de sprite animado: pose do jogador (46×48 px) e quadro da chama (12×14 px). O albedo sem luz difere nas mesmas caixas.
- [FATO] A causa era do harness. O número de updates entre capturas dependia do tempo real dos quadros pesados.
  - Correção: a captura agora roda **um update fixo por quadro desenhado** (`Game1`, `MaxElapsedTime = TargetElapsedTime`, só com `--v9-probe-capture`).
  - `iterations/legacy-run1-variable-step/` guarda a execução anterior ao ajuste.
  - [MEDIÇÃO] Duas execuções depois do ajuste deram os mesmos 228 PNGs e 57 hashes, byte a byte.
- **Não houve regressão nas tochas.**

## N7. Evidências

Tudo em `screenshots/v9-gameplay-probe/natural-exposure/`. `final-capture/` foi preservada intacta como base do defeito.

- `capture/` — modelo novo, 22 estados. Por estado: `_final`, `_albedo`, `_irradiance-diagnostic` (total), `_natural-diagnostic`, `_artificial-diagnostic`, `_sky-entries` e `_metrics.json` (aberturas, hashes, verificação e tempos).
- `legacy-point-samples/` — modelo antigo no mesmo binário, os mesmos 22 estados (82 PASS).
- `before-after/` — 68 composições lado a lado (antes | depois) de `final`, `irradiance-diagnostic`, `natural-diagnostic` e `sky-entries`, para:
  - superfície;
  - entrada (borda, dentro, fundo, vedada, reaberta, fundo fechado);
  - recenter a/b/c;
  - caverna escura;
  - tochas 1/2/5;
  - wrap;
  - abrigo aberto e selado.
- `bench/` e `bench-legacy-point-samples/` — bancada, 10 cenários cada.
- `regression-v7-gameplay/`, `regression-v8-gameplay/`, `regression-v8-direct/` e `regression-v8-ambient/`.
- `interactive-smoke/` — 25 s interativo, sem exceção.
- `code-snapshot/` e `code-changes-tracked-Game1.diff`.
- `iterations/` — execuções intermediárias preservadas.

## N8. Desempenho antes/depois

[MEDIÇÃO] CPU em ms (Stopwatch; GPU não medida), mesmo binário e mesmos cenários: `bench-legacy-point-samples/` × `bench/`. A atualização do campo é região + ocupação + exposição + coleta + aberturas + avaliação + céu aberto + recomposição + half/upload.

| Cenário | Campo, média antes → depois | Campo, máximo (recenter/troca) antes → depois | Natural depois (exposição / aberturas / avaliação / céu aberto), média |
|---|---|---|---|
| superfície parada | 0,014 → 0,002 | 0,017 → 0,004 | ~0 |
| superfície andando | 2,393 → **0,155** | 196,1 → **12,6** | 0,002 / 0,003 / 0,000 / 0,009 (máx. 1,12) |
| entrada parada | 0,014 → 0,002 | 0,131 → 0,003 | ~0 |
| entrada, câmera andando | 2,383 → **0,153** | 196,4 → **12,1** | 0,002 / 0,003 / 0,000 / 0,009 |
| céu abre/fecha a cada quadro (boca) | 593,0 → **31,6** | 598,1 → **65,9** | 0,34 / 0,25 / 25,9 / 0,03 |
| tile da superfície a cada quadro | 508,0 → **26,9** | 511,8 → **32,4** | 0,35 / 0,27 / 19,2 / 0,01 |
| caverna, 5 tochas paradas | 0,010 → 0,003 | 0,023 → 0,010 | ~0 |
| caverna, 5 tochas, andando | 0,105 → 0,135 | **7,65 → 10,7** | 0,001 / 0,001 / 0 / 0,005 (máx. 0,59) |
| tocha em movimento | **8,04 → 8,54** | 14,3 → 18,9 | ~0 (recomposição 0,26 → 0,81) |
| tile na caverna a cada quadro | **30,7 → 31,5** | 32,5 → 32,3 | 0,30 / 0,11 / 0 / 0,003 (recomposição 0,88 → 1,49) |

- Luzes artificiais (avaliação das tochas): iguais, 5,86 → 5,80 e 26,96 → 26,86 ms.
- Upload: praticamente igual nos casos de tocha (+0,02 a +0,05 ms). Menor nos casos de céu (7,5 → 4,0 e 5,0 → 3,2 ms), porque o retângulo sujo é menor.
- Draw V9 (CPU, mediana):
  - superfície andando: 1,10 → 1,11;
  - tocha em movimento: 9,12 → 9,50;
  - céu abre/fecha: 597 → 59;
  - superfície parada: 1,11 → 1,22. [HIPÓTESE] É ruído de submissão: o campo caiu, e o desenho da cena é o mesmo código.
- Custo que subiu, onde e por quê:
  - **+0,5 ms por quadro com tocha em movimento, +0,8 ms por troca de tile em caverna e +3 ms num recenter em caverna.**
  - A recomposição agora combina, por pixel, céu aberto e aberturas (`max`/`min`/escala) antes de somar as tochas, mesmo onde o natural é zero. E a troca de tile reconstrói a máscara O (0,3 ms).
  - Otimização possível e exata, não feita para não misturar trabalho: pular a combinação onde os dois termos são zero.
- Sem paralelismo e sem GPU compute.

## N9. Limitações

- [FATO] **Buracos de fundo subterrâneos não são céu.** Uma caverna na Shallow que só vê o exterior por uma fissura de fundo não recebe céu por ela. Recebe apenas pelas aberturas de foreground em até 196 px.
  - Informação futura necessária: o tipo de pano de fundo por célula (exterior × subterrâneo), ou uma regra explícita de design dizendo quais buracos de fundo enxergam o céu. Por exemplo, marcar isso no gerador (`CarveBackgroundFissures` / `SurfaceBackgroundPass`).
- [FATO] **Exposição decidida pelo envelope de 17 colunas.** Aberturas mais estreitas contam como interior (fenda); mais largas contam como exterior.
- [FATO] **Teto `E_aberto` com potência constante por célula de fronteira.**
  - Interiores rasos e muito abertos saturam no céu aberto. Exemplo: a varanda murada fica igual ao céu.
  - O número de aberturas cresce com o perímetro, o que suaviza o efeito de murar o fundo (−19% no túnel).
- [FATO] Sem rebatimento e sem GI. Um interior só recebe o que uma abertura enxerga em até 196 px; além disso fica preto, como antes.
- [FATO] **Apresentação inalterada.** O céu pintado continua visível até o piso de céu `s(c)`, então dentro da fenda aparece céu pintado mesmo onde a luz natural é fraca. Continua sem pano de fundo subterrâneo.
- [FATO] Árvores, plataformas, portas, líquidos e areia dinâmica continuam sem ocluir.
- [MEDIÇÃO] **Distribuição da luz na superfície.** O céu aberto é 5,28, contra ~13,8 no chão com a linha de pontos.
  - Luma média do quadro `surface`: 92,7 → 94,0.
  - 37.948 px ficaram mais claros (55 → 105: copas e troncos altos).
  - 30.692 px ficaram mais escuros (126 → 104: o que ficava colado à linha de pontos). O jogador foi de 162 → 135.
  - [HIPÓTESE] Pode pedir ajuste artístico de `E_aberto`. Não mexi: é decisão sua.
- [FATO] Não existe abertura lateral natural neste mundo (seed V8-GAMEPLAY-2026, Small). O caso F foi provado com construção.

## N10. Regressões

- [MEDIÇÃO] V7 smoke no gameplay: exit 0. `_light.png` é idêntico; `_final`/`_world` diferem só no texto de fps do HUD e num sprite animado de 16×16.
- [MEDIÇÃO] Captura de gameplay V8: **90/90**. Os PNGs que diferem da sessão anterior diferem no texto de tempos do HUD (ms) e em sprites animados; as verificações são todas PASS.
- [MEDIÇÃO] V8 direta **107/107**, com os 85 PNGs idênticos byte a byte. V8 ambiente **154/154**, com os 177 PNGs idênticos byte a byte.
- [MEDIÇÃO] `%LOCALAPPDATA%\Nyvorn`: os 7 arquivos têm o mesmo tamanho e o mesmo SHA-256 de antes das execuções.
- [MEDIÇÃO] Manifesto protegido: 196/197 intactos. Só `Program.cs` difere, e mudou na entrega anterior.
- [FATO] Não mudaram: `V9LightMath`, `V9LabSettings`, `V9LabReceiver.fx`, o V9-Lab, V7, V8, `V9ProbeOccupancy`, `V9ProbeSky`, assets, saves e o gameplay normal.

Arquivos desta entrega:

- **Novo:** `LightingV9Probe/V9ProbeExposure.cs`.
- **Reescrito:** `LightingV9Probe/V9ProbeField.cs` (os dois modelos naturais, buffers de diagnóstico e verificação).
- **Alterados:**
  - `LightingV9Probe/V9ProbeOptions.cs` (`--v9-probe-sky-points`);
  - `PlayingState.V9Probe.cs` (modelo e HUD);
  - `PlayingState.V9ProbeCapture.cs` (8 estados novos, diagnósticos, 3 cenários de bancada);
  - `Game1.cs` (3 linhas: um update por quadro só na captura do probe).

Comandos (a partir de `C:\dev\Nyvorn-Reborn`; o padrão é o modelo novo):

```powershell
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe                       # interativo
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-capture --v9-output <pasta>
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-bench --v9-output <pasta>
# modelo antigo, só diagnóstico: acrescente --v9-probe-sky-points
```

---

# V9 Gameplay Probe — primeira ponte para o mundo real, 23/09/2026

> Relatório da entrega anterior, mantido como histórico. A representação de céu da seção 2 (pontos acima do envelope) foi substituída como padrão pela seção de correção acima e só existe agora sob `--v9-probe-sky-points`.

**Resultado: promissor para a imagem, caro no céu.** A linguagem aprovada do V9-Lab sobrevive em terreno real sem nenhuma recalibração:

- massas negras com bordas expostas iluminadas;
- tochas quentes e localizadas, com cinco tochas mantendo cor e textura;
- personagem legível;
- luz fria entrando pela entrada natural e caindo com a profundidade;
- túnel vedado e caverna fechada exatamente pretos.

A arquitetura da ponte funciona e é exata: 42/42 verificações bit a bit contra a avaliação completa com a solidez do próprio mundo. O custo é baixo parado e ao andar em caverna. O que pesa é a representação de céu com a densidade do lab: recentrar na superfície custa ~200 ms, e alterar um tile na superfície custa ~0,5 s. Nada foi promovido; o probe é opt-in e não salva nada. Sem commit.

## 0. Consolidação do V9-Lab (item 1 da solicitação)

- `--lighting-v9-lab` agora usa **cache por fonte + máscara de ocupação**.
- `--v9-no-occupancy-mask` roda o cache sem máscara; `--v9-no-source-cache` roda a referência antiga (sem cache e sem máscara).
- `--v9-source-cache` e `--v9-occupancy-mask` continuam aceitos, agora redundantes. Combinações contraditórias são recusadas antes de abrir a janela.
- O verificador `--v9-cache-verify` compara referência × cache × cache+máscara por padrão, ou só dois caminhos com `--v9-no-occupancy-mask`.
- Verificação curta (`screenshots/v9-lab/lab-default-cache-mask/verification.txt`): novo padrão, cache sem máscara e referência deram, cada um, 26/26 capturas, os 152 PNGs de `final/` byte a byte e 28/28 testes CPU.
- Nada disso afeta o gameplay normal.

## 1. Arquitetura da janela local

`mundo → região local → ocupação → fontes relevantes → cache V9 → Energy → HalfVector4 → receptores`

- **Referencial.** Tudo no referencial da câmera, em px de mundo, com x contínuo; o mundo tem wrap horizontal de 22.400 px (2.800 tiles).
- **Janela.** Um texel por px de mundo, como o lab.
  - A 1280×720 com o zoom padrão do gameplay (2), a viewport é de 641×361 px. Somando margens de **128 px (x) e 96 px (y)**, o campo fica em **904×560** (506.240 texels), com origem alinhada a 8 px.
  - O campo só cresce: o zoom de interior do jogo (3) cabe sem realocar.
- **Coordenadas.** Texel = (x − origem.x, y − origem.y).
- **Recenter.** A câmera anda livre enquanto a viewport couber na janela: **~131 px na horizontal (≈16 tiles, ~1,45 s a 90 px/s) e ~99 px na vertical (≈12 tiles)**. Ao sair, a janela é recentrada na viewport.
- **Wrap.** Quando o sistema de wrap desloca jogador e câmera em ±22.400 px, a janela, a máscara e as chaves do cache são só relabeladas. Nada é recalculado (verificado: 0 fontes, 0 texels diferentes).
- **Cache por fonte ancorado no mundo.** Cada fonte guarda a contribuição do **quadrado do seu raio** (±R+1 px), calculada pelo `V9LightMath.Evaluate` intacto com uma única fonte. Fora desse quadrado, todo centro de pixel está a ≥ R+0,5 px, onde `Evaluate` já devolve zero exato. É o "bounding box por raio" pedido: o custo por fonte depende do raio, não da janela.
- **O que um recenter faz.**
  - Não invalida contribuições.
  - Recompõe a janela nova, calcula as fontes que entraram e descarta as que saíram.
  - Reconstrói a máscara de ocupação, que é barata (~0,2 ms).
- **Recomposição.** Parte de zero, somando as fontes na ordem da lista (céu por x, depois tochas por (y, x)), e só no retângulo sujo. A sequência de somas por pixel é a mesma da avaliação da lista inteira.
- **Upload.** Converte para HalfVector4 só o retângulo sujo e envia a textura inteira (`SetData`).
- **Ocupação local.**
  - Um `bool` por tile cobrindo a janela ± (2·196 + 16) px (217×174 tiles), que é tudo o que um raio de fonte relevante pode consultar. Consultas fora dela voltam ao mundo, exatas e contadas: foram **0** em todas as medições.
  - Sólido = colisão `Solid` do gameplay: terra, grama, pedra, areia, madeira, ferro. x com wrap; y fora do mapa vazio, como `WorldMap.GetTile`.
  - Refeita quando a janela se move ou `TileRevision` muda. Neste caso é comparada com a anterior, e só as fontes cujo quadrado contém uma célula alterada são invalidadas (conservador e exato).

## 2. Como o céu foi representado

É uma primeira representação explícita, não um sistema geral de céu.

- **Piso de céu `s(c)`.** A primeira linha sólida da coluna `c`, varrendo a partir do topo do mundo.
- **Envelope exterior `e(c)`.** A abertura morfológica de `s` numa janela de 17 colunas (máximo dos mínimos):
  - aberturas mais estreitas que isso ficam na altura da borda;
  - declives e vales largos mantêm a própria altura;
  - na geometria do lab, isso reproduz exatamente as amostras em y=28 sobre o teto em y=48.
- **Amostras.** Os valores do lab (1,4 / 196 px / RGB linear (0,46; 0,69; 1)) a cada **12 px** (o espaçamento do lab), **20 px acima de `e`** (a altura do lab sobre o teto). Ficam numa grade canônica do mundo, então não se movem com o wrap.
- **Oclusão.** Sem regra por camada e sem preenchimento: quem decide o que é iluminado é o DDA intacto.
- **Apresentação.** O céu desenhado (ElyraSky, e montanhas na camada Surface/Shallow) aparece só onde a coluna está aberta até o topo, descendo até `s(c)`. Abaixo disso é preto, sem parallax subterrâneo.
  - A primeira versão usava `e(c)` também aqui e criava uma "tampa" preta reta sobre a boca da entrada (`iterations/capture`); foi corrigido.
- **Sem dia/noite.** O relógio fica fixado ao meio-dia no probe.
- **Aberturas laterais.** São cobertas só pelo alcance do DDA a partir das amostras sobre o envelope; não há céu lateral dedicado (limitação).

Resultados:

- Na superfície, o personagem recebe energia 32,2 de 109 amostras.
- Na entrada, a energia média do ar do túnel por distância de caminho cai de **15,1 → 1,82 → 0,013** (0–6, 10–16 e 22–30 tiles).
- **Com a boca vedada por 9 blocos, 0 de 19.456 pixels do túnel recebem luz**, com 109 amostras logo acima. É a paridade com o `sky-closed` do lab.
- Reabrir restaura o campo bit a bit (0 de 383.680 texels).

## 3. Como as tochas reais foram coletadas

- **Origem.** `session.TorchRuntimeSystem.Torches[i].LightOrigin`, a origem de luz do próprio gameplay: (tx·8+4, ty·8+1). Coincide com o centro da chama nos postes 0/2 e fica a 1 px dele nos postes 1/3, na mesma célula do DDA.
- **Parâmetros.** Os do lab: raio 144, potência 4, RGB linear (1; 0,49; 0,19). Sem flicker; a animação do sprite da chama continua.
- **Relevância.** A imagem mais próxima da janela; entra se o quadrado do raio intersecta a janela.
- **Colocar, minerar ou mover** uma tocha pelo gameplay muda a lista: só essa fonte é calculada, e só o seu quadrado é recomposto.

## 4. O que foi renderizado pelo V9

O shader receptor não tem origem (`World` = posição do vértice) e não foi alterado. Cada classe de receptor é desenhada com albedo neutro num RT de tela. Depois ela é iluminada por **um quad de tela** cujos vértices estão em coordenadas locais da janela (`MatrixTransform = Translação(origem)·view·projeção`). A matemática por pixel é a do lab: despré-multiplica, decodifica sRGB, multiplica pela irradiância e pela resposta, aplica `L/(1+maxRGB(L))`, codifica e pré-multiplica.

| Ordem | Classe | Resposta |
|---|---|---|
| 1 | Céu + montanhas (só no exterior), sem luz | — |
| 2 | Árvores (camada de trás) + cogumelos de superfície | 1 |
| 3 | Paredes de fundo (`DrawBackground(..., Color.White)`, sem o tint 104/255) | **0,14** |
| 4 | Árvores da frente, água, terreno (cache de chunks, neutro), portas, plataformas, móveis | 1 |
| 5 | Entidades: itens, postes das tochas, jogador (amostrador neutro = branco), tecido | 1 |
| 6 | Chama das tochas: pixels originais, sem luz, por último (ordem do lab) | — |
| 7 | Overlay de tile e HUD, fora da iluminação e fora das capturas | — |

V6, V7 e V8 não calculam nem compõem o quadro do probe: o ramo do `Draw` retorna antes deles. F4 e as teclas de depuração V7 ficam desativadas no probe.

## 5. O que ficou de fora

- **Oclusão:** plataformas (tile `Platform` e objetos), portas, árvores, areia dinâmica e líquidos não bloqueiam. A água é desenhada só como receptor comum.
- **Fontes:** só tochas; os cogumelos, que são fonte no V8, são receptores comuns.
- **Cena:** parallax subterrâneo, chuva e clima, overlays noturnos e de umidade, sol direcional, ciclo dia/noite, bloom, atmosfera e penumbra.
- **Não implementados:** multithreading, compute, chunks finais e cache para centenas de fontes.
- **Sem save de configuração;** o V9 não é o padrão do jogo.

## 6. Capturas

Pasta `screenshots/v9-gameplay-probe/final-capture/`, com 14 estados, cada um com `_final`, `_albedo`, `_irradiance-diagnostic` (visualização `EncodeSrgb(ToneMap(E))`) e `_metrics.json` (posição, câmera, janela, fontes, tempos, verificação). São **55/55 checagens** em `checks.txt`.

| Estado | Mostra |
|---|---|
| `surface` | Superfície aberta ao meio-dia, com o personagem |
| `entrance-rim` | Personagem na borda da entrada natural; o céu entra no funil |
| `entrance-inside`, `entrance-deep` | Dentro do túnel: a luz fria cai até praticamente zero |
| `entrance-sealed`, `entrance-reopened` | Boca vedada (túnel preto) e reaberta (campo idêntico) |
| `cave-dark` | Caverna fechada da camada Shallow, sem tocha: 0 de 42.880 px com luz |
| `cave-torch-1/2/5` | Uma, duas e cinco tochas; pico de energia 3,96 / 5,81 / 13,55 |
| `cave-block-placed`, `cave-block-removed` | Bloco ao lado da tocha põe o personagem na sombra (1 fonte recalculada); removê-lo restaura o campo bit a bit |
| `wrap-seam`, `wrap-seam-shifted` | Janela atravessando a costura do mundo; salto de um mundo inteiro = relabel |

Cenas procedurais, sem fixtures:

- **Entrada:** a coluna cujo piso de céu cai mais abaixo do envelope (coluna 1432, 14 tiles, a 32 colunas do spawn).
- **Caverna fechada:** o bolsão de ar mais raso perto do spawn cujo preenchimento 4-conexo nunca toca o exterior (670 células; o topo fica 53 tiles abaixo do exterior, portanto fora do alcance do céu).
- **Construções roteirizadas, todas documentadas:** os 9 blocos da vedação, o bloco do teste F e as tochas colocadas em pisos existentes.

## 7. Desempenho

Tempos de CPU, não de GPU. Release, GTX 750, 1280×720, zoom 2, campo de 904×560, VSync e passo fixo desligados. São 20 quadros de aquecimento e 240 medidos (30 no caso de tile na superfície), com foco em 100% das amostras (`final-bench/benchmark.json`).

| Caso | Desenho V9 mediana / P95 / máx | Campo mediana | Detalhe |
|---|---:|---:|---|
| Superfície parada (109 amostras de céu) | 1,23 / 1,62 / 2,2 | 0,020 | nada recalculado |
| Superfície andando 1,5 px/quadro | 1,11 / 1,56 / **203** | 0,020 | 3 recenters: **185–203 ms**, 10–11 amostras novas cada |
| Caverna, 5 tochas paradas | 1,33 / 1,73 / 2,0 | 0,018 | — |
| Caverna andando, 5 tochas | 1,34 / 1,74 / 8,8 | 0,018 | 3 recenters de ~7 ms (0 fontes novas) |
| Uma tocha movida a cada quadro | **10,2** / 13,7 / 14,8 | 8,73 | Evaluate 6,24 + recomposição 0,32 + half/upload 2,10 |
| Tile alternado junto a 5 tochas | **37,1** / 39,7 / 47,3 | 31,8 | 5 fontes/quadro; Evaluate 27,9 |
| Tile alternado na superfície | **529** / 539 / 552 | 527 | **33 amostras de céu/quadro**; Evaluate 499, recomposição 20, upload 7,8 |

- **Etapas quando nada muda:** região ~0,001 ms, ocupação 0, coleta ~0,015 ms. Janela parada ou andando dentro da margem não reconstrói nada.
- **Primeira construção (captura):**
  - superfície a frio, 109 fontes: 1.865 ms;
  - mover a vista até a borda (21 amostras novas): 459 ms;
  - vedar ou reabrir a boca (39 amostras): 626 / 783 ms;
  - caverna a frio: 1 tocha 10,6 ms, 5 tochas 23,6 ms;
  - ~15–17 ms por amostra de céu (raio 196) e ~6 ms por tocha (raio 144).
- **Memória:**
  - contribuições em cache: **194 MB na superfície** (109 × 1,78 MB) e 4,8 MB na caverna com 5 tochas;
  - campo: 6,1 MB de Energy, 4,1 MB de HalfVector4 na CPU e 4,1 MB de textura HalfVector4;
  - três RTs de tela Color (~11 MB) e máscara de ~38 KB.

## 8. Problemas encontrados

1. **Custo do céu na densidade do lab.** Cada amostra custa ~15 ms e 1,78 MB, e há ~110 amostras numa janela de superfície. O resultado são os soluços de ~200 ms ao andar na superfície, ~0,5 s por tile alterado na superfície e 194 MB. As tochas são baratas.
2. **Copas altas escuras.** As amostras ficam 20 px acima do chão com raio 196, então a luz do céu termina ~200 px acima do chão, e as copas e o alto dos troncos escurecem contra o céu claro (`surface_final.png`). É consequência direta dos valores do lab; não houve recalibração.
3. **Cavernas reais quase sem parede de fundo.** A geração só cria parede de fundo, em manchas, na camada rasa. Por isso o túnel da entrada é quase todo preto, com bordas finas iluminadas. Onde há parede de fundo (a caverna testada, camada Shallow), o visual fica praticamente o do lab.
4. **Céu visível dentro do funil.** O ar exposto verticalmente mostra céu e parallax até `s(c)`, como V7/V8 mostram na superfície. No lab havia parede de fundo ali.
5. **Caverna fechada fora do alcance do céu.** A caverna fechada perto do spawn está 53 tiles abaixo do exterior, então o teste "fechada no alcance do céu" foi feito vedando a boca da entrada.
6. **Zoom e janela.** O gameplay usa zoom 2 (3 em interiores), não o 2,5 do lab. O probe usa a câmera real, e os estados roteirizados fixam zoom 2. Por isso a janela real é de 904×560, maior que o plano inicial de 768×480 (que supunha zoom 2,5).
7. **Composição por classe.** Dentro de uma mesma classe, sobreposições semitransparentes são misturadas antes da luz (o lab ilumina cada sprite). A matemática por pixel é idêntica.
8. **Guardas de gravação.** Havia gravações sem guarda no caminho transitório (tecla W do menu de pausa, `/world save`, "tentar de novo"). No probe elas foram bloqueadas, e W encerra o processo em vez de abrir a lista de mundos. Os modos transitórios V8 continuam como estavam; está sinalizado à parte.

## 9. Diferenças visuais em relação ao V9-Lab

As diferenças vêm de geometria, assets ou decisões explícitas do probe, não de parâmetros:

- **Superfície aberta:** no lab não havia superfície; o céu era só amostras sobre a abertura. No probe, a superfície recebe as amostras ao longo de todo o exterior e fica bem mais clara que qualquer região do lab (32,2 no personagem, contra pico de 17,9 com 5 tochas no lab), mas sem estourar para branco.
- **Cavernas sem parede de fundo:** a maior parte das cavernas não tem pano de fundo (asset/geração), o que deixa o fundo preto.
- **Apresentação do céu:** segue o piso de céu real em vez de uma faixa marcada.
- **Zoom:** 2 (gameplay) em vez de 2,5.

## 10. Recomendação

**Continuar.** Dos critérios de sucesso, 1–6 foram atendidos:

- identidade visual preservada;
- transição natural pela entrada;
- fechado escuro;
- tochas quentes e localizadas;
- cinco tochas sem destruir cor ou textura;
- personagem legível.

O critério 7 foi atendido: a câmera anda sem reconstrução, e o recenter reaproveita o cache. O critério 8 vale para tochas e cavernas, mas **não ainda para o céu na superfície**.

O próximo passo técnico deveria atacar só o custo das amostras de céu, sem mudar a imagem. Por exemplo, calcular com antecedência, em lotes por quadro, as amostras que vão entrar antes do recenter, ou paralelizar por fonte, já que as fontes são independentes. Qualquer mudança de densidade ou altura das amostras é uma decisão artística, sua.

## Execução, controles e evidências

A partir de `C:\dev\Nyvorn-Reborn`:

```powershell
dotnet build .\Nyvorn\Nyvorn.csproj -c Release --no-restore
# Interativo (sessão transitória, seed V8-GAMEPLAY-2026, Small; nada é salvo)
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe
# Evidência roteirizada (14 estados) e bancada (7 cenários); sempre com pasta nova
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-capture --v9-output <pasta>
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-bench --v9-output <pasta>
```

Controles e ajustes do modo interativo:

- Os controles normais do jogo.
- **F5** superfície (spawn), **F6** borda da entrada, **F7** caverna fechada, **F12** captura manual.
- Esc abre a pausa; ali **W encerra o probe** sem salvar.
- A hotbar começa com 99 tochas, 1 picareta de ferro e 99 blocos de pedra, só nesta sessão transitória. `/get` também funciona.
- O HUD mostra janela, fontes, recenters e os tempos de cada etapa.

Preservação:

- `%LOCALAPPDATA%\Nyvorn` não recebeu nenhum arquivo do probe; os únicos alterados hoje são das 11:07, anteriores ao trabalho.
- V7 smoke no gameplay: exit 0. Captura de gameplay V8: **90/90**. Diagnósticos V8: direta **107/107**, ambiente **154/154**.
- Manifesto protegido: 196/197 intactos; só `Program.cs` mudou, por design, com a linha de entrada do probe.

Evidências em `screenshots/v9-gameplay-probe/`:

- `final-capture/` e `final-bench/`;
- `regression-v7-gameplay/`, `regression-v8-gameplay/`, `regression-v8-direct/` e `regression-v8-ambient/`;
- `interactive-smoke/` (25 s com câmera real, sem exceção);
- `code-changes.diff`;
- `iterations/`: execuções intermediárias preservadas, incluindo a primeira com a "tampa" do céu.

Código:

- **Novos (não rastreados):** `Nyvorn/Source/Engine/Graphics/LightingV9Probe/` (`V9ProbeOptions`, `V9ProbeOccupancy`, `V9ProbeSky`, `V9ProbeField`), `PlayingState.V9Probe.cs` e `PlayingState.V9ProbeCapture.cs`.
- **Rastreados alterados, só ramos ativos com a flag do probe:** `Program.cs`, `Game1.cs`, `PlayingState.cs`, `PlayingState.LightingToggle.cs` e `PauseMenuState.cs`.
- **V9-Lab:** a inversão da flag da máscara em `V9LabGame.cs` e um comentário em `V9LabGame.SourceCacheChecks.cs`.
- **Não alterados:** `V9LightMath`, `V9LabSettings`, o shader, V7 e V8.
