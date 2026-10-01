# V9-Lab — cache diagnóstico por fonte, 23/09/2026

## Adendo de encerramento (vigente)

- **Aprovado como caminho padrão apenas do V9-Lab.** Por decisão do usuário, `--lighting-v9-lab` passou a usar o cache por fonte.
  - `--v9-no-source-cache` executa o caminho de referência antigo.
  - `--v9-source-cache` segue aceito, agora redundante. Combinado com `--v9-no-source-cache`, é recusado com erro.
  - O caminho antigo e `--v9-cache-verify` foram mantidos. O V9 não foi promovido ao gameplay normal.
  - As capturas convencionais com o novo padrão e com `--v9-no-source-cache` passaram 26/26, e os 152 PNGs históricos coincidiram byte a byte. Evidências em `screenshots/v9-lab/occupancy-mask/capture-default/` e `capture-no-source-cache/`.
  - O texto abaixo que descreve o cache como "desligado por padrão" é histórico.
- **Reindentação posterior aos testes.** `V9LightField.cs` foi regravado às 14:52:36, depois do build (14:44:20) e de todos os testes deste relatório. A mudança só reindentou o laço de conversão: seis linhas, tokens idênticos.
  - Dos 333 fontes registrados no PDB da build testada, só esse diferia do disco.
  - A DLL testada e a recompilação têm IL idêntico em 6.895 métodos, 135 deles do V9-Lab. Nos metadados só mudam os 16 bytes do MVID.
  - Portanto os resultados deste relatório valem para o código daquele estado.
- **Manifesto persistido.** O manifesto dos 197 arquivos protegidos existia apenas em `%TEMP%\nyvorn-v9-source-cache-protected.json`.
  - Cópia sem alteração: `screenshots/v9-lab/source-cache/nyvorn-v9-source-cache-protected.json`.
  - SHA-256 `BCA4761895E248925A58B9AA3E1C89182750E57A791462682BE2BB2352F4A6C2`, 31.893 bytes, 197 entradas; reconferido com 0 alterados.
- Experimento seguinte: [V9_OCCUPANCY_MASK_RESULTADOS.md](V9_OCCUPANCY_MASK_RESULTADOS.md).

**Resultado: promissor neste laboratório.** O cache preservou exatamente Energy float32, conteúdo HalfVector4 e pixels finais nos 43 estados comparados. A mediana CPU de Draw com uma fonte móvel caiu 76,0% com uma tocha e 84,0% com cinco tochas presentes. A direção visual aprovada foi preservada. **O cache continua opcional, desligado por padrão, aguardando avaliação do usuário.** Não houve commit ou promoção para produção.

## Implementação

Branch `physics`, HEAD `f7edee1e5902c5f05265d6c50076aff042afb8c9`; checkout local preexistente preservado. Opt-in: `--lighting-v9-lab --v9-source-cache`. Sem a segunda opção, o avaliador anterior calcula todas as fontes quando o campo está dirty; não há cache instanciado nesse caminho.

`V9SourceCache` mantém uma entrada por **posição na lista**: dados completos `V9Light`, um buffer `Vector3[]` de contribuição e um flag válido. O slot é a identidade mínima; não foi adicionado um sistema de IDs de gameplay. Igualdade da fonte inclui posição, RGB linear, potência e raio. A ordem continua sendo as cinco amostras de céu, depois as tochas na ordem original.

Cada entrada inválida usa o **mesmo `V9LightMath.Evaluate`, sem alterações**, com uma lista de uma fonte. Isso reutiliza exatamente fórmula, DDA, quinas e resposta terminal. Guarda somente float32 linear, sem Half, compressão ou clamp. Depois, para cada pixel, inicia `sum=Vector3.Zero` e soma as entradas de índice 0 até N−1. Não usa subtração do total anterior, agrupamento do céu ou soma em outra ordem.

A conversão para HalfVector4 e `SetData` continuam depois da soma total, nos mesmos formato, resolução e domínio. Para instrumentação, o caminho de referência separa a avaliação e a conversão em dois laços; o avaliador matemático e a ordem de suas operações não mudaram. A etapa comum de conversão inclui cálculo/verificação do pico, construção dos HalfVector4 e upload integral. Essa separação não é uma otimização das subetapas. A referência instrumentada foi comparada com os PNGs anteriores, além de ser comparada com o cache.

