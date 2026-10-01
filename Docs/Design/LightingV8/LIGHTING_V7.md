# Iluminação V7

Sistema de luz por tile (2 canais: céu escalar + bloco RGB), propagação por varreduras estilo Terraria, composição multiplicativa com faixa 0..2. Pasta de código: `Nyvorn/Source/Engine/Graphics/LightingV7/` (a criar na Fase 1).

Referências: `Nyvorn_Luz_V7_Prompt_Fable.md` e `Nyvorn_Luz_V7_Pesquisa.md` (Downloads do autor), 4 imagens de referência (caverna, casa flutuante, pôr do sol, ruínas na chuva).

Estado: **Fases 2 e 3 entregues** em 2026-09-11. Fases 0, 1 e 1.5 aprovadas na mesma data.

---

## Fases 2 e 3 — Fontes e ambiente

### Teclas

| Tecla | Ação |
|---|---|
| F6 (segurar) | Acelera o ciclo dia/noite em `DebugTimeScale` (60x). |
| F7 | Liga/desliga o sol direcional. |
| F8 | Liga/desliga a luz direta com sombra. |
| F9 | Alterna V7 e Legacy. |
| F10 | Cicla views: Off, FinalLight, SkyOnly, BlockOnly, DirectOnly, **SunOnly**, AmbientOcclusion, Medium. |
| F12 | Salva os três PNGs. |

Variáveis de ambiente para captura sem teclado: `NYVORN_V7_TIME=<0..1>` fixa a hora, além das já existentes `NYVORN_V7_AUTOSHOT`, `NYVORN_V7_VIEW` e `NYVORN_LIGHTING`.

### Fase 2 — Fontes

**Tiles emissivos.** `LightingV7Emissive` tem a tabela tipo de tile → RGB. Todos os tipos de tile do jogo hoje são inertes (Dirt, Stone, Sand, Grass, Wood, IronOre, Platform), então a tabela está vazia e custa um switch por célula. A estrutura existe para lava ou minério luminoso quando esses tiles existirem. O que de fato brilha hoje não é tipo de tile: cogumelo é decoração e tissue é campo próprio, ambos semeados como fonte.

**Tissue** é semeado direto da grade, com a intensidade escalada pela presença da célula.

**Cogumelos** entram como fontes pontuais, filtrados pela região.

**Emissivos não projetam sombra direta** (`EmissivesCastDirectShadows = false`). Um cogumelo é um brilho difuso, não uma lâmpada: dar sombra dura a ele lê errado e custa um passe de raio por fonte. Sem termo direto, eles semeiam o flood com intensidade cheia em vez da fração de bounce.

**Camada emissiva.** As chamas de tocha já saíam no passe pós-composição desde a Fase 1. Nada mais tem sprite separado de "parte brilhante".

**Arte que falta:** olhos de inimigo e parte luminosa do cogumelo não existem como sprite separado. Hoje a chama inteira é emissiva, e o cogumelo só contribui luz, sem sprite brilhante próprio. Para os olhos brilharem no escuro é preciso um sprite de olhos por inimigo; para o cogumelo, um sprite de capa luminosa.

**Halos.** Gradiente radial 64×64 gerado no load, desenhado aditivo em espaço de mundo, uma vez por fonte, na cor da fonte. Emissivos usam `EmissiveGlowScale` (0.6) do raio das tochas.

**Flicker.** Ruído de três oitavas com fase vinda da posição da fonte, então tochas vizinhas não pulsam juntas. Como o flicker entra na intensidade antes de a fonte ser registrada, a luz e o halo respiram juntos.

### Fase 3 — Ambiente

**Cor do céu por hora.** `LightingV7Sky` tem a curva própria da V7. O `SkyState.AmbientLight` antigo não é usado: ele fica perto de (78, 95, 132) à meia-noite, o que num mapa multiplicativo vira uma noite azul clara. O `SkyState` continua sendo a fonte da hora, chuva, eclipse e luas.

| Hora | Céu | Sol |
|---|---|---|
| 03:00 | 0.14 0.17 0.28 | zero |
| 06:00 | 1.00 0.84 0.72 | 0.21 0.14 0.10 |
| 12:00 | 1.00 1.00 1.00 | 1.00 0.98 0.92 |
| 18:30 | 1.00 0.72 0.55 | 0.21 0.14 0.10 |
| 21:00 | 0.14 0.17 0.28 | zero |

