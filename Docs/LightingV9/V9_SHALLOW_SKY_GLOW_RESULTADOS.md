# V9 Gameplay Probe — Sky Glow no Background do Shallow, 24/09/2026

**Resultado.** A regra simples funciona como pedida: `céu → background adjacente → glow curto`.

- Uma abertura no fundo do Shallow agora mostra o céu.
- A parede de fundo em volta recebe um halo que morre em poucos tiles.
- O foreground continua preto e a sala selada continua preta.
- O halo segue a forma da abertura: pequena fica localizada, larga fica larga.
- As tochas são bit a bit as mesmas.
- A superfície não mudou em um único pixel.

Pontos para a sua avaliação visual:

1. O nível do halo. Ele é visível mas discreto e amarronzado, porque a parede Dirt é quente e o fundo responde a 0,14. Há capturas com pico 1×, 2× e 4×.
2. A caverna "fechada" antiga do teste de tochas agora brilha, porque 15% do fundo dela são fissuras abertas ao céu.

O sol está implementado como modulação opcional e sutil. Nada foi promovido e não há commit.

Etiquetas: [FATO] código/dados; [MEDIÇÃO] número medido; [OBSERVAÇÃO VISUAL] o que vi nas imagens (a sua avaliação está pendente); [HIPÓTESE] explicação não provada.

## 1. Classificação encontrada

- [FATO] **Onde:** `Nyvorn/Source/Engine/Graphics/LightingPipeline/SceneWorldClassifier.cs` e `LightingCellClassification.cs`.
  - Vêm do Lighting V3 fase 2: commits ecb74b7 e 1d09013 ("Implement VisibleBackground with real data"), de 31/07/2026.
- [FATO] **Debug visual:** `LightingDebugVisualization.RenderClassification`, em `LightingPipeline/DebugVisualization.cs`, modo `Classification`.
  - Cores: `OpenAtmosphere` = azul, `VisibleBackground` = verde, `SolidForeground` = vermelho, todas a 50%.
  - O arquivo foi apagado em cc0f24d (05/08/2026, "background placeholders and sky light blocking"), junto com o `LightingV3Foundation`.
- [FATO] **Regra**, por tile, local, sem profundidade, camada ou conectividade:
  - foreground sólido → `SolidForeground`;
  - foreground vazio com parede de fundo → `VisibleBackground`;
  - os dois vazios → `OpenAtmosphere` ("aberto para a atmosfera/céu").
  - No V3, o foreground era `WorldMap.IsSolidAt`, que conta plataforma, e o fundo era `IsBackgroundSolidAt`.
- [FATO] **Estado atual:**
  - O classificador ainda é usado por `OccluderField`, `ForegroundTileOccluderProvider` e `SunVisibilityRayMarcher` (infraestrutura V5/V6).
  - Não existe mais implementação de `ILightingWorldGeometryProvider` ligada ao `WorldMap`, só o `MockGeometryProvider`.
  - Ou seja, estava órfão do mundo real.

## 2. Foi reutilizada?

**Sim.**

- [FATO] O probe chama o próprio `SceneWorldClassifier.ClassifyTile` por tile, através de um adaptador novo e mínimo, `V9ProbeGeometryAdapter`:
  - foreground = a ocupação aprovada do probe (colisão `Solid`). Plataforma não conta, como em todo o probe; é o único desvio em relação ao V3;
  - fundo = qualquer tile de fundo não vazio.
- [FATO] O diagnóstico `_sky-classes.png` usa as cores do debug antigo (azul, verde, vermelho), mais preto para "vazio" e amarelo para as sementes.
- [FATO] **Por que só reutilizar não bastava.** A classificação local está correta, mas "`OpenAtmosphere` = céu" só vale no Shallow:
  - a camada Surface nunca tem fundo, então todo ar subterrâneo dela (o túnel da entrada, por exemplo) viraria céu;
  - Cavern e Deep têm Stone cheio, e um buraco feito pelo jogador ali viraria céu.
