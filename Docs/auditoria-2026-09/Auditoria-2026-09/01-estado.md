---
verificado_em: 2026-09-14
commit: ec39358c71c67fd05a05063c5f52aa148fb35583
---

# Estado do projeto

Relacionado: [[00-system-map]] · [[02-iluminacao-historico]] · [[03-docs-divergentes]] · [[04-perguntas]]

Critério de "ponta a ponta": o sistema é instanciado na factory/sessão, atualizado no frame ou tick e desenhado ou persistido. **É leitura de código:** nada foi compilado nem executado.

## Funcionam de ponta a ponta (ligados no código)

- **Fluxo de telas:** seleção, criação e edição de mundo e jogador, loading, jogo, pausa, morte.
- **Mundo:** worldgen (17 passes) → `WorldMap` → save/load de mundo e jogador, incluindo portas, tochas, móveis, plataformas, árvores, decorações e itens.
- **Player:** movimento, colisão AABB, animação, ataque, natação, dano de queda.
- **Inimigos:** `GroundChaserBrain`, spawn noturno, respawn; combate com números de dano.
- **Itens:** itens, inventário, hotbar e itens no chão (`items.json`); crafting (`recipes.json`).
- **Objetos:** porta, tocha, cadeira, mesa, plataforma (com drop-through), bancada, fornalha.
- **Simulação:** areia, água, grama (tick médio), cogumelos (tick lento), umidade.
- **Ambiente:** dia/noite, duas luas, chuva e eclipse.
- **Tecido:** geração, revelação, pulsos, mutação e propagação, renderers, poder de revelação com `PowerHUD`.
- **UI e cenário:** HUD, minimapa, hub do jogador, barras de vida, preview de tile, céu, parallax com crossfade.
- **Iluminação:** V7 (padrão) e V6 (modo Legacy, F9).

## Iniciados e não terminados

| Sistema | Evidência | Último commit |
|---|---|---|
| Iluminação V8 | Existe só no working tree: `LightingV8/`, `PlayingState.V8*.cs`, 3 `.fx` e 6 arquivos modificados | nenhum (base `ec39358`) |
| Iluminação V7 | `LightingPipelineMode.cs`: "V6 kept until V7 is approved"; o commit diz "Not seen on screen: a diagonal sun shaft"; `LightingV7Emissive` vazio e sem uso | `ec39358` 09-11 |
| POC shadow stencil | Isolado na branch `poc/shadow-stencil` | `0818df4` 09-11 |
| Parallax/fundo subterrâneo | Fases "S6.1–S6.3.1"; `SubterraneanBackdropDebugRenderer` nunca é instanciado | `9eec50f` 08-20 |
| Pipeline de luz de 07-29 (V1 tardia) | "DirectionalSunlightMap incomplete"; os `Build*Map` não têm chamador | `08c2481` 07-29 |
| `WorldLightingSystem` (V1) | TODO na linha 151; a classe não é instanciada | `4e4cd99` 07-31 |
| Restos da V3 (SunVisibility etc.) | `PHASE_3_2A_PERF_ROADMAP.cs` com tasks "PENDING"; classes sem uso | `dc22f50` 08-03 |
| `LightingV4CapabilityProbe/` | README: "Implementation in progress" | `cc0f24d` 08-05 |
| `SurfaceBackgroundPass` | Fora do `WorldGenerator`; o preenchimento foi para `BaseTerrainFillPass` (`ddb56db`) | `0e62f33` 07-28 |
| Colisão de móveis por raycast | Tirada do fluxo em `34c6f7c` ("raycast bugado"); os métodos ficaram | `05f0326` 07-25 |
| Inimigo descer plataforma | TODO em `GroundChaserBrain.cs:80` | `cedc675` 07-20 |
| Interiores/refúgio | `InteriorFocusSystem` ativo; a doc de design é marcada como "ideia" | `01e7d7e` 05-23 |

## Código morto

Busca textual em todo o C# do jogo, validadores e `TestConsole`, então não detecta reflexão nem uso por string. Um **tipo** conta como morto quando o nome não aparece fora do próprio arquivo e não é instanciado nele. Um **método** conta como morto quando o nome aparece uma única vez; overrides e `Update`/`Draw`/`Dispose` foram excluídos.

