# V9-Lab — prova visual, 23/09/2026

## 25/09/2026 — mais recente: V9 promovido a iluminação padrão

[FATO] O V9 é a iluminação padrão do gameplay: o jogo, iniciado sem flag, joga com V9.

- **O que virou padrão:** o estado aprovado do Gameplay Probe com Sky Backplane — céu em FG/BG vazios, força 1 até os últimos 10 tiles do Shallow, queda por smoothstep até 0 na Cavern, Background Glow, resposta terminal do FG, tochas, cache por fonte e occupancy. Nada foi recalibrado.
- **Um só núcleo:**
  - `PlayingState.V9.cs` serve o jogo normal e o probe;
  - `LightingV9Probe/V9Gameplay.cs` concentra a seleção e a configuração;
  - o probe ficou só com a instrumentação.
- **No probe:** o padrão passou a ser o Backplane. O modelo anterior fica em `--v9-probe-exposure`.
- **V7/V8:** continuam por seleção explícita:
  - `--lighting-v7`, que é novo;
  - `--lighting-v8`;
  - `NYVORN_LIGHTING=legacy` (V6);
  - os modos de diagnóstico V7/V8 de antes.
- **Prova:** o probe no build novo reproduz o build aprovado bit a bit (34/34 estados Backplane e 40/40 exposure). O smoke novo `--v9-gameplay-smoke` passou 24/24 no caminho normal. Os saves não mudaram.
- **Pendências:**
  - o V9 não tem dia/noite; no jogo normal o relógio corre e só o céu pintado muda;
  - picos de 60 a 70 ms em recentro/edição em 1080p;
  - máscara preta residual, degradê claro→escuro e quinas ficam para depois.

Relatório: [V9_PROMOCAO_PADRAO.md](V9_PROMOCAO_PADRAO.md). Sem commit.

## 24/09/2026 — experimento paralelo de Sky Backplane (depois aprovado e promovido; ver acima)

`--lighting-v9-gameplay-probe --v9-probe-sky-backplane` testa FG/BG vazios como exposição direta, com uma só curva de profundidade Surface→Shallow→Cavern, glow no BG e resposta terminal curta de FG derivada do mesmo campo. O natural anterior continua padrão. Tochas preservadas bit a bit; nenhuma promoção. Recintos sem BG continuam expostos mesmo fechados em FG. Há regressões de custo em reconstrução da janela. Resultado e recomendação: [V9_SKY_BACKPLANE_RESULTADOS.md](V9_SKY_BACKPLANE_RESULTADOS.md).

## 24/09/2026 — mais recente: luz natural do probe (exposição + aberturas) e Sky Background Glow no Shallow

[FATO] O laboratório não mudou. No `--lighting-v9-gameplay-probe`:

- **Luz natural.** Deixou de ser a linha de amostras de céu descrita abaixo. Agora entra pela região exposta ao céu e pelas aberturas reais. A linha antiga só existe como diagnóstico, em `--v9-probe-sky-points`.
  - Relatório: seção de correção no topo de [V9_GAMEPLAY_PROBE_RESULTADOS.md](V9_GAMEPLAY_PROBE_RESULTADOS.md).
- **Céu no Shallow.** As aberturas de fundo do Shallow mostram o céu e acendem um halo curto no fundo em volta.
  - Relatório: [V9_SHALLOW_SKY_GLOW_RESULTADOS.md](V9_SHALLOW_SKY_GLOW_RESULTADOS.md).
- As tochas seguem bit a bit iguais.
- Nada foi promovido ao gameplay normal.

## 23/09/2026 — V9 Gameplay Probe (primeira ponte para o mundo real)

[FATO] O V9-Lab agora usa **cache por fonte + máscara de ocupação** por padrão. `--v9-no-occupancy-mask` e `--v9-no-source-cache` mantêm os caminhos anteriores. As capturas curtas reproduzem os 152 PNGs aprovados nos três caminhos.

[FATO] Novo modo opt-in: `--lighting-v9-gameplay-probe`, uma sessão real e transitória (seed V8-GAMEPLAY-2026, Small) que não salva nada.

