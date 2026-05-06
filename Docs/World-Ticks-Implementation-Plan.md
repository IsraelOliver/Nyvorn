# World Tick System - Plano de Implementacao

## Objetivo

Introduzir um sistema de `World Ticks` no Nyvorn para separar:

- `Frame Update`
- `Simulacao discreta do mundo`

O objetivo e permitir que mecanicas lentas, seletivas ou probabilisticas do mundo rodem em cadencias fixas, sem depender diretamente do FPS.

---

## Estado Atual do Projeto

### Loop principal

- O loop principal esta em `Nyvorn/Game1.cs`.
- Ele repassa `GameTime` para o `StateMachine`.
- O gameplay real acontece em `PlayingState`.

### Entrada atual do tempo

- Em `Nyvorn/Source/Game/States/PlayingState.cs`, o jogo usa:
  - `gameTime.ElapsedGameTime.TotalSeconds`
- Esse `dt` e repassado para `PlayingSession.Update(dt, input, mouseWorld)`.

### Onde o mundo e atualizado hoje

- O update principal do mundo esta em `Nyvorn/Source/Game/States/PlayingSession.cs`.
- Hoje esse metodo mistura:
  - simulacao por frame
  - timers em segundos
  - atualizacao de areia
  - grama
  - itens
  - inimigos proximos
  - efeitos de tecido
  - cooldowns

### Sistemas ja existentes que ajudam

#### Chunks

`WorldMap` ja possui nocao de chunk:

- `ChunkTileSize`
- `ChunkCountX`
- `ChunkCountY`
- `GetChunkCoordForTile`
- `GetChunkCoordForWorld`
- `GetChunkWorldBounds`
- `PrepareVisibleChunkCache`

Observacao:

- hoje esses chunks sao usados principalmente para render/cache
- ainda nao existe uma camada formal de `chunks ativos para simulacao`

#### Areia ativa

`SandSystem` ja possui active list real:

- `activeSand`
- `activeSandKeys`

Isso ja serve como base para o `FastTick`.

#### Grama seletiva

`WorldMap` ja possui um sistema de candidatos para grama:

- `_grassCandidateQueue`
- `_grassCandidateKeys`
- `UpdateGrassSpread()`

Esse sistema ja evita varrer o mundo inteiro, mas:

- ainda nao usa random tile update
- ainda nao e orientado por chunks ativos
- ainda depende de timer em segundos dentro de `PlayingSession`

---

## Objetivo Arquitetural

Criar uma arquitetura hibrida com dois fluxos:

### 1. Frame Update

Continuara responsavel por:

- input
- player
- camera
- UI
- console
- pause
- inventory
- minimap interaction
- entidades importantes proximas
- efeitos visuais
- render

### 2. World Tick System

Sera responsavel por:

- simulacao discreta do mundo
- mecanicas lentas
- updates probabilisticos
- updates seletivos em chunks ativos
- active lists

---

## Modelo de Ticks Proposto

### FastTick

Uso:

- areia ativa
- futuros liquidos simples
- blocos instaveis
- sistemas baseados em active list

Taxa inicial sugerida:

- `60 TPS`

### MediumTick

Uso:

- grama espalhando
- pequenas reacoes ambientais
- folhas
- random tile update local

Taxa inicial sugerida:

- `4 TPS`

### SlowTick

Uso:

- arvores
- corrupcao
- biomas
- eventos naturais
- reacoes lentas do tecido

Taxa inicial sugerida:

- `1 TPS`

---

## Scheduler Proposto

Criar um scheduler puro chamado `WorldTickSystem`.

### Responsabilidades

- acumular tempo usando `dt`
- disparar `FastTick`
- disparar `MediumTick`
- disparar `SlowTick`
- limitar catch-up em frames lentos
- expor contadores e debug simples

### Local sugerido

- `Nyvorn/Source/Gameplay/World/Simulation/WorldTickSystem.cs`

### Configuracao inicial sugerida

- `FastTick = 60 TPS`
- `MediumTick = 4 TPS`
- `SlowTick = 1 TPS`

### Limites de catch-up

Para evitar espiral de atraso:

- maximo de `8` FastTicks por frame
- maximo de `2` MediumTicks por frame
- maximo de `1` SlowTick por frame

---

## Papel de PlayingState e PlayingSession

### PlayingState

Continuara como entrada de `GameTime`.

Responsabilidade:

- calcular `dt`
- coletar input
- manter fluxo de frame
- repassar `dt` ao scheduler via `PlayingSession`

### PlayingSession

Passara a ser o orquestrador hibrido.

Devera ficar com duas rotas principais:

