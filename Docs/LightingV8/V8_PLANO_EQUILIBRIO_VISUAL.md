# V8 — Plano de equilíbrio visual (luz natural, faces, background, tochas)

## Revisão 2 — plano enxuto (vigente)

**Status (2026-09-15):** planejamento, nada implementado, sem commits. Esta revisão substitui a ordem e a proposta da revisão 1 (mais abaixo, mantida só como referência de leitura do código).

**Princípio:** um eixo por vez, primeiro com parâmetros e mudanças pequenas. Arquitetura só muda se uma captura mostrar que o ajuste simples não basta.

**Prints:** os prints comparados pelo usuário não estavam disponíveis nesta sessão. A leitura visual abaixo segue a descrição do usuário e as capturas existentes em `screenshots/v8/`.

### 1. Diagnóstico das causas

**[F]** fato do código ou de captura medida · **[H]** hipótese · **[A]** decisão artística

**Superfície escura**
- **[F] Metade do brilho da V6.**
  - V8: receptores e faces recebem céu = `AmbientLight × 0,5` (`V8AmbientContext.SkyIntensity`).
  - V6: o ar aberto recebe `AmbientLight × 1` (`ComputeSurfacePrototype`), e personagem e grama usam amostrador neutro, ficando ≈ textura original.
- **[F] Faixa rasa no terreno.**
  - V6: pesos por tile 1 / 0,65 / 0,35 / 0,15 com upscale bilinear, ≈ 3,5 tiles até o preto.
  - V8: faixa do céu de 12 px, depois núcleo preto.
- **[F] Céu de fundo diferente.**
  - V6/V7 (`DrawAtmosphericBackground`): `DrawSky` com `LinearClamp`, `DrawSunGlow` e `DrawMoons`.
  - V8 (`DrawV8Gameplay`): `DrawSky` com `PointClamp` e montanhas, sem brilho do sol nem luas.
- **[H]** O "céu frio e forte" percebido na V6 vem do mundo 2× mais claro somado ao brilho do sol. Não foi medido.
- **[F] Defeito da V6 a não copiar:** árvores e cogumelos recebem `AmbientLight` duas vezes (tint no worldRT e luz no composite).

**Caverna preenchida demais**
- **[F] Albedo do background:** V8 1,0 (`DrawBackground(…, Color.White)`) contra V6/V7 0,41 (`BackgroundTileTint` 104).
- **[F] Alcance:** V8 linear de 24 tiles (`SkyRangeTiles`) contra V6 −0,10 por tile, zero em 9 tiles.
- **[F] O pico não é a causa.** Na abertura, V8 0,50 contra V6 ≈ 0,37. A 6 tiles, 0,375 contra 0,12. Aos 9 tiles, a V6 já zerou.
- **[F] Captura com a mesma câmera** (`toggle-faceband/00–01`, com tocha, só qualitativa): background junto às fissuras escuro na V7 e claro em área extensa na V8.

**Tocha forte**
- **[F] Direta:** raio de 280 px (35 tiles) com `(1 − d/R)²`, contra 13 tiles na V7.
- **[F] Medição em `cavern-torch`** (background, final ÷ albedo):

  | Distância | 8 tiles | 12 tiles | 20 tiles |
  |---|---|---|---|
  | final ÷ albedo | 0,77 | 0,58 | 0,26 |

  Na parte iluminada, a direta dá ~75% e o local ~25%. Na sombra só há o local: 0,20.
- **[F] Local:** 0,24 × radiância em 256 px (32 tiles), somado. O albedo 1,0 amplifica as duas partes.
- **[H] Calor da V7:** vem de overbright ×2, halo aditivo de 3 tiles e fundo escurecido, que concentram o laranja. A V8 satura em 1.

**Relação dia × tocha**
- **[F] Combinação:**
  - V8 soma direta + céu + local (`V8Receiver.fx`, `V8Faces.fx`);
  - V6 usa soma limitada `c + a(1 − c)` (`ApplyBoundedAdd`): a tocha some onde já está claro;
  - V7 usa máximo.
- **[F] V7 com sol e sem sol:** céu ×0,6 com sol, ×1,0 com F7. O F7 não é subtração do sol.

**Aberturas pequenas**
- **[F] Métrica:** V6 e V8 propagam com 4 vizinhos e perda linear, o que dá isolinhas Manhattan (losango).
- **[F] V7:** também 4 vizinhos, mas multiplicativa, com semente Shallow 0,5 e albedo 0,41, então a luz quase some.
- **[H] Losango mais visível na V6:** alcance curto linear, bilinear por tile e o buraco com céu integral somam para marcar o formato.

### 2. Ordem de implementação

Cada passo é avaliado com capturas e F4 antes do seguinte. Luz natural primeiro, tochas depois, composição por último.

| Passo | Eixo | Mudança mínima | Não muda |
|---|---|---|---|
| 1 | Brilho natural dos receptores | Escala do céu separada: **background** e **demais** (entidades, decorações, faces). `DrawV8Receivers` já desenha o background no mesmo lote das árvores: separar em lote próprio e passar outro `AmbientSkyColor`. Faces recebem a escala "demais" em `Render`. Demais → ≈ 1,0 (V6); background fica em 0,5. **[A]** Noite: escala do dia × `lerp` por `WorldNightStrength`, para manter a noite atual até decisão. | Cavernas, tochas, composição |
| 2 | Faixa de 3 tiles | `SkyFaceDepth` 12 → 24 (as margens já seguem `FaceReach`); curva calibrada pela tabela da V6. | Luz pelo ar, tochas |
| 3 | Caverna | **Corrigido pelo usuário:** `SkyRangeTiles` global fica em 24, porque transporta céu para todos os receptores. Primeiro, remapear a exposição existente **só no receptor do background**: `corte = 1 − R_bg/SkyRangeTiles`, `e_bg = saturate((e − corte)/(1 − corte))`, opcionalmente `pow(e_bg, γ)`, com intensidade própria do background. Controla intensidade, alcance visual e concentração sem mexer em Dijkstra, semente, faces ou entidades. Só reduzir `SkyRangeTiles` se testes mostrarem transporte longo demais também para outros receptores. | Tochas, composição, transporte |
| 4 | Aberturas pequenas | Só depois do passo 3. Se o losango ainda incomodar: 8 vizinhos com custo √2 e bloqueio de quina em `Propagate`. Não somar células. | O resto |
| 5a | Tocha: direta | Raio 280 → **112–144 px** (14–18 tiles), curva mantida. Cor (1; 0,65; 0,28) já ≈ V7. Intensidade só depois de ver o raio. | Luz natural (congelada) |
| 5b | Tocha: local | 0,24 / 256 px → **0,10–0,15 / 96–128 px** | Luz natural, direta |
| 6 | Dia × tocha | `L = S + T·(1 − saturate(S))` em `V8Receiver.fx` e `V8Faces.fx`, com S = céu e T = direta + local | Parâmetros já aprovados |

