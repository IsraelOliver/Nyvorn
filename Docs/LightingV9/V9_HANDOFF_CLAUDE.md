# Continuidade do V9-Lab — 23/09/2026

## Mais recente (25/09/2026): V9 é a iluminação padrão do gameplay

[FATO] O V9 com Sky Backplane foi promovido a padrão, sem recalibração. Ler primeiro [V9_PROMOCAO_PADRAO.md](V9_PROMOCAO_PADRAO.md).

- **Onde fica o quê:**
  - produção em `Nyvorn/Source/Game/States/PlayingState.V9.cs`;
  - seleção e configuração em `Nyvorn/Source/Engine/Graphics/LightingV9Probe/V9Gameplay.cs`;
  - o probe (`PlayingState.V9Probe*.cs`) é só instrumentação por cima do mesmo código.
- **Nomes:** as classes `V9Probe*` são o runtime de produção; os nomes ficaram por histórico.
- **Estado aprovado herdado de 24/09 à tarde** (sessão do Codex posterior ao relatório do Backplane):
  - curva de 10 tiles;
  - parallax de caverna iluminado;
  - vazamento de 40 px para a Cavern;
  - máscara por alpha;
  - suavizações de 2 px e 3×3;
  - worldgen sem BG na Cavern/Deep e fendas do Shallow fechando antes da Cavern;
  - BG no minimapa.
- **Checks do probe:** "curva contínua" e "Cavern/Deep lit" (`shallow-bottom`) falham desde essas mudanças. Isso já acontecia no build aprovado; não é regressão.
- **Próximos assuntos separados:** dia/noite do V9, custo de reconstrução da janela (60 a 70 ms em 1080p), máscara preta residual, degradê e quinas.

## Anterior (24/09/2026): Sky Backplane paralelo, sem promoção

[FATO] Nova flag `--v9-probe-sky-backplane` no Gameplay Probe. FG/BG vazios expõem o plano de céu; uma curva de profundidade serve à Surface e ao Shallow e chega a zero na Cavern. BG recebe glow geométrico; FG próximo recebe uma contribuição fraca do mesmo campo pela seção terminal intacta. Sol não participa. O natural de exposição/aberturas descrito abaixo continua padrão e foi preservado bit a bit.

[RESULTADO] 255 verificações do experimento passaram; 453 da referência ampliada. Buffers artificiais idênticos em 35 estados comparáveis. A fenda curva recebe céu e bordas iluminadas. Recintos fechados só em FG também ficam expostos onde não há BG: consequência da hipótese, não falha de oclusão. Recenter/edições ficaram mais caros em vários cenários; não houve otimização nem promoção.

[CONTINUIDADE] Ler [V9_SKY_BACKPLANE_RESULTADOS.md](V9_SKY_BACKPLANE_RESULTADOS.md), com recomendação, limites, comandos, custos e evidências. A arquitetura é recomendada como próxima direção experimental, condicionada à aceitação visual/semântica pelo usuário; esta entrega não autoriza promover nem recalibrar tochas/FG. Não retomar automaticamente a proposta antiga de conectividade para corrigir a fenda.

## Mais recente (24/09/2026): uma definição de céu para a Surface

[FATO] `V9ProbeField.SkyVisibleAt` é a única definição de céu visível, e alimenta a cobertura do céu pintado e a resposta do foreground:

- O = 1;
- abertura do Shallow = seu peso;
- `OpenAtmosphere` do Space/Surface = 1 só se a soma das aberturas naturais no centro do tile for > 0.

Consequências:

- O ar selado fica coberto e sem luz nas paredes.
- Limite: ar aberto que a luz natural não alcança (fundo da fenda inclinada) também fica coberto. Conectividade foi proposta como fallback, não implementada.
- Tochas e glow bit a bit iguais.
- Relatório: [V9_SURFACE_SKY_UNIFICADO_RESULTADOS.md](V9_SURFACE_SKY_UNIFICADO_RESULTADOS.md).

## Anterior (24/09/2026): resposta do foreground ao céu visível

[FATO] **O que mudou.** As faces sólidas vizinhas de céu visível recebem o termo de céu aberto pela seção terminal intacta (≤ 6 px), junto por máximo. Céu visível aqui é abertura de fundo do Shallow, ou `OpenAtmosphere` no Space/Surface.

- A flag `--v9-probe-no-foreground-sky` desliga só isso.
- Tochas, glow e albedo ficaram bit a bit iguais.
- **Conflito:** paredes de ar selado na Surface acendem. Pendente decidir sobre conectividade.
- Relatório: [V9_FOREGROUND_SKY_RESULTADOS.md](V9_FOREGROUND_SKY_RESULTADOS.md).