- `UpdateFrame(dt, input, mouseWorld)`
- `AdvanceWorldTicks(dt, contextoDoFrame)`

Tambem deve receber handlers diretos:

- `OnFastTick()`
- `OnMediumTick()`
- `OnSlowTick()`

### Decisao inicial

Nao criar `IWorldTickListener` agora.

Motivo:

- reduzir refatoracao
- manter a implementacao inicial simples
- evitar criar acoplamento abstrato antes da necessidade real

Se no futuro houver varios subsistemas independentes registrados no scheduler, a interface pode entrar depois.

---

## O que Fica por Frame

Manter no update por frame:

- `InputService`
- player
- camera
- combat
- UI
- console
- pause
- inventory
- minimap interaction
- `TissueRevealController`
- inimigos proximos
- itens proximos
- autosave
- animacoes e efeitos visuais

### Motivo

Esses sistemas dependem de responsividade imediata, suavidade visual ou comportamento ligado ao frame atual.

---

## O que Migra para Ticks

### FastTick

- `SandSystem`
- futuros `activeLiquids`
- futuros `activeFallingTiles`

### MediumTick

- random tile update de grama
- pequenas reacoes naturais locais

### SlowTick

- arvores
- corrupcao
- biomas
- eventos ambientais
- reacoes lentas do tecido

### O que nao migrar agora

Para reduzir risco, manter por frame inicialmente:

- cooldown de colocar bloco
- pickup delay
- respawn de inimigo
- timers de combate

---

## Chunk Awareness para Simulacao

### Regra inicial

Atualizar apenas chunks ativos ou proximos ao jogador/camera.

### Janela sugerida

- chunks visiveis na viewport atual
- mais `1` chunk de borda em cada direcao

### Fonte da verdade

- `WorldMap` fornece coordenadas e utilitarios de chunk
- `PlayingSession` define a janela ativa a partir de camera/player

### Observacao importante

Hoje a nocao de chunk ativo e de render, nao de simulacao.

A implementacao deve adicionar uma camada explicita para:

- coletar chunks ativos de simulacao
- sortear tiles dentro desses chunks
- alimentar sistemas lentos e seletivos

---

## Random Tile Update

Inspiracao:

- Terraria
- Minecraft

### Regras

- nao varrer o mundo inteiro
- a cada `MediumTick` ou `SlowTick`, sortear tiles dentro de chunks ativos
- executar apenas tentativas locais de atualizacao

### Formula sugerida

- `samplesPerChunk * quantidadeDeChunksAtivos`
- com teto maximo por tick

### Casos de uso futuros

- grama
- saplings
- corrupcao
- musgo
- reacoes de bioma

### Primeira mecanica recomendada

- grama

Motivo:

- menor risco
- sistema atual ja existe
- facilita validar a infraestrutura antes de introduzir arvore ou corrupcao

---

## Active Lists

### Ja existe

- `activeSand`

### Preparar infraestrutura futura para

- `activeLiquids`
- `activeFallingTiles`
- `activeCorruptionFronts`

### Regra

Active list deve ser usada quando:

- o sistema ja esta em movimento
- precisa continuar sendo simulado ate estabilizar

Random tile update deve ser usado quando:

- a mecanica e natural, lenta ou oportunistica
- o estado nao exige simulacao continua

---

## Fases de Implementacao

## Fase 1

Criar `WorldTickSystem` isolado.

### Entregas

- acumuladores independentes
- `FastTick`, `MediumTick`, `SlowTick`
- contadores de disparo
- limites de catch-up
- debug opcional

### Regra

- sem integrar ao gameplay ainda
- sem alterar comportamento atual

---

## Fase 2

Conectar o scheduler ao fluxo principal.

### Entregas

- `PlayingState` continua calculando `dt`
- `PlayingSession` passa a receber o scheduler
- handlers de tick vazios

### Resultado esperado

- o jogo compila
- o comportamento atual nao muda

---

## Fase 3

Migrar a areia para `FastTick`.

### Entregas

- `SandSystem.Update(float dt)` deixa de ser usado no fluxo do mundo
- criar algo como `SandSystem.TickFast()`
- cada chamada representa um passo fixo de simulacao

### Resultado esperado

- areia continua caindo de forma estavel
- FPS nao altera a velocidade percebida da simulacao

---

## Fase 4

Adicionar chunks ativos para simulacao e random tile update.

### Entregas

- utilitarios para obter chunks ativos
- sorteio de tiles apenas nesses chunks
- sem scan completo do mapa

### Resultado esperado

- random tile update controlado
- custo proporcional a area ativa

---

## Fase 5