- **Menor extensão feita:** o `OpenAtmosphere` foi dividido em três:
  - **céu exterior:** a região exposta O do modelo de exposição, peso 1;
  - **céu do Shallow:** abertura de fundo dentro do Shallow, peso 1 com fade no fim da camada;
  - **vazio:** o resto, que não é céu nem recebe glow.
  - Não foi preciso flood-fill nem conectividade nova.

## 3. Definição real de Sky / BG / FG

- [FATO] **Worldgen:**
  - `BaseTerrainFillPass` dá fundo Dirt cheio ao Shallow e Stone cheio a Cavern e Deep. Space e Surface ficam sem fundo.
  - `CavePass.CarveBackgroundFissures` abre fissuras de fundo no Shallow inteiro: listras verticais (ruído com frequência 0,15 em x e 0,02 em y, limiar 0,35), independentes do foreground.
  - As cavernas de foreground são cavadas do topo do Shallow para baixo, com fade de 30 linhas no topo.
- [FATO] **Classes do probe**, por tile, nesta ordem:
  - `Foreground`: `SolidForeground`;
  - `Background`: `VisibleBackground`;
  - `SkyExterior`: `OpenAtmosphere` exposto (O);
  - `SkyShallow`: `OpenAtmosphere` no Shallow com peso > 0;
  - `Void`: o resto.
- [FATO] **O fundo continua receptor e não bloqueia as tochas**; nada disso mudou. Ele ganhou semântica só para o céu:
  - parede presente separa aquele tile do céu;
  - a transição céu → fundo gera semente.

## 4. Definição do Shallow

- [FATO] `LayerBoundaryPass`, por fração fixa da altura do mundo.
  - No Small (2800×900) o Shallow ocupa as **linhas 199..270**; a Surface vai de 109 a 198 e a Cavern de 271 a 765.
- [FATO] **Peso de céu de uma abertura no Shallow:** `clamp((EndY + 1 − y) / ShallowFadeTiles, 0, 1)`.
  - Vale 1 até 8 linhas do fim. Depois cai 0,875 → 0,125 na última linha. Abaixo é 0.
  - Não há fade no topo, porque acima dele (Surface) não existe fundo.
- [FATO] **O vazamento para baixo do limite** vem só da propagação das últimas aberturas, até o alcance. Não existe fonte de céu abaixo do Shallow.

## 5. Arquitetura implementada

```
SceneWorldClassifier (V3, reutilizado) + região exposta O + peso do Shallow
        ↓
classes por tile: FG / BG / céu exterior / céu do Shallow / vazio
        ↓
sementes = tiles BG que tocam céu por um lado
        ↓
distância geodésica em pixels (chanfro 5/7), só por pixels de BG, até o alcance
        ↓
glow = peso × (1 − d/R)² × pico × céu aberto × RGB frio do céu   (+ sol opcional)
        ↓
natural = máximo(natural V9 existente, glow)   ← mesma composição
        ↓
+ tochas na ordem (intactas) → half → shader V9 intacto
```

- [FATO] **Código novo:**
  - `LightingV9Probe/V9ProbeSkyGlow.cs`: classes, sementes, propagação, valor e sol;
  - `V9ProbeGeometryAdapter`, no mesmo arquivo.
- [FATO] **Integração no `V9ProbeField`:**
  - É um termo natural a mais. Junta-se ao natural por **máximo**, como o natural já junta céu aberto e aberturas, e nunca empilha sobre luz que já chega.
  - Onde o glow é zero, o natural fica intocado: bit a bit (seção 11).
  - As tochas continuam somadas depois, do mesmo cache. Não há compositor novo.
- [FATO] **Quando recalcula:**
  - classificação e propagação quando a exposição é reconstruída (mudança de geometria ou do retângulo), em nova colocação da janela, ou quando mudam os parâmetros;
  - uma mudança só de sol reavalia os valores, sem repropagar;
  - wrap é relabel, sem recalcular;
  - só recompõe os texels cujo glow mudou.