## Anterior (24/09/2026): cobertura do céu do probe por classificação

[FATO] A faixa preta na fenda era a cobertura de apresentação por coluna.

- Agora a cobertura é decidida por tile, pelo `SceneWorldClassifier`: céu visível em `OpenAtmosphere` no Space e na Surface e nas aberturas do Shallow; Cavern e Deep continuam cobertos.
- A iluminação ficou idêntica bit a bit.
- Caso incorreto conhecido: ar selado na Surface mostra o céu pintado. Pendente decidir se entra conectividade.
- Relatório: [V9_PROBE_MASCARA_CEU_RESULTADOS.md](V9_PROBE_MASCARA_CEU_RESULTADOS.md).
- As flags `--v9-probe-no-sky-mask` e `--v9-probe-pixel-report` são só de diagnóstico.

## Estado anterior (24/09/2026): Sky Background Glow no Shallow

[FATO] **Classificação.** O probe reutiliza o `SceneWorldClassifier` do Lighting V3 fase 2 através de `V9ProbeGeometryAdapter`:

- o foreground é a ocupação aprovada do probe;
- o `OpenAtmosphere` é dividido em céu exterior (O), céu do Shallow (fundo vazio no Shallow, com fade de 8 linhas no fim) e vazio.

[FATO] **O glow** (`V9ProbeSkyGlow`):

- sementes são tiles de fundo que tocam céu;
- a distância geodésica anda só pelo fundo, até 40 px;
- valor = peso × (1 − d/R)² × céu aberto × RGB do céu;
- junta-se ao natural por máximo, antes das tochas, que não mudam.

A máscara preta agora mostra o céu nos buracos do Shallow.

[FATO] **Flags:** `--v9-probe-no-sky-glow` e `--v9-probe-glow-sun` (sol opcional, a partir das funções de sol do jogo).

- **Relatório:** [V9_SHALLOW_SKY_GLOW_RESULTADOS.md](V9_SHALLOW_SKY_GLOW_RESULTADOS.md).
- **Evidências:** `screenshots/v9-gameplay-probe/shallow-sky-glow/`.
- **Tochas:** bit a bit iguais.
- **Pendente:** avaliação visual do usuário (pico, cor e alcance do halo). A caverna "fechada" antiga do teste de tochas agora brilha, porque 15% do fundo dela são fissuras do Shallow.
- Sem commit; nada foi promovido.

## Estado anterior: luz natural do probe = exposição + aberturas

[FATO] O padrão do `--lighting-v9-gameplay-probe` agora é o modelo de exposição:

- região exposta O = ar sem parede de fundo acima do envelope exterior;
- céu aberto = `E_aberto` 5,2806554 × RGB do céu;
- as aberturas são as células de O que tocam ar não exposto. Cada uma é uma `V9Light` branca de 0,9333 com raio 196, e a soma tem teto `E_aberto`;
- depois vêm as tochas, na ordem, sem mudança.

O modelo antigo, com pontos 20 px acima do envelope, fica só como diagnóstico em `--v9-probe-sky-points`.

- **Arquivo novo:** `LightingV9Probe/V9ProbeExposure.cs`.
- **Captura roteirizada:** 22 estados, com um update fixo por quadro (`Game1`, só na captura).
- **Relatório:** seção "Correção da luz natural" no topo de [V9_GAMEPLAY_PROBE_RESULTADOS.md](V9_GAMEPLAY_PROBE_RESULTADOS.md).
- **Evidências:** `screenshots/v9-gameplay-probe/natural-exposure/`.
- **Tochas:** idênticas bit a bit entre os dois modelos.
- **Pendente:** avaliação visual do usuário, em especial o nível de `E_aberto` na superfície.
- **Limite de dados:** buracos de fundo subterrâneos não são céu, porque falta informação de pano de fundo por célula.
- Sem commit; nada foi promovido.

## Estado anterior: V9-Lab consolidado + V9 Gameplay Probe

[FATO] **V9-Lab:**

- `--lighting-v9-lab` = cache por fonte + máscara de ocupação. `--v9-no-occupancy-mask` roda o cache sem máscara; `--v9-no-source-cache` roda a referência.
- Os verificadores foram mantidos: `--v9-cache-verify` compara em três vias por padrão.
- A seção seguinte, que diz que a máscara é opcional, está superada.

[FATO] **Probe:** `--lighting-v9-gameplay-probe`, com `--v9-probe-capture` e `--v9-probe-bench`.

