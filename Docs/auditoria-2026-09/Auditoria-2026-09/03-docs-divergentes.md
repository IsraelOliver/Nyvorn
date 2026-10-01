---
verificado_em: 2026-09-14
commit: ec39358c71c67fd05a05063c5f52aa148fb35583
---

# Documentação × código

Relacionado: [[00-system-map]] · [[01-estado]] · [[02-iluminacao-historico]] · [[04-perguntas]]

## Escopo — leia primeiro

- **A pasta pedida, `Docs/Auditoria`, existe mas está vazia.** O git não tem histórico dela. Como o `.gitignore` ignora `*.md`, também não dá pra saber pelo git se ela já teve arquivos. Ver pergunta 1 em [[04-perguntas]].
- Pra não entregar o arquivo vazio, comparei os documentos que fazem afirmações verificáveis sobre o código atual: [[ARCHITECTURE_FLOW]], [[BASELINE]], [[ARCHITECTURE_RULES]], [[DEPENDENCY_DEBT]], [[ConsoleCommands]] e `LIGHTING_BASELINE_2026-08-11.md` (na raiz do repo, fora do cofre).
- Ficaram de fora: `.docx`/`.pdf`; planos e ideias (`Docs/Design`, `Docs/Arquivos`); os docs `PHASE_3_2A_*`, que descrevem a V3 já removida (ver [[02-iluminacao-historico]]); e os docs V7/V8 recentes.
- Método: grep dos símbolos citados e leitura dos trechos. O jogo não foi compilado nem executado.

## ARCHITECTURE_FLOW.md

| # | A doc afirma | O código mostra |
|---|---|---|
| 1 | `PlayingSession` é fachada usada por `InventoryState` (§3) | Não existe a classe `InventoryState`; ela sumiu em `3353f16` (2026-05-21). |
| 2 | `TryActivateTouchedTissueHub` em `PlayingSession` (§11) | Não existe; a última ocorrência foi removida em `e60ce68` (2026-06-23). |
| 3 | Draw: `PrepareTerrainRender → DrawSky → DrawLoopedWorldEntities → DrawEntities → DrawTerrain → …` (§4) | `PlayingState.cs` não chama `DrawTerrain`. O mundo passa por `DrawGameplayWorld`, com render targets (`WorldColorRenderTarget`, `PixelLightBuffer`) e composição, ramificando por `lightingMode` (V7/Legacy). No working tree há ainda o desvio `DrawV8Gameplay`. |
| 4 | Ordem de worldgen com 9 passes, de `ClearWorld` a `WorldBounds` (§6) | `WorldGenerator.cs:38-54` tem 17 passes; os 8 a mais são `BiomeField`, `DesertCircle`, `Hydrology`, `DesertPixelSand`, `DesertCave`, `IronOreVein`, `TreeGeneration` e `SurfaceDecoration`. |
| 5 | `SlowTick`: "handler existe, mas esta vazio" (§9) | `OnSlowTick` chama `RunMushroomGrowthUpdates` (`PlayingSessionWorldTickCoordinator.cs:310`). |
| 6 | `FastTick` roda só `SandSystem.TickFast()` (§9) | Também roda `LiquidSystem.TickFast()` (mesmo arquivo, :117 e :264). |
| 7 | `PlayingSession.TryPlaceSandPixel` (§7) | O método está em `PlayingSessionBlockInteractionSystem`, não em `PlayingSession`. |
| 8 | `Game1` é a entrada e "delega o resto" (§1, §2.1) | `Game1.cs` tem 22 referências a `RenderedFrameProfiler` (profiler da era V3, desde `1572805`). No working tree, `Program.cs` escolhe entre `V8DiagnosticGame` e `Game1`. |
| 9 | `PlayingSessionViewCoordinator` concentra câmera, terreno, sky e HUD (§4) | Ele também guarda recursos de iluminação: `ComposeLightingEffect`, RTs de máscara, sol direcional, penumbra e bloqueio. Os métodos `Ensure*Map`/`Get*Map` não têm chamador (ver [[01-estado]]). |

A doc também não menciona estes sistemas que existem no código: líquidos, iluminação, clima/eventos, IA de inimigos, objetos/móveis, crafting e parallax.

## BASELINE.md (datado 2026-05-07, commit `ab1795d`)

| # | A doc afirma | O código mostra |
|---|---|---|
| 10 | O tecido inclui `TissueAnalyzer` | Não existe; foi removido em `cca2a3e` (2026-07-14). |
| 11 | `Engine` = "input, camera, ruido procedural, fisica base e areia" | O ruído (`OpenSimplexNoise`) está em `Gameplay/World/Generation`. Hoje `Engine` contém principalmente iluminação (`LightingPipeline`, `LightingV7`, `LightingV8`), além de líquidos. |
| 12 | Branch `feature/v0.4-mining-progression`; build em `C:\Nyvorn-Reborn\…` | Essa branch não existe mais (as locais são `main`, `physics` e `poc/shadow-stencil`), e o repo está em `C:\dev\Nyvorn-Reborn`. |