- [FATO] **Onde o glow vale:**
  - pixels de BG, até o alcance;
  - o próprio buraco do Shallow, com o peso, para iluminar quem passa na frente dele;
  - nunca no FG, nunca no céu exterior (que já tem céu aberto), nunca no vazio.
- [FATO] **Apresentação:** a máscara preta abaixo do piso de céu agora abre nas células de céu do Shallow, com alfa `1 − peso`. O céu pintado aparece através dos buracos e escurece junto com o fade. É o céu que o jogo já desenha, inclusive as montanhas e colinas do parallax.
- [FATO] **Verificação em todo estado:** o glow é recalculado do zero com os dados do mundo (classificação, sementes e propagação) e comparado bit a bit. O natural e a Energy também são comparados bit a bit contra a referência que inclui o glow.

## 6. Parâmetros (provisórios, nomeados; valores finais dependem da sua comparação)

| Parâmetro | Valor | Comparado com |
|---|---|---|
| `ReachPixels` (alcance do glow no fundo) | 40 px (5 tiles) | 24 e 64 px |
| `PeakScale` (glow na borda do céu, × céu aberto 5,28) | 1 | 2 e 4 |
| `ShallowFadeTiles` (fade no fim do Shallow) | 8 | — |
| Cor | RGB linear do céu do lab (0,46; 0,69; 1) | — |
| Sol: `SunIntensity` / `SunWarmth` | ±25% / até 30% da cor do sol | com e sem |
| Sol: direção e cor | `ComputeSunDirectionRadians` / `ComputeSunColor` do próprio jogo (cor decodificada para linear, máximo 1) | 09:36 e 14:24 |

Flags do probe:

- o glow vem ligado por padrão no modelo de exposição;
- `--v9-probe-no-sky-glow` desliga só a luz, mantendo a classificação e o céu nos buracos;
- `--v9-probe-glow-sun` liga a modulação solar;
- nenhuma das duas é aceita com `--v9-probe-sky-points`.

## 7. Casos testados

Captura roteirizada com 33 estados: os 22 anteriores, na mesma ordem, e mais 11. Resultado: `shallow-sky-glow/capture/checks.txt` com **306 PASS, 0 FAIL, 1 SKIP** (a abertura lateral natural, que não existe neste mundo). A linha de base `capture-no-glow/` teve 296 PASS e 0 FAIL.

- **A — superfície normal** (`surface`, `entrance-*`, `shelter-*`, `wrap-*`).
  - [MEDIÇÃO] Energy bit a bit igual à da entrega anterior. `_final.png` **pixel a pixel igual** nos 16 estados sem Shallow à vista.
  - Na superfície não há parede de fundo, logo não há semente.
- **B — fenda vertical no Shallow** (`shallow-fissure`; vista achada procuradamente na coluna 898, linha 233).
  - [MEDIÇÃO] 126 sementes. Glow médio no fundo de **9,21 → 2,99 → 0,18** a 0–8, 16–24 e 32–40 px da borda; zero além do alcance.
  - [MEDIÇÃO] 72,6% dos texels de fundo da vista recebem algo, porque as fissuras do Shallow são frequentes. Nenhum texel de FG recebe glow.
  - [OBSERVAÇÃO VISUAL] O céu aparece pelas fissuras; a rocha em volta ganha um halo curto; as massas de foreground continuam pretas.
- **C — abertura larga** (`shallow-room-wide-hole`: 8×4 tiles construídos no fundo da sala selada).
  - [MEDIÇÃO] 24 sementes e 9.011 texels de fundo iluminados, contra 6 e 6.078 na pequena.
  - [OBSERVAÇÃO VISUAL] O halo segue o retângulo (bordas arredondadas); não há círculos individuais.
- **D — abertura pequena** (`shallow-room-small-hole`: 1×2 tiles).
  - [MEDIÇÃO] 6 sementes; o halo vai até 39,8 px do céu.
  - [OBSERVAÇÃO VISUAL] O halo é localizado e arredondado: é a distância a partir de um buraco pequeno, não um ponto de luz.
