# Arquitetura do Tissue

O Tissue é dividido em quatro responsabilidades:

| Camada | Responsabilidade |
|---|---|
| `TissueNetwork` | Topologia planetária permanente: nós, ramos, caminhos e conectividade. |
| `TissueField` | Estado biológico atual e persistente nos tiles sólidos. |
| `ITissueQueryService` | Porta oficial de leitura para sistemas de gameplay. |
| `ITissueMutationService` | Porta oficial para alterações biológicas controladas. |

Sistemas de gameplay não devem receber ou modificar diretamente `TissueField` ou `TissueNetwork`.

## Estado biológico

Todos os valores ficam entre `0` e `1`.

| Propriedade | Significado |
|---|---|
| `Presence` | Quantidade física de Tissue no tile. |
| `Vitality` | Saúde ou atividade biológica do Tissue existente. |
| `Flow` | Capacidade de conduzir sinal ou energia. |
| `Corruption` | Fração da lógica original reescrita por uma influência externa. |
| `MemoryDensity` | Saturação local de resíduos memoriais e ecos. |

Invariantes:

- `Presence = 0` torna todos os outros valores iguais a `0`.
- `Vitality = 0` não remove o Tissue físico; ele permanece morto ou inativo.
- `Flow = 0` impede condução sem remover o Tissue.
- `Corruption = 1` representa Tissue completamente reescrito, não necessariamente morto.
- `MemoryDensity` não altera a condução por si só.

## Condução

```csharp
float signalCapacity = Presence * Vitality * Flow;
float nativeConductivity = signalCapacity * (1f - Corruption);
float corruptedConductivity = signalCapacity * Corruption;
```

A soma dos canais nativo e corrompido preserva a capacidade total. Corrupção muda qual lógica o sinal segue em vez de simplesmente matar o Tissue.

## API de leitura

A instância oficial é exposta como:

```csharp
session.TissueQueries
```

Ela consulta células, agrega regiões, encontra nós conectados e constrói mapas imutáveis de propagação sem expor índices ou coleções internas.

## API de mutação

A instância oficial é exposta como:

```csharp
session.TissueMutations
```

Operações disponíveis:

```csharp
DamageTile(tileX, tileY, amount);
RestoreTile(tileX, tileY, amount);
AddCorruption(tileX, tileY, amount);
ReduceCorruption(tileX, tileY, amount);
AddMemory(tileX, tileY, amount);
SetFlow(tileX, tileY, value);
RemoveTissue(tileX, tileY);
```

Regras:

- `DamageTile` e `RestoreTile` alteram somente `Vitality`.
- Nenhuma restauração recria Tissue com `Presence = 0`.
- Corrupção, memória e fluxo permanecem independentes.
- `RemoveTissue` cria o estado neutro/tombstone efetivo sem alterar a `TissueNetwork`.
- X usa o wrap horizontal do mundo; Y inválido e tiles de ar são rejeitados.
- Valores incrementais negativos, `NaN` e infinitos são rejeitados.
- Resultados são limitados entre `0` e `1`.
- O retorno é `true` somente quando o estado do tile realmente muda.
- Cada alteração atualiza somente o tile envolvido e utiliza Revision, dirty-state e persistência já existentes.

Mineração continua removendo Tissue pelo fluxo interno do `WorldMap`, porque a transição sólido → ar é uma regra de infraestrutura. Criaturas, bosses, biomas e eventos devem usar exclusivamente `ITissueMutationService`.