- **Arquivos novos (não rastreados):** `Nyvorn/Source/Engine/Graphics/LightingV9Probe/` (`V9ProbeOptions`, `V9ProbeOccupancy`, `V9ProbeSky`, `V9ProbeField`), `PlayingState.V9Probe.cs` e `PlayingState.V9ProbeCapture.cs`.
- **Rastreados, com ramos só ativos com a flag:** `Program.cs`, `Game1.cs`, `PlayingState.cs`, `PlayingState.LightingToggle.cs` e `PauseMenuState.cs`.
- **Salvamento:** as guardas bloqueiam todas as gravações. No probe, W no menu de pausa encerra o processo.
- **Evidências:** `screenshots/v9-gameplay-probe/` (`final-capture`, `final-bench`, regressões, `code-changes.diff`). Relatório: [V9_GAMEPLAY_PROBE_RESULTADOS.md](V9_GAMEPLAY_PROBE_RESULTADOS.md).

[FATO] **Decisões técnicas do probe que um próximo agente precisa conhecer:**

- O shader não tem origem. Os receptores são desenhados por classe num RT de tela e iluminados por um quad em coordenadas locais da janela.
- Contribuições ancoradas no mundo, no quadrado do raio; o recenter não as invalida.
- Céu: envelope = abertura de 17 colunas do piso de céu; amostras a cada 12 px, 20 px acima. A apresentação do céu segue o piso de céu real.
- O gameplay usa zoom 2 (3 em interiores); os estados roteirizados fixam zoom 2.

[FATO — limites vigentes] A direção visual segue congelada: nada de recalibrar raio, potência, cores, curva, DDA, quinas, seção, absorção, incidência, sRGB, shader, assets ou estética. O custo do céu (densidade do lab) é o gargalo medido; mudar densidade ou altura das amostras é decisão do usuário. Sem commit; nada foi promovido.

## Estado anterior: cache padrão do laboratório e máscara opcional

[FATO] O cache por fonte é o **caminho padrão apenas do V9-Lab**, por decisão do usuário.

- `--lighting-v9-lab` usa o cache; `--v9-no-source-cache` executa a referência antiga, que continua como oráculo.
- `--v9-source-cache` é aceito, mas redundante. Contradição com `--v9-no-source-cache` é erro.
- Não há integração com o gameplay: `Program.cs` e `Game1` não mudaram.
- A afirmação da seção seguinte de que o cache era opt-in está superada.

[FATO] Experimento concluído, aguardando avaliação: **máscara plana de ocupação** (`--v9-occupancy-mask`, desligada por padrão, só válida sobre o cache).

- Arquivos novos em `Nyvorn/Source/Engine/Graphics/LightingV9Lab/`: `V9OccupancyMask.cs` e `V9LabGame.OccupancyMaskChecks.cs`.
- Alterados: `V9LightField.cs` (só a classe `V9LightField`; as linhas 1–94 com `V9Light`, `V9LabSettings` e `V9LightMath` estão intactas), `V9SourceCache.cs` (parâmetro opcional `solid`), `V9LabGame.cs` (flags e bancada) e `V9LabGame.SourceCacheChecks.cs` (terceiro caminho no verificador).
- Tudo segue não rastreado. O diff está em `screenshots/v9-lab/occupancy-mask/code-changes.diff`.

[FATO] Resultados:

- 43/43 estados com Energy float32, HalfVector4 e pixels idênticos ao cache; 152 PNGs históricos idênticos.
- Zero divergências entre `scene.Solid` e a máscara em 230 milhões de consultas reais do DDA (domínio x 0..59, y 0..33, nada fora da grade), além da grade exaustiva e dos raios de borda.
- Draw móvel: 13,34 → 11,78 ms e 14,04 → 12,48 ms (5 pares ABBA, faixas sem sobreposição). Casos parados e com câmera sem regressão.
- 2.040 bytes; ~0,01 ms por reconstrução completa.
- Relatório: [V9_OCCUPANCY_MASK_RESULTADOS.md](V9_OCCUPANCY_MASK_RESULTADOS.md). Adendo com a reindentação pós-teste e o manifesto persistido: topo de [V9_SOURCE_CACHE_RESULTADOS.md](V9_SOURCE_CACHE_RESULTADOS.md).

[FATO — limites vigentes] A direção visual segue congelada. Não alterar DDA, quinas, coordenadas, raio, potência, cores, seção iluminada, absorção, incidência, shader, tone mapping, resolução ou assets. Ainda não foram implementados bounding box por raio, paralelização, dirty regions, cache de DDA, compute ou atualização temporal. Sem commit.

## Atualização posterior: cache por fonte implementado como diagnóstico

[FATO] O usuário autorizou posteriormente somente o experimento de cache individual, preservando o frame. Ler agora [V9_SOURCE_CACHE_RESULTADOS.md](V9_SOURCE_CACHE_RESULTADOS.md) antes do mapa histórico abaixo. `--v9-source-cache` habilita a variante; sem a opção, o caminho anterior continua padrão. `--v9-cache-verify` compara os dois caminhos fora do benchmark. Não há promoção ou commit.

