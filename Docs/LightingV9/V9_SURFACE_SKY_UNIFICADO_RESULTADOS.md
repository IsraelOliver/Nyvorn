# V9 Gameplay Probe — uma só definição de céu para a Surface, 24/09/2026

**Resultado.**

- A hipótese se confirmou em parte: o sistema natural do V9 já distingue ar selado de ar aberto que a luz alcança. A soma das aberturas é exatamente zero no ar selado e maior que zero onde uma abertura enxerga o ar.
- Isso virou a fonte única da verdade para a Surface/Space, sem flood-fill e sem parâmetro novo. A apresentação do céu e a resposta do foreground passam a ler a mesma função.
- Túnel vedado e abrigo selado: sem céu pintado dentro e sem luz nas faces internas.
- Exterior, abrigo aberto e a parte da fenda que a luz alcança: céu e faces acesas.
- **Limite:** ar aberto que a luz natural não alcança (a metade de baixo da fenda inclinada) é tratado como selado. Para distinguir isso seria preciso conectividade (proposta, não implementada).
- Sem commit, sem promoção.

Etiquetas: [FATO] código/dados; [MEDIÇÃO] número medido; [OBSERVAÇÃO VISUAL] o que vi nas imagens (a sua avaliação está pendente).

## 1. Investigação: o que o natural V9 já sabe

- [FATO] **A região exposta O** (`V9ProbeExposure`) não serve sozinha: a fenda abaixo da borda não está em O (fica abaixo do envelope).
- [FATO] **A soma das aberturas** (luzes da borda de O avaliadas pelo DDA, que o campo já guarda em cache) serve:
  - [MEDIÇÃO] é zero exato no ar selado. Túnel vedado: 0 de 19.456 px do caminho; abrigo selado: 0 de 3.072 px;
  - é maior que zero onde uma abertura enxerga o ar em linha reta, até 196 px.
- [FATO] **Regra aplicada:** um tile `OpenAtmosphere` do Space/Surface é céu visível se a soma das aberturas no centro dele for > 0. Os dados vêm dos caches existentes; não há campo novo.

## 2. Uma definição, dois usos

- [FATO] `V9ProbeField.SkyVisibleAt(x, y)` vale:
  - 1 em O;
  - o peso da abertura do Shallow (inalterado);
  - 1 no `OpenAtmosphere` do Space/Surface alcançado pelas aberturas;
  - 0 no resto: foreground, fundo, ar selado ou não alcançado da Surface, Cavern/Deep.
- [FATO] **Apresentação:** cobertura preta = `1 − SkyVisibleAt`, abaixo do primeiro sólido da coluna. O laço começa no topo da vista, porque linhas acima dela nunca aparecem.
- [FATO] **Resposta do foreground:** o mesmo peso alimenta o vizinho de céu visível do termo de céu aberto. A seção terminal, a profundidade, a absorção, a incidência e a cor ficaram intactas.
- [FATO] **Glow do fundo:** inalterado (sementes em O e nas aberturas do Shallow).
- [FATO] **Quando recalcula:** a máscara `surfaceSky` é refeita quando a janela muda, quando a classificação muda ou quando alguma abertura é recalculada. O termo direto é refeito em 3×3 em volta dos tiles que mudaram.
- [MEDIÇÃO] A verificação recalcula a máscara do zero com a solidez do mundo: 0 divergências em todos os estados.

## 3. Casos

Captura com 37 estados: **413 PASS, 0 FAIL, 1 SKIP** (a abertura lateral natural, que não existe).

| Caso | Céu visível no ar | Resposta do foreground |
|---|---|---|
| 1. Superfície aberta | sim (O) | topo como antes; bordas da fenda próxima onde a luz alcança |
| 2. Fenda inclinada da entrada | [MEDIÇÃO] 109 de 123 tiles do caminho até 30 passos da boca; a metade de baixo da fenda, não | [MEDIÇÃO] 817 texels nas faces voltadas para o ar alcançado |
| 3. Túnel da entrada aberto | idem | idem |
| 4. Mesmo túnel vedado | [MEDIÇÃO] 0 de 628 tiles | [MEDIÇÃO] 0 texels nas faces internas |
| 5. Abrigo aberto | [MEDIÇÃO] 52 de 52 tiles | [MEDIÇÃO] 477 texels nas faces internas |
| 6. Abrigo selado | [MEDIÇÃO] 0 de 48 tiles | [MEDIÇÃO] 0 texels nas faces internas |
| 7. Abertura do Shallow | inalterada (peso e fade) | inalterada |
| 8. Sala selada do Shallow | nenhum | 0 |
| 9. Buraco de fundo na Cavern | coberto | 0 |

