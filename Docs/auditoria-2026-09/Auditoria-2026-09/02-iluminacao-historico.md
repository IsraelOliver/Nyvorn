---
verificado_em: 2026-09-14
commit: ec39358c71c67fd05a05063c5f52aa148fb35583
---

# Histórico da iluminação — V1 a V8

Relacionado: [[00-system-map]] · [[01-estado]] · [[03-docs-divergentes]] · [[04-perguntas]]

Fonte: `git log` de todas as branches e tags, corpos de commit, arquivos deletados e `.md` removidos (lidos direto dos objetos do git). Para a V8, que não tem commit, a fonte é o working tree e a data de modificação dos arquivos. Datas de commit em -03:00. "Duração" é o intervalo entre o primeiro e o último commit da versão, não tempo de trabalho.

> **Limite da fonte.** Até 2026-09-14 o `.gitignore` ignorava `*.md`, e boa parte do planejamento das versões (manifestos, relatórios de fase) nunca foi versionada. Onde uma conclusão sai de documento e não de commit ou código removido, isso está indicado.

## Linha do tempo

| Versão | Início | Fim (último commit) | Duração | Commits | Destino |
|---|---|---|---|---|---|
| V1 (nome inferido) | 2026-07-12 `6dd077c` | 2026-07-29 `08c2481` | 17 dias | ~9 de iluminação | Tirada do runtime em `28df7ae` (08-11); o arquivo continua no repo |
| V2 | 2026-07-30 `539f2d2` | 2026-07-30 `539f2d2` | 1 dia | 1 | Código deletado em `cc0f24d` (08-05) |
| V3 | 2026-07-31 `7194ede` | 2026-08-03 `127468f` | 4 dias | 80 | Núcleo deletado em `cc0f24d` (08-05); sobras ficaram |
| V4 | sem commit próprio | 2026-08-05 `cc0f24d` | não recuperável | 0 | Só o projeto `LightingV4CapabilityProbe/` |
| V5 | 2026-08-05 `e008cd0` | 2026-08-10 `fafaeee` | ~5 dias | 2 | Deletada em `28df7ae` (08-11) |
| V6 | 2026-08-10 `fafaeee` | 2026-08-17 `b9ae266` (último em arquivo V6) | ~32 dias como padrão (até 09-11) | 37 até `9eec50f` | Rebaixada a "Legacy" pela V7 em `40d5db1` (09-11); não deletada |
| V7 | 2026-09-11 `40d5db1` | 2026-09-11 `ec39358` (HEAD) | 1 dia | 4 | Padrão atual |
| V8 | 2026-09-12 (data dos arquivos) | sem commit | — | 0 | Só no working tree; opt-in por `--lighting-v8` |

Entre `9eec50f` (2026-08-20) e `40d5db1` (2026-09-11) não há commit nenhum.

---

## V1 — `WorldLightingSystem` (nome "V1" não aparece no histórico)

- **Quando:** 2026-07-12 → 2026-07-29.
- **O que tentou:**
  - `6dd077c` (07-12): sombras por flood-fill a partir do céu, "Terraria/Starbound style".
  - `cedc675` (07-20): cria `WorldLightingSystem`, uma BFS com canais RGB independentes a partir de aberturas de céu e tochas, com textura esticada em multiply e brilho aditivo de tocha. Substitui o "old ambient-tint hack".
  - `1023896` (07-21): areia passa a atenuar; curva de gama.
  - `6b0e773` (07-28): luz entrando pelas aberturas do fundo.
  - `6dd5dc6` (07-29): "new lighting pipeline" com render targets e `ComposeLighting.fx`, mais `DirectionalSunlightMap`, penumbra e bloqueio (+3742 linhas).
- **Último commit antes do abandono:** `08c2481` (07-29), "Checkpoint: BFS lighting working, DirectionalSunlightMap incomplete".
- **Motivo:** parcialmente recuperável.
  - `6dd5dc6` desliga a composição por shader porque ela "causes black screen".
  - `08c2481` registra que `BuildDirectionalSunlightMap` não aplicava o mapa de bloqueio.
  - No dia seguinte, `539f2d2` trata o sistema como "legado" e declara "nenhuma lógica legada foi reutilizada".
  - A decisão de recomeçar não é justificada em commit.
- **Sobras hoje:** `WorldLightingSystem.cs` (sem `new`), `ComposeLighting.fx` (ainda carregado na factory), métodos `Build*Map`/`ApplyLightingComposition` em `PlayingState` e `Ensure*/Get*Map` no `ViewCoordinator`, todos sem chamador (ver [[01-estado]]).

## V2 — `Engine/Graphics/LightingV2/`