Migrar a grama para random tile update.

### Entregas

- desligar timer atual de grama em `PlayingSession`
- mover a tentativa de crescimento para `MediumTick` ou `SlowTick`
- reaproveitar o que fizer sentido do sistema atual

### Resultado esperado

- grama cresce perto do player
- o mapa inteiro nao e processado

---

## Fase 6

Preparar extensao futura.

### Objetos futuros

- crescimento de arvores
- corrupcao
- liquidos
- eventos ambientais
- reacoes do tecido

### Regra

- preparar hooks e infraestrutura
- nao ligar todas as mecanicas de uma vez

---

## Arquivos a Criar ou Alterar

### Criar

- `Nyvorn/Source/Gameplay/World/Simulation/WorldTickSystem.cs`
- opcionalmente `Nyvorn/Source/Gameplay/World/Simulation/WorldTickConfig.cs`

### Alterar

- `Nyvorn/Source/Game/States/PlayingState.cs`
- `Nyvorn/Source/Game/States/PlayingSession.cs`
- `Nyvorn/Source/Engine/Physics/Sand/SandSystem.cs`
- `Nyvorn/Source/Gameplay/World/WorldMap.cs`
- `Nyvorn/Source/Game/States/PlayingSessionFactory.cs`

### Possivel arquivo futuro

- `Nyvorn/Source/Gameplay/World/Simulation/RandomTileUpdateHelper.cs`

---

## Riscos Tecnicos

### 1. Velocidade da areia

A areia hoje depende do numero de updates recebidos, nao de segundos simulados.

Risco:

- mudar para tick fixo pode deixar a areia mais lenta ou mais rapida do que hoje

Mitigacao:

- validar visualmente com FPS alto e FPS baixo

### 2. Padrao espacial da grama

Ao trocar fila/candidatos por random tile update:

- o padrao de espalhamento muda

Mitigacao:

- aceitar mudanca controlada
- validar se a propagacao continua natural

### 3. Wrapping horizontal

O mundo possui wrapping no eixo X.

Risco:

- sortear tiles ou chunks sem respeitar wrap

Mitigacao:

- usar `WrapTileX` e `WrapChunkX` como base obrigatoria

### 4. Catch-up excessivo

Em frame com lag:

- muitos ticks acumulados podem explodir custo de simulacao

Mitigacao:

- usar caps de catch-up
- truncar acumulador quando necessario

### 5. Acoplamento em WorldMap

`WorldMap` ja mistura:

- dados
- cache de render
- logica natural

Risco:

- colocar simulacao demais la dentro

Mitigacao:

- deixar `PlayingSession` como orquestrador
- mover so helpers realmente ligados ao mapa

---

## Ordem Recomendada de Commits

1. Adicionar `WorldTickSystem` e configuracao
2. Integrar scheduler em `PlayingState/PlayingSession`
3. Migrar areia para `FastTick`
4. Adicionar chunks ativos de simulacao e random tile update
5. Migrar grama para random tile update
6. Adicionar extensoes futuras e hooks adicionais

---

## Checkpoints de Teste

### Fase 1

- build compila
- jogo abre
- nada muda no comportamento

### Fase 2

- pause continua funcionando
- inventory continua funcionando
- minimap continua funcionando
- autosave continua funcionando
- player/camera continuam identicos

### Fase 3

- areia cai igual em FPS alto
- areia cai igual em FPS baixo
- `activeSand` esvazia ao estabilizar

### Fase 4

- random tile update nao escaneia o mapa inteiro
- apenas chunks ativos sao amostrados

### Fase 5

- grama se espalha perto do player
- nao ha propagacao global indevida
- blocos continuam podendo ser colocados e quebrados normalmente

### Fase 6

- infraestrutura pronta para novas mecanicas
- build limpo
- debug simples de acompanhar

---

## Decisoes Iniciais Assumidas

- `FastTick = 60 TPS`
- `MediumTick = 4 TPS`
- `SlowTick = 1 TPS`
- primeira mecanica migrada para random tile update sera grama
- player, combate, camera e animacoes ficam por frame na primeira versao
- chunks ativos de simulacao serao baseados em viewport + 1 chunk de borda
- o sistema ficara restrito ao gameplay, nao a menus ou loading

---

## Estrategia Recomendada

Nao fazer refatoracao gigante.

Implementar em etapas pequenas, validando cada uma antes de seguir:

1. scheduler isolado
2. integracao neutra
3. areia
4. chunks ativos
5. grama
6. extensoes futuras

Essa abordagem reduz risco, facilita debug e evita quebrar mecanicas existentes enquanto a arquitetura evolui.
