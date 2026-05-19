# ARCHITECTURE FLOW

## Objetivo

Descrever o fluxo arquitetural atual do Nyvorn com base no codigo real, para que qualquer evolucao futura tenha um ponto de referencia claro. Este documento cobre:

- fluxo de runtime
- fluxo de update
- fluxo de draw
- fluxo de criacao/carregamento de mundo
- fluxo de mineracao e colocacao de blocos
- fluxo de save
- fluxo da simulacao discreta do mundo

## 1. Fluxo principal de runtime

```text
Game1
  -> StateMachine
    -> WorldSelectState
      -> WorldCreationState / WorldEditState / LoadingWorldState / PlayingState
        -> PlayingSessionFactory
          -> PlayingSession
```

### Resumo

- `Game1` e a entrada do jogo.
- `StateMachine` decide quais estados recebem `Update` e `Draw`.
- `WorldSelectState` e a porta principal da experiencia.
- `PlayingSessionFactory` prepara o mundo e a sessao.
- `PlayingState` roda o gameplay frame a frame.
- `PlayingSession` concentra o runtime jogavel do mundo.

## 2. Fluxo de Update no gameplay

### Cadeia principal

```text
Game1.Update
  -> StateMachine.Update
    -> PlayingState.Update
      -> InputService.Update
      -> console / pause / minimap / inventory gates
      -> PlayingSession.UpdateSimulationViewport
      -> PlayingSession.Update
        -> UpdateFrame
        -> AdvanceWorldTicks
      -> Camera.Follow
      -> autosave checks
      -> death transition check
```

### Detalhamento

#### 2.1 Game1

- coleta teclado global
- trata `F11` para fullscreen
- delega o resto para `StateMachine`

#### 2.2 StateMachine

- determina quais estados da pilha recebem update
- respeita `UpdateBelow`
- processa operacoes pendentes depois do ciclo

#### 2.3 PlayingState

Responsabilidades principais:

- calcular `dt` via `GameTime`
- coletar `InputState`
- controlar console, minimapa, inventario e pause
- converter posicao do mouse para mundo
- atualizar viewport de simulacao
- chamar `PlayingSession.Update(dt, input, mouseWorld)`
- controlar autosave
- detectar morte e trocar para `DeathState`
- atualizar camera no final do frame

## 3. Fluxo interno de PlayingSession

`PlayingSession` hoje e o principal orquestrador do gameplay.

### Estrutura atual

```text
PlayingSession.Update
  -> UpdateFrame
    -> PlayingSessionInputRouter.RouteFrameInput
    -> PlayingSessionCombatCoordinator.SyncEquippedWeapon
    -> PlayingSessionBlockInteractionSystem.UpdateTilePreview
    -> PlayingSessionBlockInteractionSystem.TryPlaceSelectedBlock
    -> Player.Update
    -> PlayingSessionWorldWrapSystem.NormalizePlayerAndMouse
    -> PlayingSessionTissueSystem.Update
    -> PlayingSessionBlockInteractionSystem.TryBreakTargetBlock
    -> PlayingSessionEntityRuntimeSystem.Update
    -> PlayingSessionCombatCoordinator.ResolveCombat
  -> AdvanceWorldTicks
    -> PlayingSessionWorldTickCoordinator.Advance
      -> WorldTickSystem.Advance
      -> FastTick
      -> MediumTick
      -> SlowTick
```

### Responsabilidades observadas

- player e combate ainda sao tratados no fluxo por frame
- simulacao discreta do mundo roda separada em `AdvanceWorldTicks`
- runtime de itens do mundo agora esta concentrado em `WorldItemRuntimeSystem`
- runtime de inimigos, itens proximos e respawn agora esta concentrado em `PlayingSessionEntityRuntimeSystem`
- camera, viewport de simulacao e helpers principais de render agora estao concentrados em `PlayingSessionViewCoordinator`
- interacao com blocos, preview, place e break agora estao concentrados em `PlayingSessionBlockInteractionSystem`
- tecido, presenca ambiente, ativacao de hubs, radar e fast travel agora estao concentrados em `PlayingSessionTissueSystem`
- roteamento contextual de input e selecao da hotbar agora estao concentrados em `PlayingSessionInputRouter`
- normalizacao de world wrapping do player, camera e mouse agora esta concentrada em `PlayingSessionWorldWrapSystem`
- coordenacao de world ticks, ticks manuais, tick de areia e random update de grama agora esta concentrada em `PlayingSessionWorldTickCoordinator`
- sincronizacao da arma equipada e resolucao de combate agora estao concentradas em `PlayingSessionCombatCoordinator`
- referencias-base da sessao agora podem ser agrupadas por `SessionRuntimeContext`
- os sistemas auxiliares da sessao ficam organizados em `Nyvorn/Source/Game/States/PlayingSession/`
- `PlayingSession` ainda mistura:
  - runtime de entidades
  - random updates
  - HUD support
  - world wrapping