Não foram implementados máscara de ocupação, paralelismo, retângulos sujos, atualização temporal, compute, interpolação ou cache de DDA. Não foram alterados shader, tone mapping, parâmetros, cores, receptores, amostras de céu, geometria, assets, formatos ou resolução.

## Política de invalidação

| Evento | Comportamento |
|---|---|
| Mover fonte, mesma quantidade | Recalcula somente o slot cujos dados mudaram |
| Alterar RGB/potência/raio de um slot | A igualdade detecta a mudança; nenhum parâmetro foi alterado neste experimento |
| Criar/remover fonte ou alterar quantidade | Ajusta tamanho da lista e invalida todas as entradas, por simplicidade |
| Reordenar, mantendo quantidade | Recalcula slots com dados diferentes; soma na ordem vigente |
| Duplicatas | Slots independentes; trocar duas fontes idênticas não exige recálculo |
| Geometria | `scene.Map.TileRevision` diferente invalida todas; trocar a instância da cena também |
| Reset | `ResetScene` invalida explicitamente todas, mesmo voltando ao estado idêntico |
| Câmera, zoom, personagem | Não marcam dirty; não recalculam nem recompõem |
| Lista vazia | Recomposição produz zero, sem luz residual |

`V9LabGame` continua responsável pelo dirty nos controles e benchmark. As mutações atuais de abertura/pilar marcam dirty; ao reconstruir, o cache verifica a revisão do mapa. Uma futura edição de geometria fora desses caminhos ainda deverá acionar dirty. Não foi introduzido um observador geral de mundo destrutível.

## Equivalência exata

`--v9-cache-verify` instancia os dois caminhos no mesmo runtime, fora do benchmark. Testa os 19 estados originais mais 24 passos sequenciais. A referência recalcula do zero a cada estado; o cache mantém histórico entre passos. Compara nesta ordem:

1. Os 391.680 componentes float32 de Energy por bits; registra erros absoluto/relativo máximos e SHA-256.
2. Os 130.560 `PackedValue` de HalfVector4 destinados ao upload.
3. Os 816.000 pixels RGBA do frame final lido da GPU, renderizado separadamente em cada caminho.

**43/43 estados: zero componentes float divergentes, erro absoluto máximo 0, relativo máximo 0, zero texels Half divergentes, zero pixels finais divergentes. Nenhuma tolerância foi utilizada.** Falha de igualdade ou de contagem de invalidação encerra o diagnóstico com erro.

| Sequência | Fontes | Contribuições recalculadas |
|---|---:|---:|
| Base | 6 | 6 |
| Mover primeira tocha | 6 | 1 |
| Mover a mesma novamente | 6 | 1 |
| Adicionar segunda tocha | 7 | 7 |
| Mover só a primeira | 7 | 1 |
| Fechar entrada / reabrir | 7 | 7 / 7 |
| Remover pilar / restaurar | 7 | 7 / 7 |
| Câmera / zoom / personagem | 7 | 0 / 0 / 0 |
| Inverter as duas tochas | 7 | 2 |
| Remover uma tocha | 6 | 6 |
| Passar a cinco tochas | 10 | 10 |
| Mover só a primeira das cinco | 10 | 1 |
| Remover todas as tochas, mantendo céu | 5 | 5 |
| Criar tocha novamente | 6 | 6 |
| Voltar à base / reset idêntico | 6 | 6 / 6 |
| Adicionar duplicata / inverter iguais | 7 | 7 / 0 |
| Remover todas as fontes, inclusive céu | 0 | 0 |
| Restaurar base | 6 | 6 |

Os casos originais são configurados separadamente com reset; suas contagens são de preenchimento completo. A sequência prova o reaproveitamento. Nos passos sem dirty, o campo antigo do cache ainda é comparado com a referência recém-calculada.

As 26 verificações de captura existentes passaram **nos dois conjuntos**. Os 152 PNGs históricos em `screenshots/v9-lab/final` coincidiram byte a byte com os PNGs do cache: frame, albedo, personagem, recortes e preview. A captura base do cache foi aberta para inspeção; não houve recalibração visual.

## Benchmark antes/depois

Executado depois da equivalência: Release, 1200×680, zoom 2,5, cinco fontes de céu; 30 frames de aquecimento, 120 medidos por caso; janela em foco em **120/120 amostras de todos os casos dos dois processos**; VSync/fixed step desligados. Sem capturas/readback na região medida. O probe inicial de armazenamento precede o aquecimento, como na bancada existente. GTX 750, Reach/DesktopGL, MonoGame 3.8.5.1.

