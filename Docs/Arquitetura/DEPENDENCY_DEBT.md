# DEPENDENCY DEBT

## Objetivo

Registrar dependências arquiteturais que hoje existem no código, funcionam, mas fogem do alvo estrutural desejado para o Nyvorn.

Este arquivo existe para evitar dois problemas:

- dívida invisível virar padrão
- “refatorar tudo agora” sem contexto e sem prioridade

Cada item abaixo descreve:

- a dependência atual
- por que ela é dívida
- o risco
- a direção recomendada para resolver no futuro

## 1. `PlanetSaveService` depende de `PlayingSession`

### Estado atual

`PlanetSaveService` depende de tipos de `Nyvorn.Source.Game.States`, especialmente `PlayingSession`.

### Por que é dívida

Persistência deveria depender de modelos de estado ou DTOs estáveis, não do orquestrador principal do runtime jogável.

### Risco

- save acoplado ao formato interno da sessão
- toda mudança grande em `PlayingSession` tende a impactar persistência
- dificulta separar runtime e modelo serializável

### Direção futura

Introduzir um modelo de snapshot ou exportador de sessão, por exemplo:

- `SessionSnapshot`
- `WorldRuntimeSnapshot`
- `PlayerRuntimeSnapshot`

e fazer `PlanetSaveService` depender desses modelos, não da sessão completa.

## 2. `PlayerSaveService` depende de `PlayingSession`

### Estado atual

`PlayerSaveService` também depende de `Game.States.PlayingSession`.

### Por que é dívida

O mesmo problema estrutural do save de mundo se repete para o save do jogador.

### Risco

- persistência de player presa ao runtime
- alteração em inventário, hotbar ou estado do player exigindo mexer na sessão inteira

### Direção futura

Extrair um modelo explícito de exportação do player:

- posição
- inventário
- hotbar
- hubs ativados
- estado persistível necessário

## 3. `SandSystem` em `Engine` depende de `WorldMap`

### Estado atual

`SandSystem` vive em `Engine/Physics/Sand`, mas usa `WorldMap` diretamente.

### Por que é dívida

`Engine` deveria ser camada mais genérica. Quando um módulo de engine conhece diretamente a estrutura de mundo do jogo, ele deixa de ser verdadeiramente infraestrutural.

### Risco

- confusão sobre o que realmente pertence a `Engine`
- novas físicas de mundo podendo entrar no lugar errado

### Direção futura

Duas opções aceitáveis:

1. mover `SandSystem` para `Gameplay/World/Simulation`
2. manter em `Engine`, mas extrair uma interface pequena de consulta espacial do mundo

Recomendação atual:

- manter como está por enquanto
- não replicar esse padrão em novos sistemas

## 4. `BaseTerrainFillPass` depende de `Gameplay.World.Simulation`

### Estado atual

Um passo de geração usa tipos da camada de simulação.

### Por que é dívida

Geração de mundo deveria depender de configuração, ruído e estado do mapa, não da camada de simulação viva.

### Risco

- mistura entre mundo inicial e mundo vivo
- geração puxando contratos que pertencem ao runtime

### Direção futura

Avaliar se a dependência pode ser invertida para:

- utilitário neutro em `World`
- helper específico de geração

sem depender de `Simulation`.

## 5. `PlayingSession` concentra dependências de quase todos os subsistemas

### Estado atual

`PlayingSession` importa e coordena:

- input contextual
- player
- inimigos
- itens
- combate
- UI
- mundo
- tecido
- simulação por ticks
- areia
- minimapa

### Por que é dívida

Apesar de ser o orquestrador legítimo da sessão, ele já mistura coordenação com várias regras detalhadas.

### Risco

- classe grande demais
- onboarding difícil
- alta chance de regressão quando algo muda
- testes de integração mais custosos

### Direção futura

Manter `PlayingSession` como orquestrador, mas migrar regras específicas para sistemas menores, por exemplo:

- `WorldInteractionSystem`
- `DroppedItemSystem`
- `TissueRuntimeSystem`
- `BlockPlacementSystem`

sem desmontar tudo de uma vez.

## 6. `WorldMap` ainda acumula responsabilidades demais

### Estado atual

`WorldMap` cuida de:

- grade de tiles
- wrap horizontal
- alterações em tiles
- cache de chunks para render
- partes de lógica natural
- consultas espaciais
- tecido
- suporte à persistência

### Por que é dívida

Ele é uma estrutura central demais e pode facilmente se tornar uma “classe universo” do mundo.

### Risco

- mudanças em uma área afetarem outras
- APIs cada vez maiores e menos previsíveis
- difícil separação entre dado, simulação e render auxiliar

### Direção futura

Migrar aos poucos para uma API mais explícita:

- estado base do mapa
- consultas espaciais
- render cache
- simulação natural

sem quebrar o papel de `WorldMap` como fonte de verdade dos tiles.

## 7. `Game States` ainda conhecem detalhes demais de serviços

### Estado atual

Alguns estados de UI e fluxo, como pause ou seleção de mundo, falam diretamente com serviços de save e navegação.

### Por que é dívida

Estados devem orquestrar experiência, mas o excesso de detalhe operacional neles pode tornar a navegação frágil.

### Risco

- duplicação de fluxo
- comportamento de save espalhado por múltiplos estados

### Direção futura

Com o tempo, consolidar fluxos comuns em helpers ou controladores mais explícitos:

- retorno ao menu
- salvar sessão
- carregar sessão
- reabrir mundo

## 8. `UI` e gameplay ainda estão bem próximos em alguns pontos

### Estado atual

Algumas interações, principalmente minimapa, inventário e preview de tile, ainda ficam muito próximas da lógica central de runtime.

### Por que é dívida

Embora isso seja comum em jogos pequenos e médios, pode dificultar a separação entre:

- apresentação
- intenção do jogador
- alteração efetiva do mundo

### Risco

- UI influenciando demais a lógica
- lógica de interação se espalhando entre render e sessão

### Direção futura

Tornar mais explícita a separação:

- UI calcula intenção
- sessão valida
- mundo aplica

## 9. Prioridade recomendada da dívida

### Alta prioridade

- `PlayingSession` excessivamente central
- `PlanetSaveService` dependente da sessão
- `PlayerSaveService` dependente da sessão
- `WorldMap` com muitas responsabilidades

### Média prioridade

- `BaseTerrainFillPass` dependente de `Simulation`
- estados conhecendo detalhes de save demais

### Baixa prioridade por enquanto

- `SandSystem` viver em `Engine` usando `WorldMap`

Motivo:

- hoje funciona
- é um acoplamento conhecido
- não precisa ser a primeira cirurgia arquitetural

## 10. Regra operacional

Toda vez que surgir uma nova exceção arquitetural:

1. registrar aqui
2. explicar por que ela existe
3. marcar se é temporária ou estrutural
4. dizer a direção futura de correção

Se uma dívida for removida:

1. apagar do arquivo
2. registrar a remoção no commit ou changelog da refatoração