Esse ponto deve ser observado em futuras fases arquiteturais porque ele continua sendo o maior centro de acoplamento do projeto.

### Responsabilidades que ainda permanecem em `PlayingSession`

Depois das extracoes da Fase 3, `PlayingSession` ainda permanece responsavel por:

- ordem principal do frame de gameplay
- roteamento entre player, inimigos, combate, itens, blocos, tecido e world ticks
- estado de sessao compartilhado entre subsistemas
- fachada publica para a hotbar selecionada
- regras de alcance de simulacao de entidades proximas
- delegacao da normalizacao de world wrapping no runtime do player e mouse
- fachada publica para comandos/debug de world ticks usados por `PlayingState`
- fachada publica usada por `PlayingState`, `InventoryState` e persistencia

Esses pontos ainda fazem sentido nela como orquestrador. Novas extracoes devem evitar desmontar essa funcao central e focar apenas em regras detalhadas que ainda sobrarem dentro da sessao.

## 4. Fluxo de Draw no gameplay

### Cadeia principal

```text
Game1.Draw
  -> StateMachine.Draw
    -> PlayingState.Draw
      -> PrepareTerrainRender por loops visiveis
      -> DrawSky
      -> DrawLoopedWorldEntities
      -> DrawEntities
      -> DrawTerrain
      -> DrawTissueDebug
      -> DrawHud
      -> DrawMinimap (quando ativo)
      -> DrawConsole (quando ativo)
```

### Observacoes

- O draw ja esta relativamente bem segmentado por etapa visual.
- O mundo usa loops visiveis horizontais para suportar wrapping.
- `WorldMap.PrepareVisibleChunkCache` participa do draw preparando cache de chunks visiveis.
- A camera e usada tanto em render de entidades quanto de terreno e debug.
- `PlayingSessionViewCoordinator` hoje concentra:
  - follow da camera
  - viewport de simulacao por chunks ativos
  - terrain prep/draw
  - draw de entidades loopadas
  - sky, HUD, minimap e inventario

## 5. Fluxo de criacao e carregamento de mundo

### Cadeia principal

```text
WorldSelectState / WorldCreationState
  -> PlayingSessionFactory.CreateBuildOperation
    -> LoadWorldAssets
    -> LoadPlayerProgress
    -> world snapshot OU generation passes
    -> PrepareWorld
    -> preparar tecido
    -> LoadGameplayAssets
    -> CreateSession
  -> LoadingWorldState
    -> BuildOperation.Advance
    -> PlayingState(session)
```

### Quando o mundo vem de save

- importa tile snapshot
- importa tissue snapshot
- tenta importar tissue analysis snapshot
- restaura save do jogador
- restaura itens soltos
- restaura areia, se existir snapshot

### Quando o mundo e novo

- constroi `WorldGenConfig`
- cria `WorldMap`
- roda os passes ordenados de `WorldGenerator`
- prepara o mundo para jogo
- gera tecido se necessario
- cria player, inimigos, itens e renderers

## 6. Pipeline de world generation

### Ordem atual dos passes

```text
ClearWorld
-> LayerBoundary
-> SurfaceProfile
-> BaseTerrainFill
-> DirtToStoneTransition
-> Cave
-> CaveEntrance
-> Tissue
-> WorldBounds
```

### Papel da worldgen no fluxo

- entra apenas na criacao/regeneracao de mundo
- nao participa do runtime jogavel normal
- gera o estado inicial do mundo
- o mundo vivo depois disso passa a ser papel de:
  - `WorldMap`
  - simulacao
  - persistencia
  - sistema de ticks

## 7. Fluxo de mineracao e colocacao de blocos

### Mineracao

```text
input de ataque
  -> PlayerCombat / hitbox ativa
  -> PlayingSession.TryBreakTargetBlock
    -> WorldMap.WorldToTile
    -> valida alcance e ferramenta
    -> WorldMap.TryBreakTile
    -> SandSystem.WakeAreaAboveTile
    -> SpawnBrokenBlockDrop
```

### Colocacao de bloco

```text
input de place
  -> PlayingSession.TryPlaceSelectedBlock
    -> identifica item selecionado
    -> valida alcance
    -> valida colisao com player
    -> valida ocupacao por areia
    -> WorldMap.TryPlaceTile
    -> remove 1 do slot
```

### Colocacao de areia em pixel

```text
item selecionado = SandBlock
  -> PlayingSession.TryPlaceSandPixel
    -> converte mouseWorld em pixel
    -> valida bounds, tile e ocupacao
    -> SandSystem.SetSandAt
    -> remove item do slot
```

