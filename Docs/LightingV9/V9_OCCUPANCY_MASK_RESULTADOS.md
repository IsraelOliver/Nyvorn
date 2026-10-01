# V9-Lab — máscara plana de ocupação, 23/09/2026

> **Atualização vigente:** por decisão do usuário, a máscara foi aprovada como **padrão apenas do V9-Lab**, junto com o cache por fonte.
> - `--lighting-v9-lab` usa cache + máscara; `--v9-no-occupancy-mask` roda o cache sem máscara; `--v9-no-source-cache` roda a referência antiga.
> - O `--v9-cache-verify` agora faz a comparação em três vias por padrão.
> - Verificação curta: 26/26 capturas e os 152 PNGs de `final/` idênticos nos três caminhos (`screenshots/v9-lab/lab-default-cache-mask/verification.txt`).
> - O texto abaixo que diz "desligada por padrão" é histórico. Continuação: [V9_GAMEPLAY_PROBE_RESULTADOS.md](V9_GAMEPLAY_PROBE_RESULTADOS.md).

**Resultado: ganho pequeno, consistente e exato.** Com a máscara, a mediana CPU de Draw com uma tocha móvel passou de 13,34 para 11,78 ms (−1,57 ms, −11,7%). Com cinco tochas presentes e só a primeira móvel, passou de 14,04 para 12,48 ms (−1,56 ms, −11,1%). Evaluate/Visibility caiu ~16,4%, de 9,60 para 8,01 ms e de 9,59 para 8,02 ms. Energy float32, HalfVector4 e pixels finais ficaram idênticos ao cache atual nos 43 estados, e os 152 PNGs históricos coincidiram byte a byte. **A máscara é opcional (`--v9-occupancy-mask`), está desligada por padrão e aguarda avaliação.** Não houve commit. A direção visual não foi tocada.

## 1. Encerramento do experimento anterior

- **Cache como padrão, só do V9-Lab.** Por decisão do usuário, `--lighting-v9-lab` passou a usar o cache por fonte. `--v9-no-source-cache` executa o caminho de referência antigo, com avaliação completa de todas as fontes por pixel. `--v9-source-cache` continua aceito, agora redundante. Combinado com `--v9-no-source-cache`, é recusado com erro antes de abrir a janela. O caminho antigo e o verificador `--v9-cache-verify` foram mantidos. Nada foi promovido ao gameplay normal: `Program.cs` e `Game1` não mudaram.
- A captura convencional com o novo padrão (`capture-default`) e a com `--v9-no-source-cache` (`capture-no-source-cache`) passaram 26/26 cada. Ambas reproduzem os 152 PNGs de `final/` byte a byte.
- **Reindentação posterior aos testes do cache.** `V9LightField.cs` foi regravado às 14:52:36, depois do último build (14:44:20) e de todos os testes do cache. A mudança só reindentou o laço de conversão: seis linhas, tokens idênticos.
  - O PDB da build testada guarda o SHA-256 de cada fonte. Dos 333, só esse diferia do disco.
  - A DLL testada e a recompilação têm IL idêntico em 6.895 métodos, 135 deles do V9-Lab. Nos metadados só mudam os 16 bytes do MVID.
  - Registrado também no relatório do cache.
- **Manifesto persistido.** O manifesto dos 197 arquivos protegidos existia só em `%TEMP%`. Foi copiado sem alteração para `screenshots/v9-lab/source-cache/nyvorn-v9-source-cache-protected.json`: SHA-256 `BCA4761895E248925A58B9AA3E1C89182750E57A791462682BE2BB2352F4A6C2`, 31.893 bytes, idêntico ao original. Reconferido nesta entrega: 197 inalterados.

## 2. Domínio consultado por `Solid` (determinado antes de implementar)

`V9LabScene.Solid(x, y)` é `x < 0 || y < 0 || x >= 60 || y >= 34 || Map.GetTile(x, y) != TileType.Empty`.