Chuva cheia multiplica o céu por 0.75. Eclipse cheio leva o meio-dia de 1.00 para 0.04. Conjunção cheia das duas luas levanta a noite de 0.17 para 0.27.

**Sol direcional.** Varredura inclinada linha a linha, exatamente `visível[x,y] = visível[x - inclinação, y-1] e célula não oclusora`, com o passo por linha tirado do deslocamento acumulado para que uma inclinação fracionária ainda caia em tiles inteiros. Céu final = `flood × SkyBounce + sol direto × SunColor`. Com o sol desligado o flood volta a carregar a luz cheia, então o F7 é um A/B limpo.

Duas correções durante a implementação, ambas achadas por captura:

1. Eu tinha posto um re-seed que dava sol cheio a qualquer célula de ar sem parede. Isso apagava os feixes por completo, porque toda fissura da Shallow virava fonte de sol. Removido: a recorrência é estrita.
2. Semear só a linha do topo da região funciona enquanto o céu está dentro da região. No subsolo ele nunca está, e o feixe simplesmente nunca aparecia. Agora a linha de topo é traçada para cima **através do mundo**, seguindo a mesma inclinação ao contrário, até sair do mapa ou bater num tile. Quase toda coluna bate em rocha no primeiro passo, então isso custa quase nada fora de poços abertos.

**Água por canal.** `decayWaterR/G/B` só são preenchidos e varridos quando a região tem água; fora disso os três canais dividem um único `decay`, mantendo a varredura comum barata. O canal do céu é escalar por construção, então sob água ele usa o decaimento médio e não fica azul: só a luz de bloco muda de cor com a profundidade.

**Areia.** A fração de preenchimento vem de uma amostra de 4 pixels por tile, então cai em 0, 0.25, 0.5, 0.75 ou 1. Acima de `SandOcclusionThreshold` o tile também bloqueia o raio direto e o sol. Um teste por coluna (`SandSystem.HasSandInTileColumn`) evita a sondagem por pixel em quase todo o mapa.

### Verificado

Numericamente, no validador headless:

- Feixe de sol pousa onde a inclinação prevê: +26 tiles às 09:00, +2 ao meio-dia, −29 às 16:00, nenhum feixe às 23:00.
- Água: a razão azul/vermelho sobe de 0.31 (cor da própria tocha) para 0.90 em 16 tiles de profundidade.
- Curva de céu, chuva, eclipse e conjunção conforme a tabela acima.
- Sombra de plataforma, AO e player atrás de bloco seguem passando.

Em tela, no jogo:

- Tochas com halo e chama brilhante em caverna.
- Superfície a 10:00 e 12:00, com a luz do céu chegando pelas aberturas.
- Sol parando exatamente na linha do terreno, com silhueta nítida.
- Sol zero em caverna profunda, que é o comportamento correto.

### Não verificado

**Nunca vi um feixe diagonal de sol atravessando uma abertura numa cena real do jogo.** O comportamento está provado numericamente e o caso negativo (caverna profunda sem chaminé) está verificado em tela, mas o caso positivo depende de estar numa caverna da Shallow com uma abertura vertical, e a posição do save não permitiu enquadrar isso sem controle de câmera.

Também não foram vistos em tela: chuva, eclipse, conjunção das luas, tocha debaixo d'água e areia ocluindo.

### Custo

Release, mundo Elyra, região 218×166, 8 a 11 fontes:

| Fase | ms |
|---|---|
| Classificação | 0,95 |
| Coleta de fontes | 0,01 |
| Oclusão de ambiente | 0,07 |
| Direta + sol | 0,15 |
| Propagação | 0,98 |
| Preenchimento | 0,07 |
| **Cálculo** | **~2,2** |

A classificação subiu de 0,40 para cerca de 0,95 ms. A causa é a amostragem de tissue: `TissueField.GetState` faz duas consultas de dicionário por célula de ar, e o mundo Elyra tem tissue. `TissueEmissiveEnabled = false` devolve o tempo se não valer a pena.

O jogo segue em 60 FPS travados. O `upload` continua marcando de 0,4 a 11 ms conforme o vsync, sem relação com a carga.

