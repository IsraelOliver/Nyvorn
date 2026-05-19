# BASELINE

## Identificacao

- Projeto: `Nyvorn`
- Data da baseline: `2026-05-07`
- Branch analisada: `feature/v0.4-mining-progression`
- Commit analisado: `ab1795dd3f2c165177a7642b26e342bfc365c84e`
- Pasta principal do codigo: `Nyvorn/Source`

## Objetivo desta baseline

Congelar uma foto tecnica verificavel do estado atual do projeto antes de refatoracoes arquiteturais maiores. Este arquivo existe para responder:

- qual estado do codigo foi analisado
- qual comando de build passou
- quais sistemas principais existem hoje
- quais comportamentos nao devem quebrar ao longo da evolucao da arquitetura

## Comando de build validado

Comando executado com sucesso:

```powershell
dotnet build "C:\Nyvorn-Reborn\Nyvorn\Nyvorn.csproj"
```

Resultado observado:

- build concluido com sucesso
- `0` warnings
- `0` errors

## Estrutura macro atual

O projeto hoje esta organizado, em alto nivel, nas seguintes areas:

- `Engine`
  - input, camera, ruido procedural, fisica base e areia
- `Game`
  - state machine e estados de navegacao/gameplay
- `Gameplay`
  - combate, entidades, itens, UI, mundo, geracao, persistencia, simulacao e tecido
- `Docs/Arquitetura`
  - documentacao arquitetural central
- `Docs/Arquivos`
  - documentos gerais, historicos, lore, worldgen e arquivos de apoio

## Ponto de entrada e fluxo principal atual

- `Game1` cria e atualiza o `StateMachine`
- `StateMachine` executa o estado atual
- `WorldSelectState` e a porta de entrada do jogo
- `PlayingSessionFactory` monta a sessao jogavel
- `PlayingState` executa input, update e draw do gameplay
- `PlayingSession` orquestra:
  - player
  - inimigos
  - itens
  - mundo
  - HUD
  - minimapa
  - tecido
  - simulacao por ticks do mundo

## Sistemas principais existentes hoje

### Runtime jogavel

- `PlayingState`
- `PlayingSession`
- `Player`
- `Enemy`
- `WorldItem`
- `CombatSystem`
- `HudRenderer`
- `WorldMinimapRenderer`

### Mundo

- `WorldMap`
- `Tile`
- `WorldChunkCoord`
- `TileItemMapper`

### Simulacao

- `SandSystem`
- `WorldTickSystem`
- `GrassSimulation`
- `RandomTileUpdateHelper`
- `ActiveSimulationChunkSelector`

### Geracao procedural

- `WorldGenerator`
- `WorldGenContext`
- `WorldGenConfig`
- passes em `Gameplay/World/Generation/Passes`

### Persistencia

- `PlanetSaveService`
- `PlayerSaveService`
- modelos de save de mundo, jogador, itens e tiles

### Tecido

- `TissueGenerator`
- `TissueAnalyzer`
- `TissueNetwork`
- `TissueRevealController`
- `TissueFieldDebugRenderer`

## Reorganizacao documental observada

Durante esta baseline, a documentacao ja aparece reorganizada em duas pastas:

- `Docs/Arquitetura`
- `Docs/Arquivos`

Isso significa que o repositrio local apresenta movimentacao de arquivos em `Docs`. Essa reorganizacao deve ser tratada como contexto documental, nao como alteracao funcional do gameplay.

## Sistemas que nao devem quebrar

Os seguintes comportamentos devem continuar funcionando ao longo da evolucao arquitetural:

- abrir o jogo e navegar pelos estados principais
- selecionar, criar, editar e carregar mundos
- gerar mundo proceduralmente
- entrar no gameplay com player, hotbar e camera
- mineracao e quebra de blocos
- colocacao de blocos e areia em pixel
- itens no mundo, coleta e inventario
- combate basico entre player e inimigos
- save e load de mundo/jogador
- render de terreno, HUD, minimapa e debug de tecido
- simulacao de areia
- simulacao de grama por random update
- `WorldTickSystem` e seus ticks de mundo

## Riscos conhecidos do estado atual

- `PlayingSession` ainda concentra muita orquestracao e muitas responsabilidades.
- `WorldMap` continua sendo uma classe muito central, acumulando estado, regras e partes da logica de mundo.
- A reorganizacao de `Docs` aparece no workspace e pode poluir leitura de `git status` se for confundida com mudanca de codigo.
- O sistema de ticks do mundo ja existe, entao futuras mudancas arquiteturais devem partir do estado atual e nao de um desenho conceitual anterior.

## Uso recomendado desta baseline

Antes de cada etapa de refatoracao maior:

1. confirmar que o build continua passando
2. comparar o fluxo atual com `ARCHITECTURE_FLOW.md`
3. validar se alguma mudanca invade responsabilidades que deveriam ser separadas
4. registrar explicitamente qualquer mudanca relevante no papel de `PlayingSession`, `WorldMap` ou `WorldTickSystem`