**Faixas de valores:** são pontos de partida, não valores finais. O valor sai de captura mais aprovação visual.

**Riscos conhecidos e escalonamento só com evidência:**
- **Passo 1 (brilho):** subir a escala sobe a noite na mesma proporção. Por isso a escala noturna vai separada desde o início.
- **Passo 2 (degrau em paredes finas):** com a regra da face mais próxima, paredes e tetos de 2 a 5 tiles com um lado escuro podem mostrar degrau na linha média.
  1. Primeira reação, se aparecer: multiplicar o peso do céu por uma transição pela distância da segunda face, calculada na CPU em `TryFace`. Não muda vértice nem shader.
  2. Só se isso criar faixa escura em blocos iluminados dos dois lados: duas amostras por pixel, que é a mudança maior.
- **Passo 2 (custo):** a busca vai até 24 px. Medir a construção de faces.
- **Passo 3:** alcance menor reduz a margem da grade, com custo igual ou menor.

### 3. Testes visuais por passo

Ferramentas existentes:
- `--lighting-sky-compare`: V7 sem sol / V8 / V7 com sol; exterior de dia e noite, abertura pequena e grande, sala selada, Shallow→Cavern.
- `--lighting-v8 --v8-gameplay-capture`: superfície, Shallow, Cavern, Deep, tochas, paredes, portas, câmera, zoom, wrap e areia.
- `--v8-scene --v8-ambient --v8-capture`.
- `--lighting-toggle-bench`.
- `--v8-perf-bench`.
- F4 no jogo, com a mesma câmera em V7 e V8.
- Scripts `faceband.ps1` e `torchprofile.ps1`, que só leem PNGs.

**Invariantes em todo passo:**
- céu zero em Cavern/Deep;
- sala selada preta;
- portas e sólidos bloqueando;
- faces, direta e local ancoradas no mundo sob câmera, zoom e wrap;
- F4 57/0;
- suítes sem FAIL.

| Passo | O que olhar | Critério |
|---|---|---|
| 1 | Exterior de dia e de noite, caverna | Árvore, grama e personagem com final ÷ albedo ≈ 0,9–1,0 ao meio-dia. Noite visualmente igual à atual. `cavern-*`, `deep-*` e `shallow-sealed` idênticos (diferença 0). |
| 2 | Perfil por profundidade no terreno; teto de 1 tile; tetos de 2–5 tiles; plataformas | ≈ 0,6 a 1 tile, ≈ 0,3 a 2, ≈ 0 a 3. Lado escuro do teto fino = 0. Sem degrau visível. |
| 3 | Perfil do túnel no sky-compare, `shallow-day`, câmara | Legível na abertura, ≤ ~35% na metade do alcance, zero no alcance. Restante da câmara escuro. |
| 4 | Aberturas 2×2 e 6×6 | Presença sutil na pequena, sem losango perceptível |
| 5 | Perfil de `cavern-torch`, `many-sources`, cena de 8 tochas | Background visível até ~14–18 tiles e < 0,05 depois; sombras intactas (suíte direta); sem mancha saturada com várias tochas. |
| 6 | Mesma sala fechada com tocha, de dia e de noite; tocha no exterior diurno | Diferença dia/noite ≤ 2/255. No exterior diurno a tocha acrescenta pouco (≤ ~10%). Cavernas iguais às do passo 5. |

### 4. Ajustes mínimos antes de qualquer refatoração

| Ajuste | Tipo | Onde |
|---|---|---|
| Escala do céu: demais × background, com escala noturna | Parâmetro + lote separado | `V8AmbientContext`, `V8AmbientField.Apply`, `DrawV8Receivers`, `V8LightingRenderer.Render` |
| Faixa de 24 px com curva V6 | Parâmetro + curva existente | `V8LightingRenderer.SkyFaceDepth`, `V8FaceChunks.SkyBandWeight` |
| Alcance e brilho do background | Parâmetros | `SkyRangeTiles`, escala do background |
| Raio e intensidade da tocha | Parâmetros | `CollectV8Sources` |
| Composição S/T | ~2 linhas em dois shaders | `V8Receiver.fx`, `V8Faces.fx` |

**Adiados até haver evidência de necessidade** (propostas da revisão 1):
- céu como distância com resposta por classe: `SkyRangeTiles` e a escala já separam alcance e brilho enquanto a semente não muda;
- duas faces candidatas com novo formato de vértice;
- propagação de 8 vizinhos (passo 4 decide);
- fator por tamanho de abertura;
- halo e overbright da V7;
- curva noturna própria;
- blend limitado entre várias tochas;
- brilho do sol e amostragem do céu de fundo. Avaliar no passo 1 se o "céu frio e forte" continuar faltando depois do brilho dos receptores.

---

## Revisão 1 — referência detalhada (substituída na ordem e na proposta)

**Status (2026-09-15):** planejamento para revisão. Nada foi implementado nesta entrega: nenhum parâmetro, shader, teste ou instrumentação mudou. Sem commits.

**Base:** working tree com a V8 atual, incluindo a faixa de céu de 12 px nas faces (item 1 anterior, que este plano substitui pelo alvo de 3 tiles).