- **E — caverna fechada** (`shallow-room-closed`: caixa 20×14 de solo sólido no Shallow, interior 18×12 cavado e fundo preenchido com Dirt).
  - [MEDIÇÃO] **0 de 13.824 texels** com glow ou qualquer luz.
- **F — várias tochas em caverna** (`shallow-room-torch-5`: 5 tochas na sala selada).
  - [MEDIÇÃO] Natural 0; Energy igual à soma só das tochas em 13.824 de 13.824 texels.
  - [MEDIÇÃO] O `_final` com glow difere do sem glow só numa caixa à direita: outra fissura natural, fora da sala.
- **Limite inferior do Shallow** (`shallow-bottom`: vista natural na coluna 1914).
  - [MEDIÇÃO] Glow médio de 3,07 a 10–14 linhas acima do fim, 1,26 nas últimas 3 linhas e 0,34 nas 3 abaixo; 0 além do alcance.
  - [MEDIÇÃO] Perfil por linha: 3,84 (263), 3,62, 3,41, 2,77, 1,81, 1,74, 1,29, **0,74 (270, última)**, **0,55 (271)**, 0,32, 0,13, 0,02, 0 (275).
  - Não há degrau na luz.
  - [OBSERVAÇÃO VISUAL] Aparece uma faixa cinza-azulada logo abaixo do limite. É o fundo mudando de Dirt para Stone no worldgen (dado do mundo), visível porque o vazamento a ilumina e a pedra é mais clara.
- **Caverna fechada antiga** (a dos testes de tocha; selada no foreground).
  - [FATO] 102 de 670 células dela não têm parede de fundo: são fissuras do Shallow.
  - [MEDIÇÃO] Com glow, 33.993 de 42.880 pixels são iluminados, **todos pelo glow do fundo**; nada entra pelo foreground.
  - Pela regra pedida, ela é aberta ao céu pelo fundo e brilha.
  - [OBSERVAÇÃO VISUAL] A luz quente das tochas é a mesma; o frio aparece perto das fissuras.
- **Alcance** (`reach-24` / `reach-64`).
  - [MEDIÇÃO] 17.546 / 28.765 texels de fundo iluminados, contra 23.839 com 40 px.
- **Pico** (`peak-2` / `peak-4`).
  - [MEDIÇÃO] Mesma extensão (23.839 texels). Texel de fundo mais claro: 21,6 / 43,2 (soma RGB).
  - [OBSERVAÇÃO VISUAL] Com 2× e 4× a textura da rocha em volta aparece mais, mas continua marrom-acinzentada, não azul. A Dirt é quente, o fundo responde a 0,14 e o tone map comprime.
- **Recenter e wrap:** os estados `entrance-recentre-a/b/c` e `wrap-seam(-shifted)` continuam exatos. O glow é ancorado no mundo e verificado do zero em cada estado.

## 8. Capturas

Tudo em `screenshots/v9-gameplay-probe/shallow-sky-glow/`:

- `capture/` — glow ligado (padrão), 33 estados. Por estado:
  - `_final`, `_albedo`, `_irradiance-diagnostic`, `_natural-diagnostic` e `_artificial-diagnostic`;
  - `_sky-classes`: classes e sementes, nas cores do debug antigo;
  - `_sky-glow`: até onde o glow foi;
  - `_sky-entries` e `_metrics.json`.
- `capture-no-glow/` — a mesma captura com `--v9-probe-no-sky-glow`.
- `compare/` — composições lado a lado:
  - fenda sem × com glow;
  - classes × glow;
  - alcance 24 × 64;
  - pico 1 × 2 e 2 × 4;
  - sol manhã × tarde (final e mapa);
  - limite inferior;
  - sala selada e com tochas;
  - tochas com × sem glow;
  - buraco pequeno × largo (final, mapa e classes);
  - caverna antiga e tochas antigas, com × sem glow.
- `bench/` e `bench-no-glow/`.
- Regressões V7/V8, `iterations/` (a primeira execução, antes do ajuste da checagem da caverna antiga) e `code-snapshot/`.

## 9. Desempenho

