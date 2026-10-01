# V9 Gameplay Probe — resposta do foreground ao céu e às aberturas, 24/09/2026

**Resultado.**

- O foreground já respondia à luz natural pela seção terminal do V9, mas só vindo da região exposta O e das aberturas da borda de O.
- A lacuna era que as células que o jogo agora mostra como céu não alimentavam essa resposta: as aberturas de fundo do Shallow e o ar sem fundo da Surface.
- A correção só faz essas células alimentarem o **mesmo** termo de céu aberto, pela **mesma** seção terminal (≤ 6 px, absorção 2,8 px, incidência), juntando por máximo. Não há glow no foreground nem propagação dentro da rocha.
- As faces de rocha junto ao céu ganham uma borda clara de 0–5 px e a massa continua preta.
- Tochas, glow do fundo e albedo ficaram bit a bit iguais, e o final só muda onde a resposta nova cai.
- **Conflito que precisa de decisão:** na camada Surface todo ar conta como céu visível (a regra da máscara), então paredes de túneis selados na Surface também acendem.
- Sem commit, sem promoção.

Etiquetas: [FATO] código/dados; [MEDIÇÃO] número medido; [OBSERVAÇÃO VISUAL] o que vi nas imagens (a sua avaliação está pendente).

## 1. Como o foreground natural já funcionava

- [FATO] **`V9LightMath.Visibility` com receptor sólido** (intacto): o DDA vai da fonte até o pixel.
  - Na primeira entrada em sólido, se o receptor é sólido, vale a seção terminal: profundidade ≤ 6 px (`SurfaceDepthPixels`), atenuação `exp(−d/2,8)` e incidência `0,25 + 0,75·|cos|`.
  - Se o raio atravessa sólido e volta ao ar, dá 0.
- [FATO] **Termo de céu aberto** (`DirectAt`): um pixel sólido cujo tile tem vizinho-4 em O recebe `céu aberto × Visibility(centro do vizinho → pixel)`. É o que acende o topo do terreno e as faces voltadas para o exterior.
- [FATO] **Aberturas da borda de O:** luzes brancas de 0,933 com raio 196, avaliadas pelo mesmo `Evaluate`. Acendem faces que enxergam a boca em linha reta, como o alto da fenda da entrada.
- [FATO] **Glow do fundo:** nunca toca o foreground, por construção.

## 2. Lacuna encontrada

- [FATO] As aberturas de fundo do Shallow (`SkyShallow`) só alimentavam o glow; nenhuma face sólida em volta delas recebia céu.
- [FATO] O ar sem fundo da Surface (`OpenAtmosphere`, que a máscara corrigida mostra como céu) não era fonte natural. Por isso a parte inclinada da fenda da entrada tinha céu pintado atrás e paredes pretas.
- [FATO] Não havia duplicação a evitar: o termo novo entra no mesmo máximo do termo de céu aberto. Nunca é somado a uma luz que já chega.

## 3. Reutilização

- [FATO] **Sim.** Mesma `DirectAt` e mesma `V9LightMath.Visibility` a partir do centro do tile vizinho. A classificação é a mesma que mostra o céu e semeia o glow (a do glow, `V9ProbeSkyGlow`); não há terceiro detector de abertura.
- [FATO] A única mudança no termo é aceitar, além de O, um vizinho de céu visível com peso:
  - abertura do Shallow: o peso dela, com o fade do fim da camada;
  - `OpenAtmosphere` no Space/Surface: 1.
- [FATO] **Ordem no quadro:** o glow (com sua classificação) passou a ser construído antes do termo direto. Quando a classificação muda (por exemplo, um fundo colocado ou quebrado), o termo é recalculado em 3×3 tiles em volta das células que mudaram.
- [FATO] `--v9-probe-no-foreground-sky` desliga só isso, e aí o termo volta a ser exatamente o anterior.

## 4. Regra final

Um pixel sólido recebe `céu aberto × peso × Visibility(centro do vizinho → pixel)`, pelo melhor vizinho-4 que seja céu:

- O: peso 1, como antes;
- abertura de fundo do Shallow: peso da abertura;
- `OpenAtmosphere` no Space/Surface: 1.

Cavern e Deep não entram. A cor é o mesmo RGB frio do céu natural, sem potência ou saturação nova. A geometria decide:

- só as faces que tocam o céu, e cada face pelo seu lado;
- paredes dos dois lados de uma fenda acendem, cada uma na face voltada para ela;
- tetos acendem se o céu está embaixo deles;
- o interior da rocha não recebe nada.

## 5. Profundidade efetiva

[MEDIÇÃO] Máximo de 5,5 px (centro do pixel) da face, isto é, 6 linhas de pixels, em todos os estados. A contagem cai a cada pixel, e nada acontece a 6 px ou mais.

| Estado | Texels sólidos novos | Histograma de profundidade (0,1,2,3,4,5 px) |
|---|---|---|
| fenda da entrada (`entrance-rim`) | 4.419 | 949, 864, 779, 694, 609, 524 |
| fenda do Shallow (`shallow-fissure`) | 6.288 | 1.248, 1.168, 1.088, 1.008, 928, 848 |
| abertura estreita na parede (`shallow-room-edge-small`) | 3.480 na janela, 96 na sala | 730, 670, 610, 550, 490, 430 |
| abertura larga na parede e no teto (`shallow-room-edge-wide`) | 3.816 na janela, 432 na sala | 786, 726, 666, 606, 546, 486 |

A abertura larga acende mais borda (432 contra 96 texels na sala) com a mesma profundidade (5,5 px).

## 6. Casos testados

Captura: 37 estados, **370 PASS, 0 FAIL, 1 SKIP** (a abertura lateral natural, que não existe). Sem a resposta: 368 PASS.

Em todo estado vale a verificação bit a bit do campo contra uma referência do zero, que inclui o termo novo; a profundidade máxima fica ≤ 6 px.

- **A — superfície** (`surface`).
  - [MEDIÇÃO] O topo continua igual (vizinho O).
  - [MEDIÇÃO] 3.807 texels novos, todos nas paredes da fenda da entrada, que aparece na vista.
- **B — fenda inclinada da entrada** (`entrance-*`).
  - [MEDIÇÃO] Texels sólidos com luz natural: 6.383 → 9.712.
  - [OBSERVAÇÃO VISUAL] As duas paredes ganham uma borda fina e clara ao longo de toda a inclinação; a massa segue preta. Ver `compare/B_entrance-rim_crop.png`.
- **C — abertura estreita** (1×2 tiles encostada na parede da sala selada).
  - [MEDIÇÃO] 96 texels da parede; 5,5 px.
- **D — abertura larga** (6×3 tiles na parede e no teto).
  - [MEDIÇÃO] 432 texels; mesma profundidade.
- **Fenda natural do Shallow.**
  - [MEDIÇÃO] Texels sólidos com luz natural: 0 → 6.288.
  - [OBSERVAÇÃO VISUAL] Bordas finas em volta dos buracos de céu.
- **E — sala selada** (Shallow, fundo cheio).
  - [MEDIÇÃO] 0 texels de parede com luz do céu; 0 de luz na sala.
- **F — Cavern/Deep** (buraco de fundo em caverna da Cavern).
  - [MEDIÇÃO] 0 texels sólidos em volta com luz do céu; continua coberto.
- **G — cinco tochas** (`cave-torch-5`, `shallow-room-torch-5`).
  - [MEDIÇÃO] Buffer só de tochas idêntico.
  - Na caverna antiga, que tem fissuras de fundo, o final muda só nas bordas da rocha junto às fissuras.

## 7. Verificação (com × sem a resposta, mesmo binário)