## Alvo visual

| Tema | Alvo |
|---|---|
| Luz natural | Legibilidade da **V6**, com claridade concentrada perto das aberturas e queda para o escuro. |
| Tochas | Calor e queda de brilho próximos da **V7**, com alcance visível um pouco maior que o da V7 e menor que o da V8 atual. |
| Técnica | Preservar da **V8**: sombras geométricas, oclusão, luz por pixel, reconstrução na GPU, cache de faces por chunk, veto de camada e F4. |

## 0. Fontes e limites

**Código lido** (caminho efetivamente executado, não nomes nem comentários):

| Versão | Caminho |
|---|---|
| V6 | `PlayingState.Draw` → `V6LightingSystem.Update` → `V6LightMapRenderer.Update` → `BuildPixelLightBuffer` → `PixelComposite.fx`. Modo de apresentação padrão `Pixel` (`PlayingState.cs:118`), reconstrução padrão `Linear` (`:126`). |
| V7 | `LightingV7System.BeginFrame`/`AddPointLight`/`EndFrame` → `BuildV7LightBuffer` → `PixelComposite.fx` → `DrawV7Emissives` (chama e halo). |
| V8 | `DrawV8Gameplay` → `V8LightingRenderer.Render` (geometria, direta/stencil, `V8AmbientField`, `V8FaceChunks`) → `DrawV8Receivers` com `V8Receiver.fx`, `V8Faces.fx` e `V8Ambient.fxh`. |
| Comuns | `WorldMap.DrawBackground`/`DrawBackgroundTiles`, `WorldMap.DrawDecorations`, `WorldEnvironmentSystem.SkyKeyframes`, `LightingV7Sky`, `SkyTransmissionHelper`, `NeutralEntityLightSampler`. |

**Prints do usuário:** os 18 prints citados **não estão disponíveis nesta sessão**. Usei a descrição do usuário como referência artística e, para ilustrar, capturas já existentes em disco:
- `screenshots/v8/toggle-faceband/toggle-00…01`: V7 e V8 com a mesma câmera numa caverna Shallow ao meio-dia, com tocha e cogumelos;
- `screenshots/v8/gameplay-faceband-after/`;
- `screenshots/v8/sky-compare*`.

**Medição nova:** uma só, lendo os PNGs já existentes de `gameplay-faceband-after/cavern-torch_*` com um script local, sem mudar o jogo. Está na seção 2F.

**V6 sem captura:** não fiz captura da V6 nesta entrega.

---

## 1. Tabela V6 / V7 / V8

### 1.1 Céu: cor, intensidade e horário

| | V6 | V7 | V8 |
|---|---|---|---|
| Arquivo / método | `V6LightingSystem.ComputeSurfacePrototype`, `ComputeBackgroundGlow`; `skyColor = SkyState.AmbientLight` (`PlayingState.Draw`) | `LightingV7Sky.GetSkyColor`, `GetSunColor`, `GetSunSlope`; `LightingV7System.FillTexture` | `V8AmbientContext.SkyColor`; `V8AmbientField.Apply` → `AmbientSkyColor` |
| Cor | `AmbientLight` direto, interpolado por smoothstep entre keyframes: 0h 78/95/132, 5h 170/134/118, 6h30 222/236/245, 12h 245/250/255, 14h30 238/244/250, 17h30 212/164/126, 19h30 82/95/123 | Curva própria: dia 1,0; noite 0,14/0,17/0,28; tons de amanhecer e entardecer; chuva ×0,75; eclipse; conjunção de luas | `AmbientLight × SkyIntensity (0,5)` |
| Intensidade efetiva no ar exterior | ≈ `AmbientLight` × transmissão (1 em Space/Surface/Shallow): ≈ 0,96–1,0 ao meio-dia | Sol ligado: céu × `SkyBounce` 0,6 **+** sol (até 1,0), luz até ~1,6. **F7 (sol desligado): `bounce = 1,0`, só céu ×1,0.** F7 não é uma subtração isolada do sol. | 0,48–0,50 ao meio-dia |
| Aplicação extra em sprites | Árvores e cogumelos recebem `tint = AmbientLight` no worldRT **e** a luz no composite (duas aplicações). Grama escurece até 10% à noite (`DrawSurfaceNightTint`). | Nenhuma (tint branco) | Nenhuma (receptores com cor branca) |

### 1.2 Aberturas, sementes e camadas

| | V6 | V7 | V8 |
|---|---|---|---|
| Classificação | `ClassifyMedium`: `SkyOpen` = sem frente e sem fundo; `Background` = ar com parede; `Foreground` = sólido. Não depende de camada. | `ClassifyAndSeed`: `AirOpen` (ar sem parede), `AirWalled`, `Solid`, `Water`. Linhas acima do mundo são céu. | `IsExteriorAperture`: frente e fundo vazios e `SkyWeight(y) > 0` |
| Semente | `SkyOpen` = céu × `T(y)`. Background 4-vizinho de `SkyOpen` = **0,9** (`BackgroundSeedIntensity × InitialStrength`). | Surface **1,0**, Shallow **0,5**, Cavern 0 (`rowSeed`) | Semente = `cap` = `SkyWeight`: Surface 1, Shallow 1 (cai a 0 nas 6 últimas linhas), Cavern/Deep 0 |
| Corte por camada | `SkyTransmissionHelper`: 1 em Space/Surface/Shallow, **0** em Cavern/Deep, aplicado **no receptor** (linha da célula) | `rowCap`: 1 até o fim da Shallow, depois linear até 0 em 12 linhas da Cavern | Teto na propagação `min(next, cap[j])` e veto no shader `min(exposure, layerWeight)`; faces: `skyAllowed` binário pela linha do pixel sólido |

### 1.3 Propagação, vizinhança, custo, alcance e corte