---

## Fase 1.5 — Luz direta com sombra, AO e combinação por máximo

### Combinação dos canais

Antes: `luz = Bloco + Céu × SkyColor + LayerAmbient`. De dia, a terra perto de uma tocha somava os dois e ficava cerca de duas vezes mais clara que a terra ao sol aberto.

Agora, por canal: `luz = max(Bloco, Céu × SkyColor) × AO + LayerAmbient`. A luz que domina a célula vence; elas não se empilham.

### Luz direta com sombra dura

O bloco passou a ter dois termos: `Bloco = Direta + Indireta`.

- **Direta.** Para cada fonte e cada célula dentro de `DirectRadiusTiles`, um raio DDA (travessia de tiles Amanatides-Woo) vai do centro da fonte ao centro da célula. Qualquer oclusor estritamente entre os dois zera a luz. Caso contrário `(1 - d/R)² × cor × intensidade`. Contribuições de fontes diferentes somam. A célula de destino nunca bloqueia a si mesma: um tile sólido virado para a fonte é a face iluminada.
- **Oclusores.** Tiles sólidos de foreground e plataformas. Paredes de fundo não bloqueiam, porque a sombra tem que cair *sobre* a parede e deixá-la legível.
- **Indireta.** A semente da fonte no flood passou a ser `BounceStrength × cor × intensidade`. É ela que mantém a sombra escura mas não preta.

A plataforma é o caso interessante: ela barra o raio direto (é o que projeta a sombra da tabuinha na referência) mas não freia o flood, então a luz ainda contorna.

Com `DirectShadowsEnabled = false` a fonte volta a semear o flood com intensidade cheia, que é exatamente o comportamento da Fase 1.

### Oclusão de ambiente

Para cada célula de ar, soma dos oclusores no 3×3 com peso 1 nos ortogonais e 0,5 nas diagonais, máximo 6. `ao = 1 - AOStrength × (soma / 6)`. Células sólidas ficam com 1, já que são face iluminada e não fresta.

### Config nova

| Parâmetro | Valor |
|---|---|
| `DirectShadowsEnabled` | true (F8 alterna em tempo real) |
| `DirectRadiusTiles` | 24 |
| `BounceStrength` | 0.35 |
| `AOStrength` | 0.35 |

O ciclo do F10 ganhou duas visualizações: `DirectOnly` (só o termo direto, onde as sombras moram) e `AmbientOcclusion`.

### Acessor caro por tile

Sua suspeita estava certa, e havia dois:

1. **Busca em dicionário por célula.** Existindo qualquer líquido no mundo, *toda* célula de ar chamava `GetLiquidAmountAtTile`, que monta uma chave e consulta um dicionário. `LiquidSystem.HasLiquidInRow` resolve a linha inteira com uma consulta só, e a esmagadora maioria das linhas não tem líquido nenhum.
2. **Wrap e bounds repetidos.** `GetTile` e `GetBackgroundTile` refaziam o teste de limites e o módulo do wrap a cada chamada, três módulos por célula. Agora `WorldMap.GetTilePairWrapped` lê as duas camadas de uma vez com o x já envolvido, e o loop avança o wrap incrementalmente: um módulo por linha.

Classificação caiu de 3,75 ms para 2,28 ms em Debug.

Também passou a pular por completo qualquer canal sem semente alguma. Em caverna o canal do céu é zero em toda a região, então a varredura dele sai inteira: propagação caiu de 6,47 ms para 5,13 ms em Debug.

### Medições

Mundo Elyra, 1920×1080, região 218×166 (36 188 células), 11 fontes, câmera na Cavern.

| Fase | Debug | Release |
|---|---|---|
| Classificação | 2,28 | 0,40 |
| Oclusão de ambiente | 0,22 | 0,09 |
| Luz direta | 1,45 | 0,17 |
| Propagação | 5,13 | 0,83 |
| Preenchimento | 0,97 | 0,31 |
| Upload | 0,49 | 0,35 |
| **Total** | **10,55** | **2,16** |

**É efeito de Debug, confirmado:** o mesmo trabalho, na mesma cena, custa 5,3 vezes mais em Debug. O jogo mantém 60 FPS travados nos dois builds.

A luz direta custou 0,17 ms em Release, bem abaixo do limiar de 1 ms, então não recebeu cache.