- **Janela local** de 904×560 px de mundo, que acompanha a câmera e recentra com margem de ~131/99 px.
- **Cache por fonte** ancorado no mundo, restrito ao quadrado do raio e reaproveitado no recenter.
- **Ocupação local** pela colisão `Solid` do gameplay.
- **Fontes:** tochas reais com os valores do lab; céu em amostras do lab a cada 12 px, 20 px acima de um envelope exterior.
- **Receptores** iluminados pelo shader intacto, via quad de tela em coordenadas locais.

[FATO] Resultados:

- 55/55 checagens, sendo 42 bit a bit contra a avaliação completa.
- Entrada natural: a luz cai de 15,1 → 1,82 → 0,013 ao entrar no túnel. Vedada, dá 0 de 19.456 px iluminados; reaberta, volta idêntica.
- Caverna fechada preta; tochas quentes e localizadas; wrap exato.
- Parado ou andando em caverna, o custo é ~1,1–1,3 ms de CPU. Uma tocha movida custa ~10 ms.
- **O céu é caro**: recenter na superfície ~200 ms, tile alterado na superfície ~0,5 s, 194 MB de contribuições.
- Achado visual: as copas altas ficam fora do alcance das amostras e escurecem. Não houve recalibração.

Detalhes, limitações e recomendação em [V9_GAMEPLAY_PROBE_RESULTADOS.md](V9_GAMEPLAY_PROBE_RESULTADOS.md). Nada foi promovido ao gameplay normal. A direção visual segue congelada.

## 23/09/2026 — cache padrão do laboratório e máscara de ocupação

[FATO — decisão do usuário] O cache por fonte foi aprovado como **caminho padrão apenas do V9-Lab**. Os critérios foram: Energy float32, HalfVector4 e frames idênticos à referência, os 152 PNGs históricos coincidentes e a redução material do custo móvel.

- `--lighting-v9-lab` agora usa o cache.
- `--v9-no-source-cache` executa a referência antiga.
- O verificador foi mantido.
- Não há promoção ao gameplay normal.

Também foram registradas a reindentação de `V9LightField.cs` posterior aos testes do cache (o IL continuou idêntico) e a cópia permanente do manifesto de arquivos protegidos. Ambas estão no adendo de [V9_SOURCE_CACHE_RESULTADOS.md](V9_SOURCE_CACHE_RESULTADOS.md).

[FATO] Experimento seguinte: **máscara plana de ocupação**, opcional (`--v9-occupancy-mask`) e desligada por padrão.

- É uma cópia da resposta de `V9LabScene.Solid` na grade 60×34; fora dela, sólido, sem o wrap do `WorldMap`. Só é reconstruída quando `TileRevision` muda.
- Resultado idêntico ao cache em 43/43 estados: Energy float32 bit a bit, HalfVector4 e pixels finais. Os 152 PNGs históricos coincidiram.
- `scene.Solid` e a máscara concordaram em 230.431.571 consultas reais do DDA, 1.309.608 células de grade e 2.235.312 raios de borda.
- Draw mediano com fonte móvel: 13,34 → 11,78 ms (uma tocha) e 14,04 → 12,48 ms (cinco presentes); Evaluate/Visibility −16%.
- Casos parados e com câmera sem regressão. Custo: 2.040 bytes e ~0,01 ms por reconstrução.

Detalhes em [V9_OCCUPANCY_MASK_RESULTADOS.md](V9_OCCUPANCY_MASK_RESULTADOS.md). DDA, quinas, parâmetros, shader, tone mapping, geometria e resolução não foram alterados; a direção visual segue congelada.

## 23/09/2026 — decisão posterior do usuário, vigente

[FATO — decisão comunicada pelo usuário] Depois de testar interativamente a abertura, várias tochas e o movimento das fontes, o usuário **aprovou fortemente a direção visual atual do V9-Lab**, especialmente a luz natural e as tochas, considerando-a muito superior ao que estava obtendo em V7/V8.

