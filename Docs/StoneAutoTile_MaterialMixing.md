# Autotile de Pedra e Mistura de Material (resumo da sessão)

Este documento resume as decisões e o trabalho feito numa sessão de chat sobre o sistema de autotile da pedra, para servir de contexto caso a conversa original não esteja disponível.

## Contexto

O autotile de tiles (terra, pedra, madeira, areia) é resolvido em [`WorldMap.cs`](../Nyvorn/Source/Gameplay/World/WorldMap.cs). Terra, grama e madeira compartilham o mesmo algoritmo (`GetDirtAutoTileSourceRectangle`), que escolhe a célula da spritesheet com base em quantos dos 4 vizinhos ortogonais (cima/direita/baixo/esquerda) estão conectados (0 a 4), usando `GetAutoTileSheetCell(coluna, linha)` sobre células de 8x8px com 1px de espaçamento.

A pedra ganhou um algoritmo próprio (`GetStoneAutoTileSourceRectangle`) porque, além da conectividade normal, ela também precisa reagir a **qual material** está do outro lado (ex: pedra encostando em terra), coisa que terra/grama/madeira não fazem.

## Tabela nova da spritesheet de pedra

A spritesheet de pedra foi ampliada de 7 colunas x 7 linhas para 16 colunas x 11 linhas. As primeiras 7 colunas (0-6) mantêm a lógica original de conectividade (igual terra/madeira). As colunas/linhas novas (a partir da coluna 0, linha 5) cobrem os casos de mistura com Terra:

| Categoria | Onde fica | Condição |
|---|---|---|
| Variantes 2→3 | cols 0-8, linhas 0-4 | Isolado, pontas e diagonais de 2 conexões passaram de 2 para 3 variantes visuais. |
| Canto interno duplo | cols 6-11, linhas 1-2 | 4 conexões com **dois** cantos diagonais vazios do mesmo lado (antes só tratava 1 canto). |
| `ter-X` (material misto, 3 conexões) | cols 13-15, linhas 0-3 | Falta um lado; o lado **oposto** ao que falta é Terra, os outros dois lados permanecem Pedra. |
| Perpendicular-terra (3 conexões) | cols 4-5, linhas 5-10 | Falta esquerda ou direita; um dos lados **perpendiculares** (cima ou baixo) é Terra, o oposto continua Pedra. |
| Ponta-terra (1 conexão) | col 6, linhas 5-10 | Só um lado conectado (baixo ou cima), e esse vizinho é Terra em vez de Pedra. |
| 2 conexões dividido | col 7, linhas 5-10 | Corredor vertical cima+baixo onde um lado é Pedra e o outro é Terra. |
| Diagonal-terra (4 conexões) | cols 0-1, linhas 5-10 | Os 4 lados ortogonais são Pedra, mas um canto diagonal específico é Terra. |
| Divisão ortogonal (4 conexões) | cols 2-3, linhas 5-10 | Os 4 lados cobertos, mas dois lados **adjacentes** (ex: esquerda+cima) são Terra e os outros dois Pedra. |
| 1 lado = Terra (4 conexões) | cols 8-10, linhas 5-9 | Os 4 lados cobertos, só 1 lado é Terra e os outros 3 Pedra. |
| 2 lados opostos = Terra (4 conexões) | col 10 (linhas 7-9) + cols 8-10 (linha 10) | Os 4 lados cobertos, dois lados **opostos** (esq+dir ou cima+baixo) são Terra. |
| 1 lado = Pedra (4 conexões, espelho da anterior) | cols 11-12, linhas 5-10 | Os 4 lados cobertos, só 1 lado é Pedra e os outros 3 Terra. |

**Pendente**: o usuário mencionou que ainda faltam mais duas linhas na tabela (não detalhadas nesta sessão) e que `ponta-esq`/`ponta-dir` com vizinho Terra ainda não têm célula própria (só `ponta-baixo`/`ponta-cima` têm variante com Terra hoje).

Também foi corrigido um bug onde `ponta-esq` e `ponta-dir` estavam com a lógica de esquerda/direita invertida (tanto na função compartilhada de terra quanto na de pedra).

## Refatoração: motor de regras genérico

Como a lista de casos de mistura ia crescendo (e os mesmos blocos de "minério" e outros materiais vão seguir a mesma lógica no futuro), o código foi reestruturado para não precisar de uma função nova por combinação. Hoje `GetStoneAutoTileSourceRectangle` só chama:

```csharp
EvaluateAutoTileMixRules(TileType.Stone, x, y, BaseAutoTileMixRules)
```

- `NeighborState`: `Any`, `Self` (mesmo material do tile), `Other` (qualquer material sólido diferente do self — hoje sempre acaba sendo Terra na prática, mas não está fixado nisso), `Empty` (não sólido), `Solid` (Self ou Other, não importa qual).
- `AutoTileMixRule`: descreve o estado esperado dos 4 lados ortogonais + 4 diagonais, a célula de destino (coluna, linha), quantas variantes visuais existem e se elas se espalham em linha ou coluna.
- `BaseAutoTileMixRules`: array estático com ~48 regras, avaliadas em ordem (a primeira que bater vence). Essa lista já reproduz **todas** as categorias da tabela acima.

Decisões de arquitetura confirmadas para o futuro:
- Minérios/outros blocos "duros" vão reusar a **mesma** `BaseAutoTileMixRules` (mesma disposição de spritesheet), só trocando o `TileType` e a textura.
- Blocos da família "grama" (grama normal, corrompida, de outro bioma) vão precisar de uma folha **maior**, com linhas extras. A ideia é que eles usem `BaseAutoTileMixRules` + um array próprio de regras extras concatenado, sem duplicar a tabela toda.
- "Other" pode, no futuro, representar qualquer material (não só Terra) — ex: Pedra encostando em Minério de Ferro. Isso já é suportado pela arquitetura (o "Other" é resolvido dinamicamente por tile, não fixado em Terra no código).
- Por enquanto, **só a Pedra** usa esse motor novo. Terra/Grama/Madeira continuam no `GetDirtAutoTileSourceRectangle` antigo, sem mudanças.

## Onde mexer depois

- Adicionar as 2 linhas que faltam: só criar novas entradas em `BaseAutoTileMixRules` (não precisa de função nova).
- Plugar um minério novo: criar `GetTextureForTile` apontando pra spritesheet dele e chamar `EvaluateAutoTileMixRules(TileType.NomeDoMinerio, x, y, BaseAutoTileMixRules)`.
- Extensão da grama: criar um array `GrassExtraMixRules` e concatenar com `BaseAutoTileMixRules` na hora de montar o array passado pra grama.