| | V6 | V7 | V8 |
|---|---|---|---|
| Método | Background: BFS com fila (`ComputeBackgroundGlow`, `PropagateBackgroundToNeighbor`) | `Sweep`: 4 varreduras × 2 rodadas | Dijkstra de melhor caminho (`Propagate`) |
| Vizinhança | **4** | **4** (varreduras em linha e coluna) | **4** |
| Custo / perda | **Linear −0,10 por tile**, só por células `Background`; portas fechadas bloqueiam (`CanLightPassBetween`) | **Multiplicativa**: céu ×0,94 por tile de ar, ×0,748 em sólido; bloco ×0,954 no ar; água por canal; areia fracionária; portas não bloqueiam | **Linear −(1/24) × custo**: ar 1, plataforma 1,75; sólido, porta fechada e areia bloqueiam |
| Alcance e corte | ≤ 0,01 → **9 tiles** | Corte 0,01 (céu ~74 tiles) | **24 tiles**, exato |
| Local (fontes) | — | Canal de bloco: semente da tocha ×0,35, ×0,954 por tile | Por fonte: perda `8/256` por tile (32 tiles); fontes diferentes **somam** |

### 1.4 Reconstrução e filtros

| V6 | V7 | V8 |
|---|---|---|
| Textura de tile desenhada em tela com `LinearClamp` (atravessa sólidos), limpa em branco fora do buffer | Idem, limpa em preto; texel = valor ÷ 2 | Por pixel de arte (`V8Ambient.fxh`): bilinear só entre células abertas, rejeição de quina, veto de camada após interpolar; amostragem por ponto |

### 1.5 Recepção

| Receptor | V6 | V7 | V8 |
|---|---|---|---|
| Background | `DrawBackground` sem tint → `BackgroundTileTint` **104 (0,41)** × luz do tile | Idem × luz (overbright ×2) | `DrawBackground(…, Color.White)` → albedo **1,0** × (direta + céu + local) |
| Foreground (profundidade no sólido) | `ComputeForegroundDepth`: BFS 4-vizinho **dentro do sólido** a partir de `SkyOpen`, pesos por tile **[1; 0,65; 0,35; 0,15]**, ≥ 4 preto, depois bilinear (~3,5 tiles até o preto). Não usa a face mais próxima: parede fina acende inteira. Frente junto a background: `max(existente, 0,2 × background vizinho)`. | Luz entra no sólido ×0,748 por tile + bilinear | `V8Geometry.TryFace`: face axial mais próxima até o alcance, amostra no primeiro pixel de ar. Direta/local quadrática em 8 px (`Color.r`); céu Hermite em 12 px (`Color.g`); núcleo preto. |
| Entidades e decorações | Amostrador neutro (branco) + composite por pixel; árvores e cogumelos com tint `AmbientLight` | Branco + composite | Receptor por pixel (`V8Receiver` / `Receiver`) |

### 1.6 Tochas

| | V6 | V7 | V8 |
|---|---|---|---|
| Arquivo | `GetArtificialLightSourcesForV6`, `ApplyArtificialLights` | `AddV7PointLights`, `AddPointLight`, `ComputeDirect`, `SweepRgb`, `DrawV7Emissives` | `CollectV8Sources`, `DrawSource` + `V8Direct.fx`, `V8AmbientField` (local) |
| Cor | Base (1; 0,60; 0,20) → núcleo (1; 0,82; 0,45) por `energia²`; flicker ±8% | (1; 0,62; 0,30) × 1,2; flicker ±6% | (1; 0,65; 0,28) × 1; sem flicker |
| Direta | Não separada. Energia = 1 − 0,08·d − obstáculos (0,70 por sólido atravessado, ponderado; porta 0,95), curva `pow 1,4`, raio **9 tiles**. Atravessa sólidos parcialmente; sem sombra dura. | DDA com sombra dura, `(1 − d/13)²`, raio **13 tiles** | Stencil com sombra dura, `(1 − d/280 px)²`, raio **35 tiles** |
| Preenchimento | — | Flood: semente 0,42, ×0,954 por tile (cauda longa) | Local 0,24 × radiância, linear em **256 px (32 tiles)** por melhor caminho |
| Halo | — | Sprite aditivo de 3 tiles, α 0,35 | — (chama sem luz própria) |
| Cogumelos | Não são fonte | Flood de 0,55 | Local de 0,12 em 128 px |

### 1.7 Mistura, saturação, alpha e espaço de cor

| | V6 | V7 | V8 |
|---|---|---|---|
| Céu × tocha | **Soma limitada** por canal: `c ← c + a·(1 − c)`, limitada a 1 (`ApplyBoundedAdd`) | **Máximo**: `max(bloco + direta, céu·bounce + sol) × AO + piso` | **Soma**: `direta + céu + local`. Faces: `(direta + local)·R + céu·G`. |
| Faixa | ≤ 1, sem overbright | Texel ÷ 2 e composite ×2: pode passar de 1 | Render targets de 8 bits limitados a 1; saída limitada |
| Alpha e cor | Composite preserva o alpha do mundo sobre o céu. As três versões trabalham em valores sRGB de 8 bits, sem conversão linear. | | |

### 1.8 Recursos da V8 afetados pelo plano

| Recurso | Arquivo |
|---|---|
| Grade de ambiente (margem = alcance + 2 tiles), `Propagate`, anel de 3 texturas `FieldTexture`/`LocalCellTexture`, reconstrução na GPU | `V8AmbientField.cs`, `V8Ambient.fxh` |
| Parâmetros de céu e camada | `V8AmbientContext.cs` |
| Faces: `FaceWidth`, `SkyFaceDepth`, `FaceReach`; arena, chunks de 64 px e margem de ocupação; formato de vértice | `V8FaceChunks.cs`, `V8Geometry.TryFace`, `V8Faces.fx` |
| `LightBounds` (+ alcance + 2), região de geometria, corte de fontes | `V8LightingRenderer.Render`, `PlayingState.V8.CollectV8Sources` |
| Combinação e receptores | `V8Receiver.fx`, `V8Faces.fx`; `BlendState add` do `Direct` |
| Verificações com margens e valores | `V8Validation`, `V8AmbientValidation`, `PlayingState.V8Capture`, `PlayingState.SkyCompare` |

---

## 2. Respostas às perguntas

