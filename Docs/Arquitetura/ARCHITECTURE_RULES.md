# ARCHITECTURE RULES

## Objetivo

Definir regras claras de dependência para o Nyvorn, de modo que novas features, refatorações e sistemas cresçam sem espalhar acoplamento entre camadas.

Este documento não tenta descrever a arquitetura ideal abstrata. Ele parte do código real atual e define:

- camadas
- dependências permitidas
- dependências proibidas
- exceções temporárias
- checklist de revisão arquitetural

## 1. Camadas do projeto

O projeto deve ser lido como um conjunto de camadas com direção preferencial de dependência:

```text
Game1
  -> Game
    -> Game States
      -> PlayingState
        -> PlayingSession
          -> Gameplay Systems
            -> World / Simulation / Combat / Entities / UI

Engine
  -> utilitários de base usados por Game e Gameplay

World
  -> estado do mundo, geração, persistência, tecido e simulação
```

## 2. Regra principal de direção

Como regra geral:

- camadas de alto nível podem depender de camadas de baixo nível
- camadas de baixo nível não devem depender de camadas de alto nível

Em termos práticos:

- `Game` pode usar `Gameplay`, `World` e `Engine`
- `Gameplay` pode usar `Engine` e `World`
- `World` pode usar `Engine`
- `Engine` não deve depender de `Game` nem de regras específicas de `Gameplay`

## 3. Dependências permitidas por área

### 3.1 `Engine`

Pode depender de:

- `System.*`
- `Microsoft.Xna.Framework*`
- outros módulos de `Engine`, quando fizer sentido
- `World`, apenas em casos já consolidados e bem justificados pela infraestrutura atual

Não deve depender de:

- `Game`
- `Game.States`
- `Gameplay.UI`
- `Gameplay.Combat`
- `Gameplay.Entities`

Observação:

- hoje `SandSystem` em `Engine/Physics/Sand` já depende de `WorldMap`, então `Engine -> World` é uma exceção aceita no estado atual, mas deve ser tratada como infraestrutura acoplada ao mundo, não como precedente geral para qualquer módulo de `Engine`.

### 3.2 `Game`

Pode depender de:

- `Engine`
- `Gameplay`
- `World`

Não deve depender de:

- detalhes internos de persistência que façam estados de UI implementar regra de negócio de save manualmente fora de serviços

Regra:

- `Game` deve orquestrar telas, fluxo e transição
- `Game` não deve concentrar regra detalhada de mundo

### 3.3 `Gameplay`

Pode depender de:

- `Engine`
- `World`
- outros módulos de `Gameplay`

Não deve depender de:

- `Game`, exceto em pontos de orquestração já existentes e explicitamente aceitos

Regra:

- `Gameplay` implementa comportamento jogável
- não deve assumir responsabilidade de navegação entre estados

### 3.4 `World`

Pode depender de:

- `Engine`
- outros módulos de `World`

Não deve depender de:

- `Game`
- `Game.States`
- `Gameplay.UI`
- `Gameplay.Entities.Player`
- `Gameplay.Entities.Enemies`
- `Gameplay.Combat`

Regra:

- `World` deve conhecer tiles, chunks, geração, simulação e persistência
- `World` não deve conhecer fluxo de tela ou entidades de alto nível

### 3.5 `Gameplay.UI`

Pode depender de:

- `Engine`
- `World`
- `Gameplay.Items`
- DTOs ou tipos de leitura necessários ao render

Não deve depender de:

- serviços de save
- lógica de geração
- alteração direta de estado estrutural do mundo

Regra:

- UI apresenta
- UI consulta
- UI sinaliza intenção
- UI não decide regra de gameplay estrutural

## 4. Regras específicas por componente central

### 4.1 `PlayingState`

Deve:

- controlar input de alto nível
- controlar gates de console, pause, inventory e minimap
- chamar a sessão jogável
- controlar transições de estado

Não deve:

- implementar regra detalhada de areia
- implementar regra de crescimento natural
- implementar geração de mundo

### 4.2 `PlayingSession`

Deve:

- orquestrar subsistemas do gameplay
- ser o ponto de coordenação da sessão jogável
- decidir ordem de execução entre player, mundo, combate e ticks