## ARCHITECTURE_RULES.md

| # | A doc afirma | O código mostra |
|---|---|---|
| 13 | §3.1: `Engine → World` só na exceção aceita `SandSystem → WorldMap` | Há mais casos: `LightingV7Sky.cs` usa `Gameplay.World.Simulation`, e `LiquidSystem` usa `WorldMap`. No working tree, `LightingV8/*` usa `Gameplay.Crafting` e `Gameplay.World.Objects`. |
| 14 | §3.4: `World` não deve depender de `Game.States` nem de `Gameplay.Entities.Player` | `InteriorFocusSystem`, `DoorInstance`, `DoorRuntimeSystem`, `FurnitureRuntimeSystem`, `WorldObjectMining` e `WorldObjectPlacementValidator` importam `Gameplay.Entities.Player`. `PlanetSaveService` e `PlayerSaveService` importam `Game.States`; esse segundo caso já está em [[DEPENDENCY_DEBT]]. |
| 15 | §1: `World` é uma camada irmã de `Gameplay` | No disco, `World` é subpasta de `Gameplay/`. Os namespaces misturam os dois: `Generation`, `Persistence`, `Tissue` e `Decorations` usam `Nyvorn.Source.World.*`, enquanto `Objects` e `Simulation` usam `Nyvorn.Source.Gameplay.World.*`. |

## DEPENDENCY_DEBT.md

Os itens 1–4 continuam verdadeiros (`PlanetSaveService.cs:1`, `PlayerSaveService.cs:1`, `SandSystem.cs:40`, `BaseTerrainFillPass.cs:2`). As classes citadas como "direção futura" (`SessionSnapshot`, `WorldInteractionSystem` etc.) não existem, o que é esperado em propostas. **Nenhuma divergência.**

## ConsoleCommands.md

| # | A doc afirma | O código mostra |
|---|---|---|
| 16 | `/spawn enemy signature` (alias `utility`) cria um inimigo "com `UtilityBrain` e pathfinding" | `UtilityBrain` foi substituído em `cedc675` (2026-07-20). `EnemyConfig.Signature` usa o mesmo `GroundChaserBrain` dos demais (`EnemyConfig.cs:9`). |
| 17 | A tabela de IDs do `/get` tem 9 itens, de `ironpickaxe` a `wooddoor` | `Content/Data/items.json` também define `WoodAxe`, `IronOreBlock`, `IronBar`, `Sand`, `Mushroom`, `Furnace`, `Torch`, `Chair`, `Table` e `Platform`. |
| 18 | (não documentado) | O texto do `/help` (`PlayingState.cs:2583-2620`) lista `/fps on\|off` e `/water tune …`, que não estão na doc. |

## LIGHTING_BASELINE_2026-08-11.md (raiz)

| # | A doc afirma | O código mostra |
|---|---|---|
| 19 | "V6 is the sole, official lighting pipeline. No alternatives exist." | O padrão é a V7 (`PlayingState.cs:141`). A V6 virou `Legacy`, com toggle em F9 (`:432`). No working tree existe também a V8 (`--lighting-v8`). |
| 20 | Removidos: "Pipeline mode switching infrastructure" e "All toggles" | `LightingPipelineMode { Legacy, V7 }` voltou em `40d5db1` (2026-09-11). |
| 21 | Removidos: "WorldLightingSystem"; "No V5 or Legacy references remain" | `Gameplay/World/Simulation/WorldLightingSystem.cs` e `LegacyEntityLightSampler` (`EntityLightingContext.cs`) continuam no código, só sem instanciação. |
| 22 | Draw pipeline com `DrawEntities(v6LightSampler)` | O commit `40d5db1` desliga a amostragem de luz por entidade na V7; `PlayingState` cria `NeutralEntityLightSampler` em :949, :1021 e :2006. |
| 23 | Tabela de parâmetros | Os 9 valores batem com `V6LightingConfig.cs`. Faltam os adicionados depois: `TorchLightColorShapingEnabled` e `TorchLightColorCoreExponent`. |
| 24 | "Future Work": luz por pixel com `WorldColorRenderTarget`, sombras por stencil, sun shafts | `WorldColorRenderTarget` existe desde `12bd012` (2026-08-12); o sol direcional da V7 veio em `ec39358`; stencil existe na branch `poc/shadow-stencil` (`0818df4`) e na V8 não commitada. |