Legenda: **[F]** fato do código ou de medição; **[H]** hipótese; **[A]** decisão artística.

### A. Por que a V6 tem superfície clara e claridade concentrada perto das aberturas?

1. **[F] Céu integral no ar aberto.** Células `SkyOpen` recebem `AmbientLight × 1` (≈ 0,96–1,0 ao meio-dia), sem o fator 0,5 da V8. Jogador e entidades, com amostrador neutro, recebem esse valor no composite. **Isso explica cerca de 2× de brilho exterior** em relação à V8 atual (0,48–0,50).
2. **[F] Faixa de terreno de ~3–4 tiles.** Os pesos por tile a partir do ar aberto são 1 / 0,65 / 0,35 / 0,15 e depois 0, suavizados pelo bilinear. É daí que vem a camada de terra visível sob a grama.
3. **[F] Background concentrado.** A semente 0,9 fica só nas células de parede vizinhas do ar aberto, perde 0,10 por tile, zera em 9 tiles e é multiplicada pelo albedo 0,41. O pico no background é ≈ 0,36, e a 6 tiles ≈ 0,12.
4. **[F] Tocha quase sem efeito no exterior.** A soma limitada `c + a(1 − c)` é quase nula onde a base já está perto de 1.
5. **[F] Decorações escurecem ao quadrado à noite.** Árvores e cogumelos recebem `AmbientLight` duas vezes: ≈ 0,92 ao meio-dia, e ≈ 0,09/0,14/0,27 à noite. **Não copiar** (é um defeito de dupla aplicação).
6. **[H] Legibilidade vem da distribuição, não só do brilho.** A claridade fica em superfície, faixa de terreno e borda de abertura, e a queda linear curta com bilinear deixa o resto escuro. Isso não foi medido com captura V6 controlada (M1–M3).

### B. De onde vem o excesso visual na V8?

**Comprovado pelo código:**

| Fator | Efeito |
|---|---|
| Recepção do background | Albedo 1,0 contra 0,41: **×2,45** para qualquer luz que atinge a parede (céu, direta e local). |
| Propagação | Alcance linear de 24 tiles contra 9 da V6. Numa abertura pontual, a área iluminada com vizinhança 4 cresce com o quadrado do alcance (~7×). |
| Intensidade de pico | Não é a causa: o céu da V8 (0,5) é **menor** que o da V6 (0,9 × ~1). |

Céu no background por distância da abertura (brilho final ÷ albedo neutro):

| Distância | V8 (1,0 × 0,5 × (1 − d/24)) | V6 (0,41 × (0,9 − 0,1d) × ~0,98) | Razão V8/V6 |
|---|---|---|---|
| 0 tiles | 0,50 | 0,36 | ~1,4× |
| 6 tiles | 0,375 | 0,12 | ~3,1× |
| ≥ 9 tiles | 0,31 aos 9 | 0 | a V8 continua clara |

Portanto o excesso na parede vem de **alcance + albedo**, não da semente.

- **Soma de contribuições:** só pesa onde céu e tocha coexistem (exterior, aberturas). Numa caverna sem tocha não há soma.
- **Tochas:** alcance da direta 35 tiles contra 13 na V7; local de 32 tiles somado; albedo 1,0 (ver F).

**Ainda exige medição:**
- **Participação percentual por pixel nas três versões com a mesma câmera:** a V6 não tem harness (M1).
- **Leitura perceptiva do albedo médio escuro das texturas de parede:** esse albedo faz a luz parecer menor do que os números indicam.

### C. Como controlar brilho e alcance de forma independente?

**[F]** A exposição da V8 é `e = max(0, s − d/24)`. Com semente `s` o alcance é `24·s`, então a semente altera brilho **e** alcance. `SkyIntensity` só multiplica a cor.

**Proposta:**
1. **Transporte como distância.** A semente fica sempre 1 dentro da camada elegível, e `e` passa a significar apenas "distância normalizada pelo melhor caminho" até um alcance máximo `R_max`. Hoje `R_max` = 24; mantê-lo evita mudar a margem da grade.
2. **Resposta por classe de receptor, no shader.** Para cada classe (background; entidades e decorações; faces):
   - distância pelo caminho: `d = (1 − e)·R_max`;
   - luz: `céu_classe = I_classe × forma(d / R_classe)`, com `R_classe ≤ R_max`;
   - `forma` é uma curva suave (por exemplo `(1 − x)^γ` ou smoothstep).

   `I` controla o brilho, `R` o alcance e `γ` a concentração perto da abertura. São três controles independentes, calculados sem nenhuma textura extra.
3. **Camada como multiplicador no receptor, não como teto de propagação.** Substituir `min(next, cap)` por "transporta só onde `cap > 0`" e aplicar `× layerWeight` no receptor. Isso preserva zero na Cavern (células com `cap = 0` não conduzem céu) e evita que o desvanecimento da Shallow encurte a distância.
4. **Não reduzir a semente da Shallow.** Na V6, Surface e Shallow têm o mesmo céu. Se a Shallow precisar ser mais escura, usar `I_classe × fator_da_camada` no receptor.

**Reaproveita:** `Propagate`, as texturas em anel, o canal R da textura de campo e `AmbientAt`. Muda o significado do valor e acrescenta aritmética no shader.

### D. Profundidade visual de 3 tiles preservando a oclusão

**[F] Regra atual.** Cada pixel sólido lê só a **face axial mais próxima**. Com 24 px de faixa, paredes com menos de 6 tiles (48 px) têm corte na linha média: na metade escura o peso é 0, enquanto do outro lado da linha a faixa ainda vale ~0,3–0,7. **O resultado é um degrau visível** numa parede de 2 a 5 tiles com um lado escuro.

**[F] V6.** Acende a parede fina inteira (profundidade medida a partir de qualquer ar aberto), e o bilinear vaza meio tile no ar do outro lado. **Não copiar.**