### A meta de 5 ms em Debug não foi atingida

Ficou em 10,55 ms. Tudo que dava para cortar sem perder qualidade já foi cortado. O que resta são trocas, não otimizações:

| Alavanca | Debug | O que custa |
|---|---|---|
| Nada (atual) | 10,6 ms | — |
| `PropagationRounds` 2 → 1 | ~8,0 ms | A luz contorna cantos pior. |
| `MarginTiles` 48 → 24 | ~6,4 ms | Uma tocha a 24 tiles fora da tela ainda vale 0,11 de luz. Cortar ali cria um degrau de cerca de 14/255 na borda quando ela sai de vista. |
| As duas juntas | ~4,9 ms | Soma dos dois custos. |

77 % das células da região são margem: 36 188 no total contra 8 349 visíveis. É por isso que a margem é a alavanca mais forte, e também por isso que mexer nela se paga em artefato de borda.

Decisão sua. Rodar em Release resolve sem custo nenhum.

---

## Fase 1 — Núcleo (entregue)

### Arquivos

| Arquivo | Papel |
|---|---|
| `LightingV7/LightingV7Config.cs` | Todos os parâmetros, ajustáveis em runtime (campos estáticos). |
| `LightingV7/LightingV7System.cs` | Região ativa, classificação do meio, sementes, propagação, preenchimento da textura. |
| `LightingV7/LightingV7Screenshot.cs` | F12: salva `worldRT`, `lightRT` e o frame final em `screenshots/`. |
| `Content/effects/PixelComposite.fx` | Ganhou `OverbrightScale` e `PosterizeLevels` (decisão 3: ajustar o shader existente em vez de criar outro). |
| `LightingPipeline/LightingPipelineMode.cs` | Ganhou o valor `V7`. |
| `LightingV7Validator/` | Projeto de validação headless (sem GPU, sem janela). |

### Teclas

| Tecla | Ação |
|---|---|
| **F9** | Alterna V7 ↔ Legacy (V6). Padrão do jogo: **V7**. |
| **F10** | Cicla a visualização: `Off → FinalLight → SkyOnly → BlockOnly → Medium`. |
| **F12** | Salva `screenshots/lighting_<timestamp>_{world,light,final}.png`. Funciona nos dois modos. |

O overlay mostra `V7 cpu: X ms (cls / prop / fill / upload)`, o tamanho da região e a contagem de fontes.

Variáveis de ambiente para captura sem teclado (usadas na verificação desta fase):

| Variável | Efeito |
|---|---|
| `NYVORN_V7_AUTOSHOT=<frame>` | Dispara a captura do F12 nesse frame de desenho. |
| `NYVORN_V7_VIEW=<nome>` | Começa numa visualização de debug (`SkyOnly`, `BlockOnly`, `Medium`…). |
| `NYVORN_LIGHTING=legacy` | Começa no modo Legacy, para comparação A/B. |

### O que mudou no modo V7

Ligado: propagação V7, composição com overbright 2.0, chama da tocha no passe emissivo (desenhada depois da composição, nunca escurecida).

Desligado: update da V6, `DrawSurfaceNightTint`, tint de ambiente em árvores e cogumelos (passam `Color.White`), night overlay, sampler V6 em player/inimigos/itens (todos usam o sampler neutro e são iluminados por pixel).

O modo Legacy voltou a ser a V6 limpa. O POC de shadow stencil foi movido para a branch `poc/shadow-stencil` (commit `0818df4`) e removido daqui.

### Detalhes de implementação

**Região ativa.** Definida no espaço de tiles não-envolvido da câmera. As leituras de tile são envolvidas internamente com `WrapTileX`, então uma única draw com a matriz de view cobre a tela inteira, sem o laço por offset de loop que a V6 usava (e que falhava quando a região cruzava a emenda do mundo). Fontes de luz vêm em coordenadas envolvidas e são trazidas para a região somando ou subtraindo uma largura de mundo.

**Sementes de céu.** Ar sem parede de fundo recebe `SurfaceSkySeed = 1.0` em Space/Surface e `ShallowSkySeed = 0.5` na Shallow. Abaixo da Shallow, nada. Ar com parede recebe 0 e só é iluminado por propagação.

