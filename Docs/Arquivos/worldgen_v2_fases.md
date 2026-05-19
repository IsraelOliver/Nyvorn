# Worldgen V2

Documento de continuidade para a implementacao do `worldgen v2` de `Nyvorn`.

## Objetivo

Migrar o gerador atual para uma pipeline de passes semanticos, inspirada na estrutura do `Terraria`, mas com identidade propria de `Nyvorn`:

- planeta em loop
- superficie calma e agradavel
- poucas entradas naturais
- subterraneo grande
- camadas graduais
- `Tecido` integrado a cavernas e profundidades

## Direcao geral

O `worldgen v2` nao deve depender de um unico noise.

A base desejada e:

1. shape macro do planeta
2. materiais por profundidade gradual
3. entradas naturais raras e tortuosas
4. cavernas por mistura de noise + tuneis + camaras
5. integracao com `Tecido`
6. depois estruturas de superficie, vilarejos e ruinas

## Fases

### Fase 1
Status: concluida

Objetivo:
- criar a infraestrutura de pipeline para o `worldgen v2`

O que foi feito:
- criados `WorldGenContext`, `IWorldGenPass` e `WorldLayerProfile`
- `WorldGenerator` deixou de ser totalmente monolitico
- primeiros passes extraidos:
  - `ClearWorldPass`
  - `BaseShapePass`
  - `MaterialGradientPass`
  - `CaveNetworkPass`
  - `NaturalEntrancePass`
  - `WorldBoundsPass`

Arquivos principais:
- `C:\Users\hhhhi\Desktop\Projeto\Nyvorn-Echos\Nyvorn\Source\Gameplay\World\Generation\WorldGenerator.cs`
- `C:\Users\hhhhi\Desktop\Projeto\Nyvorn-Echos\Nyvorn\Source\Gameplay\World\Generation\WorldGenContext.cs`
- `C:\Users\hhhhi\Desktop\Projeto\Nyvorn-Echos\Nyvorn\Source\Gameplay\World\Generation\IWorldGenPass.cs`
- `C:\Users\hhhhi\Desktop\Projeto\Nyvorn-Echos\Nyvorn\Source\Gameplay\World\Generation\WorldLayerProfile.cs`
- `C:\Users\hhhhi\Desktop\Projeto\Nyvorn-Echos\Nyvorn\Source\Gameplay\World\Generation\Passes\*.cs`

### Fase 2
Status: concluida

Objetivo:
- refinar entradas naturais e cavernas para ficarem mais intencionais

O que foi feito:
- entradas naturais ficaram mais estreitas e tortuosas
- entradas levam de forma mais clara ate `Cavern`
- cavernas ganharam:
  - spines horizontais
  - camaras profundas
  - conectores verticais
  - mistura de noise + tuneis + camaras

Arquivos principais:
- `C:\Users\hhhhi\Desktop\Projeto\Nyvorn-Echos\Nyvorn\Source\Gameplay\World\Generation\Passes\NaturalEntrancePass.cs`
- `C:\Users\hhhhi\Desktop\Projeto\Nyvorn-Echos\Nyvorn\Source\Gameplay\World\Generation\Passes\CaveNetworkPass.cs`

## Proximas fases

antes verificar o problema de geração no spawn

### Fase 3
Status: pendente

Objetivo:
- melhorar a leitura da superficie de `Elyra`
- integrar o futuro bloco de grama

Sugestao:
- criar `SurfacePolishPass`
- aplicar grama na camada superficial de terra exposta
- manter pedra rara na superficie
- suavizar a leitura visual de topo do terreno

Possiveis arquivos:
- `...\\Generation\\Passes\\SurfacePolishPass.cs`
- `...\\WorldMap.cs`
- `...\\Items\\ItemDefinitions.cs`

### Fase 4
Status: pendente

Objetivo:
- integrar o `Tecido` ao perfil do mundo de forma mais sistemica

Sugestao:
- usar `WorldLayerProfile` para guiar pontos do `Tecido`
- favorecer `ShallowUnderground -> Cavern -> Depths`
- aproximar o `Tecido` de espacos abertos e corredores principais

Arquivos provaveis:
- `C:\Users\hhhhi\Desktop\Projeto\Nyvorn-Echos\Nyvorn\Source\Gameplay\World\Tissue\TissueGenerator.cs`
- `C:\Users\hhhhi\Desktop\Projeto\Nyvorn-Echos\Nyvorn\Source\Gameplay\World\Generation\WorldLayerProfile.cs`

### Fase 5
Status: pendente

Objetivo:
- reservar areas para estruturas de superficie

Sugestao:
- marcar zonas candidatas para:
  - vilarejos
  - ruinas
  - marcos isolados
- ainda sem construir tudo visualmente

### Fase 6
Status: pendente

Objetivo:
- validacao e acabamento do mundo

Sugestao:
- checar spawn
- checar acessibilidade da entrada inicial
- checar distribuicao das 3 entradas naturais
- checar presenca de cavernas nas camadas corretas

## Estado atual do projeto

Hoje o projeto ja tem:

- mundo em loop
- presets de tamanho:
  - `Pequeno`
  - `Medio`
  - `Grande`
- pipeline inicial do `worldgen v2`
- `Tecido` procedural com reveal
- tela de selecao/criacao de mundos
- save `.plt` com `metadata + tileChanges`

## Observacoes importantes

- `Grande` e a escala base inspirada no mundo medio de `Terraria`
- `Medio` e `30%` menor que `Grande`
- `Pequeno` e `40%` menor que `Grande`
- ao continuar a implementacao, preservar a ideia de:
  - superficie acolhedora
  - subterraneo grande
  - transicao gradual de materiais
  - entradas naturais raras

## Proximo passo recomendado para amanha

Comecar pela `Fase 3`:

1. adicionar o bloco de grama
2. criar `SurfacePolishPass`
3. fazer a superficie de `Elyra` parecer mais viva

## Build de referencia

Ultima fase validada:
- `net8.0-worldgenv2-phase2`

