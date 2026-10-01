# V8 — estado e decisões efetivas

## Estado atual e entrada

A V8 de direta + ambiente está integrada ao **gameplay normal por opção explícita**, além do diagnóstico. V7 continua padrão. A integração seguiu o acompanhamento autorizado: relatório enviado à conversa “Pesquisa de iluminação 2D”, resposta lida e próximo escopo executado antes do sol.

Checkout `C:\dev\Nyvorn-Reborn`, branch `physics`, HEAD `ec39358c71c67fd05a05063c5f52aa148fb35583`. Sem commits, mudanças de dependências/perfil ou alteração global de `.gitignore`. Preservados os não rastreados anteriores, incluindo `Docs/Design/LightingV8/`, `hotmart.html`, `html.html` e `nyvorn_v6_diagnostic.txt`. Não foram encontradas instruções `AGENTS.md`. Markdown e `screenshots/` permanecem ignorados pelas regras existentes; os fontes novos precisam ser incluídos numa futura entrega Git.

Para jogar, na raiz:

~~~powershell
dotnet build Nyvorn/Nyvorn.csproj -c Release
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8
~~~

Abre a seleção normal de mundos; criar/carregar usa o fluxo existente. Janela e sobreposição identificam “V8 direct + ambient”. Sem a opção, inicia V7. A seleção não é persistida no save.

Controles normais: A/D movimento, Espaço salto, E inventário, F interação/porta, Alt construção, clique esquerdo usa o item selecionado, números/roda selecionam hotbar; F12 salva buffers V8 e F11 mantém fullscreen. Zoom conserva o comportamento existente da câmera. F7/F8/F9/F10 de iluminação antiga não alteram o ramo V8.

Diagnóstico isolado independente:

~~~powershell
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v8-scene --v8-ambient
~~~

Somente nesse diagnóstico: F5 alterna fixtures, WASD câmera, setas entidade, 0/1/2/3 fontes/ordem, +/- zoom, F12 captura, Esc encerra. Omitir `--v8-ambient` mantém direta isolada.

## Arquivos e integração

O núcleo fica em `Nyvorn/Source/Engine/Graphics/LightingV8/`:

| Arquivo/símbolo | Responsabilidade |
|---|---|
| `V8Geometry.Update`, `TryFace` | União de sólidos, contornos expostos e partição de faces; tiles, plataformas, portas e areia dinâmica. |
| `V8LightingRenderer.Render`, `BeginReceivers` | Direta/stencil, ambiente, faces, composição por pixel e recursos próprios. |
| `V8AmbientContext.IsExteriorAperture`, `SkyWeight` | Classificação exterior e veto de céu por camada. |
| `V8AmbientField.Update`, `Propagate`, `Reconstruct` | Grade escalar/RGB, melhor caminho, reconstrução sem vazamento diagonal. |
| `V8GameplayOptions` | Opções de processo e validação em sessão temporária. |
| `V8DiagnosticGame`, `V8DiagnosticScene`, `V8Validation`, `V8AmbientValidation` | Regressões originais reutilizando o núcleo. |
| `V8GraphicsProbe`, `V8Capture` | Prova de stencil e captura RGBA sem forçar alpha 255. |

Integração real: `Nyvorn/Source/Game/States/PlayingState.V8.cs`, métodos `InitializeV8`, `CollectV8Sources`, `DrawV8Gameplay`, `DrawV8Receivers`, `SaveV8GameplayFrame`, `DisposeV8`. `PlayingState.cs` tornou-se parcial e desvia antes do cálculo/desenho V6/V7. `PlayingState.V8Capture.cs` automatiza intervenções e medições no mundo procedural usando o mesmo ramo.

`Game1.cs` conserva a seleção normal; somente flags de teste criam sessão temporária diretamente. `Program.cs` lê opções antes de escolher Game1/diagnóstico. `WorldMap.DrawBackground` aceita tint opcional branco, mantendo padrão 104/255; `NeutralLightingAlbedo` neutraliza também partículas de background apenas na sessão V8. `DoorRuntimeSystem.Draw(openOnly)` separa receptores de portas abertas/fechadas.

Efeitos: `Nyvorn/Content/effects/V8Direct.fx`, `V8Faces.fx`, `V8Receiver.fx`. Registro `Content.mgcb` da entrega direta conservado.

## Direta, composição e atualização

Fontes usam pixels de mundo, preservando coordenadas fracionárias. Tochas reais: `TorchInstance.LightOrigin`, radiância (1,0.65,0.28), raio direto 280 px, sem flicker. Cogumelos existentes recebem somente preenchimento local fraco: radiância (0.2,0.8,1), intensidade 0,12, alcance 128 px. Coleta considera wrap e maior alcance direto/ambiental mais margem de faces. Fontes/bloqueadores fora da tela participam.

Background não entra na ocupação. Plataformas usam `SurfaceBounds` 8×3; portas fechadas, `Bounds` 8×24. Contornos internos desaparecem. Quads de sombra formam união no stencil por fonte; limpar somente stencil preserva soma RGB. Falloff suave e suporte finito. Fontes dentro de opacos não produzem direta.

Cada pixel sólido escolhe a face axial exposta mais próxima, desempate estável, sem duplicar cantos. Amostra o primeiro pixel de ar externo. Direta e local: decaimento quadrático em até oito pixels internos. Céu: faixa suave de 12 px (1,5 tile, desde 2026-09-15). Núcleo preto, sem transportar pela massa. Areia dinâmica fornece segmentos reais de pixels à geometria; mudanças invalidam a união independentemente de TileRevision.

O desenho usa terreno/cache, decorações e entidades reais. Receptores recebem `(direct + sky + local) × albedo` uma vez; foreground usa a soma calculada nas faces. Câmera efetiva e offset de wrap entram também na amostragem. Céu/parallax ficam fora da iluminação de tochas; HUD e preview de construção são desenhados depois. Chamas usam o sprite existente sem segunda iluminação, antes dos opacos que as ocultam.

O ramo V8 não executa cálculo/composição V6/V7, ProductionTexture, PixelComposite, emissivos/halos V7, tint noturno de grass, tint ambiental de decorações, escurecimento de background, InteriorFocus visual ou overlays antigos de noite/chuva/molhado. Tissue conserva simulação e núcleo como receptor, sem halo/overlay de campo. V7 conserva chamadas normais. Não foram acrescentados sol, AO, bloom, penumbra, halos, flicker, normal maps, SDF ou GI.

## Ambiente e exterior

Exposição escalar em grade colorida pelo `SkyState.AmbientLight` real e preenchimento RGB local são independentes. Propagação por quatro vizinhos, perda por distância e maior contribuição por caminho; sem soma cíclica. Fontes distintas somam uma vez; réplicas no wrap compartilham melhor caminho.

Ar: custo 1. Background não altera transporte. Sólidos e células de portas fechadas bloqueiam. Plataformas são transitáveis com custo 1,75 por célula, aproximação ambiental; direta continua fina. Para areia, qualquer pixel numa célula bloqueia transporte ambiental naquela célula, aproximação conservadora. Direta/faces continuam por pixel; areia esparsa pode apresentar transição ambiental mais escura.

ExteriorAperture exige foreground/background vazios e camada elegível. `ExteriorOverride=false` pode excluir vazio decorativo interno, mas **esse dado não foi inventado no gameplay**: o mundo atual não distingue tais vazios. Fissuras de background em Surface/Shallow elegíveis continuam exteriores mesmo sob teto, sem exigir abertura vertical.

Space/Surface são elegíveis; Shallow desvanece nas últimas seis linhas, chegando a zero na sua última linha. Cavern/DeepCavern/unknown: zero exato após interpolação e no receptor sólido das faces. Céu: intensidade 0,5 e alcance 24 tiles. Local da tocha: intensidade 0,24 × radiância e alcance 256 px independente da direta. Bilinear ignora sólidos e rejeita diagonal quando as duas rotas ortogonais estão fechadas. Nenhum buffer direto é suavizado para produzir ambiente. Texturas/arrays reutilizados; alpha do céu guarda elegibilidade da camada e é preservado nas capturas.

## Execuções e resultados

Release final passou: zero erros, mesmos dois CS0649 preexistentes (`PlayingState.debugPixelLightBuffer`, `PlayingSessionViewCoordinator.lightTexture`). DesktopGL/Reach, dependências MonoGame 3.8.5.1 e manifesto MGCB 3.8.4.1 preservados. Runtime confirmou Depth24Stencil8, máscara 256/256 e limpeza somente de stencil. `git diff --check` passou.

Comandos efetivamente executados:

~~~powershell
dotnet build Nyvorn/Nyvorn.csproj -c Release
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-gameplay-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/gameplay-delivery
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v7-gameplay-smoke
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v8-scene --v8-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/gameplay-direct-regression
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v8-scene --v8-ambient --v8-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/gameplay-ambient-regression
git diff --check
~~~

Rodadas anteriores mudaram somente a pasta de gameplay para `gameplay-run1`, `gameplay-baseline`, `gameplay-optimized`, `gameplay-validated` e `gameplay-final`. Run1 revelou zoom automático variando: duas asserções falharam por bounds diferentes. Captura passou a fixar zoom antes de cada desenho; gameplay interativo mantém câmera normal.

Rodada final `gameplay-delivery`: exit 0, **21 cenários e 58 verificações aprovadas**, zero falhas. Sessão real de PlayingSessionFactory, seed `V8-GAMEPLAY-2026`, preset Small, 2800×900 tiles. Superfície procedural não editada; câmaras subterrâneas construídas por script nesse mundo. PlayingSession.Update/simulação continuam entre frames. Mineração/construção usam TryBreakTile/TryPlaceTile e equivalentes de background; porta abre por TryToggle. Testa APIs e atualização/renderização reais; não automatiza todos os cliques/inventário. Sessão temporária sem save de usuário; autosave e save de saída desativados somente nos testes temporários.

| Critério | Resultado observado |
|---|---|
| Superfície e Shallow dia/noite | Céu noturno mais fraco; janela sob teto admite ambiente. Construir background fecha entrada e escurece sala. |
| Parede/plataforma | Sombras recortadas, faces expostas e núcleo escuro. Mineração abre passagem; reconstrução restaura direta original. |
| Cavern/Deep | Céu zero; fonte ligada ilumina, removida zera direta e preenchimento interno sem residual. |
| Porta | Interação abre e permite luz no outro lado; porta aberta permanece receptora. |
| Câmera/zoom/wrap | 1.050–1.760 amostras por comparação: erro máximo direto 1/255, local zero; inclui movimento entre frames e zoom 2/3/4. |
| Areia | 2.048 pixels dinâmicos classificados; faixa exposta visível e interior preto. |
| Recursos | Contagem gráfica estável após warmup em todos os casos. |
| V7 | Smoke sem --lighting-v8 confirmou mode=V7, exit 0; captura `screenshots/lighting_20260912_213051_final.png`. |

Regressões: **107 diretas + 95 ambientais**, exit 0. SHA-256: **14/14 buffers diretos** idênticos a `screenshots/v8/validated/`. Inspecionados PNGs reais de superfície, dia/noite, janela/fechamento, profundidade ligada/desligada, mineração/reconstrução, portas, múltiplas fontes, wrap, areia e buffers. Build não substituiu inspeção visual.

## Custo, capturas e limites

Medições: 1280×720, 30 frames/cenário, dez de warmup e 19 amostras sem gravação. CPU inclui coleta, geometria, ambiente, preparação e submissão ao driver; **não é duração GPU**. GPU não medida.

Casos estáticos em zoom 3 caíram de aproximadamente 12–13 ms para 6 ms ao mover consultas de camada/cor de cada pixel para cada linha/frame. Câmera em movimento: mediana 16,06 → **6,73 ms**, alinhando a região de contornos a 64 px com invalidação por edições; p95 ainda **17,62 ms** nas reconstruções. Quatro fontes/zoom 2: 10,26 ms; zoom 4: 4,45 ms. Frame mediano próximo de 16,67 ms com sincronização. Não extrapolar para 1920×1080, dezenas de fontes ou outros GPUs.

Capturas finais em `screenshots/v8/gameplay-delivery/`: 21 composições e buffers `_albedo`, `_direct`, `_sky`, `_local`, `_foreground`. `run.txt`, `timings.txt`, `checks.txt` e `*_frame.txt` guardam evidência. Galeria `index.html` e pacote `V8_GAMEPLAY_12_CAPTURAS.zip` facilitam revisão.

A conversa “Pesquisa de iluminação 2D” recebeu relatório textual, cuja resposta foi lida. O navegador disponível abriu ChatGPT sem sessão iniciada; a ferramenta entre conversas aceita texto, sem anexos. **Não houve envio de imagens a essa conversa** e não se alega revisão remota delas. Capturas locais e inspeção desta tarefa são evidências distintas.

Limites: Color satura somas fortes; semente ambiental local aproximada pelo tile; não existe marcação persistente de vazio interior; areia usa aproximação conservadora na grade; ambiente ainda reconstruído/uploadado pela CPU. Contornos podem reconstruir ao cruzar regiões ou editar mundo/objetos/areia. Redimensionamento manual, sessão prolongada e muitos emissores não foram medidos. Formato de save inalterado.

Próxima ação: jogar com `--lighting-v8`, revisar contraste/custo no mundo habitual e decidir ajustes com evidências. Sol e demais efeitos ficam fora desta entrega. Sem perguntas bloqueantes ou commits.

## 2026-09-14 — Relato: background natural da Shallow sem luz da tocha

**Resultado: o defeito não se reproduziu como descrito. Nenhuma correção de iluminação foi aplicada.**

Estado encontrado: branch `physics`, HEAD `abb6064` (commit "V8: iluminacao direta + ambiente (stencil)"). As seções acima citam HEAD `ec39358` e "sem commits"; o núcleo V8 agora está commitado. `.gitignore` tem alteração local anterior removendo `*.md` (as seções acima dizem que Markdown é ignorado). Alteração preservada, sem mudança. Não havia capturas F12 da sessão do relato (`screenshots/v8/gameplay/` contém somente `probe.txt` e `stencil.png`).

Leitura de código: background gerado e colocado seguem o mesmo caminho (`WorldMap.DrawBackground` → `V8LightingRenderer.BeginReceivers(foreground:false)` → `V8Receiver.fx`, `albedo × (direct + sky + local)`). O cache de chunks só assa foreground (`TryGetTileSprite` lê `GetTile`). Worldgen: `BaseTerrainFillPass.FillBackgroundShallowUnderground` preenche a Shallow com Dirt; `CavePass.CarveBackgroundFissures` esvazia fissuras de background. `SurfaceBackgroundPass` não está registrado em `WorldGenerator`.

Reprodução: nova opção `--v8-background-probe` (exige `--lighting-v8`; sessão temporária, seed `V8-GAMEPLAY-2026`, Small, sem save). O probe procura uma caverna procedural da Shallow não editada, com tocha no chão e uma parede de background gerado (Dirt, vizinhança 3×3 Dirt) a 5–7 tiles, linha livre. Também procura uma fissura (background vazio). Cada caso tem 30 frames; o último é capturado.

| Caso | Célula | Albedo | Direta | Céu | Local | Final (medido / previsto) |
|---|---|---|---|---|---|---|
| natural-lit | (618,257) fg Empty, bg Dirt gerado | 0,331/0,218/0,159 | 0,732/0,476/0,205 | 0 | 0,193/0,125/0,052 | 0,307/0,130/0,041 ‖ 0,306/0,131/0,041 |
| natural-off | idem, sem fonte | idem | 0 | 0 | 0 | 0 ‖ 0 |
| placed-lit | mesma célula; `TryBreakBackgroundTile` + `TryPlaceBackgroundTile(Dirt)` | idêntico | idêntica | 0 | idêntico | idêntico ao natural |
| placed-off | idem, sem fonte | idem | 0 | 0 | 0 | 0 |
| placed-blocked | coluna Stone por `TryPlaceTile` entre tocha e célula | idem | **0** | 0 | 0,148/0,096/0,040 (contorna) | 0,049/0,020/0,006 ‖ 0,049/0,021/0,006 |
| fissure-lit | (455,218) fg Empty, **bg Empty** | **sem receptor (64/64 px alpha 0)** | 0,732/0,476/0,205 | 0,478/0,486/0,498 | 0,193/0,125/0,052 | 0,377/0,569/0,365 = céu/parallax |

Classificação do quadro inteiro (linhas de HUD excluídas):
- natural-lit: 154.752 px de células com background, 0 sem receptor, 137.742 com direta. Final difere de `albedo × soma` em mais de 8/255 em 180 px (0,13%, máx. 118). O desvio é compatível com o sprite da chama, desenhado sem iluminação; não foi isolado.
- fissure-lit: 93.696 px de células sem background mostram céu/parallax, 81.639 deles dentro do alcance direto da tocha e 18 cobertos por algum receptor. Outros 2.859 px de células com background não têm alpha de receptor, compatível com transparência do sprite de autotile na borda da fissura (não isolado).
- Na seed, 5.934 das 30.432 células de ar da Shallow (19,5%) têm background vazio.

Inspeção visual (PNGs abertos): `bgprobe-natural-lit_final.png` mostra o background gerado iluminado em cunha a partir da tocha. `bgprobe-placed-blocked_crop.png` mostra sombra dura da coluna e só preenchimento local fraco atrás. `bgprobe-fissure-lit_final.png` mostra céu e montanhas nas fissuras ao lado da tocha, sem luz da tocha, enquanto o Dirt de background vizinho está iluminado. `bgprobe-fissure-lit_albedo.png` confirma essas áreas sem receptor.

Observação: a contribuição não desaparece em nenhuma etapa para background tile, natural ou colocado. Ela só "desaparece" onde a célula não tem background: ali o pixel visível é o céu/parallax desenhado antes dos receptores.
Hipótese (não confirmada): a superfície do relato seria uma fissura (cenário distante), não um background tile. Isso decorre das decisões vigentes ("céu/parallax ficam fora da iluminação de tochas"; fissuras sem background na Shallow contam como aberturas exteriores). Mudar isso é decisão de design, não correção estrutural. Por isso nada foi alterado.
Dados que faltam: captura F12 (gera `_final`, `_albedo`, `_frame.txt` com posição do jogador) ou coordenadas de tile da superfície relatada, mundo/seed usado e zoom. Com isso o probe distingue background tile de cenário distante nesse ponto exato.

Arquivos alterados nesta tarefa (somente diagnóstico, ativo apenas com a opção):
- `Nyvorn/Source/Game/States/PlayingState.V8BackgroundProbe.cs` (novo): busca de sítio, casos, rastreamento por célula, classificação do quadro e recortes.
- `Nyvorn/Source/Engine/Graphics/LightingV8/V8GameplayOptions.cs`: `BackgroundProbe`, incluído em `TransientWorld`.
- `Nyvorn/Source/Game/States/PlayingState.V8.cs`: chamadas Initialize/Prepare/Finish do probe, condição de captura e nome dos arquivos.

Nenhuma alteração em renderer, shaders, ambiente, geometria, intensidades ou worldgen.

Comandos executados:

~~~powershell
dotnet build Nyvorn/Nyvorn.csproj -c Release
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-background-probe --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/bg-probe-before
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-gameplay-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/gameplay-bgprobe-regression
git diff --check
~~~

Resultados:
- Build Release antes e depois: 0 erros, os mesmos dois CS0649. Um build intermediário falhou (`Vector3.Abs` inexistente) e foi corrigido.
- Probe: exit 0; `probe.txt`, `console.txt`, buffers `_final/_albedo/_direct/_sky/_local/_foreground`, `_frame.txt` e `_crop.png` por caso em `screenshots/v8/bg-probe-before/`.
- Regressão de gameplay: exit 0, **58 PASS, 0 FAIL**.
- `git diff --check` passou.
- Regressões `--v8-scene` diretas/ambientais e smoke V7 não foram reexecutadas: os caminhos não foram tocados.

Não há capturas "depois", porque não houve correção.

Pendências:
1. Confirmar com o relato qual superfície estava sem luz (dados acima).
2. Se for fissura: decidir se o cenário distante visível pela fissura deve responder a fontes locais. É mudança de convenção, a ser tratada junto da revisão do ambiente exterior, separada desta tarefa.
3. Transparência do autotile de background na borda de fissuras (2.859 px no quadro), só se for relevante visualmente.

### Encerramento (2026-09-14)

O usuário confirmou manualmente, com a V8 ativa, que a iluminação das tochas funciona, inclusive no background da Shallow. **Investigação encerrada: não é defeito.** Nenhuma correção foi aplicada. O diagnóstico `--v8-background-probe` fica disponível. As pendências 2 e 3 acima passam para a revisão do ambiente do céu, que é a próxima tarefa de iluminação (direta e ambiente local das tochas preservados; sol direto fora do escopo).

## 2026-09-14 — Alternância V7 ↔ V8 em gameplay (F4) e diagnóstico de FPS

### Como usar

~~~powershell
dotnet build Nyvorn/Nyvorn.csproj -c Release
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj                  # começa em V7 (padrão)
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 # começa em V8
~~~

- **F4** alterna V7 ↔ V8 durante o jogo, sem recarregar o mundo. Dispara no pressionamento; segurar não repete. Não age com o console (F3) aberto.
- A escolha de atalho partiu da varredura de `Keys.*` no código. F1, F2 e F4 não aparecem em lugar nenhum. F5 só existe na seleção de mundos e no diagnóstico V8. Ctrl sozinho é esquiva e Alt alterna construção, então combinações com eles teriam efeito colateral. F6–F10/F12 (V7), F11 (tela cheia), F3 (console), Shift+P/O e Ctrl+Shift+letras/dígitos (profiler) seguem iguais.
- F9 continua alternando V7/V6 Legacy quando a V8 está desligada. Voltar da V8 com F4 retorna ao modo selecionado por F9 (V7 por padrão).
- Indicador: a primeira linha começa com `LIGHTING: V7`, `LIGHTING: V8` ou `LIGHTING: LEGACY V6` e mostra para onde F4 leva. O título da janela segue o pipeline ativo (`Nyvorn — lighting V7/V8`).
- Medições (linhas laranja do HUD):
  - `FRAME <pipeline> frame median X / p95 Y (~FPS) | draw CPU a/b | update CPU c/d (updates por frame) | rest e/f (median/p95, n)`: estatísticas do pipeline ativo desde a última ativação.
  - `previous run:` resumo do pipeline anterior ao sair dele, também escrito no console como `[Lighting] A -> B; A run: ...`.
  - `V8 init … ms (once, excluded)`: custo único de criação.
  - Na V8, a linha ciano detalha a CPU por etapa (geometry, direct, ambient [grid/recon/upload], faces).
- Definições:
  - **frame**: tempo de parede entre chamadas de Draw.
  - **draw CPU**: corpo de `PlayingState.Draw`.
  - **update CPU**: soma dos `Update` do quadro (com passo fixo pode haver mais de um).
  - **rest**: frame − draw − update, ou seja, Present, esperas de GPU/driver, VSync e agendamento.
  - GPU não é medida separadamente.
- Estatísticas reiniciam a cada ativação. O quadro da transição nunca entra. Aquecimento descartado: 60 quadros **e** 1 s. Janela das últimas 1.800 amostras. O quadro de retorno de pausa ou morte é descartado.

### Implementação

- **Seleção:** `PlayingState.LightingToggle.cs` (novo) tem `SetLightingPipeline`, `ActiveLightingLabel`, `IsLightingTogglePress`, atribuição de tempos por pipeline e HUD de estatísticas. `PlayingState.cs` usa `v8Active` no lugar de `v8Renderer != null`: no desvio do `Draw`, na liberação dos atalhos V7 e na captura F12.
- **Só o pipeline ativo calcula:**
  - V7 e V6 calculam dentro do `Draw` (`v7Lighting.BeginFrame/EndFrame`, `v6LightingSystem.Update`), no ramo não V8.
  - A V8 retorna antes desse ramo.
  - `PlayingSession.Update` não executa sistema de iluminação. `InteriorFocusSystem.Update` é estado de gameplay.
  - Contadores `v7ComputeFrames`, `v6ComputeFrames` e `v8RenderFrames` verificam isso na validação.
- **Recursos:**
  - V6 e V7 continuam criados no construtor, como antes.
  - A V8 é criada uma única vez na primeira ativação (`EnsureV8Renderer`, com `V8GraphicsProbe`) e mantida até `OnExit`. `--lighting-v8` só a ativa na inicialização.
- **Dados ao reativar:**
  - V7 não guarda estado entre quadros: a região é reclassificada a cada `BeginFrame`.
  - A V8 já recoleta fontes, recalcula ambiente e invalida a geometria por `TileRevision`, portas, plataformas e areia. Além disso, a ativação chama `V8Geometry.Invalidate()`, para não depender de todo caminho de restauração incrementar a revisão.