**Cap por linha.** `SkyCap(y)` vale 1 até `shallowEnd`, desce linearmente a 0 em `SkyFadeTiles = 12` linhas, e é aplicado em **todas as quatro direções** da varredura, não só na descendente. Sem isso, luz vinda de lado ultrapassaria o teto da linha.

**Platform.** `WorldMap.IsSolid` trata `TileType.Platform` como sólido para colisão. Na V7, plataforma conta como ar, senão uma plataforma bloquearia luz como uma parede de pedra.

**Água.** Decaimento interpolado entre ar e `WaterDecay` conforme o preenchimento da célula, via `LiquidSystem.GetLiquidAmountAtTile`. Por canal RGB fica para a Fase 3.

**Areia em pixels.** Ainda não implementada (Fase 3). `TileType.Sand` conta como sólido; areia solta conta como ar.

### Três correções que só apareceram medindo

**1. Varreduras (7,9 ms → 1,3 ms).** A primeira versão varria as colunas na vertical, saltando uma largura de região a cada passo e perdendo o cache. Como cada linha só depende da anterior, iterar `y` externo e `x` interno é igualmente correto e lê a memória em sequência. Junto com isso, os três canais de bloco passaram a ser varridos numa passada só, lendo o array de decaimento uma vez por célula em vez de três.

**2. Parallax subterrâneo brilhando no escuro.** No jogo, o fundo de caverna era desenhado direto no backbuffer, fora da luz, então aparecia azul e visível dentro de uma caverna que deveria ser preta. O bloco de desenho virou o método `DrawSubterraneanParallaxLayers`, e no modo V7 ele é chamado dentro do `worldRT`, antes de tudo. Agora o fundo distante escurece junto com o resto. Legacy continua desenhando no backbuffer.

**3. Stall no upload da textura (~12 ms).** A textura de luz era escrita e desenhada no mesmo frame, então `SetData` esbarrava numa textura que a GPU ainda podia estar lendo. Passou a rodar um anel de 3 texturas.

### Resultados medidos

**Validação headless (Release), mundo sintético, região 217×165 (35 805 células), 18 fontes:**

| Fase | ms |
|---|---|
| Classificação + sementes | 0,21 |
| Propagação | 0,80 |
| Preenchimento da textura | 0,24 |
| **Total (média de 60 frames)** | **1,26** |

Cenários 1, 2, 3 e 4 verificados numericamente: `dotnet run -c Release --project LightingV7Validator/LightingV7Validator.csproj`.

**No jogo (Release, 1920×1080, mundo Elyra, câmera na camada Cavern):**

| Fase | ms |
|---|---|
| Classificação + sementes | 0,53 |
| Propagação | 0,79 |
| Preenchimento da textura | 0,19 |
| **Cálculo da luz** | **1,51** |
| `SetData` (upload) | 3,1 a 13,8 |

O upload varia muito sem relação com a carga, e o jogo fica travado em 60 FPS tanto em V7 quanto em Legacy. Isso é o ponto de sincronização com a GPU/vsync sendo cobrado no primeiro comando gráfico do frame, não custo real de mover 144 KB. O cálculo em si está dentro do alvo de 1,5 ms.

**Atenção ao build Debug:** o mesmo cálculo custa cerca de 9 ms em Debug, e o `Nyvorn.csproj` tem `TieredCompilation=false`, o que agrava. Avalie a V7 em Release.

### Verificado em screenshot

Capturas em `screenshots/`, com o jogo rodando no mundo Elyra, na camada Cavern:

- Escuridão real como padrão. Tochas fazem poças quentes que contornam a geometria e morrem; a rocha fica visível poucos tiles para dentro e preta além disso.
- Zero luz de céu na camada Cavern.
- Nenhuma sombra dura geométrica. O mapa de luz mostra apenas ausência de propagação, como nas referências.
- Chama da tocha visível em cima da poça de luz, sem ser escurecida.
- O fundo de caverna escurece junto com o mundo.

### Não verificado

- Cenário 5 (dia → noite) e a superfície de dia: o save disponível tem o player numa caverna, e o `SkyColor` está fixo em `SkyColorDay` nesta fase, conforme o prompt.
- Cenário 6 (sprite do player junto de uma tocha, iluminado por pixel): o player aparece nas capturas, mas pequeno demais para julgar o gradiente no sprite.
- Cenário 2 (abertura de caverna na Shallow) foi verificado numericamente, não em tela.