- [FATO] **Como se mede a face interna.** São os texels até 2 px da face voltada para o ar. A luz de fora não chega ali: a seção terminal alcança no máximo 5,5 px numa parede de 8 px.
- [MEDIÇÃO] Um primeiro teste com uma caixa ao redor do abrigo contou 96 texels. Eram faces em volta de um nicho de ar aberto do lado de fora, que a luz alcança: comportamento correto, erro do teste.

## 4. Verificação contra o estado anterior (todo ar da Surface = céu)

- [MEDIÇÃO] **Tochas e glow:** SHA-256 idênticos nos 37 estados.
- [MEDIÇÃO] **Albedo:** só muda onde a cobertura volta a esconder o céu pintado dentro de ar da Surface não alcançado (selado, ou a parte de baixo da fenda). Todo pixel alterado agora é preto e fica sobre esse ar: **0 inexplicados.**
  - Nada mais do albedo mudou, e o que mudou é o objetivo desta entrega.
  - A caixa do personagem fica fora da comparação, porque ele segue o cursor real do mouse.
- [MEDIÇÃO] **Final:** todo pixel alterado é um desses pixels de albedo ou um texel cuja resposta do foreground mudou. **0 inexplicados.**
- [MEDIÇÃO] **Profundidade terminal:** ≤ 5,5 px em todos os estados.
- [FATO] Nenhum parâmetro artístico foi alterado.
- [MEDIÇÃO] **Shallow, Cavern e as cavernas com tochas:** 0 px mudaram.

## 5. Limite e proposta

- [OBSERVAÇÃO VISUAL] **Na fenda inclinada**, só a metade de cima fica aberta ao céu: céu pintado atrás e bordas acesas. A metade de baixo volta a ficar preta, com paredes sem resposta.
  - Motivo: a luz natural do V9 é direta (linha de visada, até 196 px), e a curva da fenda fica fora do alcance das aberturas da boca.
  - Para o sistema natural, esse ar é tão escuro quanto um selado, então a informação existente não distingue "aberto mas não alcançado" de "selado".
- **Proposta (fallback, não implementada):** conectividade. O `OpenAtmosphere` da Surface é céu se estiver ligado pelo ar sem fundo a O, com uma busca em largura limitada à janela a partir das células de O.
  - A fenda inteira passaria a mostrar céu e a ter bordas acesas; selados continuariam cobertos.
  - Custo esperado: uma busca por ~8 mil tiles na reconstrução da janela ou em mudança de geometria.

## 6. Desempenho

- [MEDIÇÃO] A maioria dos cenários ficou dentro do ruído.
- [MEDIÇÃO] Cenários com troca de geometria no Shallow: +1 a 2 ms, porque a máscara é refeita com o glow.
- Pastas: `bench/` e, para o estado anterior, `foreground-sky/bench/`.

## 7. Evidências e arquivos

`screenshots/v9-gameplay-probe/surface-sky-unified/`:

- `capture/`: por estado, `_sky-visible.png` (a definição única) além dos diagnósticos anteriores;
- `compare/*_board.png`: por caso, classificação, natural V9, céu visível e resposta do foreground, mais o final antes e depois;
- `iterations/` e `bench/`.

**Código alterado:**

- `V9ProbeField.cs`: `SkyVisibleAt`, máscara `surfaceSky`, atualização e verificação.
- `PlayingState.V9Probe.cs`: a cobertura usa `SkyVisibleAt`, com o laço a partir do topo da vista.
- `PlayingState.V9ProbeCapture.cs`: mapa e checagens.