**Tipos sem nenhuma chamada (17):**
- `LightingPipeline/` (era V3): `AllocationMeasurementHarness`, `GameFrameTimingHelper`, `LightingCellClassificationHelper`, `Phase3_1Tests` (dentro de `MockGeometryProvider.cs`), `ProfileResultWriter`, `SunVisibilityMetricsAggregator`, `SunVisibilityRayMarcher` (com `SunVisibilityRayStats` e `SunVisibilitySkipReason`), `ForegroundTileOccluderProvider`, `StructureOccluderProvider`, `TreeOccluderProvider`.
- `Source/LightingTestRunner.cs`.
- Outros: `SurfaceBackgroundPass`, `SubterraneanBackdropDebugRenderer`, `LightingV7Emissive`, `CraftTierExtensions`.
- Dois arquivos `.cs` sem nenhuma linha de código, só comentário: `AUDIT_ACTIVE_REGION.cs` e `PHASE_3_2A_PERF_ROADMAP.cs`.
- Referenciados, mas nunca instanciados: `WorldLightingSystem` e `LegacyEntityLightSampler` (este só aparece num `is`, em `PlayingSessionViewCoordinator.cs:747`).

**Métodos sem chamada (93):**

| Onde | Qtde | Quais |
|---|---|---|
| `Engine/Graphics/LightingPipeline/` | 56 | `RenderedFrameProfiler` 21 (`EmergencyLog_*`, `AblationLog_*`, `ShouldSkip*`); `LightingPipelineCoordinator` 8 (`RecordLegacy*`, `AssertLegacyMode`); `OccluderField` 6; `ActiveLightingRegion` 4; V6 7 (`RebuildEdgeOcclusionCache`, `GetDiagnosticsString`, `SetCell`, `GetCell`, `CycleDebugMode`, `InvalidateAll`…); outros 10 |
| `PlayingSessionViewCoordinator` | 10 | `Ensure`/`Get` de `LightingMask`, `DirectionalSunlightMap`, `ForegroundBlockageMap`, `PenumbraMap`; `GetLightTexture`, `GetLastSunColor` |
| `PlayingState` | 8 | `BuildPenumbraMap`, `BuildDirectionalSunlightMap`, `BuildForegroundBlockageMap`, `BuildLightingMask`, `DrawWorldSceneToRenderTarget`, `ApplyLightingComposition`, `ComposeRenderTargetToBackbuffer`, `DrawWithCompositionNeutralPipeline` |
| `WorldMap` | 4 | `GetTissueState`, `MarkTissueDirty`, `GetSkyExposure01`, `MultiplyColors` |
| Geração | 4 | `TissueCellState.FromLegacyPresence`, `WorldFieldSampler.SampleBackgroundFissure`, `UsesDeepThreshold`, `WorldGenContext.GetNormalizedDepthInLayer` |
| `PlatformRuntimeSystem` | 3 | `GetPlatformSurface`, `GetPlatformCount`, `GetPlatformBounds` |
| Móveis/colisão | 3 | `FurnitureCollisionSystem.TryGetNearestCollision`, `FurnitureRuntimeSystem.CheckPlatformCollision`, `PlayerMotor.SetFurnitureRaycastCheck` |
| Outros | 5 | `PlayingSessionBlockInteractionSystem.HasForegroundDynamicMatter`, `InventorySlot.CanAccept`, `LiquidSystem.SetActiveSimulationChunks`, `WorldLightingSystem.SetPointLights`, `LightingV7Emissive.GetEmission` |

## TODO / HACK / FIXME por sistema

Varridos os `.cs` e `.fx` do jogo e os `.cs` dos 4 projetos auxiliares.

| Sistema | TODO | HACK | FIXME |
|---|---|---|---|
| Inimigos (`GroundChaserBrain.cs:80`) | 1 | 0 | 0 |
| Iluminação V1 (`WorldLightingSystem.cs:151`) | 1 | 0 | 0 |
| Todos os demais | 0 | 0 | 0 |
| **Total** | **2** | **0** | **0** |