- **Quando:** 2026-07-30, um único commit.
- **O que tentou:** `539f2d2` fez "Phases 0-2": auditoria do legado, 13 arquivos-base (settings, resources, system, renderer, 4 interfaces adaptadoras) e um pipeline de 8 fases com composição **neutra** (luz branca, sem cálculo). O toggle Ctrl+L alternava Legacy/V2. A fase seguinte planejada era "Ambient Sky Light".
- **Último commit antes do abandono:** `539f2d2`. Na manhã seguinte, `7194ede` já cria `LightingPipelineMode` com Legacy/**V3**. Os 16 arquivos foram apagados em `cc0f24d` (08-05), commit cuja mensagem fala de outra coisa (placeholders de caverna).
- **Motivo:** motivo não recuperável do histórico.
- **Sobras:** comentários "Lighting V2" em `PlayingSessionViewCoordinator.cs:154,163`.

## V3 — `LightingV3Foundation` e SunVisibility

- **Quando:** 2026-07-31 → 2026-08-03, 80 commits em 4 dias.
- **O que tentou:**
  - Fase 0: isolar o pipeline com contadores.
  - Fase 2: fundação espacial com frame slots duplos imutáveis, classificação de células e campos de oclusão sub-tile (2×2 amostras por tile). Declarada "sealed" em `9b553f9`.
  - Fase 3.1: provedor solar (direção e cor).
  - Fase 3.2A: campo de visibilidade do sol por DDA ray marching, com 28 testes.
  - Fase A0.0: profiler de frames renderizados, capturas de emergência e ablações A/B/C/D.
- **Último commit antes do abandono:** `127468f` (08-03), "rigorous ablation tests A/B/C/D for FPS bottleneck diagnosis". Núcleo, debug e validadores foram apagados em `cc0f24d` (08-05), junto do `LIGHTING_V3_ARCHITECTURE_STATE_REPORT.md`.
- **Motivo:** inferível (não declarado) — **performance**.
  - `PHASE_3_2A_PERF_ROADMAP.cs`, ainda no repo, marca "CURRENT PERFORMANCE (REJECTED)": build de 17,122 ms com meta ≤ 3 ms, e 1,9 milhão de células visitadas por frame.
  - `5dcf05b` diz que SunVisibility era reconstruída a cada frame, sem cache.
  - `3ab19ce`/`e5f6938` instrumentam "starvation" e travamentos; `127468f` ajusta timeouts para testes rodarem "even at 2 FPS".
  - O commit de remoção não cita o motivo.
- **Sobras hoje:** `RenderedFrameProfiler`, `FrameProfiler`, `SunVisibilityRayMarcher`, `SunVisibilityMetricsAggregator`, `OccluderField`, `SceneWorldClassifier`, `ActiveLightingRegion`, `AllocationMeasurementHarness`, `LightingTestRunner.cs`, `TestConsole/`, e dois `.cs` só com comentário (`AUDIT_ACTIVE_REGION.cs`, `PHASE_3_2A_PERF_ROADMAP.cs`). Os docs `Docs/PHASE_3_2A_*.md` também são da V3.

## V4 — probe de capacidades MonoGame

- **Quando:** não recuperável. O único rastro entra no repo em `cc0f24d` (2026-08-05 13:03), 40 minutos antes do primeiro commit da V5.
- **O que tentou:** o README e o `Program.cs` de `LightingV4CapabilityProbe/` descrevem a fase "V4.2B-B: MonoGame Capability Validation". É um harness **isolado** ("NO integration with Nyvorn main"), com testes de RenderTarget e shaders com loops de 8/16/32/64 iterações (`Loop*.fx`). Status: "Implementation in progress"; gate: "Review results before V4.2B-C (spikes)". O manifesto citado (`LIGHTING_V4_2B_B_TEST_MANIFEST.md`) não existe no disco. V4.0–V4.2A não deixaram rastro.
- **Último commit antes do abandono:** `cc0f24d`.
- **Motivo:** motivo não recuperável do histórico.

## V5 — Sky Ambient Prototype

- **Quando:** 2026-08-05 → 2026-08-10.
- **O que tentou:** `e008cd0`, "V5 Sky Ambient Prototype 0.1": luz ambiente guiada por "portais de céu" posicionados manualmente, propagação com falloff direcional, atenuação do foreground em 5 níveis de profundidade e tint pela cor do céu. 7 arquivos, com a nota "NOT integrated yet". O README (ainda em `LightingPipeline/`) mostra as fases 2 (integração) e 3 (visualizador) como TODO.
- **Último commit antes do abandono:** `fafaeee` (08-10), o primeiro commit da V6, que ainda mexe nos arquivos V5. Deletada em `28df7ae` (08-11): "Deleted 7 V5 sky-ambient files (obsolete prototypes)".
- **Motivo:** parcialmente recuperável. O protótipo nunca foi integrado e foi declarado "obsolete" quando a V6 virou pipeline único. Por que a V6 venceu não está escrito.

## V6 — `V6LightingSystem` (+ camada Pixel P1/P2)

- **Quando:** 2026-08-10 → rebaixada em 2026-09-11. O desenvolvimento ativo vai de 08-10 a 08-17/08-20.
- **O que tentou:**
  - `fafaeee`→`28df7ae`: lightmap por tile em 5 passos (classificação, luz de superfície, brilho de fundo, acoplamento fundo→frente, tochas), máscara `ProductionTexture` em multiply e oclusão por porta.
  - `615eed1` implementou tocha por BFS e foi revertido no mesmo dia (`c8f1f15`), trocado por atenuação por linha ponderada "starbound-style" (`fec0155`).
  - `28df7ae` consolidou a V6 como única, removendo V5 e os caminhos legados, e criou `LIGHTING_BASELINE.md` (hoje `LIGHTING_BASELINE_2026-08-11.md`).
  - P1/P2 (08-12 → 08-14) adicionaram `WorldColorRenderTarget`, `PixelLightBuffer`, `PixelComposite.fx`, reconstrução linear, curva e cor da tocha e tint noturno; `9202e62` tornou o modo Pixel oficial.
  - Depois vieram fundos subterrâneos e parallax (`e0fb030`, `58a9103`, `b9ae266`, `9eec50f`).
  - A tag `checkpoint/p2e-baseline-stable` (`e939065`, 08-18, fora de `physics`) desfaz um "protótipo V6 descartado" de luz no parallax.
- **Último commit antes do abandono:** `b9ae266` (08-17, "Ultima att") é o último a mexer em arquivo V6; `9eec50f` (08-20) é o último antes da V7. Em `40d5db1`, a V7 vira padrão e "Legacy mode is unchanged V6".
- **Motivo:** motivo não recuperável do histórico. Os commits da V7 descrevem o que ela faz, não o que faltava na V6.
  - *Fora do git:* `Docs/Design/LightingV8/LIGHTING_V7.md` (não versionado) lista limitações da V6: tocha com raio de 9 tiles e falloff linear, luz do céu sumindo em 10 tiles, luz presa em 0..1, `SkyOpen` forçado a branco, passe de tochas oscilando de 0 a 35 ms com 28 fontes.
- **Sobras hoje:** todo o `LightingPipeline/V6*.cs`, `V6Validator/`, e o modo `Legacy` alternável por F9.

## V7 — `Engine/Graphics/LightingV7/` (padrão atual)

- **Quando:** 2026-09-11, quatro commits entre 11:46 e 16:50. É o padrão desde `40d5db1` (`lightingMode = LightingPipelineMode.V7`, `PlayingState.cs:141`).
- **O que fez:**
  - `40d5db1` (fase 1):
    - Lightmap por tile numa região ativa (tiles visíveis + 48 de margem): canal de céu escalar e canal de bloco RGB semeado por tochas, com decaimento multiplicativo em varreduras "Terraria-style". Sai 1 texel por tile, com upscale bilinear.
    - Reaproveita a infraestrutura Pixel da V6; `PixelComposite.fx` ganha `OverbrightScale` e `PosterizeLevels`.
    - No modo V7 ficam desligados o update da V6, o tint noturno de superfície e a amostragem de luz por entidade; as chamas de tocha vão para um passe emissivo.
    - Teclas: F9 alterna V7/Legacy, F10 cicla visualizações, F12 salva PNGs. Cria `LightingV7Validator/`.
    - Custo: 1,26 ms em Release, com meta de 1,5 ms.
  - `b404ae6`:
    - O parallax subterrâneo passa a ser desenhado dentro do `worldRT` e iluminado.
    - O upload de textura passa a alternar entre 3 texturas, eliminando stall de até 12 ms.
    - Variáveis `NYVORN_V7_AUTOSHOT`/`NYVORN_V7_VIEW`/`NYVORN_LIGHTING` permitem captura sem teclado.
  - `7448730` (fase 1.5):
    - Luz direta com sombra dura: percurso Amanatides-Woo por tile, em que plataformas bloqueiam e parede de fundo não.
    - Bounce indireto, AO por vizinhança 3×3 e combinação por máximo de canal.
    - Novos acessores `LiquidSystem.HasLiquidInRow` e `WorldMap.GetTilePairWrapped`.
    - Custo: 2,16 ms em Release. F8 liga/desliga as sombras.
  - `ec39358` (fases 2 e 3):
    - Tecido e cogumelos viram fontes emissivas, com halo aditivo por fonte e flicker de 3 oitavas.
    - `LightingV7Sky` tem curva de céu própria por hora, incluindo chuva, eclipse e conjunção das luas; ignora `SkyState.AmbientLight` de propósito.
    - Sol direcional por varredura inclinada.
    - Decaimento por canal na água; areia solta atenua por estimativa de preenchimento.
    - F6 acelera o relógio, F7 liga/desliga o sol.
- **O que ficou incompleto:**
  - `ec39358`: "Not seen on screen: a diagonal sun shaft through an opening in a real scene" — provado só numericamente.
  - `LightingPipelineMode.cs` descreve o Legacy como "V6 lighting (kept until V7 is approved)", e nenhuma aprovação aparece registrada.
  - `LightingV7Emissive` tem a tabela vazia ("Every tile type in the game is inert today") e não é referenciado em lugar nenhum.
  - Em `40d5db1`, o validador da fase 1 cobria os cenários 1–4 e 7.
- **Último commit:** `ec39358` (2026-09-11 16:50), o HEAD. Nenhum arquivo de `LightingV7/`, de `LightingPipeline/` ou `PixelComposite.fx` foi modificado desde então no working tree.

## V8 — direta + ambiente (só no working tree)

- **Quando:** a data de modificação dos arquivos de código vai de 2026-09-12 09:47 a 21:31; o doc de estado é das 21:38. Não há commit.
- **Arquivos:**
  - Novos, não rastreados:
    - `Nyvorn/Source/Engine/Graphics/LightingV8/`: 11 arquivos, 1.462 linhas (`V8Geometry`, `V8LightingRenderer`, `V8AmbientContext`, `V8AmbientField`, `V8GameplayOptions`, `V8DiagnosticGame`, `V8DiagnosticScene`, `V8Validation`, `V8AmbientValidation`, `V8GraphicsProbe`, `V8Capture`).
    - `Nyvorn/Source/Game/States/PlayingState.V8.cs` (174 linhas) e `PlayingState.V8Capture.cs` (223).
    - `Nyvorn/Content/effects/V8Direct.fx`, `V8Faces.fx`, `V8Receiver.fx`.
    - Docs: `Docs/LightingV8/V8_ESTADO_E_DECISOES.md`; `Docs/Design/LightingV8/` (`LIGHTING_V7.md`, `NYVORN_V8_PESQUISA_E_AUDITORIA.md` e 4 imagens de referência).
  - Modificados, rastreados (+70/−13):
    - `Program.cs`: lê `V8GameplayOptions` e escolhe `V8DiagnosticGame` (`--v8-probe`/`--v8-scene`) ou `Game1`.
    - `Game1.cs`: em mundo temporário, abre janela 1280×720 e uma sessão procedural (preset Small, seed `V8-GAMEPLAY-2026`) no lugar da seleção de mundos. O título da janela indica V8 ou V7, e o jogo fecha quando a captura termina.
    - `PlayingState.cs`:
      - A classe vira `partial` e chama `InitializeV8`, `UpdateV8Input` e `DisposeV8`.
      - Com `v8Renderer` ativo, o `Draw` desvia para `DrawV8Gameplay` antes do caminho V6/V7, e o F9 fica desabilitado.
      - Em mundo temporário não há save nem autosave.
      - `--v7-gameplay-smoke` captura o frame 30 e encerra.
    - `WorldMap.cs`: propriedade `NeutralLightingAlbedo` e `tint` opcional em `DrawBackground`.
    - `DoorRuntimeSystem.cs`: `Draw(openOnly)` separa portas abertas e fechadas.
    - `Content.mgcb`: registra os três `.fx`.
- **O que existe:**
  - Ativação por `--lighting-v8` (sem ela, V7). Opções de diagnóstico: `--v8-scene`, `--v8-ambient`, `--v8-probe`, `--v8-gameplay-capture`, `--v7-gameplay-smoke`, `--v8-output`.
  - Segundo [[V8_ESTADO_E_DECISOES]], a V8 tem:
    - luz direta por fonte com sombras por stencil;
    - geometria de sólidos com faces expostas (tiles, plataformas, portas, areia dinâmica);
    - ambiente com grade de céu mais preenchimento RGB local, propagado por 4 vizinhos;
    - como fontes, tochas e cogumelos.
  - Os números de validação que o doc cita (21 cenários e 58 verificações; 107 regressões diretas e 95 ambientais) **não foram reexecutados** nesta auditoria.
- **O que não foi tocado:**
  - Verificado no working tree:
    - Nenhum arquivo de `LightingV7/`, `LightingPipeline/` (V6 e sobras da V3) ou `PixelComposite.fx` foi modificado.
    - A V7 continua padrão.
    - Nenhum arquivo de `Persistence/` foi alterado, e `NeutralLightingAlbedo` está comentado como "not persisted".
  - Segundo o doc:
    - A V8 não tem sol, AO, bloom, penumbra, halos, flicker, normal maps, SDF nem GI.
    - O ramo V8 não executa cálculo/composição V6/V7, tint noturno, overlays de noite/chuva/umidade nem o visual do `InteriorFocus`.
- **Último commit:** nenhum; a base é `ec39358`.
