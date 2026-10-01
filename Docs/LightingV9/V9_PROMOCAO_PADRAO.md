# V9 — promoção a iluminação padrão, 25/09/2026

**Resultado:** o V9 é a iluminação padrão do gameplay. `dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll`, sem flag, joga com V9. O visual aprovado foi preservado bit a bit: a prova está abaixo. V7 e V8 continuam disponíveis por seleção explícita. Não houve commit, alteração de save ou recalibração.

## O V9 que virou padrão

É o estado aprovado no Gameplay Probe com Sky Backplane, sem mudança de valores.

- **Céu:** FG e BG vazios mostram o céu. A força é 1 na Surface e em quase todo o Shallow e cai por smoothstep nos últimos 10 tiles do Shallow (80 px). É 0 na Cavern e na Deep.
- **BG:** bloqueia o céu direto e recebe o Sky Glow (alcance de 40 px, pico de 0,35 × céu aberto).
- **FG:** bloqueia e recebe só a seção terminal, de até 6 px (0,25 do glow vizinho).
- **Aberturas do Shallow:** podem vazar até 40 px para dentro da Cavern.
- **Tochas:** raio de 144 px, potência 4, RGB `(1; 0,49; 0,19)`, DDA, cache por fonte e occupancy.
- **Composição:** fundo de caverna iluminado (resposta 0,14) com o fade existente. No Backplane não há máscara preta por tile. A borda do céu direto na rocha tem suavização de 2 px e filtro 3×3.

Parte desse estado veio de ajustes feitos em 24/09 à tarde, depois do último relatório: a curva de 10 tiles, o parallax de caverna, o vazamento de 40 px, a máscara por alpha, as suavizações, o worldgen (Cavern/Deep sem BG, fendas do Shallow fechando 20 tiles antes da Cavern) e o BG no minimapa. O build desse estado (DLL de 24/09, 17:31) foi a referência "antes" das comparações.

## Seleção

**Antes:** o `PlayingState` usava `lightingMode = V7`. O `--lighting-v8` ativava o V8, e o V9 só existia com `--lighting-v9-gameplay-probe`.

**Agora:** a seleção fica em `LightingV9Probe/V9Gameplay.cs`. O V9 só fica desligado se outro pipeline for pedido explicitamente.

| Como iniciar | Iluminação |
|---|---|
| sem flag | **V9** |
| `--lighting-v7` (novo; o V7 não tinha flag por ser o padrão) | V7; F9 alterna o V6 legado e F4 alterna o V8, como antes |
| `--lighting-v8` | V8; F4 volta ao V7 |
| `NYVORN_LIGHTING=legacy` | V6 legado |
| `--v7-gameplay-smoke`, `--lighting-toggle-bench`, `--lighting-sky-compare`, `--lighting-composition-compare`, `--v8-*`, `NYVORN_V7_AUTOSHOT`, `NYVORN_V7_VIEW` | V7/V8, como antes |
| `--lighting-v9-gameplay-probe` | V9 + instrumentação |
| `--lighting-v9-lab` | laboratório isolado (sem mudança) |

Com V9 ativo, o F4 não troca de pipeline: o V9 roda sozinho, como no probe. O HUD mostra `LIGHTING: V9`, e o título da janela mostra `Nyvorn — lighting V9`.

## Um só núcleo

- **`PlayingState.V9.cs`** (novo) é o V9 de produção:
  - funções novas: `InitializeV9`, `DrawV9Gameplay` (HUD compacto), `RenderV9Frame` (campo + cena) e `PresentV9Frame` (frame, overlay e HUD do jogo);
  - funções movidas sem alteração do arquivo do probe: `DrawV9Scene`, `V9Receivers`, `DrawV9Backdrop`, `ComposeV9Layer` e `EnsureV9Targets`.
- **`V9Gameplay.CreateField`** é a única configuração: modelo, glow, resposta do FG ao céu e camadas. O probe parte dela.
- **`PlayingState.V9Probe.cs`** ficou só com a instrumentação e chama `RenderV9Frame`/`PresentV9Frame`.
- **No probe:**
  - o padrão agora é o Backplane (`--v9-probe-sky-backplane` continua aceito, mas é redundante);
  - o modelo anterior (exposição + aberturas) está em `--v9-probe-exposure`;
  - `--v9-probe-glow-sun` exige `--v9-probe-exposure`.