Não deve crescer indefinidamente como “classe-de-tudo”.

Meta arquitetural:

- manter `PlayingSession` como orquestrador
- deslocar regras específicas para sistemas especializados sempre que a complexidade justificar

### 4.3 `WorldMap`

Deve:

- manter estado dos tiles
- fornecer consultas e alterações no mapa
- expor chunks, bounds, wrap e utilidades espaciais
- suportar persistência e render cache do mundo

Não deve:

- conhecer player
- conhecer HUD
- conhecer lógica de estado de jogo
- conhecer serviços de navegação

Meta arquitetural:

- `WorldMap` continua central, mas deve caminhar para API mais clara e menos mistura de responsabilidades auxiliares

### 4.4 `WorldGenerator`

Deve:

- gerar estado inicial do mundo
- orquestrar passes
- parar sua responsabilidade no momento em que o mundo inicial foi criado

Não deve:

- controlar simulação viva de runtime
- depender de `PlayingState` ou de UI

### 4.5 `Persistence`

Deve:

- salvar e restaurar estado
- traduzir runtime para dados persistidos

Não deve:

- decidir gameplay
- decidir crescimento natural
- decidir update de mundo

## 5. Dependências proibidas

As seguintes direções devem ser tratadas como proibidas em código novo:

- `World -> Game`
- `World -> Game.States`
- `World -> Gameplay.UI`
- `Engine -> Game.States`
- `Engine -> Gameplay.UI`
- `UI -> serviços que alteram mundo diretamente sem passar pela sessão`
- `Generation -> PlayingState`
- `Generation -> Player`
- `Persistence -> UI`

Também devem ser evitadas:

- dependências cíclicas implícitas entre `Gameplay` e `World` que obriguem dois sistemas a se conhecer mutuamente por detalhes internos
- utilitários genéricos colocados em `Game` quando pertencem a `Engine` ou `World`

## 6. Exceções aceitas no estado atual

Estas exceções existem hoje no código e são aceitas como dívida controlada, não como modelo ideal:

- `PlanetSaveService` depende de `Game.States.PlayingSession`
- `PlayerSaveService` depende de `Game.States.PlayingSession`
- `SandSystem` em `Engine` depende de `WorldMap`
- `BaseTerrainFillPass` usa tipos de `Gameplay.World.Simulation`
- `PlayingSession` concentra dependências de quase todos os grandes subsistemas

Essas exceções devem ser registradas em `DEPENDENCY_DEBT.md`.

## 7. Regras para novos arquivos

Antes de criar qualquer classe nova, responder:

1. Ela pertence a `Engine`, `Game`, `Gameplay` ou `World`?
2. Ela precisa orquestrar ou implementar regra?
3. Ela precisa conhecer runtime jogável, ou apenas estado do mundo?
4. Ela vai salvar algo, desenhar algo, simular algo ou gerar algo?

Heurística:

- se gera mundo inicial: `World/Generation`
- se simula mundo vivo: `World/Simulation`
- se salva/carrega: `World/Persistence`
- se desenha HUD/minimapa/debug: `Gameplay/UI`
- se representa entidade jogável: `Gameplay/Entities`
- se só ajuda infraestrutura: `Engine`

## 8. Regras para PR e refatoração

Checklist de revisão arquitetural:

- a nova dependência aponta para a direção correta?
- a classe nova entrou na pasta certa?
- a UI está só apresentando ou está decidindo regra?
- `PlayingSession` ganhou coordenação ou regra detalhada demais?
- `WorldMap` ficou mais limpo ou mais acoplado?
- o sistema novo cabe em geração, simulação ou persistência sem misturar as três coisas?
- a mudança criou uma exceção nova? Se sim, ela foi documentada em `DEPENDENCY_DEBT.md`?

## 9. Meta de médio prazo

O objetivo não é eliminar todo acoplamento imediatamente. O objetivo é:

- reduzir acoplamento novo
- tornar dívidas atuais visíveis
- impedir regressão estrutural
- preparar refatorações futuras com direção clara

Este documento deve ser atualizado sempre que:

- surgir uma exceção arquitetural nova
- uma exceção antiga for removida
- uma camada ganhar responsabilidade nova de forma estável