[FATO] Novos arquivos não rastreados em `Nyvorn/Source/Engine/Graphics/LightingV9Lab/`: `V9SourceCache.cs` e `V9LabGame.SourceCacheChecks.cs`. `V9LightField.cs` e `V9LabGame.cs` receberam opção, invalidação/instrumentação e ganchos do teste. Shader, cena, assets, parâmetros, avaliador e DDA foram preservados. Novo relatório `Docs/LightingV9/V9_SOURCE_CACHE_RESULTADOS.md`, também não rastreado. Evidências novas em `screenshots/v9-lab/source-cache/`; `final/` não foi sobrescrita.

[FATO] Cache por slot + dados completos da fonte; revisão do mapa e reset invalidam todas as contribuições; quantidade diferente também invalida todas. Mesma quantidade: recalcula slots alterados e recompõe do zero na ordem original. Float32 linear por fonte, sem subtração incremental, compressão ou Half intermediário. Zero novos recursos GPU; payload adicional 8,96/14,94 MiB para 6/10 fontes.

[FATO] Equivalência exata em 43 estados e 152 PNGs históricos. CPU Draw móvel mediano 56,14→13,46 ms e 88,08→14,09 ms. 28 testes CPU, 26 verificações de captura em cada caminho, V8 direta 107/107 e ambiente 154/154 passaram. Conclusão vigente: promissor, opcional, aguardando avaliação. **Preservar exatamente o visual continua sendo o contrato artístico.**

O restante deste handoff registra o estado anterior ao experimento. Afirmações abaixo como “nenhuma otimização implementada”, “não há cache por fonte” e o mapa antigo de arquivos são históricas; foram atualizadas por esta seção e pelo relatório de resultados. Os limites que proíbem alterações visuais e promoção automática continuam vigentes.

[FATO — decisão do usuário] Após experimentar abertura, várias tochas e movimento das fontes, o usuário aprovou fortemente a direção visual do V9-Lab, em especial luz natural e tochas, considerando-a muito superior ao que obtinha em V7/V8. Isso é aprovação artística, não aprovação de desempenho nem promoção para produção. **Preservar exatamente esse visual é a prioridade artística.** A recomendação anterior de abandonar V9/avaliar V7 é histórica e foi superada. Ler primeiro a atualização no topo de [V9_LAB_ESTADO.md](V9_LAB_ESTADO.md).

[HISTÓRICO — escopo do handoff anterior] Naquela entrega houve apenas documentação e leitura técnica curta, sem alterações de código ou novos testes. As medições abaixo pertencem à implementação anterior ao cache; os resultados do experimento posterior estão no início deste documento e no relatório vinculado.

## Checkout real e mapa de arquivos

[FATO] Workspace `C:\dev\Nyvorn-Reborn`; branch `physics`; HEAD `f7edee1e5902c5f05265d6c50076aff042afb8c9`, reconfirmados em 23/09/2026. Checkout com alterações locais, sem arquivos staged na consulta. V9 ainda não foi commitado. Não tratar HEAD isoladamente como o estado executado: ele não contém o laboratório. Não fazer reset/checkout/limpeza para iniciar o trabalho.

| Caminho relativo ao repositório | Estado Git na entrega | Responsabilidade |
|---|---|---|
| `Nyvorn/Program.cs` | Rastreado, modificado, não staged | Opt-in antes de configurar V8 |
| `Nyvorn/Content/Content.mgcb` | Rastreado, modificado, não staged | Registro de um efeito V9 |
| `Nyvorn/Content/effects/V9LabReceiver.fx` | Novo, não rastreado (`??`) | Resposta de receptores e composição de cor |
| `Nyvorn/Source/Engine/Graphics/LightingV9Lab/V9LightField.cs` | Novo, não rastreado | Configuração, fontes, DDA, acumulação, buffer e upload |
| `Nyvorn/Source/Engine/Graphics/LightingV9Lab/V9LabScene.cs` | Novo, não rastreado | Geometria temporária, assets/autotiles e desenho bruto |
| `Nyvorn/Source/Engine/Graphics/LightingV9Lab/V9LabGame.cs` | Novo, não rastreado | Entrada, invalidação, passes, controles, casos e benchmark |
| `Nyvorn/Source/Engine/Graphics/LightingV9Lab/V9LabChecks.cs` | Novo, não rastreado | 28 verificações CPU |
| `Nyvorn/Source/Engine/Graphics/LightingV9Lab/V9LabEvidence.cs` | Novo, não rastreado | Capturas, recortes, métricas e referência CPU/GPU |
| `Docs/LightingV9/Verify-V9Captures.ps1` | Novo, não rastreado | Verificações das evidências do runtime |
| `Docs/LightingV9/V9_LAB_ESTADO.md` | Novo, não rastreado, atualizado nesta entrega | Decisão vigente + relatório histórico |
| `Docs/LightingV9/V9_HANDOFF_CLAUDE.md` | Novo, não rastreado, criado nesta entrega | Este handoff |