Essa é uma **aprovação artística da direção atual**, não promoção para produção, aprovação de desempenho ou resultado de uma comparação técnica controlada entre os três pipelines. A recomendação vigente **não é mais abandonar o V9** nem iniciar a alternativa V7 com sombras. A continuidade deve preservar exatamente o visual aprovado; a prioridade técnica é investigar o custo alto ao mover fontes.

Esta atualização é exclusivamente documental. Não autoriza nem executa mudanças de shader, raio, potência, cor, tone mapping, albedo, resposta de material, geometria, refatoração ou otimização. O mapa técnico de continuidade está em [V9_HANDOFF_CLAUDE.md](V9_HANDOFF_CLAUDE.md).

## 23/09/2026 — experimento posterior: cache por fonte

[FATO] Por solicitação posterior do usuário, foi implementada uma variante diagnóstica `--v9-source-cache`, **desligada por padrão**, sem alterar o visual aprovado. Em 43 estados, Energy float32, HalfVector4 e pixels finais foram exatamente idênticos à referência; os 152 PNGs históricos também coincidiram byte a byte. A mediana CPU Draw móvel caiu de 56,14 para 13,46 ms (uma tocha) e de 88,08 para 14,09 ms (cinco presentes, primeira móvel). Payload extra: 8,96/14,94 MiB para 6/10 fontes. Os 28 testes CPU, 26 verificações de captura de cada caminho e regressões V8 107/154 passaram.

Conclusão: **promissor neste laboratório, sem promoção automática**. O custo de memória e o preenchimento completo em invalidações continuam relevantes. Resultados, política, instrumentação e comandos estão em [V9_SOURCE_CACHE_RESULTADOS.md](V9_SOURCE_CACHE_RESULTADOS.md). O cache permanece aguardando avaliação; nenhuma outra otimização foi iniciada. A atualização exclusivamente documental descrita na seção anterior pertence à etapa anterior a esta solicitação.

## Histórico anterior à aprovação do usuário — avaliação do agente

O texto abaixo foi preservado como registro da avaliação anterior. Suas observações e medições continuam disponíveis; **a recomendação histórica de parar/avaliar V7 foi superada pela decisão do usuário acima**. As reservas artísticas anteriores não são uma solicitação de recalibrar o visual aprovado.

O laboratório foi implementado e executado com assets do Nyvorn. É um `Game` temporário independente, selecionado por `--lighting-v9-lab`. Não é o renderer padrão. A prova funcional está concluída; a qualidade visual ainda não demonstra uma melhora convincente sobre as versões existentes. A recomendação é parar aqui e avaliar separadamente a alternativa de V7 como base visual com as sombras desejadas. Essa integração não foi iniciada e não é presumida trivial.

**Ambiente e preservação — fatos verificados**

- Branch `physics`; HEAD `f7edee1e5902c5f05265d6c50076aff042afb8c9`.
- Já havia alterações locais em `.gitignore`, shaders/classes da V8, `PlayingState` e documentos/arquivos não rastreados. Essas alterações foram preservadas. Não houve checkout, reset, limpeza, commit, atualização de dependências ou alteração de perfil.
- Nenhum `AGENTS.md` encontrado nos ancestrais ou no repositório.
- Projeto `net8.0`; SDK disponível 10.0.302; pacotes MonoGame resolvidos 3.8.5.1. Ferramenta local MGCB 3.8.4.1. Backend DesktopGL; conteúdo e dispositivo runtime Reach.
- GPU reportada pelo runtime: NVIDIA GeForce GTX 750/PCIe/SSE2. Janela/backbuffer 1200×680, `Color`.
- Compilação Release passou com os dois avisos CS0649 já presentes: `PlayingSessionViewCoordinator.lightTexture` e `PlayingState.debugPixelLightBuffer`.
- Execução gráfica, leitura da GPU, PNGs e abertura das capturas foram confirmadas. Build não foi usado como substituto da inspeção visual.
- O V9 não constrói sessão, gerador de mundo, serviços de save ou opções da V8. O ramo de entrada vem antes de `V8GameplayOptions.Configure`. O dispositivo/configurações pertencem apenas ao processo do laboratório; sair descarta os recursos e não altera configurações globais.

