# Worldgen Biome Seed Refactor Plan

## Objetivo

Refatorar a geracao procedural de Ny'vorn para aceitar uma seed publica em texto,
converter essa seed para uma `MasterSeed` numerica estavel, derivar sub-seeds por
sistema e introduzir um campo inicial de biomas usado pelos passes da worldgen.

## Diagnostico Atual

- A pipeline principal esta em `WorldGenerator`, `WorldGenContext`,
  `WorldGenConfig` e nos passes `ClearWorld`, `LayerBoundary`,
  `SurfaceProfile`, `BaseTerrainFill`, `DirtToStoneTransition`, `Cave`,
  `CaveEntrance`, `Hydrology`, `Tissue`, `TreeGeneration` e `WorldBounds`.
- A seed publica atual e `int`, iniciada como `1337` em `WorldGenConfig` e na
  tela `WorldCreationState`.
- A seed alimenta ruido, `Random`, tecido, arvores, agua e ambiente.
- Saves persistem `PlanetWorldMetadata.Seed` como `int`; `WorldId` ja e separado
  e continua sendo o identificador unico do save.
- O save atual esta na versao 15, entao os novos campos entram como metadata
  adicional e com migracao para valores legados.

## Arquitetura Proposta

- `SeedText` e o texto digitado pelo jogador.
- `MasterSeed` e `ulong`, derivada por hash estavel SHA256 truncado para 64 bits.
- `WorldSeedSet` concentra `SeedText`, `MasterSeed`, `LegacyIntSeed` e sub-seeds
  como `TerrainSeed`, `BiomeSeed`, `CaveSeed`, `MaterialSeed`, `TissueSeed`,
  `DecorationSeed`, `HydrologySeed`, `EnvironmentSeed`, `LootSeed` e
  `CorruptionSeed`.
- Sub-seeds sao derivadas com dominio fixo no formato
  `Nyvorn:v1:<MasterSeedHex>:<label>`.
- `WorldGenConfig` mantem `Seed` como compatibilidade, mas passa a carregar
  `SeedSet`.
- `WorldGenContext` expoe `Seeds` e `BiomeField`.
- `BiomeFieldPass` entra depois de `LayerBoundary` e antes de
  `SurfaceProfile`.

## Modelo de Biomas

- `BiomeType`: `Forest`, `Desert`, com reservas para `Tundra`, `Corrupted` e
  `Mycelial`.
- `BiomeDefinition`: modificadores de relevo, suavidade, materiais, arvores e
  cavernas.
- `BiomeField`: campo 1D por coluna X, com wrap horizontal.
- `BiomeSample`: bioma dominante, secundario e blend.

## Fases de Implementacao

1. Adicionar `SeedHash` e `WorldSeedSet`, mantendo geracao antiga funcionando.
2. Integrar `WorldSeedSet` em `WorldGenConfig` e `WorldGenContext`.
3. Atualizar criacao de mundo para seed textual publica e seed aleatoria visivel.
4. Persistir `SeedText`, `MasterSeed` e `WorldgenVersion` nos metadados.
5. Adicionar os tipos de bioma e `BiomeField`.
6. Registrar `BiomeFieldPass` na pipeline.
7. Adaptar `SurfaceProfilePass` para consultar bioma.
8. Adaptar materiais em `BaseTerrainFillPass` e `DirtToStoneTransitionPass`.
9. Adaptar cavernas, arvores e sub-seeds de hidrologia/tecido.
10. Validar determinismo, wrap horizontal e compatibilidade com saves antigos.

## Compatibilidade

- Saves antigos sem `SeedText` usam `Seed.ToString()`.
- Saves antigos sem `MasterSeed` usam `WorldSeedSet.FromLegacyInt(Seed)`.
- Saves com snapshot de tiles continuam carregando snapshot e nao regeneram o
  mundo.
- `WorldId` nunca e derivado da seed.

## Criterios de Sucesso

- Mesma `SeedText` e mesma versao da worldgen geram o mesmo mundo.
- Seeds diferentes geram biomas e terrenos diferentes.
- Biomas fecham corretamente na emenda horizontal.
- Superficie, materiais, cavernas e arvores consultam o campo de bioma.
- Saves novos armazenam `SeedText`, `MasterSeed`, `WorldgenVersion` e `WorldId`.
- Saves antigos continuam abrindo.