[FATO] `screenshots/v9-lab/` contém evidências locais ignoradas pelo Git, incluindo `initial/`, `final/`, `interactive/` e regressões. `git check-ignore` confirmou essa condição para `final/base_final.png`. Um clone de HEAD ou envio só de arquivos rastreados perderá o núcleo V9 e suas evidências; copiar os arquivos novos e capturas também é necessário para continuidade em outra máquina. Este handoff não realizou cópia, staging ou commit.

[FATO] Alterações preexistentes fora do V9 continuam no checkout: `.gitignore`; `V8Ambient.fxh`, `V8Faces.fx`, `V8Receiver.fx`; `V8AmbientValidation.cs`, `V8DiagnosticGame.cs`, `V8GameplayOptions.cs`, `V8LightingRenderer.cs`, `V8Validation.cs`; `PlayingState.LightingToggle.cs`, `PlayingState.V8.cs`, `PlayingState.cs`. Também há não rastreados anteriores em `Docs/Design/LightingV8/`, `Docs/LightingV8/`, `Docs/auditoria-2026-09/`, `LIGHTING_BASELINE_2026-08-11.md`, `LightingV4CapabilityProbe/README.md`, `PlayingState.CompositionCompare.cs`, `hotmart.html`, `html.html`, `nyvorn_v6_diagnostic.txt`. Não atribuir esses diffs ao V9 nem descartá-los.

## Ordem de leitura recomendada

1. Topo de `V9_LAB_ESTADO.md`, este handoff e capturas `final/base_final.png`, `five_final.png`, `base_albedo.png`.
2. `Nyvorn/Program.cs`: limite da integração; `Nyvorn.csproj` e `Content.mgcb` só para ambiente/efeito.
3. `V9LightField.cs`: `V9LabSettings`, `V9LightMath.Evaluate`, `Visibility`, `V9LightField.Rebuild`.
4. `V9LabGame.cs`: `ResetScene`, `RebuildLights`, `Update`, `Draw`, `Render`, `Begin`, `AdvanceBenchScene`.
5. `V9LabReceiver.fx`: interpretação do albedo e curva final; **não alterar na investigação de custo**.
6. `V9LabScene.cs`: fronteiras finitas, abertura, pilar, geometria e desenho com albedo branco.
7. `V9LabChecks.cs`, `V9LabEvidence.cs`, `Verify-V9Captures.ps1`, `final/checks.txt`, `capture-checks.txt`, `benchmark.json`.

## Arquitetura e pipeline completo

[FATO] `--lighting-v9-lab` instancia um `Game` independente antes de `V8GameplayOptions.Configure`. Não constrói sessão de gameplay, worldgen ou persistência. Reutiliza `WorldMap` em memória/autotiles, `PlayerAnimator`, assets e infraestrutura MonoGame. V7/V8 não participam do cálculo ou desenho do frame V9. Backend DesktopGL, MonoGame resolvido 3.8.5.1, perfil Reach; shader novo registrado no MGCB sem mudança global de perfil.

[FATO] Sequência do runtime:

1. `V9LabScene` mantém mapa finito de 480×272 px (60×34 tiles de 8 px), parede receptora de fundo, terreno sólido, saliência e pilar. Exterior explicitamente marcado em `y<48`; montanhas distantes só nessa faixa.
2. `RebuildLights` constrói a lista ordenada: cinco amostras do céu quando habilitado, seguidas das tochas. Fontes e câmera estão em coordenadas do mundo.
3. Quando `dirty`, `Rebuild` visita os 130.560 centros de pixel do mundo; `Evaluate` considera cada fonte e chama `Visibility` para pares dentro do raio e com potência positiva.
4. Cada contribuição é `RGBlinear * potência * (1-distância/raio)² * visibilidade`. Fora do raio é zero. Soma em `Vector3` float32, sem piso global, divisão pelo número de fontes ou clamp por canal.
5. Guarda a soma no array `Energy`, converte para `HalfVector4` e envia o campo inteiro com `Texture.SetData(upload)`. A textura tem 480×272 texels ancorados no mundo. Alpha do campo é 1, sem função na energia.
6. `Render` desenha céu/montanhas distantes, background, terreno e postes, personagem, e finalmente chama emissiva. Cada receptor usa o mesmo campo com resposta explícita. Só a chama usa seus pixels originais sem iluminação; não há luz auxiliar no personagem.
7. Shader recebe textura do sprite com alpha pré-multiplicado e RGB interpretado como sRGB. Despré-multiplica, limita o albedo recuperado a [0,1], decodifica sRGB e multiplica por irradiância linear e resposta do plano. Fora do retângulo do campo, luz zero; amostragem `PointClamp`.
8. Calcula `L = albedoLinear * irradiância * resposta`, depois `mapped = L / (1 + max(L.r,L.g,L.b))`. Um único fator RGB, aplicado **depois** do albedo. Codifica sRGB, pré-multiplica pelo alpha e escreve em RT `Color` com `AlphaBlend`.
9. Exibe o RT final no backbuffer. Albedo e targets isolados do personagem são renderizados conforme captura/visualização. PNGs são pixels do runtime; recortes são cópias exatas. A imagem de irradiância é uma visualização diagnóstica identificada.