**Executar — a partir de `C:\dev\Nyvorn-Reborn`**

```powershell
dotnet build .\Nyvorn\Nyvorn.csproj -c Release --no-restore

# Interativo, sem saves
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab

# Bateria de 19 estados + benchmark; encerra automaticamente
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-capture --v9-bench --v9-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\final

# Verificações dos pixels/JSON capturados
& .\Docs\LightingV9\Verify-V9Captures.ps1
```

`--v9-output` escolhe a pasta; o padrão é `screenshots/v9-lab`. Capturas com o mesmo nome são substituídas apenas nessa pasta. `--v9-bench` também funciona sem `--v9-capture`. VSync/fixed step são desativados somente no processo de benchmark. No modo interativo, ficam ativos.

| Controle | Ação |
|---|---|
| A / D | Andar; controlador pequeno com colisão contra os tiles |
| Espaço | Saltar |
| Setas | Mover câmera |
| Clique esquerdo em ar | Reposicionar a primeira tocha; criar uma se não houver |
| 0 / 1 / 2 / 5 | Quantidade de tochas próximas |
| O | Abrir/fechar a entrada |
| B | Retirar/recolocar o pilar |
| + / − | Zoom em passos de 0,5, entre 1,5 e 4 |
| C | Exibir albedo sem iluminação |
| R | Restaurar cena e câmera |
| F12 | Capturar frame, albedo, personagem, recortes e métricas |
| Esc | Encerrar laboratório |

A janela interativa foi aberta e o fechamento da entrada por O foi observado. A automação detectou entrada do usuário durante a conferência seguinte; não continuou a disputar os controles. Os demais atalhos não têm confirmação manual individual. Movimento do personagem, câmera e zoom têm estados automáticos capturados, mas isso não equivale a testar toda a jogabilidade do controlador.

**Leitura artística e decisão de arquitetura**

As referências mostram grandes massas negras, textura nas bordas expostas, fundo discreto, focos quentes contidos e claridade fria perto de aberturas. A segunda imagem deixa escuridão entre esses focos. Nenhuma implementação do Kyora foi deduzida das imagens; nenhum material desse jogo foi importado.

`mapa finito em memória → travessia de tiles por raio → irradiância RGB linear somada → albedo e resposta do plano → compressão comum de RGB → codificação de saída`

A cena tem 60×34 tiles de 8 px, total 480×272 px do mundo. Há teto/fundo/piso irregulares, saliência à esquerda, pilar removível, personagem real e tochas. A geometria é manual e finita; o núcleo consulta sólidos, não coordenadas da tela. Fora desse retângulo é sólido, mesmo que `WorldMap` ofereça wrap em outros caminhos.

O exterior é explicitamente marcado como a faixa `y < 48`. A passagem ocupa as colunas 10–17. Fechá-la insere uma linha sólida em `y=48..55`. Cinco amostras fixas de céu estão fora da caverna, em `(86+12*i,28)`. Não são ativadas/desativadas pela porta: é a geometria que bloqueia seus raios. O fundo do poço é parede receptora; não foi usado um buraco arbitrário de background como fonte de céu. As montanhas são um plano distante restrito à faixa exterior, sem função de emissor/bloqueador.

A visibilidade usa DDA de tiles em 2D. Um sólido bloqueia completamente a luz destinada ao ar. Contatos exatos com cantos são conservadores. Para mostrar a seção exposta do terreno, somente o trecho terminal dentro da primeira massa sólida pode receber luz: máximo 6 px, absorção `exp(-profundidade/2,8)` e resposta à incidência `0,25 + 0,75*|direção · normal|`. O raio nunca sai dessa massa levando luz ao ar atrás dela. É uma aproximação artística da seção do terreno, não transporte através de rocha nem algoritmo de faces da V8.

As contribuições são somadas em float linear, sem dividir pelo número de fontes. Cada fonte usa `potência * max(0,1-distância/raio)² * visibilidade`. Depois do produto por albedo e resposta do plano, a radiância `L` vira `L/(1+max(L.r,L.g,L.b))`. O mesmo fator nos três canais preserva suas proporções lineares por pixel; o ombro reduz contraste em exposições altas. Essa troca é explícita e medida, não tratada como garantia de beleza. O limite por canal não é usado para acumular fontes.