- **Sem estado cruzado:**
  - `WorldMap.NeutralLightingAlbedo` segue o pipeline ativo: V8 usa albedo neutro, V7 usa o tint 104 do background.
  - As partículas de background resolviam o tint na emissão e agora o resolvem no desenho (`BlockParticleSystem`, `WorldMap.BackgroundAlbedoTint`). Sem isso, um estilhaço em voo durante a troca ficaria com tint duplicado ou ausente.
  - V7 limpa `WorldColorRenderTarget`/`PixelLightBuffer` a cada quadro. A V8 limpa `v8Final`, `Direct` e `Foreground`. Halos, emissivos, overlay noturno e composição de um pipeline só são desenhados no seu ramo.
- **Medição:** `Engine/Graphics/LightingPipeline/LightingFrameStats.cs` (novo). `Game1.Update` cronometra `_stateMachine.Update` e repassa por `PlayingState.RecordUpdateCpu`. `V8AmbientField` ganhou subtempos (`LastFieldMs`, `LastReconstructMs`, `LastUploadMs`), só instrumentação.
- **Validação e benchmark automatizados:** `PlayingState.LightingToggleBench.cs` (novo), opção `--lighting-toggle-bench` (sessão temporária, seed `V8-GAMEPLAY-2026`, sem save; sem outras flags de iluminação). Mede na resolução normal de gameplay e no zoom padrão da câmera. `--uncapped-frames` desliga VSync e o passo fixo de 60 Hz **só nesse processo**, para medir custo sem teto; não altera perfil gráfico nem o padrão do jogo.
- **Outros:** `V8GameplayOptions` recebeu as novas opções. `Game1` ajustou resolução do benchmark, opção sem teto e título dinâmico. A busca de sítio do `--v8-background-probe` não depende mais do renderer existir.

A sequência do benchmark:
1. **Validação, na caverna procedural:**
   - V7 e V8 com a mesma tocha.
   - Coluna construída com a V8 ativa, depois V7.
   - Coluna minerada com a V7 ativa, depois V8, com buffer direto comparado à primeira ativação.
   - Tocha removida com a V8 ativa, depois V7.
   - Tocha restaurada e porta fechada com a V7 ativa, depois V8.
   - Porta aberta com a V7 ativa, depois V8.
   - Alternância a cada Update por 30 quadros.
   - Captura do backbuffer ao fim de cada fase.
2. **Medição:** cenas `surface`, `cave` (1 tocha + cogumelos) e `cave-8-torches`, na ordem V7, V8, V7, V8. Em cada ativação, transição e aquecimento são descartados e então coletadas 300 amostras.

### Comandos executados

~~~powershell
dotnet build Nyvorn/Nyvorn.csproj -c Release
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-toggle-bench --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/toggle-bench-final-uncapped --uncapped-frames
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-toggle-bench --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/toggle-bench-final-vsync-rerun
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-gameplay-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/gameplay-toggle-regression
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v7-gameplay-smoke
git diff --check
~~~

### Resultados observados

- **Build Release:** 0 erros, os mesmos dois CS0649 preexistentes.
- **Histórico das execuções:**
  1. Uma execução intermediária, com binário anterior, falhou em 1 verificação: "V7 sem fontes após remover a tocha" (4 fontes). As 4 restantes eram cogumelos, que também são fontes V7, e a direta no alvo foi 0. A regra pretendida é refletir a remoção da tocha, então a expectativa passou a exigir exatamente uma fonte a menos que na fase com tocha. Essa execução propagou `exit=1`.
  2. Na mesma execução, a V7 na superfície mediu 32 ms de frame com 4,6 ms de draw CPU. Isso motivou medir Update e subetapas do ambiente, e o benchmark foi refeito.
  3. A execução final com VSync `toggle-bench-final-vsync` terminou no meio de `bench-surface-v7` (16:57:06), sem exceção no console, exit 0 e sem arquivos de resultado. Causa não identificada. As verificações impressas até ali (fases 00–08 e as primeiras de medição) passaram. Foi repetida isoladamente em `toggle-bench-final-vsync-rerun` e completou.
- **Validação** (`toggle-checks.txt`, sem teto e com VSync): **57 PASS, 0 FAIL** em cada uma.
  - V7 padrão; V8 não criada antes da primeira ativação; F4 dispara uma vez e não repete segurado.
  - Em cada fase, só o pipeline ativo calculou: por exemplo, V8 45/45 quadros com V7 0 e V6 0, e o contrário.
  - Indicador e albedo neutro corretos.
  - Coluna construída na V8 → V7 com direta 0 no alvo. Coluna minerada na V7 → V8 com direta > 0 e **buffer direto idêntico à primeira ativação (0 texels diferentes)**, mesma instância e nenhum recurso GPU novo.
  - Tocha removida na V8 → V7 com fontes 5 → 4 e direta 0.
  - Tocha restaurada e porta fechada na V7 → V8 com 5 fontes e direta 0. Porta aberta na V7 → V8 com direta > 0.
  - Alternância a cada Update: cada quadro calculou exatamente um pipeline (sem teto 15 V7 + 15 V8; com VSync 7 + 23), renderer mantido, sem recurso novo.
- **Regressão V8 gameplay:** exit 0, **58 PASS, 0 FAIL**.
- **Smoke V7** (sem `--lighting-v8`): exit 0, `mode=V7`, captura `screenshots/lighting_20260914_165856_*`.
- **`git diff --check`:** passou.
- **Inspeção visual** (PNGs abertos):
  - `toggle-02` e `toggle-04` (V7 depois de atividade na V8): aspecto escuro da V7, sem albedo neutro, halo ou buffer da V8.
  - `toggle-03` e `toggle-07` (V8 depois de atividade na V7): background neutro, porta colocada durante a V7 visível, sem halo ou overlay da V7.
  - O HUD das capturas mostra "warming up" porque as fases de validação têm 45 quadros.
  - A sobreposição das linhas de depuração no topo do modo V7 (`PLAYER LAYER`/`DESIRED PARALLAX` sobre as linhas V7) é preexistente.
- **Inicialização única da V8** (probe + renderer): 16–20 ms nas execuções finais (63 ms na primeira execução do processo), fora de todas as estatísticas. O primeiro quadro após trocar para V8 mediu 50–122 ms e está no aquecimento descartado.

### Medições por pipeline (mediana / p95, 300 amostras após aquecimento)

**Sem teto** (`--uncapped-frames`, backbuffer 1920×1080, zoom 2), custo contínuo:

| Cena | Execução | V7 frame | V7 draw CPU | V8 frame | V8 draw CPU | V8 ambiente (grade+propagação / reconstrução por texel / upload) |
|---|---|---|---|---|---|---|
| surface | 1 | 3,51 / 4,35 ms | 3,39 / 4,18 | 21,73 / 23,80 ms | 21,34 / 23,18 | 20,89 (1,74 / 17,72 / 1,42) |
| surface | 2 | 3,10 / 3,88 | 2,92 / 3,64 | 21,15 / 21,95 | 20,81 / 21,57 | 20,20 (1,67 / 17,31 / 1,22) |
| cave | 1 | 4,89 / 5,60 | 4,68 / 5,30 | 18,98 / 19,51 | 18,65 / 19,14 | 16,31 (2,81 / 12,27 / 1,23) |
| cave | 2 | 4,90 / 5,57 | 4,68 / 5,30 | 19,04 / 19,67 | 18,71 / 19,33 | 16,39 (2,84 / 12,28 / 1,26) |
| cave-8-torches | 1 | 5,40 / 6,37 | 5,14 / 6,06 | 19,75 / 20,21 | 19,41 / 19,86 | 16,96 (3,23 / 12,27 / 1,46) |
| cave-8-torches | 2 | 5,26 / 6,13 | 5,05 / 5,84 | 19,78 / 20,41 | 19,43 / 20,04 | 16,99 (3,25 / 12,27 / 1,47) |

Nessas execuções: update CPU 0,03–0,06 ms (1 Update por quadro); "rest" 0,07–0,31 ms. V8 geometria 0,08–0,20, direta 0,04–0,19, faces 0,11–0,14 ms. V7 CPU de iluminação 2,6–3,2 ms.

**Padrão do jogo** (VSync + passo fixo 60 Hz, `toggle-bench-final-vsync-rerun`, backbuffer 1920×1061, janela limitada pela tela):

| Cena | V7 frame (draw CPU / updates / rest) | V8 frame (draw CPU / updates / rest) |
|---|---|---|
| surface 1 | 16,66 / 16,73 ms (3,50 / 1 / 13,11) | 21,10 / 21,75 ms (20,97 / 1 / 0,08) |
| surface 2 | 32,14 / 37,72 ms (4,55 / 2 / 27,37) | 48,29 / 56,17 ms (21,77 / 3 / 26,26) |
| cave 1 | 32,52 / 37,07 ms (5,74 / 2 / 26,48) | 48,96 / 52,72 ms (19,47 / 3 / 29,15) |
| cave 2 | 32,54 / 36,43 ms (5,72 / 2 / 26,67) | 48,31 / 52,82 ms (19,51 / 3 / 28,50) |
| cave-8-torches 1 | 16,67 / 33,42 ms (5,29 / 0–1 / 11,44) | 19,36 / 19,87 ms (19,23 / 1 / 0,08) |
| cave-8-torches 2 | 16,67 / 33,18 ms (6,06 / 1 / 11,03) | 45,57 / 52,84 ms (20,41 / 3 / 23,39) |

### Diagnóstico de FPS

**Observações**
- Em condições equivalentes sem teto, a V8 custa ~19–22 ms por quadro contra ~3–5 ms da V7. A diferença está no `Draw`. Update e "rest" são desprezíveis.
- Dentro da V8, `V8AmbientField.Update` consome 16–21 ms dos 17–21 ms de CPU da V8. A reconstrução por texel (laço sobre 980×560 texels com `Reconstruct` bilinear e conversão para `Color`) sozinha leva 12,3 ms na caverna e 17,3–17,7 ms na superfície. Grade + propagação: 1,6–3,3 ms. Upload das duas texturas: 1,2–1,7 ms.
- Geometria, direta/stencil e faces somam menos de 1 ms, inclusive com 12 fontes. O número de fontes quase não altera o custo (cave 19,0 → cave-8 19,8 ms).
- Com VSync e passo fixo (padrão), os quadros se agrupam em múltiplos do intervalo de 16,7 ms. A V8 ficou em ~19–21 ms (1 Update) em algumas ativações e em **45–49 ms (~20–22 FPS), com 3 Updates por quadro e 23–29 ms de espera**, em outras. Essa segunda faixa corresponde ao relato de ~20 FPS em jogo.
- A V7 também alternou entre 16,67 ms e 32 ms de mediana com draw CPU igual (~5 ms). Portanto, sob VSync, o tempo de frame não mede diretamente o custo do pipeline: o estado de cadência persiste entre fases.

**Hipótese (não confirmada):** a faixa de 33/50 ms vem do passo fixo de 60 Hz com VSync. Quando um quadro estoura 16,7 ms, o MonoGame executa Updates de recuperação e o Present espera o intervalo seguinte, e esse regime pode se manter depois que a carga cai. Não foi instrumentado `IsRunningSlowly` nem o tempo real de GPU.

**Conclusão:** o custo contínuo que tira a V8 do orçamento de 60 Hz é a reconstrução do ambiente na CPU, por texel, a cada quadro. Não é a direta, o stencil, a geometria nem o número de fontes. Com o padrão VSync + passo fixo, esse estouro de ~3–5 ms acima do orçamento é amplificado para ~48 ms por quadro.

### Pendências

1. Reduzir o custo da reconstrução por texel do ambiente. É mudança na V8 (ambiente) e ficou **fora desta entrega** por instrução. Candidatas a avaliar depois com a mesma medição:
   - reconstruir/amostrar na GPU a partir da grade por tile;
   - não reconstruir quando grade, câmera e fontes não mudaram;
   - reduzir a resolução intermediária.
2. Investigar a cadência VSync + passo fixo (33/50 ms mesmo com CPU baixa na V7) com instrumentação de `IsRunningSlowly` e tempo de GPU, sem mudar o padrão do jogo sem decisão.
3. A execução interrompida `toggle-bench-final-vsync` (16:57:06, exit 0, sem resultados) não teve causa identificada.
4. Não medido: V6 Legacy com F4, tela cheia, outras GPUs, sessões longas, zoom diferente de 2.
5. Sol direto e ajuste do ambiente continuam fora do escopo.

## 2026-09-14 — Reconstrução ambiental V8 movida para o shader

### Evidências e causa

**Relatadas pelo usuário, do PC dele (não medidas nesta máquina):**
- V8 com zero fontes: ambiente 52,44 ms (grade 3,93, reconstrução 46,93, upload 1,58); geometria e direta 0,01 ms cada.
- Execução anterior da V8 com p95 de frame 406,81 ms, compatível com engasgos ao andar.
- V7 perto de 21 ms.

**Medidas aqui** (`screenshots/v8/perf-before-uncapped`, 1920×1080, zoom 2, sem teto, antes da mudança):
- Câmera parada com 0, 1 e 4 tochas: frame mediano 18,2 / 18,8 / 19,1 ms.
- Ambiente 15,9–16,5 ms, dos quais reconstrução 12,3 ms, grade 2,0–2,6 e upload 1,4–1,5.
- Geometria ≤ 0,2, direta ≤ 0,2 e faces 0,04 ms.
- A reconstrução não varia com o número de fontes (12,33–12,34 ms com 0, 1 e 4).

**Causa:** `V8AmbientField.Update` percorria cada pixel de arte de `LightBounds` (980×560 ≈ 549 mil nessa resolução e zoom) chamando `Reconstruct` (quatro células, testes de abertura, renormalização) e convertendo para `Color`. Depois enviava duas texturas desse tamanho por quadro. O custo acompanha a área visível, não as fontes, o que explica o caso de zero fontes. Não vem das sombras das tochas: a direta fica em 0,00–0,2 ms.

### Leitura da V7 (componentes comparados)

- **Representação:** propagação na CPU por tile (céu, bloco RGB, direta, sol, AO), combinada por tile em `FillTexture` (máximo com o céu, AO, piso ambiente, divisão por overbright) e gravada como um texel `Color` por tile.
- **Upload:** textura por tile a cada quadro, girando num anel de 3 texturas para não escrever na que a GPU ainda lê (atraso de ~12 ms medido na V7).
- **Reconstrução e apresentação:** `BuildV7LightBuffer` desenha a textura por tile com `SamplerState.LinearClamp` num `PixelLightBuffer` em espaço de tela. O bilinear de hardware não conhece sólidos e mistura através deles.
- **Composição:** `PixelComposite.fx` multiplica mundo × luz × `OverbrightScale`, com posterização opcional.
- **Teste de visibilidade por tiles:** `IsDirectlyVisible` percorre a grade `occluder` em DDA do tile da fonte ao tile alvo, para o termo direto da V7 dentro do raio. Não é usado no ambiente da V7.
- **Reaproveitado:** representação por célula enviada como textura pequena a cada quadro, anel de texturas e reconstrução na GPU.
- **Não reaproveitado:** o filtro bilinear de hardware (vazaria através de sólidos e quinas), a combinação por máximo/AO/piso (mudaria o comportamento V8) e o DDA (a direta V8 é por geometria/stencil).

### Restrições verificadas

- **Versões e perfil:** `obj/project.assets.json` resolve MonoGame.Framework.DesktopGL **3.8.5.1** (a pasta 3.8.1.263 do cache não é usada). `Content.mgcb` usa `/platform:DesktopGL` e `/profile:Reach`. Os efeitos V8 compilam como `vs_3_0/ps_3_0` no ramo `OPENGL`.
- **O que a DLL impõe:** nas strings UTF-16 da DLL 3.8.5.1, a única restrição de Reach encontrada é a de occlusion queries. Mesmo assim foi usado `SurfaceFormat.Color`: evita depender de extensão de textura float no GL e mantém os formatos já usados pelo pipeline.
- **Ordem dos samplers:** o SpriteBatch liga a textura do sprite no slot 0, então `SpriteSampler` precisa ser o primeiro sampler do efeito do receptor. Isso foi descoberto na prática; ver "Incidente".
- **Caminho DirectX:** a variante `ps_4_0_level_9_1` não foi compilada nem testada; o projeto é DesktopGL.

### Solução

- **CPU:** mantém a grade atual sem mudança de regra: classificação, custo, `SkyWeight`, aberturas exteriores, propagação de céu e local, portas, plataformas, areia e wrap. Envia duas texturas por célula (`FieldTexture`: R exposição do céu, G peso de camada da linha, B célula aberta; `LocalCellTexture`: RGB local ÷ 2), em anel de 3 como na V7. As texturas só crescem, arredondadas em 64 células, e o **anel inteiro é alocado de uma vez**: um quadro repetido e inalterado nunca aloca, e um novo tamanho máximo (margem das luzes ou vista) custa um único evento de alocação. Tamanho típico: cerca de 190×140 células.
- **Shader** (`Content/effects/V8Ambient.fxh`, incluído por `V8Receiver.fx`, `V8Faces.fx` e `V8AmbientResolve.fx`): porte direto de `Reconstruct`, calculado no centro de cada pixel de arte com amostragem por ponto:
  - célula central fechada dá zero;
  - célula fechada não pesa;
  - diagonal é rejeitada quando as duas ortogonais estão fechadas (sem ponte por quina fechada);
  - pesos são renormalizados;
  - o céu é vetado pelo peso de camada **depois** da interpolação, o que mantém céu zero abaixo da Shallow.
- **Local:** gravado dividido por 2 para que somas acima de 1 numa célula não sejam cortadas antes da interpolação; o corte continua só no pixel final, como antes.
- **Receptores:** background, decorações e entidades usam `AmbientAt`. O foreground usa a técnica `ForegroundReceiver`, que lê só o buffer de faces, igual a `AmbientScale = 0`.
- **Faces:** amostram o ambiente no primeiro pixel de ar externo, recuperado da UV da face. O céu é liberado pelo peso de camada do próprio receptor sólido, que antes era o alfa da textura de céu. Núcleo, largura de face e peso não mudaram.
- **Captura e validação:** `SkyTexture`/`LocalTexture` viraram buffers por pixel resolvidos na GPU sob demanda com o mesmo `.fxh`, restaurando os render targets anteriores. Os consumidores (`V8AmbientValidation`, diagnóstico, captura de gameplay, sonda de background) não mudaram e agora validam o caminho de GPU. Esses alvos contam em `DiagnosticResourceCreations`, fora de `ResourceCreations` do caminho de quadro.
- **Referência na CPU:** `V8AmbientField.BuildReferencePixels` guarda a reconstrução antiga, só para validação. `ObserveV8WorldCapture` compara GPU e referência nos 21 casos de gameplay.
- **Não é cache de câmera:** as texturas por célula são regeneradas a cada quadro a partir da grade propagada. Mover a câmera não reintroduz trabalho por texel; só muda os parâmetros de origem.
- **Inalterados:** intensidade, alcance, combinação das fontes, direta e stencil. Sem sol.

**Arquivos:**
- Novos: `Content/effects/V8Ambient.fxh`, `Content/effects/V8AmbientResolve.fx` (registrado em `Content.mgcb`), `Source/Game/States/PlayingState.V8PerfBench.cs`.
- Alterados: `V8Receiver.fx`, `V8Faces.fx`, `V8AmbientField.cs`, `V8LightingRenderer.cs`, `PlayingState.V8Capture.cs` (verificação de equivalência), `PlayingState.V8.cs`, `PlayingState.LightingToggle.cs`, `PlayingState.cs`, `V8GameplayOptions.cs`, `Game1.cs` (opção de benchmark).

### Benchmark `--v8-perf-bench`

- **Sessão:** temporária, seed `V8-GAMEPLAY-2026`, 1920×1080, zoom padrão 2, hora 0,5.
- **Cenários:** câmera parada e trajeto fixo de 160 tiles a +2 px de mundo por quadro renderizado (mesmo caminho independentemente do FPS), cada um com 0, 1 e várias tochas (4 pontos de chão encontrados). As fontes de decoração são suprimidas **só no benchmark**, para contagem exata.
- **Amostras:** descarta apenas os 60 primeiros quadros de cada cenário; todos os quadros seguintes entram, inclusive os de reconstrução de geometria e faces.
- **Isolamento:** sem captura nem leitura de GPU durante a coleta.
- **Saída:** `perf.txt` com mediana/p95/máximo por etapa, contagem de reconstruções e quadros acima de 33/50/100 ms; `perf-frames.csv` por quadro.

### Incidente durante a implementação

O primeiro binário depois da mudança deixou árvores, jogador e cogumelos como silhuetas pretas. Isso apareceu na inspeção de `surface-day_final`; o buffer de céu estava igual. A causa foi declarar os samplers do ambiente antes de `SpriteSampler`, o que desloca o slot 0 usado pelo SpriteBatch. A correção foi mover o `#include` para depois de `SpriteTexture`/`LightTexture`. A cadeia de medições e capturas desse binário foi interrompida e descartada.

Um segundo binário, já com os samplers corrigidos, passou na captura de gameplay, mas as regressões `--v8-scene` falharam uma verificação cada:
- `local/stable-resources`: ambiente 94 PASS / 1 FAIL;
- `primary/stable-resources`: direta 106 PASS / 1 FAIL.

Essas regressões terminaram com a exceção intencional da validação (exit 127). A verificação renderiza o mesmo quadro inalterado duas vezes e exige que a segunda não crie recurso de GPU. O anel criava cada posição na primeira vez em que era usada, e a posição 2 nascia justamente no segundo quadro do primeiro caso. A regra é correta para o caminho de quadro e **não foi alterada**; a correção foi alocar o anel inteiro de uma vez. A cadeia desse binário, que já estava no benchmark, também foi interrompida e descartada. Capturas, regressões e medições "depois" abaixo vêm do binário com as duas correções.

### Equivalência e capturas antes/depois