- **Parâmetros:** continuam onde estavam — `V9LabSettings` (tochas, céu, seção terminal, resposta do BG) e `V9ProbeBackplane` (pico do glow, resposta do FG, `DepthTransitionTiles = 10`, exposto como constante com o mesmo valor). O gameplay e o probe leem os mesmos valores.

**Só diagnóstico** (não entra no gameplay normal):
- ambiente: sessão transitória com seed fixa, 1280×720, meio-dia fixo, itens de teste, busca de cenas;
- controles: F5/F6/F7/F12;
- medição: capturas, `Verify`, bench, HUD extenso e flags `--v9-probe-*`;
- novo: `--v9-gameplay-smoke`, que roda o caminho normal numa sessão transitória, com verificação e tempos.

## Saves

O formato não mudou e não há migração. O V9 deriva tudo do mundo: `TileRevision` para FG/BG, e a lista de tochas. O jogo normal salva como antes (autosave e ao sair). Só as sessões transitórias (probe e smoke) não salvam; sair delas pelo menu de pausa fecha o processo.

## Testes

Evidências em `screenshots/v9-promotion/`, ignoradas pelo Git. `baseline-bin/` é a cópia do build aprovado.

| Teste | Resultado |
|---|---|
| Build Release | OK; só os dois CS0649 que já existiam |
| Probe, build aprovado × novo (Backplane; novo sem flag de modelo) | 34/34 estados idênticos: hashes float total/natural/artificial/glow, 306 PNGs de diagnóstico byte a byte, 68 frames final/albedo com 0 px diferentes fora do personagem, checks linha a linha iguais |
| Probe, modelo antigo (`--v9-probe-exposure`) × padrão antigo do build aprovado | 40/40 estados idênticos; 442 PASS |
| Smoke do caminho normal, com e sem VSync | 24/24 PASS em cada |
| Inicialização sem flag / `--lighting-v7` / `--lighting-v8` / probe | títulos `lighting V9` / `V7` / `V8` / `V9 probe`; listagem de `%LOCALAPPDATA%\Nyvorn` inalterada |
| V7 `--v7-gameplay-smoke` e V8 `--lighting-v8 --v8-gameplay-capture` | ambos exit 0, com capturas |
| Validação das flags novas | `--lighting-v7` junto de `--lighting-v8`, `--v9-probe-glow-sun` sem exposure, dois modelos de probe e smoke junto de `--lighting-v8` são recusados com mensagem |

Não abri nenhum mundo real pelo menu, para não tocar nos saves. O `PlayingState` criado sem flags de iluminação foi exercitado pelo smoke.

Os checks do probe dão 213 PASS, 35 FAIL e 2 SKIP, **iguais no build aprovado e no novo**. Os FAIL já existiam e têm duas causas:
- a checagem "curva contínua Surface→Cavern" ficou obsoleta com a curva de 10 tiles (33 estados);
- o vazamento aprovado de 40 px acende texels da Cavern em `shallow-bottom`.

As checagens de reconstrução bit a bit do campo passam em todos os estados.

O smoke do caminho normal roda em 1920×1080, com o relógio correndo e a câmera e o zoom do jogo. Ele confere:
- V9 ativo sem flag, com a configuração oficial;
- em 10 passos, campo idêntico ao `Verify` (reavaliação do zero sobre a solidez do mundo);
- céu na Surface;
- 5 recentros de câmera;
- tochas: +3 e −2;
- FG colocado altera o campo;
- BG colocado esconde o céu direto (0 de 2.240 texels);
- remover FG/BG devolve o campo bit a bit;
- no Shallow: céu, glow e resposta do FG;
- na Cavern: nenhuma luz natural a partir de 8 tiles abaixo do início;
- nenhum save escrito.

## Performance (sanity check)

GTX 750, CPU em ms, GPU não medida.

**Caminho normal** (smoke em 1920×1080, janela do campo 1224×736, `--uncapped-frames`):

| Caso | Frame (mediana / p95) | Frame da ação |
|---|---|---|
| Surface parada | 3,31 / 4,43 | primeiro frame: campo 126 |
| Surface andando (câmera) | 3,22 / 5,05 | 5 recentros: cerca de 62 cada |
| 3 tochas na Surface | 3,23 / 3,98 | adicionar 3: 29; remover 2: 5 |
| Colocar FG / colocar BG / remover ambos | 3,21 / 4,06 | 69 / 63 / 69 |
| Shallow / Shallow com tocha | 3,42 / 3,41 | adicionar a tocha: 10 |
| Cavern | 4,62 / 5,38 | — |