[MEDIÇÃO] CPU em ms (GPU não medida), com e sem glow no mesmo binário (`bench-no-glow/` × `bench/`). O glow se divide em classificação+sementes, propagação e valor (inclui o sol).

| Cenário | Campo, média sem → com | Máximo sem → com | Glow por evento (classificação / propagação / valor) |
|---|---|---|---|
| fenda parada | 0,003 → 0,003 | 0,013 → 0,012 | 0 (em cache) |
| andando na fenda (recenter) | 0,123 → 0,160 | 10,0 → 12,8 | 0,12 / ~2,0 / ~1,4 por recenter |
| fundo trocado a cada quadro | 2,13 → 6,13 | 2,7 → 7,0 | 0,10 / 1,79 / 2,37 |
| foreground trocado a cada quadro | 3,37 → 6,14 | 4,1 → 6,7 | 0,10 / 1,79 / 2,39 |
| sol mudando a cada quadro | 1,38 → 9,95 | 1,7 → 10,8 | só valor 2,75, mais recomposição 2,05 e upload 5,14 da área que muda |
| caverna antiga andando | 0,137 → 0,188 | 10,9 → 15,1 | até 4,8 por recenter |
| tile trocado na caverna antiga | 34,4 → 37,3 | 38,5 → 39,7 | 5,2 por troca |
| superfície andando / parada | 0,160 → 0,172 / 0,003 | 13,4 → 14,6 | ~0 (sem fundo) |
| tile trocado na superfície | 30,9 → 29,5 | 33,9 → 32,2 | valor 2,3 por troca, mesmo sem semente |

- Draw V9 (CPU, mediana): fenda com fundo trocando 6,2 → 10,2; sol mudando 2,4 → 11,1. Parado e andando ficaram no ruído (±0,3).
- Sem paralelismo e sem GPU compute. Nada foi otimizado.
- Pontos óbvios e exatos para depois:
  1. pular o passo de valor quando não há sementes nem aberturas (hoje custa ~2,3 ms em qualquer troca de geometria);
  2. limitar o passo de valor à área alcançada;
  3. o upload continua sendo da textura inteira sempre que algo muda, como antes.

## 10. Com e sem sol

- [FATO] **Regra.** Para cada texel de fundo, `facing = dot(direção até a abertura mais próxima, direção do sol)`.
  - O glow é multiplicado por `1 + 0,25 × facing` e ganha até `0,30 × max(facing, 0)` da cor do sol do jogo.
  - Só modula o glow existente: não cria outra luz, e o relógio do mundo continua ao meio-dia.
  - Nas capturas, só a modulação recebe a hora (09:36 e 14:24).
- [MEDIÇÃO] **09:36** (sol vindo da esquerda, direção (−0,81, −0,59)): 11.375 texels mais claros e mais quentes (R/B ×1,58), 12.065 mais escuros; razão de 0,75 a 1,17 em relação ao neutro.
- [MEDIÇÃO] **14:24** (sol vindo da direita): 12.188 mais claros (R/B ×1,54), 11.004 mais escuros; razão de 0,75 a 1,15.
- [OBSERVAÇÃO VISUAL] O lado da rocha voltado para o sol através da abertura fica levemente mais claro e quente. De manhã é o lado direito dos buracos, à tarde o esquerdo. Não domina.
  - [HIPÓTESE] Ao meio-dia fixo do probe, o sol vem de cima e realça o fundo abaixo dos buracos.

## 11. Não regressão das tochas