- **Fora de `[0,60)×[0,34)`: sólido**, decidido *antes* de consultar o `WorldMap`. Esta é a particularidade preservada:
  - `WorldMap.GetTile` faria wrap horizontal (`x % 60`) e devolveria `Empty` para `y` fora do mapa;
  - nenhum dos dois é alcançado pelo laboratório;
  - a máscara aplica a mesma regra explícita: fora da grade é sólido, sem wrap.
- **Dentro:** `GetTile` passa por `InBounds` (só testa `y`), por `WrapTileX(x)` (identidade para `0 ≤ x < 60`) e lê `_tiles[x, y]`, uma matriz 2D. É sólido qualquer tipo diferente de `Empty`: grama, terra ou pedra da fixture, a terra da abertura fechada e a terra ou pedra do pilar.
- **Fixture:**
  - 768 células sólidas na base;
  - 776 com a entrada fechada (+8: colunas 10–17, linha 6);
  - 748 sem o pilar (−20: colunas 37–38, linhas 18–27);
  - a faixa exterior (linhas 0–5) não tem sólidos em nenhum estado.
- **Consultas do DDA:** célula da fonte, célula do alvo, células atravessadas e as duas laterais em cada quina. Fontes e receptores do laboratório estão dentro do mapa. Pela análise do código, o DDA não sai da caixa entre as células de origem e destino. A medição confirmou: em 230.431.571 consultas reais nos 43 estados, o domínio foi x ∈ [0,59] e y ∈ [0,33], com **zero consultas fora da grade**.
- **Revisão:** as cinco escritas em `_tiles` incrementam `TileRevision` (linhas 117, 164, 500, 533 e 664 de `WorldMap.cs`). `SetTile` só incrementa quando o tile muda de fato. `SetOpening`/`SetBlocker` retornam cedo se o estado pedido já vigora.

## 3. Implementação

- **`V9OccupancyMask`** (arquivo novo, 49 linhas):
  - um `bool[60*34]` preenchido chamando o próprio `scene.Solid(x, y)` para cada célula da grade, sem reinterpretar tipos de tile;
  - a consulta é `(uint)x >= 60 || (uint)y >= 34 || cells[y*60 + x]`; a comparação sem sinal responde igual a `x < 0 || x >= 60` para qualquer `int`;
  - recusa uma grade diferente de 60×34.
- **`V9SourceCache.Rebuild`** ganhou o parâmetro opcional `solid`. Com `null`, continua usando `scene.Solid`, exatamente como antes. Política de slots, invalidação, recomposição e ordem das somas não mudaram.
- **`V9LightField`:** com a máscara, chama `Update(scene)` no início de `Rebuild` e passa `mask.Solid` ao cache. A máscara só existe junto com o cache; o construtor recusa máscara sem cache. O caminho de referência continua com `scene.Solid` e segue como oráculo.
- **`V9LabGame`:** novas flags; `--v9-occupancy-mask` junto com `--v9-no-source-cache` é recusado. A bancada passou a registrar a atualização e a reconstrução da máscara.
- **Não mudaram:**
  - `V9LightMath`: DDA, política de quinas, seção iluminada, absorção, incidência, tone mapping;
  - `V9LabSettings`: raios, potências, cores;
  - coordenadas, shader, cena e geometria, resolução do campo, HalfVector4, upload e assets.
- **Prova de que não mudaram:**
  - as linhas 1–94 de `V9LightField.cs` estão idênticas ao estado anterior;
  - `V9LabScene.cs`, `V9LabChecks.cs` e `V9LabEvidence.cs` estão byte a byte iguais;
  - o manifesto dos 197 arquivos protegidos continua inalterado.
- **Não implementado, conforme pedido:** bounding box por raio, paralelização, dirty regions, cache de DDA, GPU compute, redução de resolução e atualização temporal.

## 4. Construção e invalidação

| Evento | Máscara |
|---|---|
| Primeira reconstrução do campo no processo | Construída: 0,20–0,24 ms a frio na bancada (inclui JIT de `scene.Solid`); ~0,009 ms quando `scene.Solid` já está compilado |
| Fechar/abrir entrada, remover/restaurar pilar | Reconstrução completa; medida real 0,008–0,014 ms |
| Reset para a geometria já vigente | Não reconstrói (revisão inalterada) |
| Reset que restaura entrada ou pilar | Reconstrói (revisão mudou) |
| Abrir e fechar no mesmo passo (caso `sealed-five`) | Reconstrói: a revisão mudou mesmo com a geometria final igual. É conservador. |
| Mover, adicionar, remover ou inverter fontes | Não reconstrói |
| Câmera, zoom, personagem | Nem o campo é reconstruído |
| Troca da instância da cena | Reconstrói |