| Parâmetro fixo | Valor / unidade |
|---|---|
| Raio da tocha | 144 px do mundo |
| Potência da tocha | 4 unidades relativas de irradiância |
| RGB linear do fogo | `(1,0.49,0.19)` |
| Raio de cada amostra do céu | 196 px do mundo |
| Potência de cada amostra do céu | 1,4 unidades relativas |
| RGB linear do céu | `(0.46,0.69,1)` |
| Resposta do background | 0,14 |
| Resposta de personagem/terreno | 1, com absorção geométrica adicional no terreno |
| Fonte principal / pés do personagem | `(244,164)` / `(214,222)` |
| Câmera base / zoom | `(0,0)` / 2,5 |

Não há luz mínima global, preenchimento local, indireta, autoexposição, luz auxiliar no personagem, bloom, névoa ou atmosfera adicional. Só os pixels do asset de chama são emissivos. A atmosfera opcional foi adiada; todas as avaliações são da base.

**Buffers, cor e alpha**

| Etapa | Formato e domínio | Limitação |
|---|---|---|
| Acumulação CPU | `Vector3[]`, float32 linear, um texel por px do mundo | Soma não limitada por canal; pico observado 17,859 com cinco tochas |
| Campo amostrado pela GPU | `Texture2D HalfVector4`, 480×272, aproximadamente 1 MiB; RGB linear, A=1 | Rejeita pico não finito ou ≥65000; teste de armazenamento/leitura confirmou valor 4,0 sem recorte |
| Assets existentes | `SurfaceFormat.Color`, RGB codificado interpretado como sRGB, alpha pré-multiplicado pelo MGCB | Assume autoria sRGB; não houve recoloração/reimportação dos assets |
| Shader receptor | Despré-multiplica RGB pelo alpha, decodifica sRGB, aplica iluminação, comprime, codifica e pré-multiplica novamente | Filtragem `PointClamp`; sem recuperação de energia já cortada |
| Frame/albedo/personagem | Quatro RTs `Color`, 1200×680, RGBA8, sem depth | Saída codificada; blend premultiplicado no espaço codificado, aproximação para bordas parcialmente transparentes |

O laboratório não muda a interpretação de cores do jogo inteiro. O blending entre camadas parcialmente transparentes não é linear correto; a comparação de referência usa pixels opacos do personagem e registra essa limitação. Não foi criado um compositor HDR geral para corrigir esse caso marginal.

O campo só é reconstruído quando fonte/geometria muda. Câmera, zoom e personagem usam o campo ancorado no mundo. Os seis recursos persistentes contados são quatro targets, o campo e o pixel branco; isso é um inventário, não instrumentação de todas as alocações da GPU. Exclui assets, buffers de SpriteBatch e recursos temporários de captura. Não há criação de textures/targets no caminho normal por quadro.

**Reutilização delimitada**

- `WorldMap`: armazenamento em memória e autotiles existentes. `TryGetTileSprite` fornece textura/source do terreno. `DrawBackground(..., Color.White)` foi lido e confirmado como seleção de textura/retângulo com tint explícito, sem efeito antigo. Não é chamado o renderer cacheado de terreno. `NeutralLightingAlbedo=true` fica nessa instância temporária.
- `PlayerAnimator`: frames, pivot e desenho das duas folhas reais, sempre `Color.White`; não depende de iluminação, inventário ou save. O controlador do lab é próprio e menor que uma sessão de gameplay.
- `ContentManager` e `SpriteBatch`: carregamento/desenho MonoGame; poles/chamas, terra/pedra/grama e montanhas já pertencem ao Nyvorn.
- Nenhum renderer, campo, shader, utilitário de contornos ou classe de captura V7/V8 participa do frame V9. Não foi preciso copiar um pipeline anterior.

**Capturas e inspeção visual**