- **Captura de gameplay** (`gameplay-after-ambient-gpu`): exit 0, **79 PASS, 0 FAIL** (as 58 anteriores mais 21 de equivalência). O erro máximo GPU vs. referência na CPU é **céu ≤ 1/255 e local ≤ 2/255** em todos os casos. Texels de local com erro 2: 0 nos casos sem tocha, 1,2–6,0% nos casos com tochas (`many-sources`: 6.047 de 116.220). É a quantização de 8 bits por célula com local ÷ 2.
- **Comparação de PNGs** (`gameplay-before-ambient-gpu` vs. `after`, script C# em PowerShell):
  - `_direct`: **idêntico** nos 21 casos.
  - `_foreground` (faces): máximo 2/255 nos casos de caverna e Shallow. Superfície dia e noite chegam a 24/255 e 12/255 (55 e 28 pixels > 8), **todos nas colunas x445–446 de um buffer de 447 px**, a margem de 10 px (FaceWidth + 2) fora da área visível. Causa: a amostra externa de faces na borda cai fora de `LightBounds`; antes lia a borda da textura com clamp, agora reconstrói o valor real. Não aparece na tela: o final de superfície dia tem máximo 1, sem pixel > 8.
  - `_final`, excluindo HUD (150 linhas do topo) e hotbar (70 da base), no binário final: **0 pixels > 8** em cavern-off, deep-off, shallow-day/night/sealed, surface-day/night, sand, wall-before e wall-mined (máximo 0–2). Nos demais casos com tocha visível há 81–711 pixels > 8, **somente sobre os sprites de chama**, animados pelo tempo. Conferido nas máscaras de `cavern-torch` e `wrap`; as caixas delimitadoras repetem as posições das chamas.
  - `--v8-scene` (antes vs. depois): modo direto **idêntico** (final, faces e direta com máximo 0 nos 14 casos); modo ambiente com final ≤ 2/255, faces ≤ 3/255, entidade iluminada ≤ 2/255 e direta idêntica nos 17 casos.
- **Inspeção visual:** `surface-day_final` e `many-sources_final` antes e depois, e as máscaras citadas.

### Custo ao andar observado (não corrigido nesta entrega)

No mesmo benchmark "antes", **com a câmera em movimento**:
- As faces são reconstruídas em **639 de 640 quadros** (36,7 ms de mediana), porque a chave de cache de `BuildFaces` inclui `LightBounds`, que muda a cada pixel de câmera.
- A geometria é reconstruída a cada ~32 quadros (7,7–21,9 ms de p95, máximo 32,3 ms): a região alinhada a 64 px troca a cada 64 px percorridos.
- Houve um pico isolado da direta de 73,5 ms em `walk-1-torches`.
- Frame mediano ao andar: 66,7–68,4 ms, contra 18–19 ms parado.

Isso pertence a faces e geometria (direta/foreground), não ao ambiente autorizado, e foi mantido intacto. Fica registrado como o próximo gargalo, com medição.

### Medições antes/depois (`--v8-perf-bench`, binário final)

Valores em mediana / p95 / máximo, em ms. Câmera parada: 600 amostras. Trajeto: 640 amostras, incluindo todos os quadros de reconstrução. Sem capturas durante a coleta.

**Sem teto** (`perf-before-uncapped` → `perf-after-uncapped`, 1920×1080):

| Cenário | Frame antes | Frame depois | Ambiente antes (grade / reconstrução / upload) | Ambiente depois (grade / reconstrução / upload) | Faces depois | Quadros > 50 ms |
|---|---|---|---|---|---|---|
| parado, 0 tochas | 18,18 / 19,19 / 21,51 | **8,64 / 9,41 / 11,49** | 15,90 (2,05 / 12,34 / 1,43) | 6,33 (2,01 / 0,00 / 4,34) | 0,02 | 0 → 0 |
| parado, 1 tocha | 18,83 / 19,28 / 20,77 | **8,81 / 9,43 / 10,09** | 16,32 (2,45 / 12,34 / 1,46) | 6,42 (2,42 / 0,00 / 3,96) | 0,02 | 0 → 0 |
| parado, 4 tochas | 19,07 / 19,59 / 20,12 | **8,41 / 9,26 / 11,10** | 16,48 (2,58 / 12,33 / 1,51) | 6,01 (2,53 / 0,00 / 3,48) | 0,06 | 0 → 0 |
| andando, 0 tochas | 66,67 / 74,18 / 87,35 | 55,65 / 62,55 / 74,67 | 16,54 (1,96 / 12,34 / 2,34) | 6,81 (1,96 / 0,00 / 4,95) | 36,25 | 639 → 639 |
| andando, 1 tocha | 67,80 / 87,60 / 165,14 | 47,85 / 61,43 / 67,49 | 17,19 (2,37 / 12,31 / 2,61) | 10,70 (2,35 / 0,00 / 8,39) | 34,27 | 639 → 98 |
| andando, 4 tochas | 68,12 / 89,19 / 102,93 | 44,13 / 61,66 / 66,90 | 17,45 (2,47 / 12,31 / 2,66) | 6,24 (2,47 / 0,00 / 3,84) | 34,71 | 639 → 45 |

**Padrão do jogo, VSync + passo fixo** (`perf-before-vsync` → `perf-after-vsync`):

| Cenário | Frame antes | Frame depois | Draw CPU antes → depois | Ambiente antes → depois |
|---|---|---|---|---|
| parado, 0 tochas | 18,49 / 19,24 / 21,66 | **16,67 / 16,74 / 17,57** | 18,35 → 5,33 | 16,08 → 3,20 |
| parado, 1 tocha | 19,27 / 19,84 / 20,91 | **16,67 / 16,74 / 16,94** | 19,12 → 6,29 | 16,53 → 3,86 |
| parado, 4 tochas | 19,58 / 20,30 / 88,90 | **16,67 / 16,72 / 17,22** | 19,42 → 6,84 | 16,72 → 4,34 |
| andando, 0 tochas | 67,10 / 74,73 / 79,14 | 57,94 / 63,81 / 71,77 | 66,88 → 57,68 | 16,73 → 9,19 |
| andando, 1 tocha | 68,36 / 88,33 / 140,13 | 58,13 / 73,79 / 81,33 | 68,12 → 57,91 | 17,41 → 8,54 |
| andando, 4 tochas | 68,43 / 89,57 / 95,95 | 57,66 / 74,22 / 80,48 | 68,19 → 57,46 | 17,61 → 7,92 |

**Observações:**
- A reconstrução por texel na CPU saiu do caminho de quadro (12,3–12,6 ms → 0 em todos os cenários).
- Parado, o frame caiu pela metade sem teto e passou a travar em 60 Hz com VSync (antes 18,5–19,6 ms, abaixo de 60 FPS).
- O número de fontes continua sem influência relevante.
- Ao andar, faces seguem reconstruídas em 639 de 640 quadros (34–36 ms) e a geometria a cada ~32 quadros (p95 ~22 ms). O frame ao andar continua acima de 33 ms em todos os quadros. Houve melhora (sem teto: mediana −11 a −24 ms, p95 −12 a −28 ms, máximo 165 → 67 ms em 1 tocha; com VSync, nenhum quadro > 100 ms), mas **os engasgos ao andar não foram eliminados**.
- O upload das texturas por célula mede 3,5–4,3 ms parado sem teto e 1,1–1,5 ms com VSync, e sobe para 5–8 ms andando. As texturas somam cerca de 190×140×2 texels, pouco para esse tempo.
  - **Hipótese, não confirmada** (tempo de GPU não medido): `SetData` virou ponto de sincronização e espera a GPU, que agora faz a reconstrução e, andando, recebe o upload grande do buffer de faces.
- No `walk-0-torches` depois há ~12 ms de draw CPU fora do tempo V8 (55,45 contra 43,54 ms), que não aparecem em `walk-1`/`walk-4` depois. Antes, os três trajetos tinham ~13 ms. **Causa não identificada.**

### Regressões e verificações do binário final

| Execução | Resultado |
|---|---|
| `--v8-scene --v8-capture` | exit 0, **107 PASS, 0 FAIL** |
| `--v8-scene --v8-ambient --v8-capture` | exit 0, **95 PASS, 0 FAIL** |
| `--lighting-v8 --v8-gameplay-capture` | exit 0, **79 PASS, 0 FAIL** (58 anteriores + 21 de equivalência) |
| `--lighting-toggle-bench --uncapped-frames` (F4) | exit 0, **57 PASS, 0 FAIL** |
| `--v7-gameplay-smoke` | exit 0, `mode=V7`, captura `screenshots/lighting_20260914_174629_*` |
| Build Release | 0 erros, os dois CS0649 preexistentes |
| `git diff --check` | passou |

Comandos (saídas em `screenshots/v8/<pasta>`; o comparador de PNG é um script temporário em PowerShell/C#, fora do repositório):

~~~powershell
dotnet build Nyvorn/Nyvorn.csproj -c Release
# antes (binário com reconstrução na CPU)
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-perf-bench --uncapped-frames --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/perf-before-uncapped
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-perf-bench --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/perf-before-vsync
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-gameplay-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/gameplay-before-ambient-gpu
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v8-scene --v8-ambient --v8-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/scene-ambient-before-ambient-gpu
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v8-scene --v8-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/scene-direct-before-ambient-gpu
# depois (mesmos comandos com pastas *-after-*), mais:
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-toggle-bench --uncapped-frames --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/toggle-after-ambient-gpu
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v7-gameplay-smoke
git diff --check
~~~

### Limites

- **Só esta máquina:** medido em 1920×1080 com zoom 2. O ganho no PC do usuário (reconstrução relatada de 46,93 ms) não foi medido aqui.
- **Custo de GPU não medido.** Cada fragmento de receptor de background faz 8 leituras de textura, e o buffer de faces soma mais leituras; em GPU fraca esse custo pode aparecer como espera no upload ou no Present.
- **Quantização:** 8 bits por célula, céu ≤ 1/255 e local ≤ 2/255 em relação à reconstrução anterior. O local de uma célula é limitado a 2,0 antes da interpolação; somas maiores (muitas tochas sobrepostas no mesmo tile) divergiriam, e isso não ocorreu nos casos testados.
- **Faces na borda:** amostras externas fora de `LightBounds` agora usam o valor real em vez da borda da textura. A diferença fica na margem invisível.
- **Grade na CPU:** permanece em 2–3 ms nesta máquina (3,93 ms no relato do usuário).
- **DirectX:** a variante `ps_4_0_level_9_1` não foi compilada nem testada.
- **Benchmark:** suprime fontes de decoração, só para contagem exata de tochas.

### Pendências

1. **Principal gargalo ao andar:** `BuildFaces` reconstrói e reenvia o buffer de faces a cada movimento de câmera (34–37 ms), e a geometria reconstrói a cada 64 px (p95 ~22 ms). Corrigir exige mudar faces/geometria (direta/foreground), fora do escopo autorizado nesta entrega.
2. Medir tempo de GPU e confirmar ou descartar a espera em `SetData` do upload por célula.
3. Investigar os ~12 ms fora do tempo V8 em `walk-0-torches` depois da mudança.
4. Repetir `--v8-perf-bench` no PC do usuário para confirmar o ganho relatado.
5. Sol direto e ajuste do ambiente continuam fora do escopo.

## 2026-09-15 — Faces e geometria reutilizadas (engasgos ao movimentar a câmera)

Autorização: alterar faces e geometria, preservando resultado visual, ambiente na GPU, direta, regras de iluminação e F4. Sem mudanças artísticas, sol ou commits.

### Trabalho repetido, separado e medido

O benchmark ganhou subetapas:
- **geometria:** coleta de areia e chave de cache, preenchimento de ocupação, extração de contornos;
- **faces:** construção do array de vértices, upload e desenho do buffer de faces;
- **contadores:** reconstruções por etapa, vértices, pixels de face com peso e arestas;
- **quadro:** "rest" (frame − draw − update).

Medição "antes" (`perf-faces-before-vsync`; repetida no par `perf-faces-pair-before-vsync`; 1920×1080, zoom 2, VSync + passo fixo):

| Operação (andando) | Frequência | Custo por ocorrência (mediana / p95) |
|---|---|---|
| Construção das faces (laço por pixel de `LightBounds`) | 639 de 640 quadros | 26,5–27,4 / 29,5–31,4 ms |
| Upload dos vértices de faces | 639 de 640 quadros | 9,7–11,4 / 10,8–18,5 ms |
| Desenho do buffer de faces (CPU) | todo quadro | 0,05 / 0,06 ms |
| Extração de contornos | 40–41 de 640 quadros (a cada 64 px) | 7,3–21,2 / 7,6–26,2 ms |
| Preenchimento da ocupação | idem | 0,6–1,7 / 0,7–2,6 ms |
| Areia + chave de cache | todo quadro | 0,1–0,6 ms |

**Causas:**
- **Faces:** a chave de cache de `BuildFaces` incluía `LightBounds`, e os vértices guardavam UV relativa a `LightBounds`. Qualquer pixel de câmera refazia o laço sobre ~549 mil pixels e reenviava **2,79 milhões de vértices**, embora só **~18 mil pixels** tenham face com peso. O resto é núcleo escuro, um quad por pixel.
- **Geometria:** a região alinhada a 64 px reconstrói ocupação e contornos a cada 64 px percorridos, e a extração por pixel custava 7–26 ms.

### Solução

- **Faces por chunk em coordenadas de mundo** (`V8FaceChunks`, chunks de 64 px):
  - **Formato do vértice:** posição de mundo, e `TextureCoordinate` com a posição de mundo da amostra externa. `V8Faces.fx` calcula a UV da direta com `LightOrigin`/`LightSize`. Os dados não dependem mais da câmera.
  - **Reuso:** um chunk é reutilizado enquanto a ocupação do chunk mais `FaceWidth` em cada lado permanece **idêntica bit a bit** (cópia exata, sem hash). Mudanças de tiles, portas, plataformas e areia invalidam só os chunks cuja ocupação mudou; fontes e horário nunca invalidam.
  - **Validação barata:** quando a geometria gera nova `Generation`, os chunks visíveis são revalidados por comparação.
  - **Construção:** só chunks que entram em `LightBounds` ou tiveram ocupação alterada.
  - **Núcleo escuro:** pixels sem face dentro de `FaceWidth` viram um quad por trecho horizontal. Os mesmos pixels recebem o mesmo preto opaco, e os vértices visíveis caem de ~2,79 milhões para ~200 mil.
  - **Armazenamento na GPU:** uma arena única (`DynamicVertexBuffer`) com faixas livres e capacidade que só cresce (1,5×). Há compactação quando não cabe, chunks longe da vista (mais de 2 chunks fora) são descartados e reaproveitam faixas, e o desenho é uma chamada por chunk visível.
  - **Perda de dispositivo:** `DynamicVertexBuffer.IsContentLost` é obsoleto no MonoGame 3.8.5.1 (sempre falso; o build avisou, CS0618). Agora `GraphicsDevice.DeviceReset` marca a arena para reenvio a partir das cópias da CPU.
- **Região de geometria:** passa a cobrir também os chunks que tocam `LightBounds` mais `FaceWidth` (`Rectangle.Union` antes do alinhamento de 64 px), para que chunk reutilizado seja exato. Com fontes, a região por raio já era maior e nada muda; sem fontes, cresce até um chunk. O contador de chunks não cobertos ficou 0 em todos os cenários.
- **Geometria em bitsets** (`V8Geometry`):
  - ocupação com um bit por pixel em palavras de 64 bits;
  - preenchimento de tiles cheios em trechos por linha;
  - contornos horizontais por máscara de linha (`linha & ~vizinha`) e verticais por máscaras deslocadas, fundidas ao longo da coluna com `TrailingZeroCount`.

  O conjunto de arestas é o mesmo da varredura por pixel; muda só a ordem na lista, e a união do stencil não depende da ordem. A regressão da direta e a comparação de capturas confirmaram buffers diretos idênticos. `IsSolid`, `TryFace`, `Edges`, `Generation` e as regras de face não mudaram.
- **Wrap:** chunks usam coordenadas de mundo não embrulhadas da região da câmera. Atravessar a costura constrói chunks novos uma vez; o conteúdo é o mesmo (`WrapTileX`). A largura do mundo (22.400 px) é múltipla de 64.

### Validação visual e funcional

- **`region-cross` (novo caso de captura):** a câmera se desloca 131×67 px, atravessando fronteiras de 64 px e maiores nos dois eixos.
- **Faces ancoradas no mundo (nova verificação):** compara, pixel a pixel, o buffer de faces de toda a sala contra `many-sources`, ignorando 10 px de borda, em `camera-shift`, `zoom`, `wide-view`, `camera-pan`, `region-cross` e `wrap`. Antes da mudança: erro máximo 0 (1 em `wrap`). Depois: o mesmo.
- **Captura de gameplay antes vs. depois** (22 casos, incluindo movimento, mineração/reconstrução, portas, areia, zoom, vista larga e wrap):
  - `_foreground`, `_direct`, `_sky` e `_local` **idênticos (máximo 0)**;
  - `_final` só difere onde `_albedo` difere: chama da tocha e sprite do jogador, animados pelo tempo. Conferido na máscara de `door-closed`.
- **`--v8-scene` antes vs. depois:** final, faces e direta idênticos em todos os casos direto e ambiente.

### Regressões do binário final

| Execução | Resultado |
|---|---|
| Build Release `--no-incremental` | 0 erros; só os dois CS0649 preexistentes (o CS0618 de `IsContentLost` foi eliminado) |
| `--v8-scene --v8-capture` | exit 0, **107 PASS, 0 FAIL**; final, faces e direta idênticos à linha de base nos 14 casos |
| `--v8-scene --v8-ambient --v8-capture` | exit 0, **95 PASS, 0 FAIL**; final, faces e direta idênticos nos 17 casos |
| `--lighting-v8 --v8-gameplay-capture` | exit 0, **89 PASS, 0 FAIL** (22 casos); `_foreground`, `_direct`, `_sky` e `_local` idênticos a `gameplay-faces-before`; faces ancoradas com erro 0 (1 em wrap) |
| `--lighting-toggle-bench --uncapped-frames` (F4) | exit 0, **57 PASS, 0 FAIL** (inclui mesma instância do renderer e nenhum recurso novo após editar com o pipeline inativo) |
| `--v7-gameplay-smoke` | exit 0, `mode=V7` |
| `git diff --check` | passou |

### Medições antes/depois

**Condições:**
- Mesmo trajeto (160 tiles a +2 px de mundo por quadro), 1920×1080, zoom 2, com 0, 1 e 4 tochas.
- Todos os quadros após os 60 iniciais entram, inclusive os de reconstrução; sem capturas durante a coleta.
- "Antes" é o binário anterior guardado; "depois" é o binário final, rodados em sequência na mesma sessão.
- O binário "antes" não registra foco da janela.

**Teste normal, VSync + passo fixo** (`perf-faces-final-before-vsync` → `perf-faces-final-after-vsync`; janela ativa em 100% dos quadros "depois"). Frame em mediana / p95 / máximo, ms:

| Cenário | Antes | Depois | Quadros > 33,4 ms | > 50 ms | > 100 ms |
|---|---|---|---|---|---|
| andando, 0 tochas | 80,07 / 95,67 / 111,19 | **15,99 / 26,69 / 37,82** | 639 → 1 | 639 → 0 | 12 → 0 |
| andando, 1 tocha | 85,22 / 105,07 / 122,87 | **16,01 / 26,32 / 31,17** | 639 → 0 | 639 → 0 | 70 → 0 |
| andando, 4 tochas | 79,48 / 109,95 / 126,63 | **16,01 / 25,95 / 30,82** | 640 → 0 | 639 → 0 | 34 → 0 |
| parado, 0 tochas | 32,35 / 36,62 / 158,49 | 16,65 / 25,96 / 30,92 | 155 → 0 | 2 → 0 | 1 → 0 |
| parado, 1 tocha | 31,89 / 34,41 / 111,55 | 15,97 / 26,87 / 31,03 | 62 → 0 | 1 → 0 | 1 → 0 |
| parado, 4 tochas | 31,76 / 32,92 / 423,85 | 16,00 / 29,63 / 76,54 | 17 → 1 | 1 → 1 | 1 → 0 |

**Etapas andando com 4 tochas** (mediana / p95 / máximo, ms; reconstruções em 640 quadros):

| Etapa | Antes | Depois |
|---|---|---|
| Draw CPU | 55,02 / 74,99 / 104,30 | 6,78 / 8,65 / 16,48 |
| Faces (total) | 37,26 / 41,76 / 53,21 | 0,16 / 0,30 / 3,49 |
| Construção de faces, quando ocorre | 639 quadros; 27,41 / 31,04 / 40,76 | 17 quadros (10 chunks cada); 2,61 / 3,20 / 3,20 |
| Upload de faces, quando ocorre | 639 quadros; 9,78 / 10,88 / 12,47 | 17 quadros; 0,03 / 0,05 / 0,05 |
| Geometria (total) | 0,33 / 22,57 / 31,71 | 0,27 / 0,63 / 1,24 |
| Contornos, quando ocorre | 40 quadros; 21,41 / 24,49 / 28,89 | 40 quadros; 0,25 / 0,42 / 0,49 |
| Preenchimento, quando ocorre | 40 quadros; 1,68 / 2,21 / 2,79 | 40 quadros; 0,23 / 0,31 / 0,35 |
| Direta | 0,33 / 0,46 / 0,63 | 0,30 / 0,45 / 7,58 |
| Ambiente | 3,57 / 5,19 / 6,58 | 3,68 / 4,63 / 6,52 |
| Present (`EndDraw`) | não registrado | 0,05 / 0,10 / 0,92 |
| Vértices de faces visíveis | ~2,74–3,05 milhões | 190–222 mil |

Chunks visíveis: 160–170. Chunks construídos: 170 no trajeto inteiro. Reescritas da arena: 4 no primeiro cenário (crescimento inicial) e 1 por trajeto. Chunks sem cobertura de geometria: 0.

**Diagnóstico sem teto** (`perf-faces-final-after-uncapped`; antes, `perf-faces-pair-before-uncapped`):
- Andando: 59,09 / 67,37 / 46,05 ms → **5,96 / 6,03 / 6,67 ms** de mediana (p95 7,58 / 7,16 / 8,78).
- Parado: 6,40 / 6,56 / 6,76 ms.
- Um quadro isolado de 110 ms em `walk-4`, vindo da direta (104 ms).

### Tempo que ainda passa de 16,7 ms (investigado)

- **Blocos de ~32 ms (janela sem foco).** Numa segunda rodada "depois" (`perf-faces-final-after-vsync-2`), a janela perdeu o foco: 581, 640, 600, 640, 600 e 325 quadros inativos por cenário. **Todos os quadros inativos ficaram acima de 16,8 ms** (em torno de 31,8 ms), com draw CPU 6–8 ms, "rest" ~25 ms e Present 0,05 ms. Na primeira rodada, com a janela ativa o tempo todo, não houve esses blocos.
  - **Conclusão:** os blocos vêm da janela sem foco, não do custo da iluminação. Condiz com o `Game.InactiveSleepTime` do MonoGame (20 ms por padrão), mas o valor não foi verificado no código do MonoGame.
  - Os blocos de 32 ms nos cenários parados do binário "antes" têm o mesmo perfil. Esse binário não registra foco, então a mesma causa ali é hipótese.
- **Variação com a janela ativa.** No teste normal, 17–29% dos quadros ficam entre 16,8 e ~31 ms (p95 ~26 ms). Nesses quadros o Present é 0,05 ms e o trabalho de CPU fica em 6–9 ms. O custo total medido sem teto é 6–7 ms, bem abaixo de 16,7 ms. A espera acontece fora de Update, Draw e Present, no laço de passo fixo.
  - **Hipótese, não confirmada:** o Present com VSync não está bloqueando nesta máquina, e o passo fixo espera com granularidade de temporizador do Windows.
  - Mudar temporizador, VSync ou passo fixo não entrou nesta entrega, por instrução.
- **Picos isolados da direta** (1 quadro: 69 ms em `static-4` com VSync, 104 ms em `walk-4` sem teto). Também aparecem em rodadas anteriores à mudança (73,5 ms em 2026-09-14). **Causa não identificada.**

### Comandos

~~~powershell
dotnet build Nyvorn/Nyvorn.csproj -c Release --no-incremental
# antes: binário anterior copiado para a pasta temporária da sessão (bin-faces-before) e executado diretamente
<bin-faces-before>/Nyvorn.exe --lighting-v8 --v8-perf-bench --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/perf-faces-final-before-vsync
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-perf-bench --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/perf-faces-final-after-vsync
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-perf-bench --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/perf-faces-final-after-vsync-2
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-perf-bench --uncapped-frames --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/perf-faces-final-after-uncapped
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-v8 --v8-gameplay-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/gameplay-faces-final
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v8-scene --v8-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/scene-direct-faces-final
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v8-scene --v8-ambient --v8-capture --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/scene-ambient-faces-final
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-toggle-bench --uncapped-frames --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/toggle-faces-final
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v7-gameplay-smoke
git diff --check
~~~

A primeira rodada "antes" sem teto (`perf-faces-before-uncapped`) terminou segundos depois de começar, sem exceção e sem `perf.txt`, o mesmo padrão de janela fechada já visto em 2026-09-14; foi substituída pelo par. O comparador de PNG é um script temporário da sessão (PowerShell + C#), fora do repositório.

### Limites

- Medido só nesta máquina, em 1920×1080 com zoom 2. Tempo de GPU não medido. O desenho de faces faz uma chamada por chunk visível (~170 por quadro; CPU 0,1–0,3 ms).
- Um chunk que entra custa uma construção de ~0,25 ms. Uma coluna de 10 chunks gera 2,4–4,8 ms uma vez a cada 64 px percorridos na horizontal; movimento vertical tem o mesmo comportamento por linha e não foi medido separadamente.
- Atravessar a costura do wrap reconstrói os chunks visíveis uma vez (a chave usa coordenadas não embrulhadas). O custo dessa travessia não foi medido no trajeto.
- Sem fontes, a região de geometria cresce até um chunk para cobrir as faces. Com fontes, não muda.
- A compactação da arena regrava todos os chunks vivos. Ocorreu 4 vezes no aquecimento do primeiro cenário e 1 vez por trajeto, e está incluída nas estatísticas.

### Pendências

1. **Variação de passo com janela ativa** (p95 ~26 ms, Present ~0,05 ms): confirmar se o Present com VSync não bloqueia nesta máquina e como o passo fixo espera. Qualquer ajuste de temporizador, VSync ou passo fixo depende de decisão explícita.
2. **Picos isolados da direta** (69–104 ms, 1 quadro por rodada, também antes da mudança): causa não identificada.
   - Os picos caíram na amostra 401 de `static-4-torches` (VSync) e na amostra 44 de `walk-4-torches` (sem teto).
   - Nesses quadros não houve reconstrução de geometria, nem chunk construído, nem mudança no número de fontes (as fontes mudam só nas amostras 10, 166 e 393). Present 0,05 ms e janela ativa.
   - Outros quadros sem reconstrução também mostram a direta em ~7 ms.
   - **Hipótese, não confirmada:** espera de sincronização com a GPU dentro do passe de stencil ou pausa do coletor de lixo. Tempo de GPU e coleta de lixo não foram instrumentados.
3. **PC do usuário:** repetir `--v8-perf-bench` lá, com a janela em foco.
4. **Travessia da costura do wrap:** medir andando.
5. Sol direto e ajuste do ambiente continuam fora do escopo.

## 2026-09-15 — Diagnóstico do ambiente do céu (V7 × V8), sem correções

Somente diagnóstico. Nenhum parâmetro, shader, propagação ou composição foi alterado. O usuário aprovou o desempenho e o resultado atual da V8.

### Diferenças por componente

| Aspecto | V7 (`LightingV7System`, `LightingV7Config`, `LightingV7Sky`, `PixelComposite.fx`) | V8 (`V8AmbientContext`, `V8AmbientField`, `V8Ambient.fxh`, `V8Receiver.fx`, `V8Faces.fx`) |
|---|---|---|
| Exposição / abertura | `ClassifyAndSeed`: célula de ar sem background recebe semente `rowSeed`, independente de teto. Linhas acima do mundo são céu aberto. | `IsExteriorAperture`: frente e fundo vazios, camada elegível, independente de teto. Linhas acima do mundo não semeiam. |
| Semente por camada | Space/Surface 1,0 (`SurfaceSkySeed`); Shallow **0,5** (`ShallowSkySeed`); Cavern 0 | `SkyWeight`: Space/Surface 1; Shallow **1**, com desvanecimento nas últimas 6 linhas; Cavern/Deep 0 |
| Teto por linha | `rowCap`: 1 até o fim da Shallow, depois linear até 0 em **12 linhas dentro da Cavern** (`SkyFadeTiles`) | Peso de camada: chega a 0 **na última linha da Shallow**; Cavern exatamente 0, com veto também no shader |
| Propagação | `Sweep` (4 varreduras × 2 rodadas), **multiplicativa**: ×0,94 por tile de ar, com ou sem background; ×0,748 em sólidos (o céu entra no terreno); água atenua por canal; areia com atenuação fracionária; **portas não bloqueiam**; plataformas não atenuam | `Propagate` (melhor caminho), **linear**: −1/24 por tile de ar, −1,75/24 em plataforma; sólidos, portas fechadas e qualquer areia bloqueiam; água ignorada |
| Alcance | ~0,48 aos 12 tiles, 0,23 aos 24, 0,05 aos 48; corte 0,01 (~74 tiles) | 0,5 aos 12 tiles, **0 aos 24** |
| Cor | `LightingV7Sky.GetSkyColor`: curva própria (dia 1,0; noite 0,14/0,17/0,28; amanhecer e entardecer; chuva ×0,75; eclipse; conjunção de luas). O comentário do código diz que `SkyState.AmbientLight` "fica claro demais à noite" para um mapa multiplicativo. | `SkyColor = SkyState.AmbientLight × 0,5` (`WorldEnvironmentSystem.SkyKeyframes`: meio-dia 245/250/255, noite 78/95/132, com chuva e eclipse misturados) |
| Intensidade efetiva no exterior | `FillTexture`: céu × `SkyBounce` 0,6 **+ sol direcional** (`ComputeDirectionalSun`, até 1,0, fachos inclinados por aberturas) | Céu × 0,5; sem sol |
| Combinação com fontes | **Máximo por canal**: `max(bloco + direta, céu) × AO + piso` | **Soma**: `direta + céu + local` |
| Mínimo artificial | Piso Shallow **0,02** (`AmbientShallow`); os demais 0. AO até −35% junto a oclusores. | Nenhum piso; nenhum AO |
| Faixa de valores | Texel = valor ÷ `OverbrightScale` 2; o composto multiplica ×2, então a luz pode ultrapassar 1 | Soma limitada a 1 por textura; sem overbright |
| Reconstrução | Textura por tile com `LinearClamp` de hardware (`BuildV7LightBuffer`): suave, atravessa sólidos | Por pixel de arte, com máscara de células abertas e rejeição de quinas (`AmbientAt`) |
| Background | `DrawBackgroundWalls` → `WorldMap.DrawBackground` com tint **104/255 (0,41)**, depois × luz do tile | Albedo neutro (1,0) × (direta + céu + local) por pixel |
| Frente sólida | Luz do tile vizinho atenuada 0,748 por tile dentro do sólido: gradiente de vários tiles | Faces: amostra no primeiro pixel de ar, peso quadrático em 8 px, núcleo preto |
| Entidades | Amostrador neutro, desenhadas no worldRT e multiplicadas pela luz em espaço de tela | Receptores por pixel (`V8Receiver`, técnica `Receiver`) |
| Parallax / fundo distante | Parallax subterrâneo dentro do worldRT (iluminado); montanhas e céu atrás (sem luz) | Céu, montanhas e parallax subterrâneo fora dos receptores (sem luz) |
| Decorações e emissivos | Cogumelos e tecido semeiam o canal de bloco; halos aditivos | Cogumelos como fonte local fraca (0,12); sem halos |

### Fatos observados

Capturas abertas e medidas nesta sessão.

- **V7 no spawn** (`screenshots/v8/sky-diag/`, `--v7-gameplay-smoke` com `NYVORN_V7_TIME` e `NYVORN_V7_VIEW`, sem mudança de código; 1280×720, zoom 2, câmera no jogador):
  - **Meio-dia:** buffer de luz no ar exterior 203/201/193 (valor ÷ 2, logo luz ≈ **1,59/1,58/1,51**). `SkyOnly` = 255 (céu 1,0); `SunOnly` ≈ 254/249/234 (sol ≈ 1,0). Receptores exteriores (árvores, jogador, cogumelos): final/albedo **1,22/1,16/1,24**, com saturação.
  - **Dentro do terreno:** buffer de luz 27 a 3 tiles (luz ≈ 0,21) e 3 a 10 tiles (≈ 0,02). O gradiente dentro do chão é visível na captura.
  - **SunOnly:** mostra o facho vertical na abertura da encosta.
  - **Noite:** receptores final/albedo **0,088** / 0,280 / 0,261. Verde e azul incluem o preenchimento dos cogumelos, que no teste de fumaça da V7 não pode ser desligado. O vermelho, que os cogumelos quase não afetam (0,30), fica perto do céu previsto (0,14 × 0,6 = 0,084). Na imagem, árvores e jogador são silhuetas quase pretas.
- **V8 no spawn** (`screenshots/v8/gameplay-faces-final/surface-*`, zoom 3, centro 40 px acima do spawn, mesmo horário):
  - Buffer de céu no ar: 122/124/127 ao meio-dia e 39/47/66 à noite, iguais a `AmbientLight × 0,5`.
  - Receptores (árvores): final/albedo **0,48/0,50/0,51** ao meio-dia e **0,155/0,195/0,267** à noite. Local ≈ 0.
  - À noite, árvores e jogador continuam legíveis, mais claros que na V7.
- **Razões:**
  - Exterior ao meio-dia: V7 ≈ 1,2 (saturado; luz 1,6) contra V8 0,5. Sem o sol, o céu da V7 seria 0,6.
  - Exterior à noite (vermelho): V7 0,09 contra V8 0,155.
  - Dia/noite no vermelho: V7 ≈ 13× (≈ 7× só com o preenchimento de céu); V8 ≈ 3,1×.
- **V8 em salas roteirizadas** (só V8):
  - `shallow-day_final`/`_sky`: janela de background 4×6 sob teto. O céu entra em gradiente radial a partir da abertura, bloqueado pelas paredes da câmara, até ~24 tiles; o background do lado oposto fica preto.
  - `shallow-sealed_final`: preto total.
  - `cavern-off_final`: preto total.
- **Mesma câmera nas duas versões** (`screenshots/v8/toggle-faces-final/toggle-00…08`, caverna Shallow com fissuras, meio-dia, zoom 2): **inclui tocha (e cogumelos na V7), então só serve qualitativamente.** A V7 mostra o background junto às fissuras quase preto; a V8 mostra o background marrom visível junto às fissuras.

### Fatos derivados do código (não medidos em captura V7 equivalente)

- **Background perto de abertura na Shallow** (albedo × luz, sem sol): V7 = 0,41 × 0,6 × 0,5 × 0,94ⁿ, isto é **0,12** na abertura, 0,06 aos 12 tiles e 0,03 aos 24. V8 = 1,0 × 0,5 × (1 − n/24), isto é **0,50**, 0,25 e 0. A V8 é ~4× mais clara junto às fissuras, mais clara até ~20 tiles e zero depois, onde a V7 ainda tem uma cauda fraca.
- **Background perto de abertura na Surface:** V7 0,25 na abertura contra V8 0,50.
- **Transição:** a V7 ainda ilumina 12 linhas da Cavern (teto decrescente); a V8 zera ao terminar a Shallow.
- **Portas fechadas:** não barram o céu na V7; barram na V8.

### Hipóteses (não confirmadas)

1. A impressão de "exterior fraco" na V8 vem principalmente da ausência do **sol direcional** da V7, cerca de 62% da luz exterior da V7 ao meio-dia, e do overbright ×2. Só a parte de preenchimento de céu, a V8 fica ~17–20% abaixo (0,5 contra 0,6).
2. A noite da V8 parece mais clara que a da V7 porque `SkyState.AmbientLight` foi feito para tingir sprites, como já observa o comentário em `LightingV7Sky`, e a V8 o usa diretamente.
3. As cavernas da Shallow parecem mais claras junto às fissuras na V8 por três fatores somados: albedo de background neutro (2,45×), semente Shallow 1,0 contra 0,5 e alcance linear. A V7 compensava com cauda longa e piso de 0,02. Falta medir com a V7 nos mesmos pontos.

### Escolhas artísticas (decisões, não defeitos)

- **V7:** sol direcional com fachos, overbright ×2, combinação por máximo, AO, piso Shallow 0,02, curva noturna própria, tint 104 de background, luz entrando no terreno.
- **V8:** albedo neutro, faces com núcleo preto, soma de contribuições, alcance de 24 tiles, intensidade 0,5, uso de `AmbientLight`, fissuras como aberturas exteriores, céu zero na Cavern, sala fechada preta.

### Capturas pedidas que faltam

Os recursos existentes não permitem reproduzir os mesmos pontos na V7 e na V8 com a mesma câmera, horário e zoom, sem fontes locais:
- A captura de gameplay da V8 (`--v8-gameplay-capture`) não tem modo V7.
- O teste de fumaça da V7 só fotografa o spawn.
- O benchmark de alternância (`--lighting-toggle-bench`) usa uma cena com tocha.
- A V7 sempre soma cogumelos e tecido.

Faltam: aberturas pequenas e grandes, corredor avançando para dentro, sala fechada na V7, transição Shallow → Cavern e comparação noturna nos mesmos pontos.

**Instrumentação proposta (não implementada):** opção `--lighting-sky-compare`, só em sessão temporária.
- **Cenas:** reaproveitar o construtor de salas de `PlayingState.V8Capture` para montar exterior (spawn), janela de background 2×2 e 8×10 sob teto, corredor de 40 tiles a partir de uma entrada na Surface e outro a partir de uma fissura na Shallow, sala fechada e poço vertical cruzando as linhas 260–290 (Shallow → Cavern).
- **Renderização:** para cada cena, fixar câmera, zoom e horário (meio-dia e noite) e renderizar V7 e V8 no mesmo processo por `SetLightingPipeline` (o caminho do F4).
- **Fontes locais:** um sinalizador só de diagnóstico que remove tochas, cogumelos e tecido nas duas versões (na V7, pular `AddV7PointLights` e a semente de tecido; na V8, lista de fontes vazia).
- **Arquivos gravados:**
  - V7: final, world, luz, e as vistas `SkyOnly` e `SunOnly`, também com `SunEnabled` desligado;
  - V8: final, albedo, `_sky` e `_local`.
- **Perfis numéricos:** CSV ao longo do corredor e do poço com o céu da V7 (`GetSkyAt`), a luz final da V7 (com e sem sol), a exposição e o céu da V8, e final/albedo de background e receptores.
- **Não altera o jogo normal:** o sinalizador de fontes só existe com a opção.

### Menor proposta de ajuste (para decisão; não implementada)

**Não clarear a V8 globalmente.** Pelos números, o exterior de dia fica abaixo da V7 sobretudo pela falta do sol, que é tarefa separada. Já as cavernas da Shallow junto às fissuras tendem a ser mais claras que na V7.

A menor mudança que aproxima a V8 das proporções de referência sem mexer em shader, propagação ou composição são duas constantes em `V8AmbientContext`:
1. **`SkyIntensity` 0,5 → 0,6:** o mesmo nível do preenchimento de céu da V7 (`SkyBounce`).
2. **Semente de abertura da Shallow ×0,5:** o equivalente a `ShallowSkySeed`. Aplica-se só à semente (`sky[i] = cap[i] × fator` para aberturas da Shallow), mantendo o teto da linha, para que a luz vinda da Surface continue descendo como na V7.

Estritamente, o item 2 toca `V8AmbientField.Update`, na linha que semeia as aberturas, não o laço de propagação.

**Efeito esperado:**

| Local | Hoje | Com a proposta |
|---|---|---|
| Receptores exteriores de dia | 0,48 | ~0,58 (+20%) |
| Exterior à noite (vermelho) | 0,155 | ~0,186 (+20%) |
| Interiores da Surface ligados ao exterior | — | +20% |
| Interiores alimentados só por fissura da Shallow | 0,48 | ~0,30 (−40%; ainda ~2,4× a V7, por causa do albedo neutro) |
| Cavern e Deep | 0 | 0 |
| Direta, local, faces, otimizações, F4 | — | inalterados |

**Riscos:**
- Escurece a Shallow, cuja aparência atual foi aprovada.
- Deixa a noite exterior ainda mais clara que a da V7 (hipótese 2), o que talvez peça uma curva noturna separada, fora desta proposta.
- As verificações "abertura admite céu" e "noite mais fraca" continuam satisfeitas pela regra, mas precisam ser executadas.
- Não resolve a falta de sol nem a cauda longa da V7.

**Como validar:**
1. Implementar antes a instrumentação acima.
2. Medir final/albedo de background e receptores nos mesmos pontos na V7 (com e sem sol) e na V8 (antes e depois).
3. Executar `--v8-gameplay-capture` e `--v8-scene --v8-ambient` (céu zero proibido, sala selada preta, abertura admite céu, noite mais fraca).
4. Revisão visual do usuário nos mesmos locais com F4.

**Alternativas registradas para decisão separada:** curva de cor noturna própria para a V8 (reusar a lógica de `LightingV7Sky`); alcance com cauda exponencial; piso na Shallow. Nenhuma foi escolhida.

> **Nota de 2026-09-15 (comparação visual):** duas afirmações acima foram corrigidas pela leitura do código, detalhada na seção seguinte.
> - "Sem o sol, o céu da V7 seria 0,6" só vale para a parcela de céu **com o sol ligado**. Com o sol desligado (F7), `FillTexture` usa `bounce = 1,0`.
> - Reduzir a semente da Shallow para 0,5 **não** equivale ao `ShallowSkySeed` da V7: na V8 também reduz o alcance para 12 tiles.
>
> Nenhuma das mudanças propostas foi aplicada.

## 2026-09-15 — Comparação visual V7 × V8 do ambiente do céu (instrumentação mínima)

Autorizado: somente instrumentação para comparar V7 e V8 na mesma cena, câmera, zoom e horário, sem fontes locais. Não autorizado: `SkyIntensity`, semente da Shallow, parâmetros, shaders, propagação, composição, sol, desempenho.

### Semente, brilho e alcance (confirmado no código)

**V8** (`V8AmbientField.Update` / `Propagate`, `V8AmbientContext`):
- **Semeadura** (`V8AmbientField.cs:108`, `:119`): `cap[i] = SkyWeight(y)` e cada abertura recebe `sky[i] = cap[i]` (1 em Surface e Shallow, fora das 6 últimas linhas).
- **Perda** (`:121`, `:293`): cada passo **subtrai** um valor fixo, `next = value − loss × cost`, com `loss = 1 / SkyRangeTiles = 1/24` e `cost` 1 no ar e 1,75 em plataforma.
- **Teto** (`:294`): `next = min(next, cap[j])`. No shader (`V8Ambient.fxh:71`), o céu final é `AmbientSkyColor × min(exposição, pesoDaCamada)`.
- **Intensidade** (`V8AmbientContext.cs:23`): `SkyColor = AmbientLight × SkyIntensity` multiplica só a cor, depois da propagação.
- **Consequências:**
  - Com semente `s`, a exposição a `d` tiles é `max(0, s − d/24)`, e o **alcance é 24·s tiles**.
  - Com `s` = 0,5, a luz some aos **12 tiles**. Na abertura o brilho cai pela metade; aos 6 tiles fica em 0,25 contra 0,75, isto é, um terço; aos 12 tiles, zero contra 0,5.
  - Brilho e alcance ficam acoplados: a semente mexe nos dois ao mesmo tempo.
  - `SkyIntensity` escala o brilho em qualquer distância sem mudar o alcance.
  - A margem da grade (`:88`) depende de `SkyRangeTiles`, não da semente.

**V7** (`LightingV7System.ClassifyAndSeed` / `Sweep`):
- **Semeadura** (`:899`): `sky[i] = hasWall ? 0 : rowSeed`.
- **Perda** (`:945`): cada passo **multiplica**, `l × decay`, com 0,94 no céu em ar.
- **Consequências:**
  - O brilho a `d` tiles é `s × 0,94^d`. Reduzir a semente escala o brilho **proporcionalmente em todas as distâncias**.
  - O alcance até o corte de 0,01 encolhe só `ln 2 / −ln 0,94` ≈ 11 tiles: ~74 → ~63 tiles.

**Conclusão:** um "×0,5" equivalente ao da V7 na V8 seria um fator multiplicativo sobre a contribuição das aberturas da Shallow, aplicado depois da propagação ou carregado junto dela. Reduzir a semente não serve. Nada disso foi implementado.

### Correção sobre a V7 "sem sol"

`LightingV7System.FillTexture` usa `bounce = SunEnabled ? SkyBounce (0,6) : 1,0`. O comentário do código diz que, com o sol desligado, o preenchimento de céu leva a luz do dia inteira.
- **V7 sem sol (F7):** céu × **1,0**.
- **V7 padrão (sol ligado):** céu × **0,6** + sol direcional.

À noite o sol é zero, mas a V7 padrão continua com o céu × 0,6, então a "V7 sem sol" é mais clara que a V7 padrão à noite.

### Instrumentação adicionada (só diagnóstico)

- **Opção:** `--lighting-sky-compare`, em sessão temporária. Parte da V7 padrão e não combina com outras opções de captura (`V8GameplayOptions`).
- **Arquivo novo:** `Source/Game/States/PlayingState.SkyCompare.cs`.
- **Ganchos:**
  - `PlayingState.Update` (avanço de caso e variante);
  - `BeginLightingFrame`/`EndLightingFrame` (câmera e horário fixos por quadro, captura);
  - `InitializeV8`.
- **Guarda no caminho normal da V7:** `PlayingState.Draw` só chama `AddV7PointLights` quando `v7SuppressLocalSources` é falso. O jogo normal nunca liga essa flag; só a opção nova liga. O F4 fica bloqueado durante a opção.
- **Sem fontes locais nas duas versões:**
  - tochas removidas (`TorchRuntimeSystem.Restore` vazio);
  - cogumelos suprimidos (`v7SuppressLocalSources` na V7, `v8SuppressDecorationSources` na V8);
  - emissão de tecido da V7 desligada (`TissueEmissiveEnabled` restaurado ao fim).
- **Variantes por caso:** V7 sem sol (`SunEnabled` falso), V8 e V7 padrão com sol (referência), trocadas por `SetLightingPipeline`, o caminho do F4.
- **Condições fixas:** câmera, zoom 2 e hora reaplicados em todo quadro. O jogador fica estacionado 90 tiles à direita, na mesma camada. Assentamento de 180 quadros no primeiro render do caso, depois 20 por variante.
- **Cenas relativas aos limites reais das camadas** (`LayerDefinitions`), com posição horizontal relativa ao tile de spawn:
  - **Exterior:** tile de spawn, ao meio-dia (0,5) e à noite (0,02).
  - **Túnel da Shallow:** 56×6 tiles de ar sob casca sólida de 16 tiles, começando 2 linhas abaixo do início da Shallow, com background Stone em tudo. A abertura é um furo de background (frente e fundo vazios): 2×2 nas linhas 2–3 do túnel (pequena), 6×6 (grande) ou nenhuma (sala fechada).
  - **Poço Shallow → Cavern:** 10×28 tiles centrado na última linha da Shallow; a abertura são as 6 linhas superiores.
  - A cena falha se a camada for curta demais.
- **Saídas** em `screenshots/v8/sky-compare/`:
  - `compare-<caso>.png`: três painéis lado a lado, recorte 960×500 abaixo do HUD e acima da hotbar, em meia escala, com rótulo;
  - `<caso>_<variante>_frame.png` e `_crop.png`;
  - `<caso>-profile.csv`: por tile ao longo do túnel ou por linha do poço, com luma final das três variantes, luz V7 × overbright, céu da V8, exposição da V8, peso de camada e escalar de céu da V7;
  - `compare-summary.txt` e `compare-checks.txt`.
- **Verificações próprias:**
  - nenhuma fonte na V7 (`SourceCount`), tecido desligado e estado do sol conforme a variante;
  - nenhuma fonte na V8;
  - pipeline ativo correto;
  - mesma matriz de câmera e mesmo estado do céu (hora, `AmbientLight`, chuva, eclipse) nas três capturas;
  - número de aberturas pela regra da V8 igual ao tamanho do furo;
  - tiles com líquido na cena, apenas informado.

### Execução e verificações

~~~powershell
dotnet build Nyvorn/Nyvorn.csproj -c Release
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-sky-compare --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/sky-compare
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --v7-gameplay-smoke
dotnet run -c Release --no-build --project Nyvorn/Nyvorn.csproj -- --lighting-toggle-bench --uncapped-frames --v8-output C:/dev/Nyvorn-Reborn/screenshots/v8/toggle-sky-compare
git diff --check
~~~

- **Build:** 0 erros, os dois CS0649 preexistentes. O primeiro build falhou (`?.` sobre `WorldLayerDefinition`, que é struct) e foi corrigido.
- **`--lighting-sky-compare`:** exit 0, **54 PASS, 0 FAIL** em `compare-checks.txt`. Nenhuma fonte na V7 e na V8 em todas as capturas, tecido desligado, sol conforme a variante, pipeline correto, câmera idêntica e estado do céu idêntico nas três variantes. Aberturas pela regra da V8: pequena 4, grande 36, sala fechada 0, poço 60. Nenhum tile com líquido nas cenas.
- **Estado do céu:** meio-dia 0,5 com `AmbientLight` 245/250/255; noite 0,02 com 78/95/132; chuva 0 e eclipse 0 em todos os casos.
- **Limites reais do mundo usados:** Surface 109..198, Shallow 199..270, Cavern 271..765. Túneis nas linhas 201–238 (interior 217–222); poço nas linhas 241–300, cruzando a última linha da Shallow (270).
- **Regressões:** smoke da V7 exit 0 (`mode=V7`); alternância F4 exit 0, **57 PASS, 0 FAIL**. `git diff --check` passou.

### Imagens (abertas e inspecionadas)

Em `screenshots/v8/sky-compare/`, cada imagem tem três painéis: V7 sem sol | V8 | V7 padrão com sol (referência).

| Arquivo | O que mostra |
|---|---|
| `compare-exterior-noon.png` | V7 sem sol: árvores e grama perto do albedo, gradiente de luz entrando no chão. V8: árvores com cerca de metade do brilho, terreno preto abaixo das faces. V7 com sol: tudo mais claro, tons saturados. |
| `compare-exterior-night.png` | V7 sem sol e V8 parecidos, com silhuetas legíveis. V7 padrão mais escura. |
| `compare-shallow-opening-small.png` | Furo 2×2. V8 mais clara perto do furo e preta antes do meio do túnel. V7 sem sol mais escura, mas alcança quase todo o túnel e ilumina a casca sólida em volta. V7 padrão mais escura que sem sol. |
| `compare-shallow-opening-large.png` | Furo 6×6. Mesmo padrão; a luz começa 5 tiles mais adiante. |
| `compare-shallow-sealed.png` | V8 totalmente preta. V7 com textura quase preta (piso 0,02). |
| `compare-shallow-to-cavern.png` | V8 iluminada perto da abertura, apagando nas últimas linhas da Shallow, preta na Cavern. V7 continua iluminando o poço e a rocha ao redor dentro da Cavern. |

**Artefato fora da iluminação:** o quadrado vermelho nos painéis V8 é a pré-visualização de tile sob o cursor do mouse (`DrawTerrainOverlay`). Na V7 ele entra na composição iluminada e fica apagado; na V8 é desenhado depois.

### Leitura dos perfis

Fonte: `*-profile.csv` e `compare-summary.txt`. "Luz" é o valor antes do albedo: luz V7 × `OverbrightScale` no canal R; céu R da V8. "Luma" é o pixel final do background.

| Túnel, abertura pequena | 2 t | 6 t | 12 t | 20 t | 24 t | 32 t | 40 t |
|---|---|---|---|---|---|---|---|
| Luz V7 sem sol | 0,486 | 0,384 | 0,267 | 0,172 | 0,133 | 0,086 | 0,063 |
| Céu V8 (exposição) | 0,459 (0,96) | 0,377 (0,78) | 0,259 (0,54) | 0,098 (0,20) | 0,020 (0,04) | 0 | 0 |
| Luma V7 sem sol / V8 / V7 padrão | 28 / 67 / 18 | 20 / 48 / 12 | 11 / 27 / 7 | 10 / 13 / 6 | 6 / 2 / 4 | 4 / 0 / 3 | 3 / 0 / 2 |

| Túnel, abertura grande | 6 t | 12 t | 20 t | 24 t | 32 t | 40 t |
|---|---|---|---|---|---|---|
| Luz V7 sem sol | 0,486 | 0,337 | 0,212 | 0,172 | 0,110 | 0,071 |
| Céu V8 | 0,459 | 0,341 | 0,180 | 0,098 | 0 | 0 |
| Luma V7 sem sol / V8 / V7 padrão | 21 / 49 / 13 | 14 / 35 / 9 | 11 / 23 / 7 | 10 / 14 / 6 | 6 / 0 / 4 | 4 / 0 / 2 |

**Alcance** (luma > 2, contado a partir do início do furo):

| Abertura | V7 sem sol | V8 | V7 padrão |
|---|---|---|---|
| Pequena | 47 tiles | 24 tiles | 40 tiles |
| Grande | 52 tiles | 29 tiles | 41 tiles |

| Poço (linha relativa à última da Shallow) | −13 | −6 | −3 | −1 | 0 | +1 (Cavern) | +6 | +9 | +12 |
|---|---|---|---|---|---|---|---|---|---|
| Luz V7 sem sol | 0,455 | 0,455 | 0,384 | 0,337 | 0,322 | 0,282 | 0,204 | 0,172 | 0 |
| Céu V8 (peso da camada) | 0,478 (1) | 0,439 (1) | 0,235 (0,5) | 0,074 (0,17) | 0 (0) | 0 | 0 | 0 | 0 |

| Exterior (ar aberto) | Luz V7 sem sol | Céu V8 | Luz V7 padrão |
|---|---|---|---|
| Meio-dia | 0,996 / 0,996 / 0,996 | 0,478 / 0,490 / 0,498 | 1,592 / 1,576 / 1,514 |
| Noite | 0,133 / 0,165 / 0,275 | 0,153 / 0,184 / 0,259 | 0,078 / 0,102 / 0,165 |

### Diferenças observadas

- **Brilho na Shallow (aberturas e corredor):** até ~12 tiles, a **luz** da V7 sem sol e da V8 é praticamente igual (diferença ≤ 0,03). Aos 2 tiles: V7 semente 0,5 da Shallow × bounce 1,0; V8 semente 1 × `SkyIntensity` 0,5. A V8 **parece ~2,4× mais clara** (luma 67 contra 28) só pelo albedo do background: 1,0 contra 104/255 = 0,41, e 1/0,41 = 2,45. A V7 padrão fica em ×0,6 da V7 sem sol.
- **Brilho no exterior:** aqui a diferença é de luz. A V8 recebe **metade** da V7 sem sol (0,48–0,50 contra 1,0), porque a V7 semeia 1,0 na Surface e a V8 usa 1 × 0,5 em todas as camadas. A V7 padrão soma o sol (≈1,6).
- **Alcance:** a V8 zera aos 24 tiles da origem (queda linear de 1/24). A V7 mantém cauda (0,13 aos 24, 0,06 aos 40) e ilumina a rocha em volta do túnel. A luz da V8 cai abaixo da V7 depois de ~12–16 tiles.
- **Resposta ao tamanho da abertura:** pequena nas duas versões e da mesma natureza. Nenhuma soma células de abertura (V7 usa máximo nas varreduras; V8 usa o melhor caminho). A abertura grande só aproxima a origem: +5 tiles de alcance e luz ~26–32% maior aos 12 tiles, igual em V7 e V8.
- **Transição Shallow → Cavern:** o peso da camada da V8 limita o céu nas 6 últimas linhas (0,235 a −3, 0 na última linha) e zera a Cavern. A V7 mantém ~0,32 na última linha e entra 9–11 linhas na Cavern (teto decrescente de 12 linhas). A V8 cumpre a regra "céu zero na Cavern"; a V7 não.
- **Sala fechada:** V8 preta; V7 quase preta, por causa do piso 0,02 da Shallow × albedo do background.
- **Cor:**
  - Meio-dia: V7 neutra (1/1/1); V8 levemente fria (0,478/0,490/0,498).
  - Noite: V7 mais azul (B/R 2,07 sem sol) que a V8 (B/R 1,69).
  - Nível noturno da V8: ≈ V7 sem sol, e ≈ 2× a V7 padrão.

### Limites desta comparação

- **Uma seed e um horário de cada:** seed `V8-GAMEPLAY-2026`, meio-dia e 0,02.
- **Resolução e perfil:** 1280×720, zoom 2. O perfil amostra um ponto por tile (média 5×5 px na tela) numa linha do túnel ou do poço.
- **Cenas construídas** (túnel e poço de pedra sob casca sólida), não cavernas naturais.
- **Composição e HUD:** a imagem lado a lado usa meia escala, e o recorte exclui o HUD e a hotbar.
- **Emissão de tecido:** foi desligada na V7 durante a comparação (`TissueEmissiveEnabled`), porque semeia o canal de bloco, que é fonte local, e restaurada ao fim. A verificação "sources 0" cobre as duas versões.
- **Local do perfil:** no túnel, a linha 3 do túnel (linha inferior da abertura pequena), a partir da origem da abertura. No poço, a coluna central.

Nenhuma mudança de `SkyIntensity`, semente da Shallow, parâmetros, shaders, propagação, composição ou sol foi aplicada. Sem commits.

## 2026-09-15 — Faixa iluminada do foreground nas faces expostas ao céu (item 1 do reequilíbrio)

**Pedido:** primeiro item da ordem definida pelo usuário, sem mexer nos demais:
- aprofundar suavemente a faixa iluminada do foreground, com alvo inicial de ~1,5 tile nas faces expostas ao céu;
- a faixa deve cair suavemente, sem brilho uniforme;
- revelar a textura perto da face sem atravessar paredes;
- sem sol.

Background, intensidade/alcance do céu, tochas e composição ficam para os itens seguintes.

### Situação anterior (código)

- **Peso único:** `V8Geometry.TryFace` escolhia a face axial exposta mais próxima (até `FaceWidth` = 8 px, 1 tile) e gravava um único peso `(1 − (d − 0,5)/8)²` na cor do vértice.
- **Aplicação no shader:** `V8Faces.fx` multiplicava `(direta + local + céu)` por esse peso.
- **Perfil medido:** no terreno real da superfície (`surface-day`), a luz da face caía de 124/255 no primeiro pixel para 44 aos 4 px, 13 aos 6 px e 0 a partir de 8 px. Na tela, isso é um contorno fino sobre preto.

### Decisão

- **Pesos separados na cor do vértice:**
  - `R`: direta + local, **inalterado** (quadrático em 8 px);
  - `G`: céu, com faixa própria `SkyFaceDepth` = **12 px** (1,5 tile).
- **Curva do céu:** Hermite `1 − t²(3 − 2t)`, com `t = (d − 0,5)/12`. Vale ~1 junto à face, ~0,56 aos 6 px, ~0,32 aos 8 px, ~0,11 aos 10 px e 0 depois de 12 px.
- **Shader:** `V8Faces.fx` passa a calcular `(direta + local) × R + céu × G` (o veto de camada do receptor sólido continua).
- **Motivo da separação:** o item 1 pede a faixa do céu. Aprofundar também direta e tochas aumentaria a dominância das fontes, que é o item 4, e misturaria duas mudanças numa avaliação.
- **Sem vazamento:** cada pixel sólido continua lendo **só a face mais próxima**, com o mesmo desempate. Numa parede de 1 tile, a metade mais próxima do lado escuro amostra o lado escuro. A faixa não é fonte de transporte: o ambiente continua bloqueado no limite da rocha.
- **Margens:** `FaceReach = max(FaceWidth, SkyFaceDepth)` substitui `FaceWidth` como margem dos limites de luz, da região de geometria, dos chunks de faces, da coleta de fontes e das verificações de faces ancoradas. `LightBounds` cresce 4 px por lado.

**Alterados:** `V8Geometry.cs` (`TryFace` devolve a profundidade), `V8FaceChunks.cs`, `V8Faces.fx`, `V8LightingRenderer.cs`, `PlayingState.V8.cs`, `PlayingState.V8Capture.cs`.

**Diagnóstico:** `V8DiagnosticScene.cs`, `V8DiagnosticGame.cs` e `V8AmbientValidation.cs` ganharam a cena `sky-band`: bloco de terra de 7×5 tiles na Surface sobre o teto de 1 tile da sala selada, com a câmera 80 px acima.

### Verificações

| Execução | Antes | Depois |
|---|---|---|
| `--v8-scene --v8-capture` | 107 / 0 | **107 / 0** |
| `--v8-scene --v8-ambient --v8-capture` | 95 / 0 | **103 / 0** (8 novas em `sky-band`) |
| `--lighting-v8 --v8-gameplay-capture` | 89 / 0 | **89 / 0** (faces ancoradas: erro máximo 0–1) |
| `--lighting-sky-compare` | 54 / 0 | **54 / 0** |
| `--v7-gameplay-smoke` | — | exit 0, `mode=V7` |
| `--lighting-toggle-bench --uncapped-frames` | — | **57 / 0** |
| build Release / `git diff --check` | — | 0 erros (2 CS0649 antigos) / OK |

**Novas verificações (`sky-band`):**
- **Queda suave:** o azul da face por profundidade 1..20 no centro do bloco é `112,108,100,89,77,63,49,35,23,12,4,0,0,…`. É monotônico, com 0,79 do valor inicial aos 4 px e 0,31 aos 8 px.
- **Fim da faixa:** zero exato a partir de 13 px.
- **Teto de 1 tile:** as linhas 0..3 do teto recebem `112,108,100,89`; as linhas 4..7, mais próximas da sala selada, ficam `0,0,0,0`.
- **Sala selada:** interior escuro, veto de camada e receptor aditivo continuam passando.

**Comparação registrada no mundo** dos buffers de faces antes e depois (script local; os `LightBounds` diferem 4 px):
- **Onde não há céu num raio de 13 px:** diferença **0** em todos os casos. Isso inclui `primary`, `right`, `two`, `local`, `local-cavern`, `cavern-torch`, `deep-torch`, `many-sources` e `shallow-sealed`. Direta, preenchimento local das tochas, núcleo e alfa não mudaram.
- **Onde há céu:** mudou apenas perto das faces expostas.
- **Frames finais (sem HUD):** idênticos em `cavern-off`, `cavern-torch`, `deep-off`, `deep-torch`, `sand` e `shallow-sealed`. Nos demais casos com tocha, as diferenças acima de 8 se limitam às chamas animadas e ao quadrado de pré-visualização do cursor, conferido nas máscaras de `camera-pan` e `region-cross`.
- **V7 na comparação do céu:** frames sem mudança acima de 8, fora o quadrado do cursor (16×16 px). As variações de até 7/255 fora dele não vêm da iluminação, que é o mesmo código.

**Perfil do terreno real** (média do canal máximo da face, 0..255, em colunas com massa sólida ≥ 20 px abaixo e ±14 px ao lado):

| Profundidade (px) | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| surface-day antes (339 col.) | 124 | 93 | 66 | 44 | 27 | 13 | 5 | 0 | 0 | 0 | 0 | 0 |
| surface-day depois | 138 | 131 | 119 | 105 | 89 | 73 | 56 | 40 | 26 | 14 | 5 | 0 |
| surface-night antes | 70 | 53 | 38 | 25 | 15 | 8 | 3 | 0 | 0 | 0 | 0 | 0 |
| surface-night depois | 78 | 72 | 65 | 57 | 48 | 38 | 29 | 21 | 13 | 7 | 3 | 0 |
| shallow-day antes (28 col.) | 37 | 28 | 20 | 13 | 8 | 4 | 1 | 0 | 0 | 0 | 0 | 0 |
| shallow-day depois | 42 | 40 | 37 | 33 | 29 | 24 | 18 | 13 | 9 | 5 | 2 | 0 |

**Tempo:** nos quadros estáveis das capturas de gameplay, a etapa de faces ficou em 0,03–0,06 ms antes e depois. O custo extra de construir um chunk (busca até 12 px em vez de 8 para pixels de núcleo) **não foi medido isoladamente**. A investigação de desempenho não foi ampliada.

### Imagens (inspecionadas)

Em `screenshots/v8/faceband-compare/`:

| Arquivo | Leitura |
|---|---|
| `gameplay-surface-day.png`, `zoom-surface-day.png` | Antes: grama com contorno fino e preto logo abaixo. Depois: faixa de terra com textura legível sob a grama, escurecendo até o preto em ~1,5 tile. O interior segue preto. |
| `gameplay-surface-night.png` | Mesmo efeito, mais escuro e frio. |
| `sky-exterior-noon.png`, `zoom-exterior-noon.png` | V8 depois ganha a faixa. A V7 sem sol ainda mostra terra iluminada por ~3–4 tiles e grama bem mais clara. Essa diferença de brilho do dia é o item 3. |
| `sky-exterior-night.png`, `zoom-exterior-night.png` | Faixa discreta. V7 e V8 continuam próximas no nível noturno. |
| `gameplay-shallow-day.png`, `shallow-opening-large.png` | Na Shallow, as faces em volta da abertura e do túnel ganham a mesma faixa. O background não mudou. |

A cena `sky-band` do diagnóstico fica escura no final porque o céu da fixture (130,175,225 × 0,5) sobre o albedo da terra é baixo. O buffer de faces confirma a faixa.

### Limites e pendências

- **Sol fora:** a faixa só existe onde a amostra de ar tem céu. Faces à sombra de uma saliência, ou sob o teto na Cavern, continuam com a faixa antiga (direta/local) ou preta.
- **Profundidade fixa:** 12 px em todos os materiais e horários.
- **Diferença de espessura:** faces iluminadas por tocha continuam com 8 px quadráticos, então a espessura aparente difere entre céu e tocha. Isso deve ser reavaliado no item 4.
- **Aproximação de 8 bits:** peso do céu quantizado em 1/255 na cor do vértice.
- **Próximos itens da ordem, não iniciados:**
  - resposta do background ao ambiente;
  - equilíbrio do dia na superfície;
  - tochas;
  - sol.

Sem commits.

## 2026-09-15 — Plano do equilíbrio visual definitivo (planejamento, sem implementação)

**Pedido:** planejar antes de alterar código.
- Luz natural com a legibilidade da V6.
- Tochas com calor e queda da V7.
- Técnica da V8 preservada.
- Faixa do foreground a **3 tiles**, substituindo o alvo de 1,5 tile da entrega anterior.

**Plano completo:** [`V8_PLANO_EQUILIBRIO_VISUAL.md`](V8_PLANO_EQUILIBRIO_VISUAL.md). Contém a tabela V6/V7/V8, as respostas A–G, a proposta mínima, as etapas com critérios, os riscos e as medições que faltam.

**Leitura dos três caminhos executados:**

| Versão | Achados que orientam o plano |
|---|---|
| V6 | Céu integral (`AmbientLight`) no ar aberto. Faixa de terreno com pesos 1/0,65/0,35/0,15 por tile. Background 0,9 − 0,1/tile (9 tiles) com albedo 0,41. Tochas por soma limitada `c + a(1 − c)`. Árvores e cogumelos com `AmbientLight` aplicado duas vezes. |
| V7 | Céu ×0,6 com sol e **×1,0 com F7**. Composição por máximo. Direta de 13 tiles mais flood de cauda longa. |
| V8 | Albedo de background 1,0. Alcance linear de 24 tiles com 4 vizinhos (métrica L1, também forma losango). Soma direta + céu + local. Direta de 35 tiles e local de 32. |

**Medição feita só lendo PNGs existentes** (`gameplay-faceband-after/cavern-torch`):
- no background iluminado, a direta responde por ~75% da luz a 8–12 tiles;
- final ÷ albedo: 0,77 a 8 tiles, 0,58 a 12 e 0,26 a 20;
- na sombra, só o local: 0,20 a 2–4 tiles.

**Sequência proposta:** transporte de 8 vizinhos com bloqueio de quina → céu como distância com resposta por classe (brilho, alcance e curva independentes) → faixa de 3 tiles com duas faces candidatas e porta suave → exterior diurno e noite → tochas (direta, depois local) → combinação céu/tocha por nível de luz.

Os 18 prints citados pelo usuário não estavam disponíveis nesta sessão. Nenhum código, parâmetro, shader, teste ou instrumentação foi alterado. Sem commits.

## 2026-09-15 — Plano enxuto (revisão 2), sem implementação

**Pedido:** caminho mais simples até o alvo:
- luz natural da V6;
- tochas da V7;
- técnica da V8;
- um eixo por vez, sem refatorar sem evidência.

A revisão 2 está no topo de [`V8_PLANO_EQUILIBRIO_VISUAL.md`](V8_PLANO_EQUILIBRIO_VISUAL.md). A revisão 1 fica abaixo, só como referência.

**Leitura adicional do código:**
- V6/V7 desenham o céu de fundo com `LinearClamp`, brilho do sol e luas; a V8 usa `PointClamp`, sem brilho do sol e sem luas.
- `WorldNightStrength` já existe na sessão e permite escala noturna separada.

**Mudança principal em relação à revisão 1:** a semente continua intocada. Com isso, `SkyRangeTiles` e a escala do céu **já** controlam alcance e brilho de forma independente. A reinterpretação do céu como distância, as duas faces candidatas e a propagação de 8 vizinhos ficam adiadas até uma captura mostrar necessidade.

**Ordem:**

| Passo | Mudança |
|---|---|
| 1 | Escala do céu dos receptores e faces ≈ V6, com background e noite separados |
| 2 | Faixa de 24 px |
| 3 | Alcance e brilho do background |
| 4 | Aberturas pequenas, só se necessário |
| 5 | Tocha: primeiro a direta (raio 14–18 tiles), depois o local |
| 6 | Composição `S + T·(1 − S)` |

Nenhum código alterado. Sem commits.

## 2026-09-15 — Passo 1: intensidade diurna do céu nos receptores (implementado)

**Pedido:**
- receptores exteriores, entidades, decorações e faces com intensidade diurna próxima da V6;
- background com a intensidade atual;
- noite da V8 idêntica;
- sem mudança de propagação, alcance, tochas ou composição.

**Correções do usuário ao plano:**
- **Passo 3:** reduzir `SkyRangeTiles` globalmente **não** está aprovado. Testar primeiro um remapeamento da exposição só no receptor do background:
  - `corte = 1 − R_bg/SkyRangeTiles`;
  - `e_bg = saturate((e − corte)/(1 − corte))`;
  - opcionalmente `pow(e_bg, γ)`.
- **Passo 2:** começa com a arquitetura atual e `SkyFaceDepth` ≈ 24 px; segunda face só se aparecer defeito real nas paredes finas.
- **8 vizinhos:** só se o losango persistir depois do ajuste do background.

### Implementação

- **`V8AmbientContext`:**
  - `ReceiverDaySkyIntensity` (nulo = usa `SkyIntensity`);
  - `NightStrength`;
  - `ReceiverSkyIntensity = lerp(ReceiverDaySkyIntensity ?? SkyIntensity, SkyIntensity, NightStrength)`;
  - `ReceiverSkyColor = AmbientLight × ReceiverSkyIntensity`.
  - `SkyColor` (background) não muda.
- **`V8AmbientField`:**
  - guarda as duas cores;
  - `Apply(effect, offset, backgroundSky)` escolhe qual vai para `AmbientSkyColor`;
  - a resolução de capturas (`SkyTexture`) continua com a cor do background, igual à referência de CPU.
- **`V8LightingRenderer.BeginReceivers`:** parâmetro `backgroundSky`. As faces continuam em `Apply(faceEffect, 0)`, portanto recebem a cor dos receptores.
- **`PlayingState.V8`:**
  - contexto de gameplay com `ReceiverDaySkyIntensity = 1` (referência V6: `AmbientLight × 1` no ar aberto);
  - `NightStrength = session.WorldNightStrength` a cada quadro;
  - o background passa a ter lote próprio entre as árvores de trás e as da frente, na mesma ordem de desenho.
- **`V8DiagnosticGame`:** background marcado como background. A fixture não define `ReceiverDaySkyIntensity`, então as cenas de diagnóstico ficam numericamente iguais.
- **Classificação dos receptores:**

  | Grupo | Cor do céu |
  |---|---|
  | Background | Background (inalterada) |
  | Árvores, cogumelos, água, móveis, portas abertas, entidades, núcleo do tecido, faces do foreground | Receptores |

- **Noite exata:** nas capturas (hora 0,02) `NightStrength` = 1, logo `lerp(1; 0,5; 1)` = 0,5 exatamente. Em amanhecer e entardecer a intensidade transita suavemente.

Não mudaram: propagação, semente, alcance, faces (largura e curva), direta, local, tochas, composição, V7.

### Verificações

| Execução | Antes (`*-faceband-after`, `toggle-faceband`) | Depois (`*-skyrx`) |
|---|---|---|
| build Release | — | 0 erros (2 CS0649 antigos) |
| `--v8-scene --v8-capture` | 107 / 0 | **107 / 0** |
| `--v8-scene --v8-ambient --v8-capture` | 103 / 0 | **103 / 0** |
| `--lighting-v8 --v8-gameplay-capture` | 89 / 0 | **89 / 0** |
| `--lighting-sky-compare` | 54 / 0 | **54 / 0** |
| `--v7-gameplay-smoke` | exit 0 | exit 0, `mode=V7` |
| `--lighting-toggle-bench --uncapped-frames` | 57 / 0 | **57 / 0** |
| `git diff --check` | — | OK |

### Valores antes/depois

Script local `receivers.ps1`: soma(final) ÷ soma(albedo) por canal em pixels opacos de receptor, sem HUD. O grupo de terreno usa o alfa do buffer de faces e inclui o núcleo preto.

| Caso | Grupo | Antes R/G/B | Depois R/G/B |
|---|---|---|---|
| surface-day | Árvores e decorações | 0,484 / 0,513 / 0,530 | **0,963 / 1,001 / 1,029** |
| surface-day | Caixa do personagem | 0,478 / 0,486 / 0,498 | **0,958 / 0,976 / 0,997** |
| surface-day | Terreno (inclui núcleo) | 0,085 / 0,119 / 0,057 | 0,159 / 0,227 / 0,103 |
| surface-night | Árvores e decorações | 0,159 / 0,211 / 0,289 | **0,159 / 0,211 / 0,289** |
| surface-night | Caixa do personagem | 0,152 / 0,185 / 0,258 | **0,152 / 0,185 / 0,258** |
| shallow-day | Background (predomina no grupo "outros") | 0,108 / 0,110 / 0,112 | **0,108 / 0,110 / 0,112** |
| shallow-day | Faces | 0,041 / 0,039 / 0,036 | 0,083 / 0,078 / 0,073 |

Faixa do foreground (`faceband.ps1`, média do canal máximo da luz da face por profundidade):

| Caso | 1 px | 4 px | 8 px | 10 px | 12 px |
|---|---|---|---|---|---|
| surface-day, antes | 138 | 105 | 40 | 14 | 0 |
| surface-day, depois | 254 | 206 | 80 | 28 | 1 |
| surface-night, antes = depois | 78 | 57 | 21 | 7 | 0 |
| shallow-day, antes | 42 | 33 | 13 | 5 | 0 |
| shallow-day, depois | 83 | 67 | 26 | 9 | 0 |

A faixa continua com 12 px; só a intensidade dobrou de dia. Os primeiros 2 px da superfície chegam a 250–254 (perto da saturação).

### Diferença zero onde não deveria mudar

- **Buffers `_sky` e `_local`:** diferença **0** em todos os 22 casos do gameplay. Propagação, alcance e preenchimento das tochas não mudaram.
- **Faces:** diferença **0** em `surface-night`, `shallow-sealed`, `cavern-off`, `cavern-torch`, `deep-torch`, `many-sources` e `sand`. Em todos os casos, pixels sem céu num raio de 13 px têm diferença 0.
- **Frames finais sem HUD, diferença 0:** `cavern-off`, `cavern-torch`, `deep-off`, `deep-torch`, `door-closed`, `door-open`, `many-sources`, `sand`, `shallow-night`, `shallow-sealed`, `wall-before`, `wall-mined`, `wall-rebuilt`.
- **`surface-night`:** 1152 pixels > 8 — só os dois quadrados de pré-visualização do cursor (máscara conferida).
- **Casos com câmera ou zoom diferentes** (`camera-pan`, `camera-shift`, `region-cross`, `wide-view`, `wrap`, `zoom`): diferenças apenas em chamas animadas e no cursor, como nas entregas anteriores.
- **Comparação do céu:**
  - V7: sem diferença > 8 fora do cursor;
  - V8 `exterior-night`: 416 pixels > 8, só os dois quadrados do cursor (máscara conferida);
  - V8 `shallow-sealed`: 512 pixels = dois cursores.
- **Mudaram como esperado:** `surface-day`, `shallow-day` (faces perto da janela) e, na comparação do céu, V8 `exterior-noon`, `shallow-opening-small`/`large` e `shallow-to-cavern` (faces próximas às aberturas).

### Imagens (inspecionadas)

Em `screenshots/v8/skyrx-compare/`:

| Arquivo | Leitura |
|---|---|
| `gameplay-surface-day.png`, `zoom-surface-day.png` | Árvores, tronco e personagem próximos da textura original; grama e faixa do terreno mais claras; interior do chão continua preto. |
| `sky-exterior-noon.png` | V8 depois com árvores e grama no nível da V7 sem sol; a V7 ainda mostra terra iluminada mais funda (passo 2). |
| `gameplay-surface-night.png`, `sky-exterior-night.png` | Visualmente idênticos (só o cursor mudou de lugar). |
| `gameplay-shallow-day.png`, `sky-shallow-opening-large.png` | Background igual; bordas das faces perto da janela e do túnel mais claras. |

### Limites

- **Saturação:** a composição continua por soma. Onde houver tocha ou cogumelo no exterior diurno, o receptor satura mais cedo (passo 6).
- **Faixa de 12 px:** os primeiros pixels do terreno exterior ficam perto de 255 (a profundidade de 3 tiles é o passo 2).
- **Céu de fundo sem brilho do sol e luas:** sem mudança, fora do escopo.
- **Custo:** nenhuma mudança de custo medida ou esperada, além de um lote extra de sprites por cópia de wrap. Não medi.

Sem commits.

## 2026-09-15 — Passo 2: faixa do céu nas faces com 24 px (implementado, para revisão)

**Pedido:**
- `SkyFaceDepth` ≈ 24 px, mantendo a arquitetura de uma face;
- curva perto da V6: ~0,6 a 1 tile, ~0,3 a 2 tiles, ~0 a 3 tiles;
- testar paredes e tetos de 1, 2, 3 e 5 tiles;
- sem segunda face;
- sem mudar background, propagação, tochas, intensidade diurna ou composição;
- degrau ou vazamento: registrar a evidência, sem corrigir.

### Implementação

- **`V8LightingRenderer.SkyFaceDepth`:** 12 → **24**. `FaceReach` = 24, então `LightBounds` cresce 12 px por lado (455×268 → 479×292 na captura) e a margem dos chunks de faces passa a ser 24 px.
- **`V8FaceChunks.SkyBandWeight`:** Hermite → `(1 − x/24)^1,15` (`SkyBandExponent`), com `x` = distância do centro do pixel à face e zero além de 24 px.

  | Profundidade | 1 px | 1 tile (9º px) | 2 tiles (17º px) | 24º px | ≥ 25 px |
  |---|---|---|---|---|---|
  | Peso | 0,976 | 0,605 | 0,26 | 0,012 | 0 |

- **Sem mudança:** regra da face mais próxima, direta/local (8 px), shaders, propagação, background, intensidade, tochas e composição.

### Fixtures de paredes finas (diagnóstico de ambiente)

**Cenas:**
- **`sky-band-ceilings`:** teto da sala selada engrossado para 1, 2, 3 e 5 tiles, com céu exterior em cima e sala escura embaixo.
- **`sky-band-walls`:** paredes verticais de 1, 2, 3 e 5 tiles, com céu exterior à esquerda e bolsão escuro de 2 tiles (parede de fundo) à direita.
- **`sky-band`:** o bloco espesso existente, que funciona como teto de 6 tiles.

**Verificações, independentes da curva:** ao longo da linha a partir da face do céu, os pixels mais próximos dela seguem `SkyBandWeight` relativo à profundidade 1 (erro ≤ 2) e os mais próximos da face escura ficam em **0**. O bolsão não recebe céu nem local. O valor antes da linha média é registrado como INFO (degrau).

Executadas primeiro com a faixa de 12 px (`scene-ambient-band12-fixtures`) e depois com 24 px (`scene-ambient-band24`).

| Fixture | Vazamento 12 px | Vazamento 24 px | Degrau na linha média (azul) 12 px → 24 px |
|---|---|---|---|
| Teto 1 tile / parede 1 tile | nenhum | nenhum | 89 → 0 / **94 → 0** |
| Teto 2 tiles / parede 2 tiles | nenhum | nenhum | 35 → 0 / **73 → 0** |
| Teto 3 tiles / parede 3 tiles | nenhum | nenhum | 0 / **53 → 0** |
| Teto 5 tiles / parede 5 tiles | nenhum | nenhum | 0 / **16 → 0** |
| Bloco 6 tiles | nenhum | nenhum | 0 / 1 → 0 |

**Linha medida no bloco com 24 px (azul por pixel):** 109, 104, 99, 94, 88, 83, 78, 73 | **68** (1 tile, 0,62), … | **29** (2 tiles, 0,27), … 4, 1, 0. A curva bate com o alvo.

**Evidência visual** (`screenshots/v8/band24-compare/fixture-sky-band-ceilings.png` e `fixture-sky-band-walls.png`, buffer de luz das faces):
- **Sem vazamento:** o lado escuro fica preto em todas as espessuras.
- **Degrau (defeito registrado, não corrigido):** em tetos e paredes de 2 e 3 tiles a faixa termina num corte reto na linha média (73 e 53 de ~109). Com 12 px, só existia em 1 e 2 tiles.
- **Cortes diagonais:** no teto e na parede de 5 tiles, e nas quinas dos blocos, aparecem cortes a 45° onde a face mais próxima muda de lado (em cima para o lado, lado de fora para o bolsão). É a partição da regra de face única, agora visível porque a faixa ficou mais funda que meia espessura.
- **Onde vale olhar no jogo:** situações reais com tetos ou paredes finas sob o céu. Nas capturas de superfície não encontrei esse caso (o terreno é espesso).

### Verificações

| Execução | Antes (passo 1) | Depois (24 px) |
|---|---|---|
| build Release | — | 0 erros |
| `--v8-scene --v8-capture` | 107 / 0 | **107 / 0** |
| `--v8-scene --v8-ambient --v8-capture` | 103 / 0 (141 / 0 com as fixtures a 12 px) | **141 / 0** |
| `--lighting-v8 --v8-gameplay-capture` | 89 / 0 | **89 / 0** (faces ancoradas: erro 0–1) |
| `--lighting-sky-compare` | 54 / 0 | **54 / 0** |
| `--v7-gameplay-smoke` | exit 0 | exit 0, `mode=V7` |
| `--lighting-toggle-bench --uncapped-frames` | 57 / 0 | **57 / 0** |
| `git diff --check` | — | OK |

### Valores antes/depois

**Faixa real do terreno** (`faceband.ps1`, média do canal máximo da luz da face, 343 colunas em `surface-day`):

| Profundidade | 1 | 4 | 8 | 9 (1 tile) | 12 | 16 | 17 (2 tiles) | 20 | 24 | 25 |
|---|---|---|---|---|---|---|---|---|---|---|
| surface-day, 12 px | 254 | 206 | 80 | 52 | 1 | 0 | 0 | 0 | 0 | 0 |
| surface-day, 24 px | 253 | 216 | 164 | 153 | 119 | 77 | 66 | 37 | 2 | 0 |
| surface-night, 12 px | 78 | 57 | 21 | 13 | 0 | 0 | 0 | 0 | 0 | 0 |
| surface-night, 24 px | 77 | 59 | 43 | 40 | 31 | 20 | 17 | 10 | 0 | 0 |
| shallow-day, 12 px | 83 | 67 | 26 | 17 | 0 | 0 | 0 | 0 | 0 | 0 |
| shallow-day, 24 px | 82 | 70 | 54 | 51 | 39 | 24 | 20 | 11 | 4 | 4* |

\* O filtro de colunas do script exige só ±14 px de sólido lateral; alguns pixels recebem a faixa de uma face lateral a menos de 24 px.

**Leitura:** de dia, 1 tile ≈ 0,60 e 2 tiles ≈ 0,26 do primeiro pixel, que está perto de 255. O brilho junto à face não subiu; a faixa passou a se distribuir por 3 tiles.

**Outros efeitos:**
- **Receptores** (`receivers.ps1`): árvores/decorações e personagem **idênticos** de dia (0,963/1,001/1,029; 0,958/0,976/0,997) e de noite (0,159/0,211/0,289; 0,152/0,185/0,258).
- **Terreno (inclui núcleo preto), final ÷ albedo:** dia 0,159/0,227/0,103 → 0,266/0,337/0,221; noite 0,033/0,050/0,034 → 0,043/0,067/0,058.
- **Noite:** a intensidade continua igual; só a profundidade da faixa mudou, também à noite, como consequência direta do passo.

### Diferença zero onde não deveria mudar

- **Buffers `_sky`, `_local` e `_direct`:** comparados pelas coordenadas de mundo (`registered.ps1`, porque o tamanho mudou), diferença **0** nos 22 casos. Exceção: `wide-view` direta com 1/255 na borda. Propagação, alcance, tochas e direta inalterados.
- **Faces:** diferença **0** em `shallow-sealed`, `cavern-off`, `cavern-torch`, `deep-torch`, `many-sources` e `sand`.
  - A contagem "sem céu" do `faceband.ps1` usa raio de 13 px e não vale mais para uma faixa de 24 px.
  - As mudanças em superfície e Shallow são a faixa.
- **Frames finais sem HUD:**
  - diferença 0 em `cavern-off`, `cavern-torch`, `deep-off`, `deep-torch` e `shallow-sealed`;
  - portas, paredes, `many-sources`, `sand` e casos de câmera: só chamas animadas (máscara de `sand` conferida) e cursor;
  - `shallow-night`: máximo 7/255, faixa noturna mais funda perto da janela.

### Desempenho (`--v8-perf-bench --uncapped-frames`, antes e depois, 1920×1080, zoom 2)

| Métrica | 12 px | 24 px |
|---|---|---|
| Construção de faces nos quadros de reconstrução ao andar (18 de 640) | mediana 3,51–3,68 / máx 4,10–4,26 ms | mediana **5,89–6,13** / máx **6,89–6,94 ms** |
| Vértices de faces (estático) | 278.520 | **490.320** (+76%) |
| Pixels de face com peso | 36.736 | 72.448 |
| Desenho das faces (CPU) | 0,08 ms | 0,08 ms |
| CPU V8 mediana (estático / andando) | 3,36–4,02 / 3,34–3,86 ms | 3,54–4,03 / 3,50–3,88 ms |
| CPU V8 máxima ao andar | 7,99–8,62 ms | 10,70–10,98 ms |
| Quadros > 16,8 ms | 0 | 0 |

Custo de GPU não medido. O aumento fica só nos quadros em que chunks entram na tela; a reutilização por ocupação não mudou (18 quadros de reconstrução, 180 chunks).

### Imagens (inspecionadas)

Em `screenshots/v8/band24-compare/`:

| Arquivo | Leitura |
|---|---|
| `zoom-surface-day.png`, `gameplay-surface-day.png` | Faixa de terra texturizada de ~3 tiles sob a grama, escurecendo suavemente até o preto; interior preto; árvores e personagem iguais. |
| `zoom-exterior-noon.png` | V8 com 24 px próxima da leitura da V7 sem sol nos primeiros ~3 tiles; a V7 continua iluminando mais fundo (luz atravessa sólido). |
| `zoom-surface-night.png` | Mesma intensidade noturna; faixa de terra discreta mais funda. |
| `gameplay-shallow-day.png` | Background igual; faces junto à janela com faixa mais funda. |
| `fixture-sky-band-ceilings.png`, `fixture-sky-band-walls.png` | Sem vazamento; degrau na linha média em 2–3 tiles e cortes diagonais nas quinas e na espessura de 5 tiles (evidência acima). |

**Pendente de decisão do usuário:** se o degrau e os cortes diagonais em estruturas finas justificam uma correção, que só será feita depois da revisão. Sem commits.

## 2026-09-16 — Passo 3: alcance visual do background (diagnóstico, para escolha)

**Pedido:** sem tocar em `SkyRangeTiles` (segue 24) nem no transporte, dar ao background um alcance visual próprio, remapeando a exposição que ele já recebe. Sem gamma, sem mudar intensidade. Comparar 24 (atual), 10, 12 e 14 tiles e parar para avaliação.

### Alterações (só o receptor do background)

| Arquivo | Mudança |
|---|---|
| `Content/effects/V8Ambient.fxh` | Parâmetro `AmbientSkyCutoff` e, em `AmbientAt`, `visible = saturate((min(exposure, layerWeight) − corte) / (1 − corte))`. Com corte 0 é idêntico ao anterior. |
| `V8AmbientContext.cs` | `BackgroundSkyRangeTiles` (nulo = comportamento atual) e `BackgroundSkyCutoff = 1 − alcance / SkyRangeTiles`. |
| `V8AmbientField.cs` | Guarda o corte e o envia **apenas** no lote do background (`Apply(..., backgroundRange)`); a resolução das capturas `_sky` continua com exposição bruta. |
| `V8LightingRenderer.cs` | `BeginReceivers` repassa o sinalizador. |
| `V8GameplayOptions.cs`, `PlayingState.V8.cs` | Opção de diagnóstico `--v8-bg-range <tiles>`, nula por padrão. |

Sem textura nova, sem propagação nova, sem buffer novo e sem trabalho por chunk: a operação é aritmética no shader do receptor.

### Transporte inalterado (fato medido)

- **Exposição bruta por distância** (canal R de `_sky`, CSV do `--lighting-sky-compare`): **idêntica** nas quatro variantes, dos 0 aos 28 tiles, nas duas aberturas.
- **Buffers `_sky`, `_local` e `_direct`** entre variantes, registrados por coordenadas de mundo: diferença **0** em `shallow-day`, `surface-day` e `cavern-torch`.
- **Faces:** diferença **0** entre a rodada sem a opção e o estado aprovado do passo 2 (`surface-day`, `surface-night`, `shallow-day`).
- **Receptores:** árvores, decorações e personagem idênticos de dia e à noite.
- **Rodada sem a opção × passo 2 aprovado:** buffers idênticos; nos frames finais só o quadrado do cursor (576 px, 1 tile a zoom 3) e chamas animadas. O grupo "terreno" medido varia 0,266 → 0,273 exatamente por esse quadrado.

### Perfis, túnel com abertura 2×2 (luma final do background, 0..255)

| Ponto | 24 (atual) | 10 tiles | 12 tiles | 14 tiles |
|---|---|---|---|---|
| Abertura (0) | 121,8 | 121,8 | 121,8 | 121,8 |
| 25% do alcance | 47,8 (6 t) | ~55 (2–3 t) | 50,1 (3 t) | ~51 (3–4 t) |
| 50% | 26,9 (12 t) | 38,3 (5 t) | 34,8 (6 t) | 28,2 (7 t) |
| 75% | 14,3 (18 t) | 19,4 (7–8 t) | 19,8 (9 t) | 17,5 (10–11 t) |
| Fim do alcance | 1,8 (24 t) | 4,4 (10 t) | 3,7 (12 t) | 4,0 (14 t) |
| 2 tiles depois | 0 (26 t) | 0 (12 t) | 0 (14 t) | 0 (16 t) |
| **Alcance (luma > 2)** | **23 t** | **10 t** | **12 t** | **14 t** |

Perto da abertura a perda é pequena: a 2 tiles, 67,4 → 62,9 / 64,0 / 65,0 (−7% a −4%).

### Perfis, túnel com abertura 6×6 (a abertura ocupa os tiles 0–5)

| Ponto | 24 (atual) | 10 tiles | 12 tiles | 14 tiles |
|---|---|---|---|---|
| 1º tile após a abertura (6 t) | 49,2 | 46,0 | 47,0 | 47,4 |
| 9 t | 50,2 | 35,9 | 39,9 | 42,7 |
| 12 t | 35,3 | 15,0 | 20,7 | 24,9 |
| 16 t | 27,0 | 0 | 4,0 | 10,6 |
| 18 t | 22,9 | 0 | 0 | 3,5 |
| **Alcance (luma > 2)** | **28 t** | **14 t** | **16 t** | **18 t** |

A abertura grande mantém **+4 tiles** de alcance sobre a pequena em todas as variantes, o que corresponde à sua largura extra.

### Shallow real (`shallow-day`), indicativo

`final ÷ albedo` em pixels de background numa faixa de ±12 linhas a partir da janela (script local):

| Distância | 24 (atual) | 10 tiles | 12 tiles | 14 tiles |
|---|---|---|---|---|
| 0 | 0,791 | 0,762 | 0,770 | 0,776 |
| 6 t | 0,583 | 0,456 | 0,522 | 0,508 |
| 12 t | 0,549 | 0,319 | 0,378 | 0,427 |
| 20 t | 0,392 | 0,289 | 0,289 | 0,289 |

**Limite dessa medição:** a faixa amostrada inclui receptores que não são a parede de fundo (água, móveis, cogumelos, jogador) e outras aberturas da câmara, então os valores não caem de forma monotônica entre variantes (a 8 tiles: 0,460 com 10, 0,525 com 12, 0,441 com 14). A exposição bruta na mesma linha é idêntica nas quatro. Os túneis controlados acima são a evidência numérica; aqui valem as imagens.

### Regressões

| Item | Resultado |
|---|---|
| `--v8-scene --v8-capture` | 107 / 0 |
| `--v8-scene --v8-ambient --v8-capture` | 141 / 0 |
| `--lighting-v8 --v8-gameplay-capture` (24, 10, 12 e 14) | **89 / 0 em cada** |
| `--lighting-sky-compare` (24, 10, 12 e 14) | **54 / 0 em cada** |
| `--lighting-toggle-bench --uncapped-frames` | 57 / 0 |
| `--v7-gameplay-smoke` | exit 0, `mode=V7` |
| Sala selada (`shallow-sealed`) | luma V8 **0,0** em todos os pontos, nas quatro variantes |
| Cavern (`shallow-to-cavern`, linhas +1 em diante) | **0,0** nas quatro |
| `git diff --check` | OK |
| Build Release | 0 erros |

**Shallow → Cavern, últimas linhas da Shallow** (luma V8): linha −7: 56,1 / 52,3 / 53,4 / 54,2; linha −4: 39,7 / 11,0 / 19,1 / 25,1; linha −1: 7,8 / 0 / 0 / 0. Alcance: 13 / 10 / 10 / 11 linhas. O desvanecimento da camada e o remapeamento se somam nas últimas linhas.

### Observações visuais (`screenshots/v8/bgrange-compare/`)

- **10 tiles:** mancha curta; a parede escurece pouco depois da janela e a maior parte da câmara fica preta. É o mais próximo da V6 (9 tiles).
- **12 tiles:** luz clara junto da abertura e queda perceptível dentro da mesma câmara; grandes áreas seguem escuras.
- **14 tiles:** ainda cobre boa parte de uma câmara média; menos preenchido que 24, mais que 12.
- **24 (atual):** parede visível por quase toda a câmara, que é o problema relatado.
- **Abertura pequena:** continua claramente perceptível nas três variantes (perda ≤ 7% junto à abertura).
- **Abertura grande:** ilumina área maior que a pequena sem clarear a sala inteira em 10 e 12; em 14 a mancha ainda é ampla.
- **Registrado, não tratado:** o formato losangular da mancha continua visível na Shallow real e nas aberturas pequenas.

### Hipóteses

- 10 tiles deve parecer corte abrupto em câmaras médias, apesar de ser o mais fiel à V6.
- 12 tiles tende a ser o equilíbrio pedido (perto da V6, com um pouco mais de alcance).
- O losango deve ficar mais evidente quanto menor o alcance, porque a queda fica concentrada. Só o passo de 8 vizinhos responderia isso.

**Nenhum valor foi escolhido.** O padrão do jogo continua 24 tiles; 10, 12 e 14 existem apenas via `--v8-bg-range`. Sem commits.

### Decisão do usuário (2026-09-16)

Passo 3 aprovado provisoriamente com **12 tiles**: 10 escurece cedo demais, 14 ainda revela área grande da câmara. `BackgroundSkyRangeTiles = 12` passou a ser o padrão do jogo (`PlayingState.V8`); `--v8-bg-range` continua existindo só para diagnóstico. Gamma segue 1 e a intensidade do background não mudou.

## 2026-09-16 — Passo 4: propagação do céu com 8 vizinhos (diagnóstico, para escolha)

**Pedido:** comparar 4 × 8 vizinhos **apenas** no campo de céu, com custo ortogonal 1 e diagonal √2, bloqueio de quina obrigatório e nenhuma outra mudança. Não tornar padrão.

### Alterações

| Arquivo | Mudança |
|---|---|
| `V8AmbientField.Propagate` | Parâmetro `diagonals`. Com ele, além das quatro ortogonais, as quatro diagonais com passo `loss × custo da célula × √2`. A diagonal só é usada quando **as duas** células ortogonais daquela quina são transitáveis (`cost > 0`). Transitabilidade e custos por célula são os existentes. |
| `V8AmbientContext` | `SkyDiagonalTransport` (falso por padrão). Só o campo de céu o recebe; o preenchimento local continua em 4 vizinhos. |
| `V8GameplayOptions`, `PlayingState.V8`, `V8DiagnosticScene` | Opção `--v8-sky-diagonals`. |
| `V8DiagnosticScene`, `V8DiagnosticGame`, `V8AmbientValidation` | Fixtures novas `sky-aperture` (furo de fundo 2×2 dentro da câmara, longe das paredes) e `sky-corner` (quina diagonal fechada). |

**Fixture de quina** (`sky-corner`), em tiles de mundo: ar iluminado em (0,11); sólidos em (1,11) e (0,12); bolsão em (1,12), cercado também por sólidos em (2,12) e (1,13), com parede de fundo para não semear sozinho. O único contato com a luz é a diagonal fechada.

### Perfil eixo × diagonal (fixture `sky-aperture`, exposição do buffer de céu, 0..255)

| Passo | 1 | 2 | 4 | 6 | 8 | 10 | 12 | Alcance |
|---|---|---|---|---|---|---|---|---|
| Eixo, 4 vizinhos | 107 | 103 | 93 | 84 | 74 | 65 | 56 | > 12 |
| Eixo, 8 vizinhos | 107 | 103 | 93 | 84 | 75 | 65 | 56 | > 12 |
| Diagonal 45°, 4 vizinhos | 103 | 93 | 74 | 55 | 37 | 18 | 0 | 11 |
| Diagonal 45°, 8 vizinhos | 105 | 99 | 85 | 72 | 59 | 46 | 33 | > 12 |

**Fato medido:** a perda por passo no eixo é ~4,7 nas duas vizinhanças. Na diagonal ela cai de ~9,4 (4 vizinhos) para ~6,5 (8 vizinhos). A razão diagonal/eixo passa de **2,02** (métrica Manhattan) para **1,40**, que é √2. Ou seja, ao longo de 45° a distância vira a euclidiana; o alcance diagonal sai de 50% do axial para ~71%, que é o valor geometricamente correto.

**Limite:** isso mede só 0° e 45°, os dois ângulos em que a métrica de 8 vizinhos é exata. O erro residual de um octógono aparece em ângulos intermediários (~22,5°) e **não foi medido**.

### Fixture de quina

| Vizinhança | Ar ao lado da quina | Bolsão atrás da diagonal fechada |
|---|---|---|
| 4 vizinhos | 112 | **0** |
| 8 vizinhos | 112 | **0** |

O bloqueio de quina impede a passagem diagonal, como pedido.

### Cenas de corredor (a geometria restringe o caminho)

| Cena | Exposição bruta | Luma final | Alcance |
|---|---|---|---|
| Abertura 2×2 | igual dentro de ±0,004 (quantização de 8 bits) | igual dentro de 0,3 | 12 tiles nas duas |
| Abertura 6×6 | idêntica | idêntica | 16 tiles nas duas |

**Fato:** num túnel de 6 tiles de altura quase não existe caminho diagonal, então 8 vizinhos praticamente não muda nada. A diferença aparece em câmaras abertas.

### Regressões

| Item | Resultado |
|---|---|
| `--v8-scene --v8-capture` | 107 / 0 |
| `--v8-scene --v8-ambient --v8-capture` (4 e 8 vizinhos) | **154 / 0 em cada** |
| `--lighting-v8 --v8-gameplay-capture` (4 e 8) | **89 / 0 em cada** |
| `--lighting-sky-compare` (4 e 8) | **54 / 0 em cada** |
| `--lighting-toggle-bench --uncapped-frames` | 57 / 0 |
| `--v7-gameplay-smoke` | exit 0, `mode=V7` |
| `git diff --check` / build Release | OK / 0 erros |

**Buffers, 4 × 8 vizinhos, registrados por coordenadas de mundo:**
- `_direct` e `_local`: diferença **0** em todos os 12 casos comparados.
- `_sky`: diferença 0 em `surface-day`, `surface-night`, `shallow-sealed`, `cavern-off`, `cavern-torch`, `deep-torch`, `many-sources`, `door-closed`, `door-open`, `sand` e `wrap`. Muda só em `shallow-day` (48.631 pixels > 1, máximo 37/255), que é o único caso com abertura e câmara aberta na vista.
- **Sala selada:** luma V8 0,0 nas duas. **Cavern:** 0,0 a partir da primeira linha nas duas, com alcance de 10 linhas dentro da Shallow em ambas. **Portas fechadas:** continuam bloqueando (buffers idênticos).

### Custo (`--v8-perf-bench --uncapped-frames`, `LastFieldMs` = `ambient_grid_ms`, mediana/p95)

| Cenário | 4 vizinhos | 8 vizinhos |
|---|---|---|
| parado, 0 tochas | 2,33 / 2,75 ms | 2,40 / 2,95 ms |
| andando, 0 tochas | 2,20 / 2,61 ms | 2,29 / 3,07 ms |
| parado, 4 tochas | 2,79 / 3,09 ms | 2,93 / 3,23 ms |
| andando, 4 tochas | 2,63 / 2,96 ms | 2,75 / 3,15 ms |

**Fato:** +3% a +6% na mediana e +7% a +18% no p95. Nenhuma otimização foi feita. Um pico isolado de 18,5 ms no ambiente com 8 vizinhos (andando, 0 tochas) veio do **upload** (máximo 14,7 ms), não da grade.

### Observações visuais (`screenshots/v8/n8-compare/`)

- **`fixture-sky-aperture.png`:** com 4 vizinhos a mancha ao redor do furo tem contorno de losango, com as quinas cortadas em 45°. Com 8 vizinhos a região vira um quadrado de cantos arredondados: a luz alcança visivelmente mais longe nas diagonais e a borda fica mais contínua.
- **`zoom-shallow-day.png`:** na câmara real, o cone abaixo da janela deixa de ter a aresta reta em 45° e a parede continua iluminada mais adiante na diagonal. A área clara cresce moderadamente, mas a queda e as regiões escuras do passo 3 continuam.
- **`sky-shallow-opening-small.png`, `sky-shallow-opening-large.png`:** praticamente idênticas, como esperado num corredor.
- **`fixture-sky-corner.png`:** o bolsão continua preto nas duas; a cruz de sólidos aparece igual.
- **`sky-shallow-sealed.png`, `sky-shallow-to-cavern.png`:** sem diferença visível.

### Hipóteses

- O losango residual em ângulos intermediários deve continuar existindo com 8 vizinhos, embora bem menos visível; só um perfil a ~22,5° confirmaria.
- Como 8 vizinhos alarga a mancha nas diagonais, o alcance visual de 12 tiles do background pode parecer um pouco mais generoso na prática; avaliar junto, não isoladamente.
- O aumento de custo medido é pequeno, mas o Dijkstra passou a avaliar o dobro de arestas; em mundos com câmaras muito abertas a diferença pode ser maior que a medida aqui.

**Nada foi tornado padrão.** O jogo continua com 4 vizinhos; 8 vizinhos existem apenas via `--v8-sky-diagonals`. Sem commits.

### Decisão do usuário (2026-09-16): 8 vizinhos promovidos a padrão

Aprovado pela melhora visual na abertura pequena e na `shallow-day`. Nenhuma calibração artística nova nesta promoção.

| Arquivo | Mudança |
|---|---|
| `V8AmbientContext` | `SkyDiagonalTransport` passa a nascer **`true`**: ortogonal 1, diagonal √2, diagonal só quando as duas ortogonais da quina são transitáveis. Vale **só para o céu**; o preenchimento local das fontes continua em 4 vizinhos. |
| `V8GameplayOptions` | `--v8-sky-diagonals` deixou de existir; entrou `--v8-sky-4-neighbours`, que volta ao transporte antigo para diagnóstico e regressão. Sem referências pendentes ao nome antigo. |
| `PlayingState.V8`, `V8DiagnosticScene` | `SkyDiagonalTransport = !V8GameplayOptions.SkyFourNeighbours`. O caminho normal não ganhou ramificação. |

**O padrão reproduz exatamente a variante já medida:**
- `_sky`, `_local` e `_direct` com diferença **0** em **22 casos** da captura de gameplay, registrados por coordenadas de mundo, contra a rodada `gameplay-n8`.
- Perfis do `--lighting-sky-compare` idênticos: exposição bruta e luma final iguais, alcance 12 tiles (abertura 2×2) e 16 tiles (6×6).
- Fixture `sky-aperture`, diagonal de 45°: `105,99,92,85,79,72,66,59,52,46,39,33`, igual à rodada de 8 vizinhos.
- Fallback `--v8-sky-4-neighbours` reproduz a linha antiga: `103,93,84,74,65,55,46,37,28,18,9,0`, alcance 11.
- Fixture `sky-corner`: bolsão em **0** no padrão e no fallback.

**Regressões finais:** cena direta 107/0; ambiente 154/0 no padrão e 154/0 no fallback; gameplay 89/0; comparação do céu 54/0; alternância F4 57/0; smoke da V7 exit 0 (`mode=V7`); build Release 0 erros; `git diff --check` OK.

**Frames finais** contra `gameplay-n8`: apenas chamas animadas e o quadrado de pré-visualização do cursor. Os 72 pixels de `surface-day` na borda esquerda (x0..2) são esse quadrado cortado pela borda da tela, conferido em recorte ampliado (`screenshots/v8/n8-compare/edge-surface-day.png`).

**Congelados:** `BackgroundSkyRangeTiles` 12, gamma 1, intensidade do background, `SkyRangeTiles` 24, `SkyFaceDepth` 24 px, curva das faces, intensidade diurna dos receptores, noite, direta, local, tochas, composição e sol.

**A luz natural sem sol fica encerrada.** A próxima etapa é a calibração da tocha, começando exclusivamente pela luz direta e mantendo o preenchimento local atual no primeiro experimento. Sem commits.

## 2026-09-16 — Passo 5: raio da luz direta da tocha (diagnóstico, para escolha)

**Pedido:** única variável artística é o alcance/falloff da **direta** da tocha. Comparar 35 (atual), 14, 16 e 18 tiles. Preenchimento local intocado. Nada promovido a padrão.

### Como raio e falloff funcionam hoje (fato do código)

- **Raio:** `PlayingState.V8.CollectV8Sources` criava a tocha com `new V8Light(torch.LightOrigin, (1; 0,65; 0,28), 280)` — 280 px = **35 tiles**. Cogumelos têm raio direto 0 e só preenchimento local (128 px).
- **Falloff** (`V8Direct.fx`): `t = saturate(1 − distância/Radius)` e a saída é `Radiance × t²`. **A curva é normalizada pelo raio.**
- **Consequência:** reduzir o raio **não corta apenas a cauda**. Em qualquer distância fixa o valor cai, porque `t` é relativo a `R`; o pico na fonte continua 1 e a curva fica mais concentrada. A 4 tiles o fator é 0,787 (35 tiles), 0,605 (18), 0,563 (16) e 0,510 (14).
- **Quem usa o raio:** o quad de luz (±R), os quads de sombra (extrusão 2R+2, sempre além do quad de luz), o recorte de arestas por fonte, a varredura de cópias de wrap, o parâmetro do shader, a região de geometria (`ceil(maxRadius) + alcance de faces + 2`) e o descarte de fontes (`max(Radius, AmbientRadius) + FaceReach + 2`).
- **Não usa o raio:** `LightBounds` (só depende de `FaceReach`) e `V8AmbientField`, que usa apenas `AmbientRadius`. Por construção, o preenchimento local não muda.

**Instrumentação:** `--v8-torch-radius <tiles>`, só diagnóstico; o padrão continua 35 tiles. Raio aplicado confirmado nas capturas: 280 / 112 / 128 / 144 px, com `AmbientRadius = 256` nas quatro.

### Perfil da direta (cena `cavern-torch`, eixo horizontal a partir da tocha)

Canal R do buffer `_direct`; o `_local` da mesma linha é idêntico nas quatro variantes.

| Distância | 35 tiles | 14 tiles | 16 tiles | 18 tiles | `_local` (igual nas 4) |
|---|---|---|---|---|---|
| 2 t | 226 | 185 | 193 | 200 | 57 |
| 4 t | 199 | 128 | 142 | 153 | 53 |
| 6 t | 174 | 82 | 98 | 112 | 49 |
| 8 t | 151 | 46 | 63 | 78 | 45 |
| 10 t | 129 | 20 | 35 | 50 | 41 |
| 12 t | 110 | 5 | 15 | 28 | 37 |
| 14 t | 91 | 0 | 4 | 12 | 33 |
| 16 t | 75 | 0 | 0 | 3 | 29 |
| 18 t | 60 | 0 | 0 | 0 | 25 |
| 20 t | 46 | 0 | 0 | 0 | 22 |
| **Limite efetivo** | **> 20 t** | **13 t** | **15 t** | **17 t** | — |

**Ponto de cruzamento** (onde a direta cai abaixo do preenchimento local): ~8 tiles com 14, ~9 com 16 e ~11 com 18. Com 35 tiles a direta ainda é 2× o local aos 20 tiles.

`final ÷ albedo` na mesma linha (informação visual secundária):

| Distância | 35 | 14 | 16 | 18 |
|---|---|---|---|---|
| 4 t | 0,987 | 0,709 | 0,759 | 0,810 |
| 8 t | 0,768 | 0,360 | 0,424 | 0,480 |
| 12 t | 0,582 | 0,165 | 0,203 | 0,253 |
| 20 t | 0,264 | 0,088 | 0,088 | 0,088 |

Aos 20 tiles as três variantes convergem para 0,088, que é o preenchimento local puro.

### Céu e local inalterados (fato medido)

Diffs registrados por coordenadas de mundo, 35 × 14, 35 × 16 e 35 × 18:
- **`_sky`: diferença 0** em todos os casos comparados.
- **`_local`: diferença 0** em todos os casos comparados.
- **`_direct`:** única contribuição que muda (`cavern-torch`, `deep-torch`, `door-closed`, `wall-before` e `many-sources`). Nas cenas sem tocha (`surface-day`, `shallow-day`, `shallow-sealed`) a direta também é **idêntica**, como esperado.
- **Sombras:** a silhueta das sombras é a mesma; muda só até onde a luz chega. A extrusão dos quads de sombra é `2R + 2`, sempre além do quad de luz `±R`, então a umbra continua cobrindo toda a área iluminada.
- **Dia × noite numa sala fechada:** em Cavern o céu é zero por veto de camada, conferido pela verificação `sky zero in forbidden layers`, que passa nas quatro rodadas. A tocha ali não depende da hora por construção.

### Regressões

| Execução | Resultado |
|---|---|
| `--v8-scene --v8-capture` | 107 / 0 |
| `--v8-scene --v8-ambient --v8-capture` | 154 / 0 |
| `--lighting-sky-compare` | 54 / 0 |
| `--lighting-toggle-bench` (35, 14, 16, 18) | **57 / 0 em cada** |
| `--v7-gameplay-smoke` | exit 0, `mode=V7` |
| `--lighting-v8 --v8-gameplay-capture` | 89 / 0 com 35 tiles; **88 / 1 com 14, 16 e 18** |
| build Release / `git diff --check` | 0 erros / OK |

**O único FAIL é `wall-mined: mining changes direct`**, e a causa está medida: os pixels com direta > 0 na cena da parede ficam **iguais antes e depois de minerar** (28.810 com 14 tiles, 34.787 com 16, 39.502 com 18), porque a parede minerada cai fora do novo raio. Com 35 tiles o número muda (54.002 → 63.806) e a verificação passa. É uma suposição do harness amarrada ao raio atual, não um defeito de iluminação. **Não alterei a verificação**, já que nenhum raio foi escolhido; quando houver decisão, a testemunha precisa ficar dentro do raio escolhido.

### Custo (observação, sem investigação nova)

- **Fontes coletadas:** 1 em todas as variantes (`cavern-torch`). O descarte usa `max(Radius, AmbientRadius)`, então abaixo de 32 tiles quem manda é o raio local de 256 px.
- **`LightBounds`:** 479×292 idêntico nas quatro, como esperado.
- **Região de geometria:** encolhe com o raio, por `ceil(maxRadius)` na fórmula (fato do código, não medido isoladamente).
- **Etapa direta, quadro da captura:** 3,08 ms com 35 tiles contra 0,018–0,026 ms nas outras três. É **um quadro**, não uma bancada; é coerente com um quad de luz 6× maior em área e mais arestas por fonte, mas não vale como medição de desempenho.

### Observação visual (`screenshots/v8/torch-compare/`)

- **35 tiles (atual):** a tocha ilumina praticamente toda a câmara visível; a parede de fundo lê como "parede inteira acesa".
- **14 tiles:** poça de luz forte concentrada nos primeiros ~5 tiles, escuridão a partir de ~13; a região distante passa a depender só do preenchimento local. É a variante mais próxima da concentração da V7.
- **16 tiles:** mesma leitura, com transição terminando ~15 tiles.
- **18 tiles:** ainda concentrada perto da fonte, mas mantém bem mais da parede visível.
- **`door-closed` e `direct-buffer-cavern`:** as cunhas de sombra têm forma idêntica nas quatro; muda só o alcance.
- **Shallow com tocha:** a contribuição da tocha ao redor do jogador encolhe visivelmente; a luz das fissuras não muda.

**Nada foi promovido a padrão.** O jogo continua com 35 tiles; 14, 16 e 18 existem só via `--v8-torch-radius`. O preenchimento local não foi tocado e fica para a etapa seguinte. Sem commits.

## 2026-09-16 — Passo 5 encerrado: raio direto da tocha = 14 tiles (padrão)

**Decisão do usuário:** raio direto aprovado em **14 tiles (112 px)**.

**Motivo visual:** é a variante em que a luz forte fica concentrada nos primeiros ~5 tiles, a transição para o escuro termina por volta de 13 tiles e a parede de fundo deixa de ler como "inteira acesa"; além disso, a partir de ~8 tiles a região passa a depender do preenchimento local, que é a leitura pretendida. As sombras geométricas continuam idênticas em forma.

**Falloff inalterado:** `t = saturate(1 − d/R)`, `Direct = Radiance × t²`. Como a curva é normalizada pelo raio, o valor **14** define ao mesmo tempo o suporte e o formato da queda; foi aprovado como conjunto e a curva **não** foi compensada.

### O que mudou

| Arquivo | Mudança |
|---|---|
| `PlayingState.V8.CollectV8Sources` | `(V8GameplayOptions.TorchDirectRadiusTiles ?? 14f) × TileSize`. Nenhum caminho de gameplay usa mais 280 px. |
| `V8GameplayOptions` | Comentário atualizado; `--v8-torch-radius <tiles>` continua existindo e sobrescreve o padrão só quando usada. |
| `PlayingState.V8Capture` | Correção do harness (abaixo). |

**Raio confirmado em captura:** sem opção → `Radius = 112` em `cavern-torch` e `wall-before`; com `--v8-torch-radius 35` → `Radius = 280` nos mesmos casos, com a bateria completa passando (90/0) nas duas.

### Correção da regressão `wall-mined` (teste, não renderer)

A verificação antiga comparava o buffer `_direct` inteiro e dependia de a parede minerada estar dentro do raio; com 14 tiles ela ficava a 22 tiles da tocha e nada mudava.

- **Cena:** nas cenas de parede a tocha passou de `(14, 14)` para `(30, 14)`, a 6 tiles do divisor sólido em `x = 36`. As demais cenas mantêm a posição original.
- **Testemunha:** `(38, 15)`, atrás do divisor, a ~8 tiles da fonte, dentro do raio.
- **Asserts, sempre no buffer `_direct`:**

  | Caso | Verificação | Medido |
  |---|---|---|
  | `wall-before` | parede bloqueia a direta na testemunha | **0** |
  | `wall-mined` | minerar abre a direta na testemunha (> 20) **e** o buffer inteiro muda | **44** |
  | `wall-rebuilt` | reconstruir devolve a testemunha a zero **e** o buffer volta a ser idêntico à linha de base | **0** |

A severidade aumentou: além da comparação de buffer inteiro que já existia, há agora uma testemunha explícita com valor esperado. O bloco passou de 2 para 3 verificações, e por isso o total do gameplay foi de 89 para **90**.

### Regressões

| Execução | Resultado |
|---|---|
| Build Release | 0 erros |
| `--v8-scene --v8-capture` | 107 / 0 |
| `--v8-scene --v8-ambient --v8-capture` | 154 / 0 |
| `--lighting-v8 --v8-gameplay-capture` | **90 / 0** |
| `--lighting-v8 --v8-gameplay-capture --v8-torch-radius 35` | 90 / 0 |
| `--lighting-sky-compare` | 54 / 0 |
| `--lighting-toggle-bench --uncapped-frames` | 57 / 0 |
| `--v7-gameplay-smoke` | exit 0, `mode=V7` |
| `git diff --check` | OK |

### Comparações contra o estado aprovado (rodada de 14 tiles do experimento)

- **19 casos sem parede** (`cavern-torch`, `deep-torch`, `many-sources`, portas, `sand`, `wrap`, superfície, Shallow, selada, câmera, zoom, wrap, region-cross): **`_sky`, `_local` e `_direct` idênticos** (máximo 0). Promover o padrão não mudou nada.
- **Cenas de parede:** `_sky` idêntico; `_local` e `_direct` mudam **porque a tocha da fixture foi movida**, não por mudança de renderer. O preenchimento local acompanha a posição da fonte.
- **Sombras:** com os buffers `_direct` byte a byte idênticos nas cenas sem parede, a silhueta é a mesma por construção. Nas cenas de parede, o buffer isolado mostra a mesma cunha do pilar nos três estados, com a passagem aberta só no caso minerado.
- **Frames finais:** onde os três buffers são idênticos, as diferenças restantes são o **sprite do jogador** e a **chama**, ambos desenhados depois da iluminação (máscaras de `shallow-day` e `cavern-torch` conferidas), mais o quadrado do cursor.

### Custo (observação factual, sem investigação)

No quadro da captura, a etapa direta ficou em **0,024–0,038 ms** com 112 px contra **13,7 ms** com 280 px nas mesmas cenas. É um quadro por caso, não uma bancada; o ganho é consequência do raio menor (quad de luz e arestas por fonte), não o critério da escolha, que foi visual.

### Pendência registrada: acumulação e saturação de luz (etapa futura de composição)

**Não é objeto desta entrega nem da calibração do local.** `many-sources` segue apenas como regressão; se continuar saturado, fica registrado e não corrigido aqui. Depois de calibrar o preenchimento local de **uma** tocha, haverá uma etapa específica para investigar e decidir a acumulação, separando:

- **A. Várias fontes artificiais:** levantar onde as diretas são acumuladas hoje — `RenderTarget` usado, formato, `BlendState`, em que ponto ocorre clamp/saturação e se informação acima de 1 se perde antes do receptor. O mesmo para o preenchimento local: acumulação na grade, na textura, no shader e onde é limitado.
- **B. Direta + local:** avaliar retorno decrescente, conceitualmente `T = D + A × (1 − D)`. **Não aprovado para implementação.**
- **C. Várias fontes:** avaliar soma limitada, conceitualmente `C_novo = C + F × (1 − C)`, equivalente a `C = 1 − Π(1 − F_i)`. **Não aprovado para implementação.**
- **D. Artificial + céu:** avaliar `L = S + T × (1 − S)`, com a intenção artística de exterior diurno onde a tocha acrescenta pouco, ambiente escuro onde ela funciona plenamente e várias luzes somando de forma progressiva, sem estourar para branco.

**Próxima etapa:** calibração exclusiva do preenchimento local de uma única tocha, antes de qualquer mudança na regra de acumulação. Sem commits.

## 2026-09-16 — Passo 6: alcance do preenchimento local (diagnóstico, para escolha)

**Pedido:** primeira rodada da calibração do local, mexendo **só no alcance**. Intensidade intocada, direta congelada em 14 tiles, acumulação e composição fora de escopo.

### Como o local funciona hoje (fato do código)

- **Definição:** `V8Light` é `record struct (Position, Radiance, Radius, AmbientIntensity = .24f, AmbientRadius = 256)`. A tocha usava os padrões: **0,24** e **256 px = 32 tiles**. Cogumelos passam 0,12 e 128 px.
- **Semente:** por fonte, `V8AmbientField.Update` limpa o campo `path`, semeia a célula da tocha (e suas cópias de wrap) com **1** e acumula `local[i] += Radiance × AmbientIntensity × path[i]`.
- **Perda:** `Propagate(path, size / AmbientRadius, false)` — perda por tile = `tileSize / AmbientRadius`, hoje **1/32 por tile**, multiplicada pelo custo da célula. Propagação em **4 vizinhos** (os 8 vizinhos valem só para o céu).
- **O raio define a inclinação:** como a semente é 1 e a perda é `1/R`, o valor a uma distância d é `1 − d/R`. Reduzir o raio **escurece todas as distâncias**, não só corta a cauda. Confirmado na medição: a 8 tiles o `_local` cai de 45 (32 tiles) para 24 (14 tiles).
- **Independência:** o pico na fonte é `Radiance × AmbientIntensity` e **não** depende do raio; fora da fonte, os dois parâmetros se combinam. Intensidade escala o campo inteiro; o raio define a escala de distância.
- **Reconstrução:** `AmbientAt` interpola por pixel de arte com bilinear mascarada por células abertas e rejeição de quina; a textura guarda o local dividido por `LocalScale = 2`.
- **Unidades:** `AmbientRadius` em **pixels**; vira perda por tile na propagação e é convertido para tiles ao dimensionar a margem da grade.

**Instrumentação:** `--v8-torch-local-radius <tiles>`, só diagnóstico, alterando apenas o raio (a intensidade continua vindo do padrão do `V8Light`). Alcance aplicado, conferido nas capturas: 256 / 112 / 128 / 144 / 160 px.

### Perfil do `_local` na linha iluminada (`cavern-torch`, canal R)

| Distância | 32 (atual) | 14 | 16 | 18 | 20 | `_direct` (igual nas 5) |
|---|---|---|---|---|---|---|
| 2 t | 57 | 50 | 52 | 52 | 54 | 185 |
| 4 t | 53 | 42 | 44 | 46 | 47 | 128 |
| 6 t | 49 | 32 | 36 | 39 | 41 | 82 |
| 8 t | 45 | 24 | 28 | 32 | 35 | 46 |
| 10 t | 41 | 16 | 21 | 26 | 29 | 20 |
| 12 t | 37 | 6 | 14 | 18 | 23 | 5 |
| 14 t | 33 | 0 | 6 | 12 | 17 | 0 |
| 16 t | 29 | 0 | 0 | 5 | 11 | 0 |
| 18 t | 25 | 0 | 0 | 0 | 5 | 0 |
| 20 t | 22 | 0 | 0 | 0 | 0 | 0 |
| **Limite** | 31 t | **13 t** | **15 t** | **17 t** | **19 t** | 13 t |

A queda é exatamente linear em `1 − d/R`, como previsto pelo código. A direta termina em 13 tiles em todas as variantes.

### Medição dentro da sombra geométrica (mesma cena, linha abaixo da tocha)

Aqui `_direct = 0` a partir de 2 tiles e o céu é zero na Cavern: **tudo o que se vê vem só do preenchimento local.**

| Distância | 32 | 14 | 16 | 18 | 20 |
|---|---|---|---|---|---|
| `_local` a 4 t | 51 | 36 | 39 | 41 | 43 |
| `_local` a 8 t | 43 | 19 | 23 | 29 | 31 |
| `_local` a 12 t | 35 | 1 | 9 | 15 | 19 |
| `final ÷ albedo` a 4 t | 0,200 | 0,144 | 0,152 | 0,160 | 0,168 |
| `final ÷ albedo` a 10 t | 0,152 | 0,040 | 0,072 | 0,088 | 0,096 |

### Regressões e identidade

| Execução | Resultado |
|---|---|
| `--lighting-v8 --v8-gameplay-capture` (5 variantes) | **90 / 0 em cada** |
| `--lighting-toggle-bench --uncapped-frames` (5 variantes) | **57 / 0 em cada** |
| `--v8-scene --v8-capture` | 107 / 0 |
| `--v8-scene --v8-ambient --v8-capture` | 154 / 0 |
| `--lighting-sky-compare` | 54 / 0 |
| `--v7-gameplay-smoke` | exit 0, `mode=V7` |
| Build Release / `git diff --check` | 0 erros / OK |

**Diffs registrados entre variantes:** `_direct` e `_sky` com diferença **0** em todos os casos comparados; o `_local` é a única contribuição que muda. Cenas sem tocha (`shallow-day`, `shallow-sealed`, `cavern-off`, `surface-day`) ficam idênticas inclusive no `_local`. Sombras, sala selada preta, céu zero na Cavern e bloqueio por portas e sólidos seguem intactos, por consequência de `_direct` e `_sky` idênticos.

### Observação visual (`screenshots/v8/local-compare/`)

- **32 tiles (atual):** o preenchimento cobre toda a câmara visível; a parede continua legível muito além da poça da direta.
- **14 tiles:** o local termina junto com a direta, e o escuro chega logo depois da poça. A leitura é mais dramática e há pouco ambiente residual.
- **16 e 18 tiles:** mantêm um ambiente discreto por 2 a 4 tiles além do fim da direta.
- **20 tiles:** ambiente perceptível até ~19 tiles, ainda bem mais curto que hoje.
- **`wall-before`** (direta bloqueada pelo divisor): é onde o local aparece isolado. Com 32 tiles ele ilumina toda a metade esquerda da sala; com 14 vira um halo compacto em volta da tocha.
- **Shallow com tocha:** a mudança é discreta porque o céu domina a cena; encolhe o halo ao redor do jogador.
- **Formato:** o buffer `_local` tem **losango** visível, porque a propagação local continua em 4 vizinhos por decisão desta fase. Quanto menor o raio, mais íngreme a queda e mais aparente o formato.

### Hipóteses

- 14 tiles faz o local morrer junto com a direta, o que pode ler como "tudo apaga de uma vez"; 18–20 preservam um ambiente residual depois que a direta acaba.
- Se um raio curto for escolhido, o losango do local tende a incomodar mais, e talvez seja preciso estender os 8 vizinhos ao campo local. **Não feito, não aprovado.**
- Nos raios maiores o local pode parecer forte demais perto da fonte; isso é intensidade, e a instrução foi **não** mexer nela nesta rodada. Fica registrado para a rodada seguinte.

### Custo

Os tempos da etapa de ambiente vieram de **um quadro por caso** e ficaram ruidosos (11,4 / 15,3 / 2,9 / 5,2 / 5,3 ms para 32 / 14 / 16 / 18 / 20), portanto **não permitem conclusão**. O que é fato de código: a margem da grade é `ceil(max(SkyRangeTiles 24, AmbientRadius/tile)) + 2`, ou seja 34 células hoje e 26 com qualquer raio local ≤ 24 tiles; o descarte de fontes usa `max(raio direto 112 px, raio local)`.

**Nada foi promovido.** O padrão do jogo continua 32 tiles; 14, 16, 18 e 20 existem só via `--v8-torch-local-radius`. Intensidade local inalterada, múltiplas fontes não tocadas. Sem commits.

## 2026-09-23 — Passo 6 encerrado: alcance local = 18 tiles (padrão)

**Decisão do usuário:** alcance do preenchimento local da tocha = **18 tiles (144 px)**.

**Motivo (espacial):** a direta termina em ~13 tiles; 14 faz o local morrer junto com ela; 16 deixa cauda curta demais; **18 deixa ~4 tiles de preenchimento residual depois da direta**; 20 volta a espalhar por boa parte da câmara; 32 é excessivo. A escolha **não** aprova a intensidade 0,24: com 18 tiles o alcance está adequado, mas o preenchimento perto da fonte ainda parece forte.

- `PlayingState.V8.CollectV8Sources`: `(TorchLocalRadiusTiles ?? 18f) × TileSize`. `--v8-torch-local-radius` continua só para diagnóstico.
- **Verificação:** sem opção, `AmbientRadius = 144` e `AmbientIntensity = 0,24`; com `--v8-torch-local-radius 32`, `AmbientRadius = 256` (gameplay 90/0).
- **Identidade:** o novo padrão reproduz a rodada diagnóstica de 18 tiles **byte a byte** — `_sky`, `_local` e `_direct` com diferença 0 em 14 casos (`cavern-torch`, `deep-torch`, três de parede, portas, `many-sources`, `sand`, `wrap`, `shallow-day`, `surface-day`, `shallow-sealed`, `cavern-off`).

Alcance local congelado em 18 tiles.

## 2026-09-23 — Passo 7: intensidade local (diagnóstico, para escolha)

**Pedido:** única variável é `AmbientIntensity`, com alcance fixo em 18 tiles. Comparar 0,24 (atual), 0,20, 0,16 e 0,12. Nada promovido.

**Instrumentação:** `--v8-torch-local-intensity <valor>`. Quando passada, aplica `light with { AmbientIntensity = k }` sobre a tocha; sem ela vale o padrão do `V8Light`, sem o valor reescrito no código. Aplicado, conferido no `frame.txt`: 0,24 / 0,2 / 0,16 / 0,12, todos com `AmbientRadius = 144` e raio direto 112.

**Fato do código:** a intensidade multiplica o campo inteiro (`local += Radiance × AmbientIntensity × path`), então a forma espacial não muda.

### Perfil na linha iluminada (`cavern-torch`, canal R)

| Distância | `_local` 0,24 | 0,20 | 0,16 | 0,12 | `_direct` (igual nas 4) |
|---|---|---|---|---|---|
| 2 t | 52 | 45 | 35 | 27 | 185 |
| 4 t | 46 | 39 | 31 | 23 | 128 |
| 6 t | 39 | 33 | 27 | 19 | 82 |
| 8 t | 32 | 27 | 21 | 16 | 46 |
| 10 t | 26 | 21 | 17 | 13 | 20 |
| 12 t | 18 | 15 | 13 | 9 | 5 |
| 14 t | 12 | 11 | 9 | 6 | 0 |
| 16 t | 5 | 4 | 3 | 3 | 0 |
| **Limite** | 17 t | 17 t | 17 t | 17 t | 13 t |

`final ÷ albedo` na mesma linha: a 4 t 0,684 / 0,658 / 0,620 / 0,595; a 8 t 0,304 / 0,288 / 0,264 / 0,240; a 14 t 0,048 / 0,040 / 0,032 / 0,024.

**Razão direta ÷ local:** a 2 tiles 3,6× / 4,1× / 5,3× / 6,9×; a 8 tiles 1,4× / 1,7× / 2,2× / 2,9×. A partir de 13 tiles só existe o local.

### Medição dentro da sombra (linha abaixo da tocha; `_direct = 0`, céu zero na Cavern)

| Distância | `_local` 0,24 | 0,20 | 0,16 | 0,12 | `final ÷ albedo` 0,24 | 0,20 | 0,16 | 0,12 |
|---|---|---|---|---|---|---|---|---|
| 4 t | 41 | 35 | 29 | 21 | 0,160 | 0,136 | 0,112 | 0,080 |
| 8 t | 29 | 23 | 19 | 15 | 0,112 | 0,088 | 0,072 | 0,056 |
| 10 t | 21 | 17 | 15 | 11 | 0,088 | 0,072 | 0,056 | 0,040 |
| 12 t | 15 | 13 | 11 | 7 | 0,062 | 0,053 | 0,044 | 0,027 |

Limite na sombra: 16 tiles nas quatro.

**Escala confirmada numericamente:** relativo a 0,24, a 4 tiles na sombra os valores ficam em 0,85 / 0,71 / 0,51 (esperado 0,83 / 0,67 / 0,50); a 8 tiles, 0,79 / 0,66 / 0,52. As diferenças estão dentro do degrau de quantização (o local é guardado ÷ 2 em 8 bits, em passos de 2). O suporte é o mesmo: 35.875 pixels mudam em `cavern-torch` tanto em 0,24 × 0,16 quanto em 0,24 × 0,12.

### Identidade entre variantes

Diffs registrados 0,24 × 0,20, × 0,16 e × 0,12: **`_direct` e `_sky` com diferença 0** em todos os casos; só o `_local` muda (máximo 10 / 22 / 32 no buffer). Cenas sem tocha (`shallow-day`, `shallow-sealed`, `surface-day`) idênticas inclusive no `_local`.

### Regressões

| Execução | Resultado |
|---|---|
| `--lighting-v8 --v8-gameplay-capture` (padrão, 0,20, 0,16, 0,12 e `--v8-torch-local-radius 32`) | **90 / 0 em cada** |
| `--lighting-toggle-bench --uncapped-frames` (4 intensidades) | **57 / 0 em cada** |
| `--v8-scene --v8-capture` / `--v8-ambient` | 107 / 0 / 154 / 0 |
| `--lighting-sky-compare` | 54 / 0 |
| `--v7-gameplay-smoke` | exit 0, `mode=V7` |
| Build Release / `git diff --check` | 0 erros / OK |

### Observação visual (`screenshots/v8/local-intensity-compare/`)

- **`cavern-torch`, ampliado:** em 0,24 a umbra da plataforma e a área depois da direta ficam claramente legíveis em marrom. Em 0,20, levemente mais escuras. Em 0,16 a umbra fica escura, mas a textura continua legível perto da tocha. Em 0,12 a umbra quase apaga fora da vizinhança da fonte e a cunha da direta domina fortemente.
- **`wall-before`** (direta bloqueada, local isolado): em 0,24 a textura atrás do divisor aparece inteira ao redor da tocha; em 0,12 vira um halo escuro e discreto.
- **Losango:** evidente no `_local` isolado em todas as intensidades (propagação local em 4 vizinhos). Nos frames finais aparece como borda diagonal leve em 0,24 e 0,20 na cena da parede; fica menos perceptível em 0,16 e 0,12 porque o nível é menor. Registrado, sem correção.
- **`many-sources`:** reduzir o local escurece um pouco o espaço entre as tochas; a saturação perto das fontes sobrepostas continua com o mesmo caráter. Apenas regressão, não corrigida.
- **Shallow com tocha:** painel gerado e enviado; não fiz leitura visual detalhada dele.

### Hipóteses

- 0,16 pode ser o equilíbrio: a direta fica ~5× o local perto da fonte e a umbra continua legível.
- 0,12 corre o risco de "preto logo fora da direta", que é o que o local deveria evitar.
- 0,24 → 0,20 é uma diferença pequena (máximo 10/255 no buffer), talvez pouco perceptível em jogo.

**Nada foi promovido.** O padrão continua `AmbientIntensity = 0,24`; as outras intensidades existem só via `--v8-torch-local-intensity`. Múltiplas fontes, composição e propagação não foram tocadas. Sem commits.

## 2026-09-23 — Rota 1: auditoria da composição e protótipo (diagnóstico; nenhuma decisão aprovada)

**Pedido:** tentar manter a V8 corrigindo acumulação e composição, aproximando o visual da V7 sem perder sombras geométricas, oclusão e otimizações. Auditoria curta, uma candidata mínima e reversível atrás de opção, comparação V7 | V8 atual | V8 candidata, e parada honesta se a candidata não trouxer ganho claro.

### Estado real e linha de base congelada

- **Checkout:** branch `physics`, HEAD **`f7edee1` ("test")**, não mais o `abb6064` citado nas seções antigas: o trabalho da V8 foi commitado pelo usuário nesse intervalo. Alterações locais no início desta etapa: só `.gitignore` e arquivos não rastreados (docs, html). Nada foi descartado.
- **Parâmetros efetivos, lidos no código e confirmados nos `frame.txt`:**

  | Componente | Valor em uso |
  |---|---|
  | Direta da tocha | 14 tiles / 112 px; radiância (1; 0,65; 0,28); `(1 − d/R)²` |
  | Local da tocha | 18 tiles / 144 px (promovido); intensidade **0,24**, padrão do `V8Light`. **Nenhuma intensidade foi aprovada.** |
  | Céu | background ×0,5 e alcance visual 12 tiles; receptores ×1,0 de dia; `SkyRangeTiles` 24; 8 vizinhos |
  | Faces | `SkyFaceDepth` 24 px; `FaceWidth` 8 |

  Todos congelados durante o experimento.
- **Linha de base reproduzível:** capturas novas em diretórios próprios (`mix/`, `gameplay-csum/`, `scene-*-csum/`, `perf-csum/`); nenhuma referência anterior foi sobrescrita.

### Auditoria: fonte → acumulação → buffers → receptor (fatos do código)

**Documentação oficial do MonoGame consultada:** `SurfaceFormat.Color` = "Unsigned 32-bit ARGB … 8 bits per channel". `Blend.One` multiplica por 1; `Blend.InverseSourceColor` por `(1 − Rs, 1 − Gs, 1 − Bs, 1 − As)`.

| Etapa | Onde | Fato | Onde perde informação |
|---|---|---|---|
| Direta | `V8LightingRenderer.DrawSource`, `V8Direct.fx` | Cada fonte grava `float4(Radiance × t², 0)` no alvo `Direct` (`SurfaceFormat.Color`, 8 bits) com `BlendState add`: cor `One + One`, alpha `Zero + One`. | A soma entre fontes é feita **dentro do alvo de 8 bits**: cada canal satura em 1. Como R = 1,0 na radiância, **R satura primeiro** e o G continua subindo, levando o laranja para amarelo e depois para branco. |
| Local | `V8AmbientField.Update` / `Upload`, `V8Ambient.fxh AmbientAt` | Soma em float na grade (`local += Radiance × 0,24 × path`); a textura guarda `local ÷ 2` em 8 bits (teto 2,0); o shader reconstrói ×2 e aplica `saturate`. | Teto 1,0 por canal depois da reconstrução. Os buffers `_local` das capturas são o resultado resolvido, não a textura armazenada. |
| Céu | `AmbientAt` | `saturate(cor do céu × exposição remapeada)`; background ×0,5, receptores ×1,0 de dia. | Já limitado a 1. |
| Receptor (background, árvores, móveis, **personagem**) | `V8Receiver.fx` técnica `Receiver` | `luz = direta + céu + local`, **sem limite**, depois `albedo × luz`. | O corte só acontece na escrita do quadro final (8 bits): quando `luz > 1`, o canal estoura e a cor lava. |
| Faces (terreno) | `V8Faces.fx`, alvo `Foreground` (8 bits) | `(direta + local) × R + céu × G`. | Limitadas a 1 no próprio alvo: **o terreno nunca passa do albedo, mas background e personagem passam** — tratamento inconsistente. |
| Personagem | `DrawV8Receivers` → `session.DrawEntities(batch, neutralEntityLightSampler)` | Desenhado dentro do lote de receptores da V8; o amostrador neutro devolve branco, então o albedo é aplicado **uma vez**, com luz por pixel. | Mesmo caminho do background. |
| V7 (referência) | `LightingV7System.FillTexture`, `PixelComposite.fx` | Soma bloco + direta **em float** por célula; `max(artificial, céu × bounce + sol) × AO + piso`; guarda ÷2 em 8 bits e o composto multiplica ×2 (sobrebrilho até 2); **background com tint 0,41**; halos aditivos nas fontes; chamas emissivas. Portas não bloqueiam a luz. | O teto 2,0 e o tint 0,41 dão folga: a parede quase nunca estoura. |

**Medição que corrigiu uma hipótese:** nas cenas antigas (`many-sources`, 4 tochas afastadas) o buffer `_direct` **não tem nenhum pixel com R = 255** e o G máximo é 164 (núcleo de uma tocha). O corte na acumulação da direta só aparece com tochas próximas, como na casa com 5 tochas (abaixo).

### Reprodução controlada (`--lighting-composition-compare`)

Harness novo, só diagnóstico, em sessão temporária (`PlayingState.CompositionCompare.cs`). **Reprodução controlada, não o mundo exato dos prints.** Meio-dia, zoom 3, mesma câmera, horário e fontes por caso (conferido no `mix-summary.txt`).

- **Casa de madeira** na superfície: interior 26×10 com parede de fundo de madeira e porta à direita; 0, 1, 2 e 5 tochas com a porta fechada, e 2 tochas com a porta aberta.
- **Caverna** (Cavern) com pilar de pedra para a sombra geométrica, sem e com uma tocha.
- **Shallow** com três aberturas de fundo 2×2, sem e com 3 tochas.

**Variantes:** V7 no padrão do jogo (sol ligado, tecido ligado, tochas e cogumelos como fontes) | V8 soma (atual) | V8 A | V8 A+B. Os cogumelos do mundo contam nas duas pipelines. **A V7 ignora portas para luz**, então o interior da casa fechada não fica preto nela (medido: 24/16/11). Resultado do harness: **50/50 verificações** (fontes, porta, pipeline ativa e regra de composição por variante).

### Candidata implementada (protótipo atrás de `--v8-composition sum|direct-screen|bounded`; padrão `sum`)

- **A — acumulação da direta:** `BlendState` `One / InverseSourceColor`, ou seja `C_novo = C + F(1 − C)` por canal. Com uma tocha é idêntico; é comutativo; não usa buffer novo.
- **B — composição no receptor e nas faces** (`V8Ambient.fxh ComposeLight`), por canal: `T = D + A(1 − D)` e `L = S + T(1 − S)`. As três entradas já estão em [0, 1] (8 bits e `saturate`), então `L ≤ 1` e o resultado nunca passa do albedo. Num lugar escuro (S = 0) a tocha entra inteira; sob céu claro ela acrescenta pouco. A mesma função vale para background, personagem e faces.
- **Por que é a menor intervenção:** uma troca de `BlendState` mais uma função de shader; sem HDR, sem tone mapping, sem pass ou buffer novo; céu, local, raios, radiância e faixas intocados.

### Resultados medidos (`mixsample.ps1`; "pushed-to-clip" = canal final ≥ 254 com albedo ≤ 240, ou seja corte causado pela luz e não material claro)

| Caso | Métrica | V7 | V8 soma | V8 A | V8 A+B |
|---|---|---|---|---|---|
| Casa, 5 tochas | Direta no personagem (R/G/B) | — | 246/182/78 | 187/140/70 | 187/140/70 |
| | Parede, final ÷ albedo | (tint 0,41) | **1,26**/0,82/0,35 | 1,03/0,72/0,33 | 0,78/0,60/0,31 |
| | Personagem, final ÷ albedo | — | **1,33**/0,98/0,42 | 1,16/0,82/0,39 | 0,84/0,67/0,36 |
| | Personagem empurrado ao corte | 7,2% | **23,1%** | 7,0% | **0%** |
| | Cena empurrada ao corte | 0,3% | **8,3%** | 5,3% | **0%** |
| Shallow, 3 tochas | Personagem, final ÷ albedo | — | 1,10/0,87/0,64 | 1,06/0,85/0,63 | 0,75/0,66/0,55 |
| | Personagem empurrado ao corte | 0% | 12,7% | 12,7% | **0%** |
| Casa, 1 tocha | Parede / personagem (final R) | 24 / 67 | 37 / 61 | 37 / 61 | 35 / 55 |
| Caverna, 1 tocha | Parede / personagem (final R) | 56 / 54 | 45 / 46 | 45 / 46 | 42 / 42 |
| Casa, 2 tochas, porta aberta | Céu bruto na parede / no personagem | — | 38 / 28 | 38 / 28 | 38 / 28 |
| | Personagem empurrado ao corte | 0% | 0% | 0% | 0% |
| Sem tochas (casa, caverna, Shallow) | Final | — | idêntico nas três variantes V8 | | |

- **Uma tocha:** a candidata perde de 5% a 10% perto da fonte (onde direta e local coexistem); longe da fonte, só local, fica igual.
- **Porta aberta: o estouro relatado não foi reproduzido.** O céu que entra pela porta desta casa é baixo, e nenhuma variante corta. Faltam as condições reais do print (tamanho da abertura, horário, número de tochas).

### Observação visual (`screenshots/v8/mix-compare/`)

- **Casa, 5 tochas:** a V8 atual reproduz a queixa, com a metade de baixo da parede em faixa amarela estourada e a pele do personagem clareada. A reduz o amarelo, mas continua quente demais perto das tochas. A+B elimina o estouro e preserva a textura e a camisa vermelha, porém o conjunto fica **marrom e apagado** e a pele do personagem parece **acinzentada**. A V7 também é laranja, mas bem mais escura, com contraste e halos nas chamas.
- **Casa com 2 tochas, caverna com 1 tocha:** diferenças pequenas entre as variantes da V8; A+B é levemente mais escura. A V7 continua visivelmente mais escura e mais concentrada perto da chama.
- **Shallow, 3 tochas:** a soma lava o personagem; A+B dá tons naturais, porém dessaturados. A V7 é bem mais escura, com brilho concentrado nas chamas.

### Efeito colateral identificado (fato de cálculo)

Aplicar `C + F(1 − C)` **por canal** comprime mais o canal forte da tocha (R) do que G e B. Com direta e local medidos no personagem da casa com 5 tochas, a soma crua daria luz (1,18; 0,84; 0,40) e a candidata dá (0,85; 0,68; 0,37): a razão G/R sobe de 0,71 para 0,80. **A luz da tocha perde calor e fica mais neutra quanto mais forte fica.** Nunca corta, mas é a causa do aspecto acinzentado e apagado.

### Regressões e custo

| Execução | Soma (padrão) | A+B (candidata) |
|---|---|---|
| `--v8-scene --v8-capture` | 107 / 0 | 107 / 0 |
| `--v8-scene --v8-ambient --v8-capture` | 154 / 0 | 154 / 0 |
| `--lighting-v8 --v8-gameplay-capture` | 90 / 0 | 90 / 0 |
| `--lighting-sky-compare` | 54 / 0 | — |
| `--lighting-toggle-bench --uncapped-frames` | 57 / 0 | — |
| `--v7-gameplay-smoke` | exit 0 | — |
| Build Release / `git diff --check` | 0 erros / OK | |

- **Verificações que codificavam a soma** passaram a derivar da regra ativa:
  - "overlapping sources add independently": a expectativa usa soma limitada (padrão) ou `a + b − ab/255` (candidata); passou com erro 0 nas duas;
  - "source order direct/composition": 0 na soma; tolerância de 1 passo de quantização na candidata, porque cada fonte é guardada em 8 bits antes da próxima;
  - "additive-receiver": a expectativa usa a mesma função `ComposeLight`; erro máximo 1.
- **Identidade da regra padrão:** depois da troca de shader, `_sky`, `_local`, `_direct` e `_foreground` ficaram idênticos ao estado aprovado em 9 casos.
- **Candidata contra a soma:** céu bruto e local idênticos; `_direct` idêntico com uma tocha e alterado só em sobreposições (`many-sources`, máximo 17/255); oclusão e bloqueios intactos (suítes).
- **Custo** (`--v8-perf-bench --uncapped-frames`, 3 execuções alternadas por regra, 6 cenários cada; `perf-csum[-r2|-r3]`, `perf-cbounded[-r2|-r3]`):
  - 7 dos 36 cenários tiveram a janela inativa (328–640 quadros; o quadro sobe para 28–33 ms por espera, com o `Draw` normal) e foram **excluídos**.
  - Nos válidos: mediana do CPU do `Draw` de 5,44–6,26 ms na soma (13 cenários) e 5,34–6,35 ms na candidata (16); CPU da V8 de 3,47–4,05 ms e 3,39–4,07 ms.
  - A variação entre execuções da mesma regra (~0,8 ms) é maior que qualquer diferença entre regras. GPU não medida.
  - **Custo indistinguível;** não há ganho nem regressão a declarar.

### Avaliação objetiva da Rota 1

- **Melhorou:** o defeito concreto de estouro — luz empurrando o receptor para o corte e deslocando a cor para amarelo — some em todos os casos reproduzidos, com tratamento consistente entre background, personagem e faces, uma tocha praticamente preservada e custo não mensurável.
- **Piorou:** cenas com várias tochas ficam mais apagadas e acinzentadas (perda de calor por canal, descrita acima).
- **Sem solução:** a distância visual para a V7. O contraste da V7 vem sobretudo de fatores que **não são composição**: parede de fundo com tint 0,41 sob luz artificial, brilho concentrado com halos e sobrebrilho até 2 perto da chama. O estouro da porta aberta não foi reproduzido.
- **Conclusão:** a candidata resolve o estouro, mas **não entrega o ganho visual pedido** (aproximar a V7). Seguir exigiria pelo menos duas decisões que não são só de composição:
  1. um limite que preserve o matiz (pelo canal máximo, em vez de por canal);
  2. uma resposta do background à luz artificial separada da dos demais receptores, análoga à escala de céu do background.

  Ambas são **hipóteses, não testadas**. Parado para revisão, conforme o pedido; a Rota 2 não foi iniciada.

### Arquivos alterados nesta etapa

| Arquivo | Mudança |
|---|---|
| `V8LightingRenderer.cs` | Enum `V8Composition`, propriedade `Composition`, `BlendState screen`, parâmetro `BoundedComposition` para faces e receptores |
| `V8Ambient.fxh` | `BoundedComposition` e `ComposeLight` |
| `V8Receiver.fx`, `V8Faces.fx` | Usam `ComposeLight` (idêntico à soma com `sum`) |
| `V8GameplayOptions.cs` | `--v8-composition`, `--lighting-composition-compare` |
| `PlayingState.V8.cs` | Regra do renderer pela opção; início e captura do harness |
| `PlayingState.cs`, `PlayingState.LightingToggle.cs` | Ganchos do harness; F4 bloqueado durante o harness |
| `PlayingState.CompositionCompare.cs` | Novo harness |
| `V8DiagnosticGame.cs` | Regra pela opção nas suítes de cena |
| `V8Validation.cs`, `V8AmbientValidation.cs` | Expectativas derivadas da regra ativa |

**Estado das decisões:**
- **Diagnóstico:** feito.
- **Protótipo:** existe só atrás de `--v8-composition`.
- **Decisão aprovada:** nenhuma. O jogo continua com a regra `sum`. Sem commits, sem alterações em saves.