## 8. Fluxo de save

### Save em runtime

```text
PlayingState.Update
  -> autoSaveTimer
    -> PlanetSaveService.Save(session) ou SavePlayerOnly(session)
```

### Save em transicao

- `PlayingState.OnExit` salva a sessao
- `PauseMenuState` pode salvar antes de voltar aos mundos
- comandos de console podem forcar save manual

### Save structure

`PlanetSaveService` persiste:

- metadados do mundo
- snapshot de tiles
- snapshot de tecido
- analise de tecido
- snapshot de areia
- itens do mundo
- alteracoes persistentes de tiles
- dados do jogador

## 9. Fluxo da simulacao discreta do mundo

### Cadeia principal atual

```text
PlayingState.Update
  -> PlayingSession.UpdateSimulationViewport
  -> PlayingSession.Update
    -> AdvanceWorldTicks
      -> PlayingSessionWorldTickCoordinator.Advance(dt)
        -> WorldTickSystem.Advance(dt)
        -> FastTick
        -> MediumTick
        -> SlowTick
```

### FastTick

Hoje:

- `SandSystem.TickFast()`

Uso atual:

- areia ativa

### MediumTick

Hoje:

- random tile update em chunks ativos
- tentativa de crescimento de grama

Uso atual:

- `RandomTileUpdateHelper`
- `GrassSimulation`

### SlowTick

Hoje:

- handler existe, mas esta vazio

Uso esperado futuro:

- arvores
- corrupcao
- biomas
- eventos naturais
- reacoes lentas do tecido

## 10. Fluxo de chunks ativos de simulacao

### Cadeia principal

```text
PlayingState.Update
  -> PlayingSession.UpdateSimulationViewport(screenW, screenH)
    -> calcula viewport em tiles via camera
    -> ActiveSimulationChunkSelector.Collect(...)
    -> atualiza activeSimulationChunks
```

### Uso atual

- os chunks ativos alimentam random tile updates
- a simulacao discreta tenta se manter local ao jogador/camera

### Distincao importante

Hoje existem dois conceitos relacionados a chunks:

- `chunks de render/cache` em `WorldMap`
- `chunks ativos de simulacao` em `PlayingSession`

Essa separacao ja existe conceitualmente e deve ser preservada em futuras refatoracoes.

## 11. Fluxo de tecido

### Geracao

- `TissuePass` durante worldgen
- ou `TissueGenerator` ao preparar o mundo para jogo

### Runtime

- `TissueRevealController.Update` no fluxo por frame
- `TryActivateTouchedTissueHub` em `PlayingSession`
- `WorldMinimapRenderer` usa hubs ativos para fast travel
- `TissueFieldDebugRenderer` desenha debug no mundo

### Persistencia

- tissue field pode ser salvo e restaurado
- tissue analysis tambem pode ser salva e restaurada

## 12. Fronteiras arquiteturais atuais

### Fronteiras relativamente claras

- `Game1` delega em vez de centralizar gameplay
- `StateMachine` esta bem isolado como mecanismo de telas/estados
- `PlayingSessionFactory` separa bem criacao da sessao do runtime jogavel
- `WorldGenerator` concentra a pipeline de criacao de mundo
- `WorldTickSystem` ja separa a nocao de tempo discreto do mundo

### Fronteiras ainda tensionadas

- `PlayingSession` continua muito central e com muitas responsabilidades
- `WorldMap` ainda concentra estado e varias regras de mundo
- parte da simulacao ainda mistura regras de runtime, mundo e interacao de player

## 13. Arquivos centrais para entender primeiro

Se alguem novo for estudar o projeto, a ordem recomendada e:

1. `Nyvorn/Game1.cs`
2. `Nyvorn/Source/Game/StateMachine.cs`
3. `Nyvorn/Source/Game/States/PlayingState.cs`
4. `Nyvorn/Source/Game/States/PlayingSession.cs`
5. `Nyvorn/Source/Game/States/PlayingSessionFactory.cs`
6. `Nyvorn/Source/Game/States/PlayingSession/`
7. `Nyvorn/Source/Gameplay/World/WorldMap.cs`
8. `Nyvorn/Source/Gameplay/World/Simulation/WorldTickSystem.cs`
9. `Nyvorn/Source/Gameplay/World/Generation/WorldGenerator.cs`

## 14. Uso recomendado deste documento

Este arquivo deve ser atualizado sempre que houver mudanca estrutural em:

- fluxo de update
- fluxo de draw
- criacao/carregamento de mundo
- sistema de ticks
- fronteiras de `PlayingSession`
- responsabilidades de `WorldMap`

Ele deve ser lido junto com:

- `BASELINE.md`
- `Source-Inventory-Detailed.docx`
- `Planejamento_Arquitetura_10_10_Nyvorn.docx`