[FATO] Não há indireta, preenchimento local, luz mínima global, autoexposição, bloom ou atmosfera adicional. Blend de camadas semitransparentes ocorre em espaço codificado, uma aproximação já documentada; não tentar corrigir essa escolha como parte de uma otimização.

| Valor atual — [FATO] | Configuração a preservar |
|---|---|
| Tocha | Raio 144 px; potência 4 relativa; RGB linear `(1,0.49,0.19)` |
| Céu | Cinco fontes em `(86+12*i,28)`, `i=0..4`; raio 196 px; potência 1,4 cada; RGB linear `(0.46,0.69,1)` |
| Background | Resposta 0,14; parede de pedra, não bloqueia raios |
| Terreno/personagem/postes | Resposta 1; terreno recebe atenuação geométrica terminal adicional |
| Seção sólida | Profundidade máxima 6 px; comprimento de absorção 2,8 px |
| Incidência | `0,25 + 0,75*abs(componente da direção na normal)` |
| Compressão | `L/(1+maxRGB(L))`, sem exposição adaptativa |
| Fonte/personagem base | Tocha `(244,164)`; pés `(214,222)` |
| Câmera/janela base | Origem `(0,0)`; zoom 2,5; 1200×680 |
| Buffers | CPU float32; irradiância HalfVector4; quatro RTs Color RGBA8 |

## Oclusão, seção dos sólidos e quinas

[FATO] `V9LabScene.Solid` considera qualquer tile não vazio como sólido e o exterior do mapa como sólido, sem wrap. Background e montanhas não bloqueiam; personagem não projeta sombra. Fechar a abertura insere sólidos nas colunas 10–17, linha 6 (`y=48..55`). Fontes do céu continuam fora; o bloqueio decorre dos raios.

[FATO] `Visibility` faz DDA, avançando até a próxima fronteira X/Y, não passos espaciais arbitrários. Fonte em sólido retorna zero. Para alvo no ar, encontrar sólido retorna zero. Para alvo sólido, aceita somente os últimos até 6 px dentro da primeira massa encontrada; aplica incidência e `exp(-profundidade/2,8)`. Sair dessa massa para ar retorna zero: não é transmissão através da rocha.

[FATO] No empate de fronteiras, `abs(tx-ty)<0.000001`, testa as duas células laterais. Se qualquer uma é sólida, retorna zero; caso contrário avança diagonalmente. Essa regra conservadora faz parte do visual/contrato atual. Nem a oclusão 8×8 deve ser substituída pela transparência dos sprites, nem essa política de quinas deve ser relaxada durante uma otimização.

## Leitura técnica curta: fonte móvel

| Entrada / método | Conclusão |
|---|---|
| Inicialização | [FATO] `dirty=true` desde a construção do jogo. |
| `ResetScene` | [FATO] Marca `dirty=true`, restaura fontes/geometria e câmera. |
| `SetTorchCount` | [FATO] Marca `dirty=true`, inclusive para zero tochas. |
| `Update` | [FATO] O e B alteram abertura/pilar e marcam dirty. Clique válido em ar cria/move a primeira tocha e marca dirty. A captura do mouse é por borda do clique, não arrasto contínuo. |
| `ConfigureCase` / `StartBench` | [FATO] Invalidam indiretamente por `ResetScene` e/ou `SetTorchCount`. A captura automática configura um caso novo a cada frame; não mede o custo do cache interativo. |
| `AdvanceBenchScene` | [FATO] Nos casos `source-moving`, move só `torches[0]` e marca dirty a cada quadro. Nos casos `camera`, só muda a câmera. |
| `RebuildLights` | [FATO] Limpa/repreenche a lista, mas não marca dirty por si mesmo. |
| `SetOpening` / `SetBlocker` da cena | [FATO] Mudam o mapa; não notificam o campo diretamente. Hoje a invalidação pertence aos chamadores em `V9LabGame`; não há assinatura/revisão de geometria checada pelo campo. |
| `Draw` | [FATO] Consome dirty chamando `RebuildLights` e `field.Rebuild`, depois limpa o flag. Não distingue fonte alterada, céu estático ou região afetada. |
| Câmera/zoom/personagem | [FATO] No interativo e benchmark de câmera, não invalidam. O campo cobre o mundo inteiro da cena, não o viewport. |