Arquivos em `screenshots/v9-lab/final`, gerados pelo jogo, com recortes exatos dos pixels renderizados. Nenhum retoque ou pós-processamento externo foi aplicado. `*_irradiance-diagnostic.png` é explicitamente uma visualização `Encode(ToneMap(E))` do campo CPU, não uma captura final melhorada. O albedo mantém a cor original e a chama desenhada do asset.

| Evidência | Arquivo |
|---|---|
| Cena inteira | [base_final.png](../../screenshots/v9-lab/final/base_final.png) |
| Sem iluminação | [base_albedo.png](../../screenshots/v9-lab/final/base_albedo.png) |
| Personagem | [base_player-crop.png](../../screenshots/v9-lab/final/base_player-crop.png) |
| Parede próxima e chama | [base_wall-crop.png](../../screenshots/v9-lab/final/base_wall-crop.png) |
| Sombra preservada | [base_shadow-crop.png](../../screenshots/v9-lab/final/base_shadow-crop.png) |
| Cinco fontes | [five_final.png](../../screenshots/v9-lab/final/five_final.png) |
| Só céu / fechamento | [sky-open_final.png](../../screenshots/v9-lab/final/sky-open_final.png), [sky-closed_final.png](../../screenshots/v9-lab/final/sky-closed_final.png) |
| Sombra acompanha fonte | [source-moved_final.png](../../screenshots/v9-lab/final/source-moved_final.png) |
| Bloqueador removido | [blocker-removed_final.png](../../screenshots/v9-lab/final/blocker-removed_final.png) |
| Câmera e zoom | [pan_final.png](../../screenshots/v9-lab/final/pan_final.png), [zoom2_final.png](../../screenshots/v9-lab/final/zoom2_final.png), [zoom3_final.png](../../screenshots/v9-lab/final/zoom3_final.png) |

Observação visual: a entrada admite azul localizado, o fogo mantém uma região quente, o personagem é distinguível na base, e o pilar deixa uma região inteiramente escura. Cinco fontes tornam o fundo mais dominante, embora detalhes e cores ainda existam. O tecido visual continua repetitivo, as massas parecem recortadas e a resposta do terreno revela uma borda estreita, sem volume convincente. A abertura parece um poço com parede, não uma vista longa do exterior. Sem luz legítima, o personagem desaparece; isso é deliberado.

Houve uma calibração coerente, preservada em `screenshots/v9-lab/initial`: o primeiro fundo tinha manchas de terra delimitadas por bordas claras e resposta 0,32. Na revisão final foram removidas essas manchas, reduzida a resposta do fundo para 0,14 e usado o asset distante de montanhas na faixa exterior. Raios, potência, cores das fontes e regra de acumulação ficaram iguais. Não houve parâmetros específicos por caso. A revisão melhorou a separação local, mas não resolveu a linguagem visual do terreno.

**Medições — não equivalem a julgamento artístico**

Pontos fixos: parede `(244,142)`, região do personagem `(214,206)` e sombra `(328,210)`, em px do mundo. O recorte grande da parede contém emissores; a medição de material usa uma faixa separada sem chama, centrada em `(244,142)` com semieixos `(14,4)`.

| Tochas | RGB linear na parede | Y médio da parede | Desvio de Y da parede | Desvio/média | Y médio do personagem |
|---|---|---|---|---|---|
| 1 | 2,894 / 1,418 / 0,550 | 0,03562 | 0,02443 | 0,686 | 0,10709 |
| 2 | 5,713 / 2,799 / 1,085 | 0,06443 | 0,04236 | 0,657 | 0,14592 |
| 5 | 13,155 / 6,446 / 2,500 | 0,12231 | 0,07324 | 0,599 | 0,20368 |

A sombra ficou RGB linear `(0,0,0)` com uma e cinco tochas. Os picos de canais no personagem foram `(205,121,126)`, `(224,145,156)` e `(241,184,198)`, respectivamente. Nenhum pixel opaco do personagem chegou a 254 em qualquer canal. Isso não prova ausência de perda de contraste: o coeficiente de variação da parede cai cerca de 13% de uma para cinco fontes. O brilho absoluto e seu desvio aumentam ao mesmo tempo.