Não há dirty regions: cada reconstrução refaz as 2.040 células.

## 5. Equivalência exata

`--v9-cache-verify --v9-occupancy-mask` é a extensão mínima do verificador. Sem a nova opção, ele continua sendo o verificador de dois caminhos. Com ela, roda referência, cache e cache+máscara no mesmo processo: os 19 estados originais mais os 24 passos da sequência. O reset invalida apenas o cache do campo ativo; o verificador repete o mesmo evento no cache mascarado, para que os dois caches tenham históricos idênticos.

Resultados nos 43/43 estados, sem tolerância alguma:

- **Referência × cache:** zero diferenças, como antes.
- **Cache × cache+máscara:** zero divergências em Energy float32 bit a bit (391.680 componentes por estado), em HalfVector4 (130.560 texels) e nos pixels RGBA finais lidos da GPU (816.000).
- **Fontes recalculadas:** iguais entre os dois caches e às contagens esperadas.
- **Reconstruções da máscara:** 14, exatamente nos passos em que algum tile mudou. Zero nos passos declarados explicitamente como sem reconstrução: mover fonte (02, 03, 05, 16), câmera (10), zoom (11) e personagem (12).

Verificações específicas `scene.Solid` × máscara, repetidas em cada um dos 43 estados:

1. **Grade exaustiva:** toda célula inteira em `[-64,124)×[-64,98)`, uma margem maior que o período de wrap de 60 colunas. São 30.456 células por estado, 1.309.608 no total. Somam-se 400 pares de inteiros extremos por estado (`int.MinValue`, −120, −60, 60, 120, `int.MaxValue` etc.). Zero divergências.
2. **Raios de borda:** 228 pontos sobre, logo dentro e logo fora das bordas do mapa, nos cantos e arestas da abertura (que coincide com a linha y=48), no pilar e nas fontes fixas.
   - São 51.984 raios por estado, 2.235.312 no total; `Visibility` deu bit a bit o mesmo com as duas consultas.
   - Esses raios geraram 1.194.107 consultas fora da grade (domínio x ∈ [−1,61], y ∈ [−1,35]), que exercitaram a regra de fronteira.
3. **Sonda do DDA real:** o campo inteiro foi avaliado com uma consulta que confere máscara × `scene.Solid` no momento de cada chamada.
   - Foram 230.431.571 consultas, com zero divergências e zero fora da grade.
   - A Energy resultante é bit a bit igual à da referência.

Capturas:

- 26/26 verificações em cada um dos três conjuntos: referência, cache e máscara.
- Os 152 PNGs históricos são idênticos byte a byte aos três conjuntos.
- 176 PNGs idênticos entre cache e máscara, e entre cache e referência.
- A captura convencional com `--v9-occupancy-mask` passou 26/26 e reproduz os 152 PNGs.

Testes: 28 testes CPU aprovados em todos os processos; V8 direta 107/107 e ambiente 154/154.

## 6. Benchmark

Mesmas condições da bancada vigente:

- Release, 1200×680, zoom 2,5, cinco fontes de céu;
- 30 quadros de aquecimento e 120 medidos por caso;
- VSync e passo fixo desligados, sem captura nem readback;
- GTX 750, Reach/DesktopGL, MonoGame 3.8.5.1.

Foram cinco execuções de cada caminho, em ordem ABBA (c1 m1 m2 c2 c3 m3 m4 c4 c5 m5). Cada valor é a mediana das cinco medianas por execução; a faixa vai da menor à maior mediana por execução. A janela esteve em foco em 120/120 amostras, em todos os casos de todas as execuções. Todos os tempos são de CPU, não de GPU.