[FATO] O laço externo de `Rebuild` visita 480×272 = **130.560 pixels**. `Evaluate` itera toda a lista para cada pixel: com céu e uma tocha são 6 fontes, **783.360 testes pixel–fonte**; com céu e cinco tochas são 10, **1.305.600 testes**. Nem todo teste lança um raio: distância/raio/potência filtram antes do DDA. O `while` de `Visibility` percorre as células até alvo ou oclusão. Após isso ainda há conversão de todos os pixels e upload integral (~1 MiB).

[INFERÊNCIA] O trabalho cresce com **pixels e fontes**, e, para os pares elegíveis, com o comprimento dos raios em células até a saída antecipada. Uma descrição útil é `O(P*S + soma dos passos DDA dos pares elegíveis + P para conversão/upload)`, não apenas O(fontes). Geometria e distância mudam o custo dos raios. Maior resolução da janela sozinha não aumenta P do campo fixo.

[FATO] A instrumentação mede `Rebuild` inteiro, incluindo `SetData`; não separa travessia, conversão e upload. Os dados mostram ~54,72/87,33 ms de campo+upload contra ~55,74/88,32 ms de Draw com fonte móvel. Assim o bloco de reconstrução é o gargalo observado; não se mediu tempo GPU.

[INFERÊNCIA] Dentro desse bloco, a estrutura mais provável de dominar é `Rebuild → Evaluate → Visibility`, por repetir testes e DDA para toda a cena e fontes estáticas também. Não há perfil por método que prove a fração exata do DDA ou descarte stalls do upload. Não apresentar essa leitura como profiling detalhado.

[FATO] Já persistem `Energy` (Vector3[]), `upload` (HalfVector4[]), `Texture` do campo, mapa/assets, listas `lights`/`torches`, SpriteBatch/Effect, pixel branco e quatro targets. Rebuild reutiliza os arrays e a textura; ausência de novas texturas por quadro não elimina recomputação. Não há cache por fonte, cache de visibilidade, máscara plana própria de ocupação ou região suja. Os recursos temporários de recorte/preview são restritos à captura.

## Cache, câmera e fonte: medições existentes

[FATO] `screenshots/v9-lab/final/benchmark.json`: Release, GTX 750, Reach/DesktopGL, 1200×680, zoom 2,5; cinco amostras de céu; 30 quadros de aquecimento, 120 medidos por caso, todos com foco. VSync e passo fixo desligados. Capturas/readback excluídos. Valores em ms CPU, não GPU.

| Caso | Draw mediana | Draw P95 | Campo+upload mediana |
|---|---:|---:|---:|
| Uma tocha, parado | 0,99 | 3,55 | 0 em cache |
| Uma tocha, câmera móvel | 0,97 | 1,33 | 0 em cache |
| Cinco tochas, parado | 0,95 | 1,27 | 0 em cache |
| Cinco tochas, câmera móvel | 1,00 | 1,28 | 0 em cache |
| Uma tocha movendo | 55,74 | 63,88 | 54,72 |
| Cinco tochas, só a primeira movendo | 88,32 | 102,91 | 87,33 |

[FATO] A prioridade técnica vigente é o custo alto ao mover fontes. O usuário não aprovou esse custo por aprovar a imagem. As medições em cache não demonstram viabilidade de fontes móveis a 60 Hz.

## Três candidatas de otimização — não implementadas, menor para maior risco