- [MEDIÇÃO] Buffer só de tochas: SHA-256 idêntico nos 37 estados.
- [MEDIÇÃO] Glow do fundo: SHA-256 idêntico nos 37 estados.
- [MEDIÇÃO] Albedo: idêntico em todos os estados. O único trecho fora da comparação é a caixa do personagem, cujo lado segue o cursor real do mouse durante a execução.
- [MEDIÇÃO] Final: **todo pixel que mudou cai em um texel iluminado pela resposta nova. 0 fora dela em todos os estados.**
- [MEDIÇÃO] Sem céu visível na janela (`wrap-seam`, `cavern-bg-hole`), a Energy é idêntica.

## 8. Desempenho (CPU, com × sem; só o custo adicional)

- [MEDIÇÃO] **Termo direto (onde a resposta vive):**
  - no recenter, o pior quadro vai de 0,4–0,75 ms para 1,1–1,4 ms (+0,6 a 0,9 ms);
  - por troca de tile de fundo ou foreground, a média vai de ~0,003 para ~0,02 ms.
- [MEDIÇÃO] Preparação das aberturas: a mesma classificação do glow, sem custo novo.
- [MEDIÇÃO] Recomposição e upload: sem mudança fora do ruído.
- [MEDIÇÃO] A média do campo e o Draw ficam dentro do ruído do mesmo cenário em duas execuções.
- Não otimizei nada. Pastas: `bench/` e `bench-no-fg-sky/`.

## 9. Limitações e conflito

- [MEDIÇÃO] **Paredes de ar selado na Surface acendem.**
  - Túnel da entrada vedado (`entrance-sealed`): +6.108 texels.
  - Abrigo selado (`shelter-sealed`): +4.802.
  - Motivo: na Surface não existe parede de fundo no mundo, então todo ar é `OpenAtmosphere` "válido" pela regra da máscara, conectado ao exterior ou não.
  - É o mesmo caso incorreto já documentado para a máscara (céu pintado dentro de ar selado), agora também na luz das paredes.
  - O ar desses lugares continua em zero; só as faces sólidas acendem.
- [FATO] **O que resolve os dois (máscara e resposta):** aceitar o `OpenAtmosphere` da Surface só se ele estiver ligado pelo ar ao exterior, ou seja, conectividade. Não implementei.
  - Sem a parte da Surface, a fenda da entrada (caso B) não ganha nada. Ela fica toda na Surface.
- [FATO] A resposta é só pela face adjacente ao céu (vizinho-4). Uma face a um tile de distância do buraco não recebe desta resposta; recebe apenas da luz natural existente, se enxergar uma abertura de O.
- [OBSERVAÇÃO VISUAL] A borda iluminada fica em tons de rocha (a cor fria sobre textura quente).

## 10. Recomendação

**Continuar**, com uma decisão sua antes.

- A resposta faz exatamente `céu → borda iluminada → poucos pixels → preto`, reaproveitando o que o V9 já tinha, e não mexe em tochas nem no glow.
- A decisão é sobre a Surface:
  - aceitar que ar selado nela seja tratado como céu, na máscara e agora também nas paredes;
  - ou autorizar a conectividade (o ar da Surface só é céu se ligado ao exterior). Isso resolveria os dois casos de uma vez, sem mudar o resto.

Execução (a partir de `C:\dev\Nyvorn-Reborn`):

```powershell
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe                                  # resposta ligada
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-no-foreground-sky     # sem ela
```

**Arquivos:**

- `LightingV9Probe/V9ProbeField.cs`: vizinho de céu visível no `DirectAt`, reordenação, verificação e diagnóstico.
- `LightingV9Probe/V9ProbeSkyGlow.cs`: diff de classes.
- `LightingV9Probe/V9ProbeOptions.cs`: a flag.
- `PlayingState.V9Probe.cs`: configuração e HUD.
- `PlayingState.V9ProbeCapture.cs` e `PlayingState.V9ProbeShallowGlow.cs`: diagnóstico `_fg-sky-response.png`, métricas, 2 estados novos e checagens.

**Evidências:** `screenshots/v9-gameplay-probe/foreground-sky/`: `capture/`, `capture-no-fg-sky/`, `bench*/` e `compare/`.