| Caso | Cache: Draw mediana (faixa) / P95 | Cache+máscara: Draw mediana (faixa) / P95 | Δ mediana |
|---|---:|---:|---:|
| 1 tocha, parado | 0,77 (0,75–0,77) / 0,97 | 0,77 (0,76–0,78) / 0,97 | +0,00 |
| 1 tocha, câmera móvel | 0,76 (0,73–0,78) / 1,00 | 0,77 (0,75–0,78) / 0,97 | +0,01 |
| 5 tochas, parado | 0,75 (0,70–0,77) / 0,92 | 0,76 (0,74–0,78) / 0,92 | +0,01 |
| 5 tochas, câmera móvel | 0,77 (0,70–0,77) / 0,93 | 0,76 (0,74–0,77) / 0,95 | −0,01 |
| **1 tocha movendo** | **13,34 (13,27–13,46) / 14,03** | **11,78 (11,77–11,84) / 12,47** | **−1,57 (−11,7%)** |
| **5 presentes, primeira movendo** | **14,04 (13,88–14,11) / 14,61** | **12,48 (12,47–12,52) / 13,10** | **−1,56 (−11,1%)** |

| Etapa — mediana CPU ms | 1 móvel, cache | 1 móvel, máscara | 5 presentes, cache | 5 presentes, máscara |
|---|---:|---:|---:|---:|
| Evaluate/Visibility (faixa) | 9,598 (9,47–9,66) | **8,010** (7,98–8,06) | 9,586 (9,45–9,67) | **8,021** (7,99–8,03) |
| Recomposição do cache | 1,046 | 1,046 | 1,689 | 1,697 |
| Pico + HalfVector4 + SetData | 1,783 | 1,774 | 1,801 | 1,793 |
| Atualização/reconstrução da máscara | — | 0,0002 (0 reconstruções) | — | 0,0002 (0 reconstruções) |
| Campo completo | 12,474 | 10,900 | 13,152 | 11,592 |
| Draw total | 13,34 | 11,78 | 14,04 | 12,48 |
| Fontes recalculadas por quadro | 1 | 1 | 1 | 1 |

- Evaluate/Visibility caiu 16,5% e 16,3%, e as faixas das cinco execuções não se sobrepõem.
- Recomposição e HalfVector4+upload não mudaram, como esperado.
- Nos casos parados e com câmera, as diferenças (até 0,01 ms) estão dentro das faixas: sem regressão material.
- A máscara não foi reconstruída em nenhuma amostra. Em cada processo ela é construída uma única vez, no primeiro quadro, fora da medição.
- A referência antiga (`--v9-no-source-cache`) rodou uma vez, para confirmar a flag e dar continuidade: 56,06 e 90,36 ms nos casos móveis, contra 56,14 e 88,08 ms antes.

[INFERÊNCIA] Cerca de 1,6 dos ~9,6 ms de Evaluate/Visibility vinham do caminho de consulta: limites de `Solid`, `InBounds`, módulo do wrap, acesso à matriz 2D e comparação do tipo. O restante (aritmética do DDA, chamada do delegate, custo por pixel de `Evaluate`) continua dominante. Não houve profiling por método que separe essas partes.

## 7. Custo da máscara

- Reconstrução completa aquecida, 2.000 repetições fora de qualquer quadro: mediana 0,0072 ms, P95 0,0091 ms, máximo 0,0292 ms.
- Reconstruções reais por mudança de geometria, durante a verificação: 0,008–0,014 ms.
- Primeira construção de cada processo da bancada: 0,196–0,239 ms, incluindo o JIT de `scene.Solid`.
- Checagem de revisão a cada reconstrução do campo: ~0,0002 ms.
- Uma invalidação de geometria já recalcula todas as fontes (~55–110 ms). A máscara acrescenta ~0,01 ms e ainda barateia esse recálculo. Exemplos indicativos da verificação: base 69,3 → 58,4 ms; cinco tochas 109,5 → 92,3 ms. São amostras únicas intercaladas com readback, não benchmark.

## 8. Memória

