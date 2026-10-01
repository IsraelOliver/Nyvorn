# V9 Gameplay Probe — a faixa preta na fenda e a correção da máscara do céu, 24/09/2026

**Resultado.**

- A faixa preta era só a cobertura de apresentação do probe; a iluminação não participava.
- A correção autorizada troca a regra "primeiro sólido da coluna → tudo abaixo preto" pela classificação existente (`SceneWorldClassifier`). A faixa some na fenda inclinada.
- A iluminação saiu idêntica bit a bit: 136 hashes de floats e 204 PNGs de diagnóstico.
- O albedo só mudou onde a cobertura antiga escondia o céu: 0 violações nos 33 estados.
- A regra simples tem um caso concreto incorreto, registrado abaixo: **ar selado na camada Surface agora mostra o céu pintado**. Não adicionei conectividade.
- Sem commit, sem promoção.

Etiquetas: [FATO] código/dados; [MEDIÇÃO] número medido; [OBSERVAÇÃO VISUAL] o que vi nas imagens (a sua avaliação está pendente).

## 1. Causa (investigação)

- [FATO] **Onde:** `Nyvorn/Source/Game/States/PlayingState.V9Probe.cs`, método `DrawV9Scene`, bloco de apresentação do céu.
- [FATO] **O que fazia:**
  - Para cada coluna visível, um retângulo `consolePixel` (textura branca 1×1) tingido de `Color.Black`, do primeiro bloco sólido da coluna (`SkyFloors`, varrendo do topo do mundo) até o fim da tela.
  - É um preenchimento sólido, não uma textura ou asset.
  - Existe desde a primeira entrega do probe, para não pintar céu em cavernas.
- [FATO] **Ordem de desenho:**
  1. limpa de preto;
  2. céu pintado (`DrawSky`) e montanhas do parallax;
  3. **cobertura preta**;
  4. receptores (árvores, paredes de fundo, terreno, entidades) pelo shader V9;
  5. chamas.
  - No ar da fenda não há parede de fundo (a Surface não tem fundo no mundo), terreno nem entidade. A cobertura era a última a escrever.
- [MEDIÇÃO] **Pixel de prova:** tela (672, 420), mundo (11476, 1230), tile (1434, 153).
  - Foreground e fundo vazios, classe vazio, camada Surface.
  - O primeiro sólido da coluna está na linha 146 (a beirada da margem direita).
  - Com a cobertura: final e albedo (0,0,0). Com a cobertura desligada (`--v9-probe-no-sky-mask`): (106,123,156), o céu pintado.
- [FATO] **Por que a forma.** A regra é por coluna. Em uma fenda inclinada, ou embaixo de qualquer tile no alto da coluna, todo o ar abaixo fica preto, mesmo aberto para o lado.

## 2. Correção aplicada (só apresentação)

A cobertura agora é decidida por tile, pela classificação do V3 já usada no probe:

| Classe do tile (abaixo do primeiro sólido da coluna) | Cobertura |
|---|---|
| `SolidForeground` (terreno) | preta, como antes; o terreno é desenhado por cima |
| `VisibleBackground` (parede de fundo) | preta, como antes; o fundo é desenhado por cima |
| `OpenAtmosphere` nas camadas Space/Surface | **nenhuma: céu e parallax visíveis** |
| `OpenAtmosphere` nas aberturas do Shallow | alfa `1 − peso` (inalterado desde a entrega do glow) |
| `OpenAtmosphere` em Cavern/Deep | preta (não liberado) |

- [FATO] **O Space foi incluído junto com a Surface.** É o próprio céu acima dela; excluí-lo cobriria de preto o céu acima do chão.
- [FATO] **A cobertura só encolhe.** Acima do primeiro sólido de cada coluna nada mudou, então nenhum pixel que antes mostrava o céu passa a ser coberto.
- [FATO] Não mudaram: glow, luz natural, tochas, `V9LightMath`, shader e parâmetros.

## 3. Validação A/B

A = cobertura antiga; C = corrigida; B = sem cobertura nenhuma (diagnóstico). Pastas em `screenshots/v9-gameplay-probe/black-band-investigation/`.

- [MEDIÇÃO] **Iluminação:** 136 hashes (Energy, natural, tochas e glow, por estado) e 204 PNGs de diagnóstico (irradiância, natural, artificial, glow, classes, entradas) **idênticos** entre A e C.
- [MEDIÇÃO] **Albedo e final:** todo pixel que mudou de A para C era preto em A e agora é exatamente o pixel de B, ou seja, o céu pintado que ele escondia. **0 violações em todos os estados.**
  - A única diferença fora disso é o sprite do personagem, espelhado porque segue o cursor real do mouse durante a execução. Ele ficou fora da comparação; as linhas de base sem cobertura de dois binários diferentes são idênticas pixel a pixel.

| Caso | Estado | Resultado |
|---|---|---|
| Fenda inclinada da investigação | `entrance-rim` (e `entrance-*`) | faixa preta some; 23.680 px (22–41 mil nos estados da entrada) passam a mostrar céu/parallax |
| Superfície normal | `surface`, `wrap-*` | 17.280 px revelados na `surface` (a fenda à direita da vista); `wrap-*` sem mudança |
| Abertura de fundo no Shallow | `shallow-fissure*`, `shallow-bottom` | 0 px mudaram |
| Sala selada (Shallow, fundo cheio) | `shallow-room-closed` | a sala não muda; mudam 1.024 px só nas linhas da Surface no alto da vista |
| Cavern/Deep | `cavern-bg-hole` (novo: fundo removido de um bloco 3×3 de caverna na Cavern) | continua coberto: 0 de 2.304 px de albedo não pretos; sem cobertura, ali apareceria céu |
| Região com tochas | `cave-torch-1/2/5`, `shallow-room-torch-5` | 0 px mudaram nas cavernas; na sala, só as linhas da Surface |

A captura corrigida teve 315 verificações aprovadas, 0 falhas e 1 pulada (a abertura lateral natural, que não existe neste mundo).

## 4. Caso incorreto da regra simples (documentado; conectividade não adicionada)

- [OBSERVAÇÃO VISUAL] **Ar selado na camada Surface mostra o céu pintado.** A camada Surface não tem parede de fundo no mundo, então todo ar dela é `OpenAtmosphere`, conectado ou não ao exterior.
  - `shelter-sealed`: o abrigo construído e fechado por todos os lados mostra céu e montanhas por dentro (antes, preto).
  - `entrance-sealed`: o túnel da entrada vedado com pedra mostra céu e colinas até o fundo. O personagem aparece como silhueta, porque a luz ali continua zero.
- [FATO] **A luz continua certa.** A iluminação desses ambientes não mudou: o selado segue com zero de luz natural. O que ficou errado é só o fundo pintado.
- **O que resolveria (não aplicado, aguarda decisão):** liberar `OpenAtmosphere` da Surface só quando o tile estiver ligado pelo ar (sem parede de fundo) ao céu aberto, ou seja, conectividade. A alternativa sem conectividade seria aceitar esse caso.
- [OBSERVAÇÃO VISUAL] **O céu pintado inclui colinas verdes do parallax.** Numa fenda funda elas parecem "grama" no fundo. É a arte já existente do jogo.

## 5. Estado das flags de diagnóstico

`--v9-probe-no-sky-mask` e `--v9-probe-pixel-report "x,y;…"` continuam no código, desligadas por padrão, até você encerrar a comparação.