1. **[HIPÓTESE — menor risco] Máscara persistente de ocupação por revisão da geometria.** Ler uma grade booleana plana equivalente a `scene.Solid`, atualizada só quando abertura/pilar/mapa muda, pode reduzir consultas repetidas de `WorldMap`/delegates durante DDA. Manter fronteiras, posições, ordem aritmética e decisões de quina idênticas. Risco principal: invalidação incompleta da máscara. Ganho pode ser modesto; não elimina os raios nem foi medido.
2. **[HIPÓTESE — risco intermediário] Paralelizar blocos de pixels na CPU.** Cada pixel mantém exatamente a mesma ordem de fontes e cálculo; mapa/fontes ficam imutáveis durante o trabalho, pico é reduzido de máximos locais e `SetData` permanece no thread gráfico. Pode usar vários núcleos sem mudar amostragem/shader. Riscos: overhead, sincronização, acessos concorrentes a dados não garantidos imutáveis e variabilidade; não há ganho estimado validado.
3. **[HIPÓTESE — maior risco entre estas] Cache float32 das contribuições por fonte.** Recalcular só a fonte que mudou; invalidar todos os caches quando geometria muda e recompor em ordem idêntica à lista original. Preservar céu e tochas estáticos sem refazer seus raios. Recombinar contribuições completas evita acumular erro de subtração/adição; não comprimir/clampá-las antes da soma. Riscos: mais memória (~1,49 MiB por fonte para este campo), identidade/remoção/reordenação/invalidação, diferenças numéricas se agrupar somas. Pode ter maior ganho no caso móvel, mas exige mais estado.

[HIPÓTESE — critério de avaliação futura] Só considerar uma candidata bem-sucedida se preservar o visual aprovado e os contratos de oclusão/ordem/cores, usando a mesma configuração e evidências antes/depois, e reduzir custo em medições repetidas. Não baixar resolução do campo, alterar raio/curva, agrupar céu como uma luz, introduzir interpolação temporal ou mudar shader para obter um número melhor. Essas seriam mudanças de resultado, fora desta continuidade. Nenhuma candidata está autorizada a ser implementada por este documento sem uma próxima solicitação do usuário.

## Execução, controles, evidências e limites

[FATO] Comandos a partir de `C:\dev\Nyvorn-Reborn` (documentados, não executados nesta entrega). Usar pasta nova para preservar `final/` como referência:

```powershell
dotnet build .\Nyvorn\Nyvorn.csproj -c Release --no-restore
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\next-interactive
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-capture --v9-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\next-capture
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-bench --v9-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\next-bench
# Também aceita --v9-capture --v9-bench juntos.
& .\Docs\LightingV9\Verify-V9Captures.ps1 -CaptureDirectory C:\dev\Nyvorn-Reborn\screenshots\v9-lab\final
```

[FATO] Controles: A/D andar; Espaço saltar; setas câmera; clique esquerdo em ar reposicionar primeira tocha; 0/1/2/5 quantidade; O entrada; B pilar; +/- zoom; C albedo; R restaura cena; F12 captura; Esc encerra. Capturas automáticas/bench encerram sozinhas. O usuário confirmou testes interativos de abertura, múltiplas tochas e movimento de fontes; isso atualiza a confirmação manual limitada registrada no histórico.

[FATO] Evidências anteriores: 19 estados capturados em `final/`; `checks.txt` com 28 testes CPU; `capture-checks.txt` com 26 verificações dos pixels/JSON; `environment.txt`; `benchmark.json`; `*_metrics.json`, albedo, personagem isolado, recortes e preview identificado de irradiância. `base_final.png`, `five_final.png`, `sky-open_final.png`, `sky-closed_final.png`, `source-moved_final.png`, `blocker-removed_final.png`, `pan_final.png`, `zoom2_final.png` e `zoom3_final.png` são referências úteis. `initial/` documenta uma calibração anterior e **não deve substituir o visual final aprovado**.

[FATO] Regressões anteriores: V8 direta 107/107, ambiente 154/154; probe Reach passou. V7 numérico executou e Game1 rodou pela sessão temporária `--v7-gameplay-smoke`; não houve teste do menu carregando saves reais. V6Validator foi bloqueado por erro preexistente CS1073, `{medium,-12s}`, linha 258 de `V6TerrariaValidator.cs`. Não declarar V6 aprovada. Resultados, comandos e capturas de regressão estão no relatório de estado e em `screenshots/v9-lab/regression-*`.

[FATO] Limites: laboratório finito/manual; sem mundo destrutível geral, areia/água, wrap, clima, ciclo solar, objetos gerais, inventário/combat, sombra do personagem, penumbra ou atmosfera. Exterior explicitamente marcado; oclusão em tiles completos; superfície sólida é seção artística de até 6 px; blending semitransparente é aproximado; controlador não resolve toda despenetração ao editar geometria. Não há comparação controlada da mesma cena em V7/V8/V9. A aprovação comparativa do usuário é um julgamento artístico legítimo e registrado, não uma medição técnica equivalente.

[FATO — próximo limite de ação] Preservar saves, alterações locais e visual. Não promover V9 a padrão, não iniciar V7+sombras, não atualizar dependências/perfil, não fazer commits por inferência. Esta sessão termina na documentação; priorizar diagnóstico do custo quando o usuário autorizar o próximo trabalho.