- 2.040 bytes de dados (um `bool` por célula, 60×34), mais o cabeçalho do array e um objeto pequeno. Zero recursos GPU.
- É desprezível diante da memória do cache (8,96 / 14,94 MiB).
- Não cresce com o número de fontes, só com o número de células da grade. Um mundo de gameplay com wrap exigiria outra política de bordas, fora do escopo.

## 9. Complexidade

- Código de execução:
  - classe nova de 49 linhas;
  - 2 linhas alteradas no cache (parâmetro e `??=`);
  - ~23 linhas no campo;
  - ~11 linhas no jogo (flags e colunas da bancada).
- Diagnóstico: ~61 linhas no verificador existente e 151 linhas no arquivo novo de checagens.
- Invariante a manter: toda mudança de geometria precisa alterar `TileRevision` (já era o contrato do cache) e chegar ao campo marcando dirty. A máscara é específica da grade finita 60×34.

## 10. O ganho justifica mantê-la?

**Sim, como opção.**

- O ganho é modesto: ~1,6 ms por quadro com uma fonte móvel, ~12% do Draw e ~16% de Evaluate/Visibility.
- Ele é consistente entre execuções, não muda nenhum resultado, ocupa 2 KB e custa ~0,01 ms por reconstrução.
- Com uma tocha móvel, o Draw cai para ~11,8 ms e o P95 para ~12,5 ms: mais folga dentro dos 16,7 ms de 60 Hz.
- Não muda a ordem de grandeza: Evaluate/Visibility continua sendo a maior parte (~8,0 de ~11,8 ms).

Promover a máscara a padrão do laboratório é decisão do usuário, pelo mesmo critério usado no cache. Esta entrega a mantém desligada por padrão.

## Arquivos, comandos e evidências

Código, todo não rastreado:

- novos: `V9OccupancyMask.cs` e `V9LabGame.OccupancyMaskChecks.cs`;
- alterados: `V9LightField.cs`, `V9SourceCache.cs`, `V9LabGame.cs` e `V9LabGame.SourceCacheChecks.cs`, todos em `Nyvorn/Source/Engine/Graphics/LightingV9Lab/`.

O diff completo contra o estado pós-cache está em `screenshots/v9-lab/occupancy-mask/code-changes.diff`, porque o Git não o mostra. Os diffs V8 preexistentes continuam pertencendo ao trabalho anterior.

A partir de `C:\dev\Nyvorn-Reborn`, sempre com uma pasta nova em `--v9-output`:

```powershell
dotnet build .\Nyvorn\Nyvorn.csproj -c Release --no-restore

# Referência x cache x cache+máscara: 19 estados + 24 passos, com as checagens de ocupação
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-cache-verify --v9-occupancy-mask --v9-output C:\dev\Nyvorn-Reborn\screenshots\v9-lab\occupancy-mask\equivalence
& .\Docs\LightingV9\Verify-V9Captures.ps1 -CaptureDirectory C:\dev\Nyvorn-Reborn\screenshots\v9-lab\occupancy-mask\equivalence\mask

# Bancada: cache (padrão), cache+máscara e referência antiga explícita
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-bench --v9-output <pasta nova>
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-occupancy-mask --v9-bench --v9-output <pasta nova>
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-no-source-cache --v9-bench --v9-output <pasta nova>

# Interativo: padrão (cache), com a máscara, ou a referência antiga
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-occupancy-mask
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-lab --v9-no-source-cache
```

Evidências em `screenshots/v9-lab/occupancy-mask/`:

- `equivalence/`: `equivalence.json`, `equivalence-summary.txt`, `occupancy-mask.json`, `checks.txt` e as pastas `reference/`, `cache/` e `mask/`, cada uma com seu `capture-checks.txt`;
- `capture-default/`, `capture-no-source-cache/` e `capture-occupancy-mask/`;
- `bench-cache-1..5/`, `bench-mask-1..5/` e `bench-no-source-cache/`;
- `benchmark-comparison.json`, `historical-comparison.txt` e `protected-files-check.txt`;
- `regression-v8-direct/`, `regression-v8-ambient/` e `code-changes.diff`.

As evidências anteriores (`final/`, `source-cache/`) foram preservadas. Em `source-cache/` só entrou a cópia do manifesto.