**Proposta recomendada: duas faces candidatas com porta suave.**
1. **Dados por pixel sólido:** as duas melhores faces axiais (distâncias `d₁ ≤ d₂` e amostras de ar `p₁`, `p₂`).
2. **Luz de céu no pixel:**
   - `L = max(banda(d₁)·S(p₁), banda(d₂)·S(p₂)·porta(d₁ − d₂))`, com `porta(x) = saturate((x + k)/(2k))` e `k` ≈ 2–4 px;
   - usar **máximo** e nunca soma, para não clarear quinas.
3. **Comportamento:**
   - **Lado escuro de parede fina:** a face escura é a mais próxima e `S = 0`; a face do céu fica atenuada pela porta, que zera a menos de `k` px da face escura. O lado escuro fica preto.
   - **Bloco com os dois lados sob céu:** usa o máximo, sem linha média.
4. **Direta e local** podem continuar na regra da face mais próxima (8 px). O item 4 decide se aprofundam.

**Detalhes:**

| Tema | Tratamento |
|---|---|
| Distâncias | Em vez de `TryFace` (até `alcance × 4` leituras por pixel de núcleo), calcular por chunk passes 1D sobre a ocupação com margem: esquerda/direita por linha e cima/baixo por coluna. Custo O(pixels do chunk + margem), independente do alcance. |
| Quinas convexas | Busca axial não enxerga ar na diagonal; a faixa fica com recorte quadrado na quina. Aceitar na primeira versão; se incomodar, trocar por distância chamfer de 8 vizinhos com bloqueio de quina, por chunk. |
| Vértice | Hoje `VertexPositionColorTexture` (1 UV). Precisa de um formato próprio com 2 UVs e 2 pesos, com a arena e o desenho de chunks iguais. Cerca de +8 bytes por vértice. |
| Margens | `FaceReach = 24`: margem de ocupação do chunk 24 px (lado 112), `LightBounds` +26, cobertura de geometria +24, corte de fontes +26, margens das verificações de faces ancoradas. |
| Limite Shallow → Cavern | `skyAllowed` é binário pela linha do pixel sólido e cortaria a faixa em degrau horizontal na fronteira. Usar o **peso de camada como multiplicador**, já suave nas 6 últimas linhas da Shallow, mantendo zero na Cavern. |
| Curva da faixa | **[A]** Calibrar pela tabela da V6, que por centro de tile dá 1; 0,65; 0,35; 0,15; 0. Família inicial `banda(d) = (1 − d/D)^p`, com `D` ∈ [24; 32] px e `p` ∈ [1,0; 1,5]. Critério: preto aos ~3 tiles e ≈ 0,6 a 1 tile (seção 5). |

A distância da faixa **não** é o alcance do ambiente pelo ar: são parâmetros separados (`SkyFaceDepth` × `R_classe`).

### E. Causa provável do losango na V6

**[F] Métrica L1.** A BFS de 4 vizinhos com perda linear constante dá `0,9 − 0,1·d₁`, onde `d₁` é a distância **Manhattan** pelo ar de background. As isolinhas em torno de um ponto são losangos. A semente em cruz (os 4 vizinhos do buraco) reforça o formato.

**[F] Contraste e reconstrução.** A queda linear dá degraus de brilho iguais por tile, e o bilinear tile→pixel preserva o contorno com escada de tile. O buraco `SkyOpen` recebe céu integral e o bilinear o espalha meio tile.

**[H] Aberturas pequenas e V7/V8.**
- **Por que aparece mais em aberturas pequenas:** buracos de 1 a 4 células funcionam como fonte pontual; aberturas grandes geram retângulos arredondados, menos perceptíveis.
- **V8:** tem a **mesma métrica** (4 vizinhos, perda linear), então também forma losangos.
- **V7:** também é L1, mas o decaimento exponencial disfarça o contorno.

**Proposta (sem blur indiscriminado):**
1. **Vizinhança de 8** com custo diagonal √2 (≈ 1,414), no céu e no local. Contorno octogonal, com erro ≤ ~8% contra o euclidiano.
2. **Diagonal só se as duas ortogonais estiverem abertas.** É o bloqueio de quina, coerente com a rejeição de diagonal da reconstrução, e impede luz passando entre dois sólidos em diagonal.
3. **Curva de resposta suave** (C) para tirar os degraus lineares.
4. **Chamfer 16 vizinhos** (5-7-11) se o octógono ainda aparecer.

### F. Por que a tocha da V8 domina o background e muda tanto o personagem?

**[F] Medido** em `gameplay-faceband-after/cavern-torch` (1 tocha, meio-dia, Cavern, só buffers existentes), numa linha horizontal pelo background sem oclusão:

| Distância (tiles) | 0 | 4 | 8 | 12 | 16 | 20 |
|---|---|---|---|---|---|---|
| Direta, R (0..255) | 254 | 199 | 151 | 110 | 75 | 46 |
| Local, R | 61 | 53 | 45 | 37 | 29 | 22 |
| Background final ÷ albedo | 1,00 | 0,99 | 0,77 | 0,58 | 0,41 | 0,26 |

- **Na sombra abaixo da tocha** (direta 0): só o local, com final ÷ albedo 0,20 a 2–4 tiles, 0,13 a 12 e 0,09 a 18.
- **Coerência com as fórmulas:** os valores batem com `(1 − d/35)²` e `0,24 × (1 − d/32)`.

**Leitura:**
- **Background iluminado:** a **direta responde por ~75%** da luz a 8–12 tiles e o local por ~25%.
- **Sombra:** o local responde por 100%.

**[F] Comparação pelas fórmulas da V7** (não medida), background com albedo 0,41:
- direta `1,2·(1 − d/13)²` + flood `0,42·0,954^d`;
- resultado ≈ 0,38 a 4 tiles, 0,19 a 8 e 0,10 a 12;
- **a V8 fica ~2,6×, ~4× e ~6× mais clara** nessas distâncias.

**Causas, em ordem:**
1. Raio direto de 35 tiles contra 13.
2. Albedo 1,0 contra 0,41.
3. Local de 32 tiles somado por cima.