Com VSync, o normal do jogo, fica em 16,67 ms (60 fps) nos casos estáveis, com os mesmos picos.

**Probe, build aprovado × novo** (1280×720, janela 904×560, uma execução cada): as medianas coincidem dentro do ruído.

| Caso | Frame (aprovado → novo) |
|---|---|
| Surface parada | 1,51 → 1,54 |
| 5 tochas | 1,71 → 1,80 |
| Tocha móvel | 10,78 → 10,53 |
| Alternar tile na caverna | 77,3 → 76,8 |
| Alternar FG no Shallow | 46,3 → 47,3 |
| Alternar BG no Shallow | 46,1 → 47,6 |

**Conclusão:** a integração não acrescenta custo. O frame estável é barato. Os picos de recentro e edição são a reconstrução da janela inteira do Backplane, já documentada no probe (35 a 42 ms). No jogo normal eles ficam maiores porque a janela em 1080p é 1,8× a do probe.

## Pendências conhecidas

- **Dia e noite.** O V9 não tem ciclo: a luz natural é sempre a do meio-dia aprovado. O probe fixa 12:00. No jogo normal o relógio não foi congelado, porque isso mudaria o gameplay e o save, então só o céu pintado acompanha a hora. À noite, o céu fica escuro com o terreno iluminado como de dia. Falta decidir.
- **Custo de reconstrução.** Recentro de câmera e edições de FG/BG reconstroem a janela inteira do Backplane (60 a 70 ms em 1080p). Esse custo já era conhecido e não foi otimizado aqui.
- **Visuais combinados para depois:** máscara/artefato preto residual, suavização claro→escuro e quinas quadradas.
- **Camadas do caminho V7 que o V9 não desenha** (como já acontecia no V8 e no probe): chuva frontal, overlay noturno, brilho do sol, umidade, halo e campo do Tissue, overlay de foco de interior, emissivos do V7 (cogumelos) e HUD de camada/parallax.
- **Fundo de montanhas na Cavern.** Com o jogador no Shallow, buracos sem FG e sem BG na Cavern mostram o fundo de montanhas sem iluminação, até a troca para o parallax de caverna. É o estado aprovado, idêntico no probe.
- **Checks desatualizados do probe:** as duas causas descritas em [Testes](#testes).
- **Nomes históricos.** As classes de produção continuam em `LightingV9Probe` com prefixo `V9Probe`. Renomear fica como limpeza futura.

## Arquivos

- **Novos:**
  - `Nyvorn/Source/Engine/Graphics/LightingV9Probe/V9Gameplay.cs`
  - `Nyvorn/Source/Game/States/PlayingState.V9.cs`
  - `Nyvorn/Source/Game/States/PlayingState.V9Smoke.cs`
  - `Docs/LightingV9/Verify-V9Promotion.ps1`
  - este documento
- **Alterados:**
  - `Nyvorn/Program.cs`
  - `Nyvorn/Game1.cs`
  - `PlayingState.cs`
  - `PlayingState.LightingToggle.cs`
  - `PauseMenuState.cs`
  - `PlayingState.V9Probe.cs`
  - `PlayingState.V9ProbeShallowGlow.cs` (`V9BaseGlow`)
  - `V9ProbeOptions.cs`
  - `V9ProbeBackplane.cs` (constante e comentários)
  - `V9_LAB_ESTADO.md`
  - `V9_HANDOFF_CLAUDE.md`

O diff exato em relação ao checkout anterior está em `screenshots/v9-promotion/code-changes.diff`.

## Executar

```powershell
dotnet build .\Nyvorn\Nyvorn.csproj -c Release --no-restore
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll                                   # jogo normal, V9
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe      # probe (mundo transitório)
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-capture --v9-output <pasta>
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-exposure   # modelo anterior
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --v9-gameplay-smoke --v9-output <pasta> [--uncapped-frames]
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v7                     # V7 (F9: V6, F4: V8)
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v8                     # V8
.\Docs\LightingV9\Verify-V9Promotion.ps1 -Reference <captura A> -Candidate <captura B>
```