Uma execução completa de cada caminho com múltiplas amostras, referência primeiro e cache depois. Não é estudo estatístico entre máquinas/repetições independentes. Diferenças pequenas nos casos estáticos não sustentam conclusão de desempenho.

| Caso | Referência Draw mediana / P95 ms | Cache Draw mediana / P95 ms |
|---|---:|---:|
| 1 tocha, parado | 0,78 / 1,09 | 0,79 / 1,05 |
| 1 tocha, câmera móvel | 0,74 / 0,95 | 0,79 / 0,96 |
| 5 tochas, parado | 0,77 / 0,99 | 0,79 / 0,94 |
| 5 tochas, câmera móvel | 0,76 / 0,99 | 0,79 / 0,96 |
| 1 tocha movendo | **56,14 / 57,89** | **13,46 / 13,95** |
| 5 presentes, primeira movendo | **88,08 / 106,20** | **14,09 / 15,52** |

Ganhos móveis de mediana: **4,17× (76,0% de redução)** e **6,25× (84,0%)**. Os casos em cache completo/câmera móvel continuam próximos de 0,8 ms, sem recálculo. São medidas CPU, não tempo GPU ou garantia de 60 FPS em gameplay.

| Etapa — mediana CPU ms | 1 móvel, referência | 1 móvel, cache | 5 presentes, referência | 5 presentes, cache |
|---|---:|---:|---:|---:|
| Evaluate/Visibility | 52,8955 | 9,5245 | 84,5236 | 9,5206 |
| Recomposição separada | 0 | 1,0177 | 0 | 1,6475 |
| Pico + HalfVector4 + SetData | 2,2534 | 1,8975 | 2,4128 | 1,9184 |
| Fontes recalculadas/quadro | 6 | 1 | 10 | 1 |
| Intervalo mediano / P95 entre frames | 56,1995 / 57,9629 | 13,5460 / 14,0679 | 88,1720 / 105,1392 | 14,2016 / 15,6167 |

Na referência a soma já está em `Evaluate`; zero em recomposição separada não significa soma gratuita. No cache, atualização inclui checagens de validade e, quando houver, redimensionamento/invalidação. Half+SetData não separa CPU de espera do driver. Medianas das etapas não precisam somar exatamente a mediana de Draw. Todos os casos têm média, mediana, P95, mínimo e máximo das etapas e intervalos nos JSONs.

O cache tem custo inicial e em mudanças de geometria/conjunto: preenche todos os buffers e ainda os recompõe. No primeiro estado base da verificação, observou-se **59,93 ms na referência e 67,33 ms no cache**. Essa amostra fria inclui inicialização/JIT/alocação e **não é benchmark comparável às amostras aquecidas**; evidencia que o custo não desaparece e pode aumentar quando nada é reutilizado. Não houve bancada extensa de edições de geometria.

## Memória adicional

`Marshal.SizeOf<Vector3>()` confirmou 12 bytes. Payload por fonte: `480*272*12 = 1.566.720 bytes` (**1,49414 MiB**), incluindo pixels zero. Nenhum formato foi alterado.

| Fontes | Payload adicional das contribuições |
|---|---:|
| 6 (5 céu + 1 tocha) | **9.400.320 bytes = 8,96484 MiB** |
| 10 (5 céu + 5 tochas) | **15.667.200 bytes = 14,94141 MiB** |

Além desse payload, persistem um objeto de entrada e um array `V9Light[1]` por fonte, flags/revisão, a lista de referências e o objeto do cache; existem cabeçalhos/alinhamento gerenciados. Os totais acima **não medem todo o heap nem incluem esse overhead**. Energy final e upload Half já existiam. O cache adiciona **zero recursos GPU** no modo normal/benchmark; continua o inventário de seis texturas/targets próprios. O verificador separado cria campos extras e arrays de readback para comparar os caminhos.

Mover fonte reutiliza seus buffers, sem novo buffer de contribuição por quadro. Aumentar quantidade aloca entradas; diminuir remove excedentes, sujeitos ao GC. O caminho sem opção não instancia o cache.