**Personagem:**
- **V8:** soma `céu + direta + local`. De dia no exterior, 0,5 + ~0,6 a 3 tiles ≈ 1,1, quase o dobro.
- **V7:** o **máximo** faz a tocha quase não aparecer de dia.
- **V6:** a soma limitada faz o mesmo.

**Proposta:** calibrar **primeiro a direta** (raio e curva), avaliar, **depois o local** (intensidade e alcance). Não reduzir os dois de uma vez.

### G. Relação céu/tocha da V6 sem enfraquecer tochas em sala escura de dia

**[F] V6.** A soma limitada `c + a(1 − c)` depende do **nível de luz no ponto**, não da hora. Onde o céu já ilumina, a tocha acrescenta pouco; onde está escuro, acrescenta tudo.

**Proposta:** combinar por pixel no receptor e nas faces:
- `L = S + T·(1 − saturate(S / W))`, por canal;
- `S` = céu reconstruído (natural); `T` = direta + local das fontes (artificial); `W` = branco de referência, inicialmente 1.

**Comportamento:**
- **Sala escura de dia:** `S ≈ 0`, então `T` sai integral, **igual à noite**.
- **Exterior diurno:** `S ≈ W`, então a tocha contribui pouco.
- **Hora do dia:** não entra na fórmula.

**Várias tochas (opcional):** trocar o blend aditivo do `Direct` por `One / InverseSourceColor`. Isso é exatamente `c + a(1 − c)` na GPU e mantém o stencil por fonte.

**[A] Cogumelos:** contam como `T` (artificiais).

---

## 3. Fatos, hipóteses e decisões artísticas

**Fatos:** tudo o que está marcado **[F]** acima. Os centrais:
- céu da V6 integral no ar;
- faixa da V6 de 4 pesos por tile;
- background da V6 com 9 tiles lineares e albedo 0,41;
- soma limitada da V6;
- F7 da V7 muda o céu de ×0,6 para ×1,0;
- V8 com albedo 1,0, alcance 24, métrica L1, soma e raio direto de 35 tiles;
- medição da tocha da V8.

**Hipóteses:**
- legibilidade da V6 por distribuição (A6);
- losango mais visível em aberturas pequenas e disfarçado pela exponencial na V7 (E);
- peso perceptivo das texturas escuras de parede (B).

Todas exigem as medições M1–M5.

**Decisões artísticas (do usuário ou a confirmar):**

| Tema | Decisão | Origem |
|---|---|---|
| Faixa de terreno | 3 tiles | usuário |
| Exterior diurno | Próximo da V6, sem tocha nem sol | usuário |
| Tochas | Calor da V7; alcance entre V7 e V8 | usuário |
| Aberturas pequenas | Mais sutis que na V6 | usuário |
| Curva da faixa | — | a confirmar |
| Níveis `I`/`R`/`γ` por classe | — | a confirmar |
| Curva noturna | Separada | a confirmar |
| Cogumelos | Tratados como `T` | a confirmar |
| Flicker e halo da V7 | Fora de escopo até aprovação | a confirmar |

---

## 4. Proposta mínima recomendada

**Reaproveitado sem mudança:**
- geometria em bitset, stencil e direta por fonte;
- grade de ambiente, Dijkstra e anel de texturas;
- reconstrução por pixel na GPU, veto de camada e aberturas;
- chunks de faces (arena, reuso por ocupação, invalidação por `Generation`);
- F4, captura de gameplay, `--lighting-sky-compare`, `--v8-scene --v8-ambient`, bancada de alternância e bancada de desempenho.

**Mudanças mínimas, cada uma isolada e medível:**

| # | Mudança | Onde |
|---|---|---|
| N1 | Propagação de 8 vizinhos com custo √2 e bloqueio de quina (céu e local) | `V8AmbientField.Propagate` |
| N2 | Céu como distância com resposta por classe (`I`, `R`, `γ`) e camada multiplicando no receptor | `V8AmbientField` (semente e teto), `V8Ambient.fxh`, `V8Receiver.fx`, `V8Faces.fx`, `V8AmbientContext` |
| N3 | Faixa de 3 tiles com duas faces candidatas, porta suave e distâncias por passes 1D | `V8FaceChunks`, `V8Geometry`, `V8Faces.fx`, formato de vértice |
| N4 | Nível do exterior diurno por `I` de entidades e decorações (≈ V6) separado de `I` do background; escala noturna própria | `V8AmbientContext` e shader |
| N5 | Tochas: raio e curva da direta, depois intensidade e alcance do local | `CollectV8Sources`, `V8Light` |
| N6 | Combinação `S`/`T` por nível no receptor e nas faces; blend limitado entre fontes (opcional) | `V8Receiver.fx`, `V8Faces.fx`, `V8LightingRenderer` |
| N7 | Só se ainda necessário após N1–N2: fator de abertura por semente (fração de células de abertura num 5×5 ou 7×7) levado como rótulo do melhor caminho, **sem somar células** | `V8AmbientField` |

---

## 5. Sequência de implementação e critérios de aceitação

**Invariantes verificados em toda etapa:**
- céu zero em Cavern/DeepCavern (buffer e faces);
- sala totalmente fechada sem fontes preta;
- portas e sólidos bloqueando;
- faces, direta e local ancoradas no mundo sob câmera, zoom, pan, região e wrap (erro ≤ 1);
- F4 57/0;
- `--v8-scene` e `--v8-scene --v8-ambient` sem FAIL;
- `--v8-gameplay-capture` sem FAIL;
- teste básico da V7;
- recursos estáveis.

**Critérios numéricos:** são proporções a confirmar com o usuário na primeira calibração, medidas com as ferramentas existentes.

### Etapa 1 — Luz natural

**1a. N1: transporte de 8 vizinhos**
- *Funcional:* nenhuma luz atravessa diagonal fechada por dois sólidos (novo caso na cena `corner`); `sky-window`, `sky-fissure` e `door-*` passam.
- *Visual:* numa abertura 2×2 (`--lighting-sky-compare`), a razão entre alcance diagonal e axial no buffer de céu fica ≥ 0,9, contra ~0,71 hoje. Losango não perceptível.