---

## Fase 0 — Mapeamento do pipeline atual

### 1. Pipeline de render hoje (`PlayingState.Draw` → `DrawGameplayWorld`)

O working tree local está **à frente** do commit `28df7ae` analisado na pesquisa. Já existe um modo "Pixel" (padrão) que implementa parte da ordem de render pedida para a V7:

| Etapa | Onde | Detalhe |
|---|---|---|
| CPU luz | `PlayingState.Draw` L566 | `v6LightingSystem.Update(...)` + `v6LightMapRenderer.Update()` todo frame. Margem = 8 tiles. |
| `WorldColorRT` (limpo transparente) | `DrawWorldContentToRenderTarget` L1328 | Árvores (back), paredes de fundo, árvores (front); por loop: água, sand+tiles, `DrawSurfaceNightTint` (só Pixel), umidade (multiply), móveis (`DrawWorldLitObjects`), [ProductionTexture multiply só no modo Tile], overlay de terreno; inimigos/itens/partículas/números de dano/**tocha corpo+chama**; halo do tissue (additive) + core + overlay; player; `InteriorFocusOverlay`; tissue debug. |
| `PixelLightBuffer` (RT tela, limpo branco) | `BuildPixelLightBuffer` L1488 | Desenha `RawLightTexture` (1 texel/tile, `Color` 0..1) com transform da câmera, `LinearClamp`, `Opaque`, **só para o loop cujo índice bate com `bufferOriginTileX / worldWidthTiles`** (falha quando a região cruza a emenda do loop). Depois roda o POC de stencil. |
| Backbuffer | `DrawGameplayWorld` L1076 | Clear preto → céu (`LinearClamp`) → sun glow → luas → parallax montanhas (Surface/Shallow) → parallax subterrâneo com crossfade → **composição** `WorldColorRT × PixelLightBuffer` via `PixelComposite.fx` (AlphaBlend, sem overbright, sampler Point) → chuva → night overlay (só modo Tile) → HUD/minimapa/hub/console/labels de debug. |

Entidades: no modo Pixel já usam `NeutralEntityLightSampler` (branco). `v6LightSampler` só é usado no modo Tile. O item "remover luz por amostra única" do prompt já está resolvido no modo Pixel; falta garantir o mesmo na V7.

Chamas de tocha (`TorchRuntimeSystem.DrawFlames`) são desenhadas **dentro** do `WorldColorRT`, logo são escurecidas pelo lightmap. A V7 precisa movê-las para o passe emissivo (o overload `drawTorchFlames:false` já existe em `DrawLoopedWorldEntities`).

Árvores e cogumelos (`WorldMap.DrawDecorations`) recebem tint `SkyState.AmbientLight` no draw. Na V7 isso vira dupla escuridão à noite; passar `Color.White` no modo V7 (overload com `tint` já existe em `PlayingSession.DrawTreeDecorations`).

### 2. O que a V6 tem que a pesquisa apontou (confirmado no código local)

| Achado da pesquisa | Estado local |
|---|---|
| Lightmap com `PointClamp` chapado | **Já corrigido** no modo Pixel (`LinearClamp`, reconstrução bilinear). |
| Raio da tocha 9 tiles, falloff linear | Ainda assim (`V6LightingConfig.TorchLightRadiusTiles = 9`, `PointLightAirAttenuationPerTile = 0.08`). |
| Luz do céu some em 10 tiles | Ainda assim (`BackgroundMaxDistance = 10`, `BackgroundFalloff = 0.10`). |
| Luz presa em 0..1 | Ainda assim (`RawLightTexture` é `Color` byte, `PixelComposite.fx` multiplica direto). |
| `SkyOpen` forçado branco | Ainda assim (`UpdateProductionTexture`). |
| Entidades por amostra única | **Já corrigido** no modo Pixel (neutro). |

Diagnóstico `nyvorn_v6_diagnostic.txt`: passe de tochas da V6 com 28 fontes oscila entre 0 ms e 35 ms por frame. `V6LightingSystem.Update` grava `v6_perf_log.txt` na raiz do repositório a cada 30 frames.

### 3. Pontos de integração da V7

- **Criação**: construtor de `PlayingState` (L170–215). Criar `LightingV7System` ao lado da V6, escolhido por `LightingPipelineMode`.
- **`LightingPipelineMode`** (`LightingPipeline/LightingPipelineMode.cs`): hoje só tem `Legacy`. Plano: adicionar `V7`; `Legacy` passa a significar "caminho V6 atual". Exposto em `ViewCoordinator.LightingPipelineMode`.
- **Update CPU**: substituir a chamada da V6 em `PlayingState.Draw` L566 no modo V7.
- **`lightRT`**: reutilizar `PixelLightBuffer` (já é RT do tamanho da tela). Preencher com a textura V7 desenhada uma vez por offset de loop visível (`GetVisibleLoopOffsets`, L3229), com `destRect = origemDaRegião × 8 + loop × larguraDoMundoPx`.
- **Composição**: novo `LightingV7Compose.fx` (entrada no `Content.mgcb` ao lado de `PixelComposite.fx`, L304). Recebe `OverbrightScale` e `PosterizeLevels`.
- **Emissivos e halos**: novos passes depois da composição e antes de `DrawRainFront` (L1260).
- **Fontes**: `session.TorchRuntimeSystem.Torches[i].LightOrigin` (topo do poste). Não existe tocha na mão do player (só o `ItemId.Torch` para colocar) nem sistema de projéteis. Fase 1 = tochas colocadas.
- **Emissivos disponíveis**: chama da tocha, cogumelo (`SurfaceDecorationType.Mushroom`, único tipo), tissue network (já tem halo/core com `AfterglowColor`). Não há lava nem minério luminoso (`IronOre` é o único minério).
- **Mundo**: `WorldMap.GetTile/IsSolidAt/GetBackgroundTile/WrapTileX/InBounds`, `TileSize = 8`. Tiles sólidos: Dirt, Grass, Stone, Sand, Wood, IronOre, **Platform** (plataforma conta como sólida em `IsSolid`; na V7 tratar Platform como ar).
- **Água**: `LiquidSystem` é por pixel (`HasLiquidAt(px,py)`, `GetLiquidCoverage(Rectangle)`), com célula interna do tamanho do tile. Precisa de um acessor barato por tile (ex.: `TryGetCellAmount(tileX, tileY)`) para não fazer 25 mil lookups de retângulo por frame.
- **Areia em pixels**: `SandSystem` guarda `HashSet<long>` por pixel. Contar 64 pixels por tile em 25 mil células é caro. Fase 3: só contar em chunks que contêm areia solta (`occupiedSandColumns`). Fase 1: `TileType.Sand` = sólido, areia solta = ar.
- **Camadas**: `session.LayerDefinitions` (`WorldLayerDefinition`: `LayerType`, `StartY`, `EndY`, `Contains`). Percentuais: Space 12 %, Surface 22 %, Shallow 30 %, Cavern 85 %. `shallowEnd` = 270 (Small 2800×900), 360 (Medium 4200×1200), 480 (Large 6000×1600). `WorldMap.HasOpenSkyAbove` tem `y >= 960` hard-coded (não bate com nenhum preset); a V7 não vai usar essa função.
- **Câmera**: `Camera2D.Position` = canto superior esquerdo em pixels de mundo, `Zoom` padrão 2. Em 1080p: 120×68 tiles visíveis; com margem 48 → 216×164 ≈ 35 mil células.
- **Dia/noite**: `session.EnvironmentSystem.SkyState` (record struct): `AmbientLight`, `RainIntensity`, `EclipseIntensity`, `MoonConjunction01`, `NearMoon/FarMoon` (`Progress`, `Opacity`, `Phase01`), `VisualTimeSeconds`. `WorldDayNightCycle`: `TimeOfDay01`, `NightStrength`, `CurrentPhase`. Comandos de console já mudam a hora (`session.SetWorldTimeOfDay`). `AmbientLight` por keyframe: dia ≈ (245,250,255), 17h30 ≈ (212,164,126), noite ≈ (78,95,132); é mais claro que o `SkyColorNight` do prompt (0.14,0.17,0.28). Fase 3: curva própria da V7 a partir de `TimeOfDay01` + `RainIntensity` + `MoonConjunction01`.
- **Debug/teclas livres**: F11 = fullscreen (`Game1`). Shift+P, Shift+O, Ctrl+Shift+M, Ctrl+Alt+T já usados em `PlayingState`; P/O/U/E/4–8/Alt+W só quando o profiler está ativo. **F10** (ciclar visualização) e **F12** (screenshot) estão livres.
- **Screenshot**: não existe captura PNG hoje. `RenderTarget2D.SaveAsPng` serve para `worldRT`/`lightRT`; o frame final sai por `GraphicsDevice.GetBackBufferData`.
- **Docs**: vault Obsidian em `Docs/` (`Arquitetura/`, `Arquivos/`, `Design/`). Este arquivo é a página da V7.

### 4. Parede de fundo nas cavernas Shallow (verificação pedida)

`BaseTerrainFillPass` preenche **toda** a camada Shallow com parede Dirt, Cavern e DeepCavern com parede Stone. A camada Surface não recebe parede. `CavePass` cava o terreno de `shallow.StartY` até `deep.EndY` e mantém a parede.

Exceção: `CavePass.CarveBackgroundFissures` remove a parede em faixas verticais alongadas (ruído `freqX 0.15`, `freqY 0.02`, limiar 0.35) em **toda** a camada Shallow, independentemente de a caverna ter contato com o céu. Com a regra "Sky = 1 em ar sem parede com y ≤ shallowEnd", essas fissuras viram sementes de luz plena dentro de cavernas fechadas.

Túneis cavados pelo jogador na camada Surface (sem parede) também ficam totalmente iluminados. Isso é igual ao Terraria e aceitável.

### 5. O que da V6 fica desligado no modo V7

- `v6LightingSystem.Update` e `v6LightMapRenderer.Update` (CPU).
- `BuildPixelLightBuffer` (substituído pelo preenchimento V7) e `PixelComposite.fx` (substituído por `LightingV7Compose.fx`).
- POC de shadow stencil (`EnableShadowStencilProof`, `DrawShadowStencilProof`, e o pulo da tocha 0 em `GetArtificialLightSourcesForV6`). São mudanças **não commitadas** no working tree (151 linhas em `PlayingState.cs`, stencil no `PixelLightBuffer`).
- `DrawSurfaceNightTint` (a noite passa a vir do lightmap).
- Tint `AmbientLight` em árvores e cogumelos.
- Chama da tocha dentro do `WorldColorRT` (vai para o passe emissivo).
- Toggle Shift+P Tile/Pixel e label "LIGHTING: PIXEL" (ficam só no modo Legacy).

Não mexer (fora do escopo, só com autorização): `WorldLightingSystem.cs` legado, `ComposeLighting.fx`, RTs de máscara/sol direcional/penumbra/bloqueio e os `Build*Map` em `PlayingState` (L746–1075), que parecem caminho morto.

### 6. Build

`dotnet build Nyvorn/Nyvorn.csproj` falhava com `dotnet mgcb` não encontrado. Causa: manifesto `Nyvorn/.config/dotnet-tools.json` pede `dotnet-mgcb 3.8.4.1` e a ferramenta não estava restaurada. `dotnet tool restore` dentro de `Nyvorn/` resolveu; o build passa com 2 warnings pré-existentes. Os `.fx` compilam pelo pipeline do `Content.mgcb`. O jogo roda via `.claude/launch.json` (`dotnet run`, janela 1920×1080).

---

## Decisões pendentes antes da Fase 1

1. **Fissuras da Shallow**: manter a regra do prompt (Terraria puro, fissuras acendem) ou semear Sky só em colunas com linha vertical livre até o topo do mundo (fissuras fechadas ficam escuras e só recebem luz propagada)?
2. **POC de stencil no working tree**: manter desligado no modo V7 ou descartar as mudanças não commitadas?

## Plano da Fase 1 (resumo)

Arquivos novos em `LightingV7/`: `LightingV7Config`, `LightingV7Region` (arrays Sky/BlockRGB/Decay), `LightingV7System` (classificação + sementes + propagação + preenchimento da textura), `LightingV7Debug` (F10 ciclo de visualização, F12 PNG, overlay de ms). `Content/effects/LightingV7Compose.fx`. Enum `LightingPipelineMode.V7`. Integração em `PlayingState` gated por modo.