- [MEDIÇÃO] O buffer só de tochas (hash SHA-256 dos floats) é **idêntico nos 33 estados** com e sem glow, e nos 22 estados comparáveis com a entrega anterior.
- [MEDIÇÃO] Sem glow, a Energy é **idêntica à da entrega anterior nos 22 estados**. A classificação nova e a apresentação não tocam o campo; a única mudança de luz é o glow.
- [MEDIÇÃO] Com glow, a Energy também é idêntica onde não há glow na janela: 16 estados. Isso inclui `entrance-bg-closed`, onde o glow existe mas não passa do céu aberto que já chega ali; a combinação por máximo o absorve.
- [MEDIÇÃO] Sala selada com 5 tochas: Energy igual à soma só das tochas em todos os texels da sala.
- [MEDIÇÃO] `_albedo` idêntico com e sem glow em todos os estados.
- [FATO] Não mudaram: `V9LightMath`, DDA, raio, potência e cor das tochas, cache por fonte, máscara de ocupação, tone map, shader, assets, saves, V7 e V8.
  - V7 smoke ok. V8 gameplay 90/90. V8 direta 107/107, com os 85 PNGs idênticos. V8 ambiente 154/154, com os 177 PNGs idênticos.
  - `%LOCALAPPDATA%\Nyvorn` com o mesmo SHA-256 nos 7 arquivos.

## 12. Limitações

- [FATO] **"Buraco de fundo no Shallow = céu"** é a regra de design que você deu, e só vale no Shallow. Fora dele, fundo vazio é vazio. Os buracos cavados pelo jogador no Shallow também viram céu.
- [MEDIÇÃO] **As fissuras são frequentes.** Com 40 px, 72,6% do fundo visível da fenda recebe algum glow. A caverna não fica "majoritariamente escura" em área, embora fique em intensidade. Reduzir o alcance para 24 px dá 53%.
- [OBSERVAÇÃO VISUAL] **O halo é discreto e pouco azul.** A cor fria é neutralizada pelo albedo quente da Dirt e pela resposta 0,14 do fundo. Mudar pico ou cor é decisão artística sua.
- [FATO] **A propagação usa distância geodésica por chanfro.** As isolinhas são octogonais (erro < 2%), e contornar uma quina mede o caminho, não a linha reta. Não é física de luz.
- [FATO] **O céu pintado através dos buracos** inclui montanhas e colinas do parallax (apresentação já existente). Na camada Surface, o ar subterrâneo continua preto, sem céu.
- [OBSERVAÇÃO VISUAL] **No limite inferior** a faixa visível vem da troca de material do fundo (Dirt → Stone) no worldgen, não da luz.
- [FATO] **Custo por mudança de geometria:** +4,3 ms no Shallow e +2,3 ms em qualquer lugar, pelo passo de valor. Sol animado: ~10 ms por quadro de campo.

## 13. Recomendação

**Continuar.** A leitura `céu → fundo adjacente → glow curto` aparece sem arquitetura nova e sem mexer nas tochas.

Antes de avançar, peço duas decisões visuais suas:

1. **Nível e cor do halo.** Compare pico 1×, 2× e 4× (`compare/shallow-fissure-peak-*`). Se quiser o halo mais azul, a cor do glow é o parâmetro.
2. **Alcance.** Compare 24 e 64 px, sabendo quantas fissuras o Shallow tem.

A modulação do sol é opcional e sutil. Eu a manteria desligada por padrão até o glow estar aprovado.

Depois disso, o próximo passo técnico seriam as duas otimizações exatas da seção 9.

## Execução

A partir de `C:\dev\Nyvorn-Reborn`:

```powershell
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe                              # glow ligado
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-glow-sun          # + sol (meio-dia)
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-no-sky-glow       # sem glow
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-capture --v9-output <pasta>
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-bench --v9-output <pasta>
```

O HUD mostra o glow: estado, alcance, pico, fade, sementes, texels iluminados e o tempo de cada etapa.

**Arquivos desta entrega:**

- **Novos:** `LightingV9Probe/V9ProbeSkyGlow.cs` e `Game/States/PlayingState.V9ProbeShallowGlow.cs`.
- **Alterados:**
  - `LightingV9Probe/V9ProbeField.cs` (termo de glow, estatísticas, verificação);
  - `LightingV9Probe/V9ProbeOptions.cs` (duas flags);
  - `PlayingState.V9Probe.cs` (Shallow, configuração, sol do jogo, máscara do céu, HUD);
  - `PlayingState.V9ProbeCapture.cs` (ganchos, 11 estados, checagem da caverna antiga, mapas, bancada).
- **Nenhum arquivo rastreado pelo git mudou nesta entrega.**