A compressão preserva a proporção RGB linear de cada radiância individual. A proporção média de uma imagem inteira pode mudar por causa da iluminação espacial e da compressão diferente em materiais claros/escuros. Portanto a média RGB do personagem não é usada como teste de conservação por pixel. A concordância shader/referência CPU teve erro RGB normalizado máximo 0,00217 nos casos 1/2/5; no pan subpixel, 0,00558, cerca de 1,42 códigos de 8 bits. Isso inclui quantização half/Color e rasterização. Os JSONs também registram erros de cromaticidade.

Perto da entrada, o Y médio do personagem foi 0,18081 aberta e 0,14484 fechada, com a mesma tocha e posição. Há alteração legítima de intensidade/cor, sem canais no limite. No teste de céu sem tocha e entrada fechada, todos os pixels do interior abaixo de `y=56` do mundo foram exatamente pretos; a faixa exterior continua visível.

**Testes e regressões**

- 28 verificações CPU passaram (`final/checks.txt`): sala selada, abertura/reclosure, movimento de fonte, remoção/restauração de obstáculo, segunda fonte, fontes todas bloqueadas, fonte distante, ordem, sobreposição 1/2/5, profundidade terminal, canto diagonal, curva e sRGB.
- 26 verificações das capturas passaram (`final/capture-checks.txt`). Frames completos de cinco fontes em ordem invertida são idênticos. Recolocar o bloqueador restaura o frame base. A tocha distante não altera os pixels do personagem original. O campo visualizado é idêntico com pan, zoom 2/3 e personagem movido. Foram analisados pixels reais da GPU, além do núcleo CPU.
- V8 direta: 107/107; V8 ambiente: 154/154; probe de stencil em Reach passou. Execuções separadas, nas pastas `regression-v8-direct` e `regression-v8-ambient`.
- V7 numérico executou, exit 0 (`regression-v7.txt`); é um relatório de observações, não uma suíte geral de asserts. Seu benchmark reportou média 1,58 ms acima da meta interna de 1,5 ms.
- O `Game1` normal foi exercitado pelo caminho existente `--v7-gameplay-smoke`, em sessão temporária, com 30 frames e capturas em `regression-v7-gameplay/screenshots`. Ele consulta somente a identidade sintética `v8-transient-validation`; o arquivo dessa identidade não existe no ambiente. O menu normal não foi aberto porque carrega o jogador selecionado do usuário. Não se alega validação completa do menu/saves.
- O validador V6 não compilou: `V6Validator/V6TerrariaValidator.cs:258`, CS1073, interpolação `{medium,-12s}`. O defeito foi confirmado em HEAD e o arquivo não foi alterado. Não se alega regressão V6 aprovada.
- Não há comparação equivalente V7|V8|V9 nesta caverna: os diagnósticos existentes usam outras cenas e contextos. As capturas V7/V8 foram abertas para confirmar execução, mas não sustentam superioridade visual V9. Criar um adaptador de três pipelines foi deixado fora do laboratório.

Comandos de regressão executados (saídas V8 são caminhos novos):

```powershell
dotnet run --project .\LightingV7Validator\LightingV7Validator.csproj -c Release --no-restore
dotnet run --project .\V6Validator\V6Validator.csproj -c Release --no-restore
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --v8-scene --v8-capture --v8-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\regression-v8-direct
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --v8-scene --v8-ambient --v8-capture --v8-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\regression-v8-ambient
# Smoke executado a partir da pasta própria de evidências, para isolar seus logs:
dotnet C:\dev\Nyvorn-Reborn\Nyvorn\bin\Release\net8.0\Nyvorn.dll --v7-gameplay-smoke
```

**Custo medido**

Release, 1200×680, zoom 2,5, cinco amostras de céu; 30 quadros de aquecimento e 120 amostras por caso, todos com foco. Sem VSync/fixed step. Capturas e readbacks excluídos. `CpuDrawSubmissionMs` é tempo CPU de preparar/submeter o desenho, incluindo reconstrução quando necessária; não é tempo GPU. Intervalo entre inícios de frame inclui update/present/agendamento.