**1b. N2: resposta do background**
- *Funcional:* sala selada 0; Cavern 0; a V7 não muda.
- *Visual:*
  - background na abertura claramente legível;
  - queda **significativa**: a metade do alcance com ≤ ~35% do pico, zero em `R_bg`;
  - sem preenchimento extenso fraco.
- *Faixas iniciais de calibração:*

  | Parâmetro | Faixa | Referência |
  |---|---|---|
  | `R_bg` | 10–16 tiles | V6 9, V8 24 |
  | `γ_bg` | 1,5–2,5 | — |
  | `I_bg` | 0,35–0,5 | V6 ≈ 0,36 no pico com albedo 0,41 |

  Escolha por perfil de túnel (sky-compare) e aprovação visual com F4.

**1c. N3: faixa de 3 tiles**
- *Funcional:*
  - paredes de 1, 2, 3 e 5 tiles entre exterior e sala selada: lado escuro = 0;
  - quina convexa sem valor acima da face isolada;
  - teto sob céu com núcleo preto além da faixa;
  - chunks reutilizados com erro ≤ 1.
- *Visual:* no terreno real (perfil por profundidade, como `faceband.ps1`), ≈ 0,6 do valor da face a 1 tile, ≈ 0,25–0,35 a 2 tiles e ≤ 0,05 a 3 tiles, sem degrau na linha média.

**1d. N4: exterior diurno e noite**
- *Visual:* ao meio-dia, receptores exteriores (árvores, jogador, grama) com final ÷ albedo ≈ 0,85–1,0, próximos da V6 **sem** a dupla aplicação nas decorações. Noite aprovada separadamente.
- *Funcional:* `real night sky weaker than day` passa.

**1e. Decisão sobre N7:** comparar abertura pequena e grande no sky-compare. Implementar só se a pequena ainda parecer forte demais.

### Etapa 2 — Tochas

**2a. N5: direta**
- *Visual:* alcance visível maior que na V7 e menor que na V8. Faixa inicial: raio de 14–20 tiles (112–160 px), curva `(1 − d/R)^p` com `p` ∈ [1,5; 2,5]. Background final ÷ albedo < ~0,05 entre 16 e 22 tiles.
- *Funcional:* verificações de direta, sombras, suporte finito e ordem de fontes passam.

**2b. N5: local**
- *Visual:* sombra legível, mas sem "lavar" a parede. Faixa inicial: intensidade 0,08–0,16, alcance 10–16 tiles.
- *Funcional:* `platform shadow receives weak local fill`, `direct remains dominant`, portas e paredes finas passam.

**2c. Várias tochas:** 1, 4 e 8 tochas (bancada de desempenho `cave-8-torches`) sem mancha saturada, avaliando o blend limitado (opcional).

### Etapa 3 — Combinação

**3a. N6: combinação `S`/`T`**
- *Funcional:* mesma sala fechada com tocha, dia contra noite, com diferença ≤ 2/255.
- *Visual:* no exterior diurno a tocha acrescenta ≤ ~10% nos receptores com `S` perto de `W`; o personagem junto à tocha não "acende o dia".

**3b. Ajuste final:** comparação lado a lado V6/V7/V8 nas mesmas cenas (M1) e aprovação do usuário.

---

## 6. Riscos de desempenho e preservação

| Mudança | Risco | Como preservar |
|---|---|---|
| N1 (8 vizinhos) | ~2× arestas no Dijkstra (grade 2–4 ms hoje) | Medir `LastFieldMs` na bancada de desempenho antes e depois. Se subir demais, fila por baldes (custos inteiros 2/3) no lugar de `PriorityQueue`. Sem mudança de textura nem de reconstrução. |
| N2 | Aritmética no shader, sem leitura extra de textura | Manter as 8 leituras atuais por fragmento |
| N3 | Construção de chunk com alcance de 24 px; comparação de ocupação com lado 112; vértice maior (+~1,6 MB de arena com ~200 mil vértices) | Distâncias por passes 1D, custo independente do alcance. Reuso por ocupação e anel inalterados. Medir `LastFaceBuildMs` máximo ao andar e as reescritas da arena. |
| N3 (margens) | `LightBounds` +16 px e cobertura de geometria maior | Seguem sem depender da câmera. Verificação de chunks não cobertos continua 0. |
| N5 | Alcances menores reduzem a margem da grade e a região de geometria | Tende a ganho de desempenho |
| N6 | Mudar `BlendState` do `Direct` | Não mexe no stencil; validar soma e ordem de fontes |

**Regras gerais:**
- Nenhuma reconstrução por pixel volta à CPU.
- Nenhum cache passa a depender da câmera.
- Uma rodada de `--v8-perf-bench` com VSync por etapa, só para regressão; sem nova investigação de desempenho.

---

## 7. Medições que realmente faltam

| # | Medição | Ferramenta existente |
|---|---|---|
| M1 | Perfis da **V6** nas cenas de túnel, abertura pequena e grande, e sala: forma do losango e queda | `--lighting-sky-compare` só tem V7 e V8; precisa de uma variante Legacy (instrumentação a autorizar). A V6 não tem cogumelos como fonte, só tochas. |
| M2 | Exterior da V6 ao meio-dia e à noite: final ÷ albedo de árvore, grama e jogador (confirma ≈ 0,92 e a dupla aplicação) | Mesma variante, caso `exterior-*` |
| M3 | Faixa da V6 no terreno, por profundidade | Mesma captura + `faceband.ps1` (scratchpad) |
| M4 | Tocha da V7 na mesma sala da V8: direta (`DirectOnly`) e flood (`BlockOnly`) por distância | `NYVORN_V7_VIEW`; a sala da captura de gameplay exige a variante V7 ou a bancada de alternância (cena com tocha) com perfil numérico |
| M5 | Depois de N1: forma e alcance em aberturas 2×2 e 6×6 | `--lighting-sky-compare` (já existe) |
| M6 | Custo de construção das faces com alcance 24 ao andar | `--v8-perf-bench` (já existe) |