Escala `pixels * fontes`: 100 fontes seriam ~149,4 MiB só de contribuições neste pequeno campo; 1.000, ~1,46 GiB. Campo de gameplay maior piora proporcionalmente. Não foram implementados orçamento, eviction, compactação ou cache espacial. Viabilidade nesta cena não demonstra viabilidade de manter todas as fontes de um mundo grande residentes.

## Testes, preservação, complexidade e arquivos

- Build Release aprovado, mesmos dois avisos CS0649 anteriores.
- 28 testes do núcleo aprovados nas inicializações de referência e cache.
- 43 estados de equivalência exata e contagens de invalidação aprovados.
- 26 verificações de captura aprovadas para cada caminho.
- 152 PNGs anteriores idênticos; `final/` preservada.
- V8 direta 107/107, ambiente 154/154, probe Reach aprovado; código V8 intocado.
- Manifesto SHA-256 verificou **197 arquivos protegidos** sem alterações: referência `final/`, shader V9, cena, testes CPU originais, Program/MGCB e fontes/shaders V8 consultados. `V9LabSettings` e `V9LightMath` não foram editados.

Complexidade: uma pequena classe de cache CPU por slots, seleção/reset no campo/jogo, métricas de etapas e verificador separado em arquivo parcial. Não há gerenciador geral de fontes. A invalidação conservadora por quantidade evita remapeamento de identidades. Memória e estado extras são a troca pelo reaproveitamento; não se oculta o custo com refatorações/otimizações adjacentes.

Alterados nesta entrega: `V9LightField.cs`, `V9LabGame.cs`. Novos: `V9SourceCache.cs`, `V9LabGame.SourceCacheChecks.cs`. Todos em `Nyvorn/Source/Engine/Graphics/LightingV9Lab/`, ainda não rastreados, como o restante do V9. Documentos: este relatório novo e atualizações em `V9_LAB_ESTADO.md`/`V9_HANDOFF_CLAUDE.md`. Os diffs V8 preexistentes continuam pertencendo ao trabalho anterior.

## Comandos e evidências

A partir de `C:\dev\Nyvorn-Reborn`; escolher outra pasta ao repetir para preservar também esta bateria.

Os comandos abaixo estão exatamente como foram executados nesta bateria. **Desde o encerramento, o padrão é o cache.** Para repetir a bancada de referência, acrescente `--v9-no-source-cache`; sem essa opção, o comando `bench-reference` abaixo mediria o cache.

```powershell
dotnet build .\Nyvorn\Nyvorn.csproj -c Release --no-restore

# Ambos os caminhos, 19 cenas + sequência; capturas separadas
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-cache-verify --v9-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\source-cache\equivalence
& .\Docs\LightingV9\Verify-V9Captures.ps1 -CaptureDirectory C:\dev\Nyvorn-Reborn\screenshots\v9-lab\source-cache\equivalence\reference
& .\Docs\LightingV9\Verify-V9Captures.ps1 -CaptureDirectory C:\dev\Nyvorn-Reborn\screenshots\v9-lab\source-cache\equivalence\cache

# Bancadas separadas, sem captura/readback durante a medição
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-bench --v9-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\source-cache\bench-reference
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-source-cache --v9-bench --v9-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\source-cache\bench-cache

# Interativo opcional; retirar --v9-source-cache retorna ao caminho anterior
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-source-cache --v9-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\source-cache\interactive

# Captura convencional também aceita o cache
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-source-cache --v9-capture --v9-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\source-cache\capture
```

`--v9-cache-verify` não pode ser combinado com captura/benchmark, para separar readback da medição. Para equivalência ele cria os dois caminhos internamente, independentemente de `--v9-source-cache`. Esta bateria usou o verificador para capturas e os dois comandos de benchmark; os exemplos interativo/captura convencional são opções disponíveis.

Evidências em `screenshots/v9-lab/source-cache/`: `equivalence/equivalence.json`, `equivalence/equivalence-summary.txt`, `equivalence/reference/`, `equivalence/cache/`, `bench-reference/benchmark.json`, `bench-cache/benchmark.json`, `benchmark-comparison.json`, `historical-comparison.txt`, `protected-files-check.txt` e `regression-v8-direct/`/`regression-v8-ambient/`.

**Conclusão para avaliação:** nesta cena, frame exato, oclusão preservada, ausência de resíduos nos estados testados e redução material do custo móvel. Memória adicional e preenchimento completo nas invalidações são custos reais. O resultado é favorável ao experimento; o caminho padrão permanece anterior, e nenhuma outra otimização foi iniciada.