| Caso | CPU mediana ms | CPU P95 ms | Campo+upload mediana ms | Intervalo mediana ms |
|---|---|---|---|---|
| 1 tocha, parado | 0,99 | 3,55 | 0 em cache | 1,02 |
| 1 tocha, câmera móvel | 0,97 | 1,33 | 0 em cache | 1,00 |
| 5 tochas, parado | 0,95 | 1,27 | 0 em cache | 0,98 |
| 5 tochas, câmera móvel | 1,00 | 1,28 | 0 em cache | 1,03 |
| 1 tocha movendo | 55,74 | 63,88 | 54,72 | 55,85 |
| 5 tochas, primeira movendo | 88,32 | 102,91 | 87,33 | 88,40 |

Reconstruir todos os raios por pixel é caro para luz móvel. A medição quente confirma o problema; cache de câmera não o resolve. Não foi iniciada campanha de otimização ou troca de arquitetura para escondê-lo. Dados completos em `final/benchmark.json`.

**Limites e hipótese de continuidade**

Fatos do escopo: não implementa areia dinâmica, líquidos, worldgen, clima, ciclo solar, wrap, inventário, combate, objetos gerais, penumbra ou mundo destrutível completo. O fundo não bloqueia luz. O personagem não projeta sombra. As fontes do céu representam uma abertura marcada, não uma solução geral de exterior. Fechar/mover geometria arbitrária ao redor do jogador não resolve despenetração. A posição inicial dos estados automáticos é diagnóstica e pode ficar suspensa; a física interativa é separada. Fontes colocadas em sólidos não emitem luz. A transparência visual de uma borda de tile não muda sua oclusão geométrica de 8×8.

Observação: a mesma regra mantém escuro, frio/quente e sombras, mas a textura do fundo é dominante com sobreposição forte e o terreno carece de volume. Não foi demonstrado que uma calibragem pequena restante resolveria isso.

Hipótese, não resultado: V7 pode ser uma base visual mais produtiva, preservando sua resposta de materiais e recebendo sombras geométricas em uma avaliação separada. Essa alternativa precisa de investigação própria sobre composição e receptores. O V9 fica como prova reproduzível; não deve ser promovido por passar testes numéricos.

**Arquivos desta entrega**

Compartilhados: `Nyvorn/Program.cs` (oito linhas para opt-in) e `Nyvorn/Content/Content.mgcb` (registro de um efeito). Novos: `Nyvorn/Content/effects/V9LabReceiver.fx`; `V9LightField.cs`, `V9LabScene.cs`, `V9LabGame.cs`, `V9LabChecks.cs`, `V9LabEvidence.cs` sob `Nyvorn/Source/Engine/Graphics/LightingV9Lab`; este documento e `Verify-V9Captures.ps1`. Os arquivos V8 que aparecem em `git status` já estavam modificados antes deste trabalho.

**Referências técnicas efetivamente consultadas**

- [Amanatides e Woo — A Fast Voxel Traversal Algorithm](https://www.eecs.yorku.ca/~amana/research/grid.pdf): travessia por fronteiras de células para evitar saltar obstáculos; adaptação pequena para 2D com política conservadora de cantos.
- [MonoGame — SurfaceFormat](https://docs.monogame.net/api/Microsoft.Xna.Framework.Graphics.SurfaceFormat.html) e [HalfVector4](https://docs.monogame.net/api/Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4.html): armazenamento half de irradiância sem recorte RGBA8. A disponibilidade no dispositivo foi confirmada em runtime, não inferida só da enumeração.
- [MonoGame — TextureProcessor](https://docs.monogame.net/api/Microsoft.Xna.Framework.Content.Pipeline.Processors.TextureProcessor.html): contrato de alpha pré-multiplicado, confrontado com o MGCB local.
- [Khronos — EXT_texture_sRGB](https://registry.khronos.org/OpenGL/extensions/EXT/EXT_texture_sRGB.txt): funções de transferência, alpha linear e limitação de blending em buffer codificado. A compressão comum de RGB é uma escolha explícita deste laboratório, não atribuída a essas referências.
