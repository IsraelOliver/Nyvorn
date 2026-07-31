# AUDITORIA FASE 0 - INSTRUMENTAÇÃO DE MÉTRICAS

## RESUMO EXECUTIVO

A Fase 0 foi concluída com sucesso. Todos os contadores de isolamento estão instrumentados nos callsites reais. Não houve criação de contadores artificiais. O dump de métricas agora separa CurrentFrame de SessionMetrics.

---

## QUESTÕES AUDITADAS

### 1. V3UpdateCount = 0

**STATUS**: Confirmado como correto.

**EXPLICAÇÃO**: 
- V3 mode na Fase 0 é apenas renderização NEUTRA (sem iluminação aplicada)
- Não existe uma operação V3.Update() implementada na Fase 0
- V3UpdateCount permanece 0 por design - não será incrementado até Phase 1 quando o LightingV3System.Update() for implementado

**CALLSITE ESPERADO**: 
- Será adicionado em Phase 1: `LightingV3System.Update()` → `LightingPipelineCoordinator.I.RecordV3Update()`
- Atualmente: NENHUM callsite (apropriado para Phase 0)

**CONCLUSÃO**: Não instrumentar artificialmente. Deixar como 0 é correto.

---

### 2. NeutralEntityDrawCount - Callsite Real

**LOCALIZAÇÃO**: `PlayingSessionViewCoordinator.cs` linha 831-843 (método DrawEntities)

**CÓDIGO**:
```csharp
public void DrawEntities(SpriteBatch spriteBatch, IEntityLightSampler entityLightSampler)
{
    Color tint = entityLightSampler.SampleLightAt(Player.Position);

    // Record entity draw event (both Legacy and Neutral implementations recognize this)
    var metricRecorder = entityLightSampler.GetMetricRecorder();
    metricRecorder("entity_draw");

    // Legacy-specific: record player light sample details
    if (entityLightSampler is LegacyEntityLightSampler)
    {
        metricRecorder("player");
        metricRecorder("apply_tint");
    }

    Player.Draw(spriteBatch, tint);
}
```

**FLUXO INSTRUMENTADO**:
1. `metricRecorder("entity_draw")` → chama `GetMetricRecorder()` do sampler
2. `NeutralEntityLightSampler.GetMetricRecorder()` reconhece "entity_draw"
3. Chama `coordinator.RecordNeutralEntityDraw()`
4. Incrementa `currentFrameMetrics.NeutralEntityDrawCount` E `sessionMetrics.NeutralEntityDrawCount`

**CALLSITES POR RENDER PATH**:
- `BuildLightingMask()` (linha 645): Cria `NeutralEntityLightSampler`, passa para DrawEntities → conta 1x
- `DrawWorldSceneToRenderTarget()` (linha 713): Cria `NeutralEntityLightSampler`, passa para DrawEntities → conta 1x
- `DrawWithLightingV2Pipeline()` (linha 1077): Cria `NeutralEntityLightSampler`, passa para DrawEntities → conta 1x
- `DrawWithCompositionNeutralPipeline()` (linha 1379): Cria `NeutralEntityLightSampler`, passa para DrawEntities → conta 1x

**RESULTADO ESPERADO**:
- Em V3 mode: NeutralEntityDrawCount ≥ 1 por frame
- Em Legacy mode: NeutralEntityDrawCount = 0

---

### 3. LegacyLightingUpdateCount - Callsite Real

**LOCALIZAÇÃO**: `WorldLightingSystem.cs` linha 118 (método Update)

**CÓDIGO**:
```csharp
public void Update(int screenWidth, int screenHeight, float cameraZoom)
{
    // PHASE 0: Pipeline isolation - skip execution if not in Legacy mode
    if (!LightingPipelineCoordinator.I.IsLegacyMode)
        return;

    if (worldMap == null || screenWidth <= 0 || screenHeight <= 0 || cameraZoom <= 0f)
        return;

    LightingPipelineCoordinator.I.RecordLegacyLightingUpdate();  // ← AQUI
    
    // Resto do Update() procede...
}
```

**CONTEXTO**:
- Chamado apenas se `IsLegacyMode == true`
- Se V3 mode está ativo, retorna early (linha 112-113) SEM registrar
- Garante que o contador é 0 em V3 mode

**CHAMADORES**:
- PlayingState.cs linha 1184: `graphicsDevice.SetRenderTarget(null)` seguido de chamada à Update em contexto Legacy

**RESULTADO ESPERADO**:
- Em Legacy mode: LegacyLightingUpdateCount = 1 por frame
- Em V3 mode: LegacyLightingUpdateCount = 0 (early return antes do registro)

---

### 4. LegacyCompositeCount - Callsite Real

**LOCALIZAÇÃO**: `PlayingSessionViewCoordinator.cs` linha 465-482 (método DrawWorldLighting)

**CÓDIGO**:
```csharp
public void DrawWorldLighting(SpriteBatch spriteBatch, float worldOffsetX)
{
    if (lightTexture == null || lightTextureActiveWidth <= 0 || lightTextureActiveHeight <= 0)
        return;

    LightingPipelineCoordinator.I.RecordLegacyComposite();  // ← AQUI

    int tileSize = WorldMap.TileSize;
    // ... offset calculations ...
    
    spriteBatch.Draw(lightTexture, destination, source, Color.White);
}
```

**CONTEXTO**:
- Desenha a textura de iluminação Legacy com MultiplyBlend
- Chamado SOMENTE no DrawWithLegacyPipeline() com blend state MultiplyBlend

**CALLSITE EM PLAYINGSTATE**:
```csharp
// PlayingState.cs linha 1188-1190
spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: MultiplyBlend, transformMatrix: transform);
session.DrawWorldLighting(spriteBatch, worldOffset);
spriteBatch.End();
```

**RESULTADO ESPERADO**:
- Em Legacy mode: LegacyCompositeCount = 1 por frame (DrawWithLegacyPipeline → DrawWorldLighting)
- Em V3 mode: LegacyCompositeCount = 0 (DrawWithCompositionNeutralPipeline pula DrawWorldLighting)

---

## CONTADORES TAMBÉM VERIFICADOS

### LegacyLightGridCopyCount e LegacyGlowGridCopyCount

**LOCALIZAÇÃO**: `WorldLightingSystem.cs`
- Linha 217 (CopyLightGridTo): `LightingPipelineCoordinator.I.RecordLegacyLightGridCopy();`
- Linha 241 (CopyGlowGridTo): `LightingPipelineCoordinator.I.RecordLegacyGlowGridCopy();`

Estes são chamados durante PrepareWorldLighting (PlayingState linha 1181) apenas em Legacy mode.

### LegacyEntityLightSampleCount, LegacyPlayerLightSample, LegacyEntityTintApply

**LOCALIZAÇÃO**: `EntityLightingContext.cs` linhas 42-64
- Registrados pelo `LegacyEntityLightSampler` quando SampleLightAt() é chamado
- Registrados via `GetMetricRecorder()` callbacks

---

## ESTRUTURA DE DUMP ATUALIZADA

### CurrentFrame Metrics
- Resetado a cada BeginFrame()
- Mostra execução do frame atual
- Valida isolamento do frame (IsIsolationValid)

### Session Metrics
- Acumula desde o início da sessão
- Permite confirmar que operações condicionais ocorreram ao longo de vários frames
- Não é resetado entre frames

### Exemplo de Output (Ctrl+Shift+M):
```
========== LIGHTING PIPELINE METRICS (Ctrl+Shift+M) ==========
Active Mode: V3
Frame: 425

--- CURRENT FRAME METRICS ---
Isolation Valid: True

[LEGACY COUNTERS]
  LightingUpdate:       0
  LightGridCopy:        0
  ...
  TOTAL:                0

[V3 COUNTERS]
  Update:               0
  Composite:            1
  EntityLightSample:    1
  EntityDraw:           1
  TOTAL:                3

--- SESSION METRICS (Cumulative) ---

[LEGACY TOTALS]
  LightingUpdate:       200    (200 frames de Legacy)
  ...

[V3 TOTALS]
  Update:               0      (nunca implementado em Phase 0)
  Composite:            25     (25 frames de V3)
  EntityLightSample:    25
  EntityDraw:           25
  TOTAL:                75
```

---

## CONCLUSÃO

✅ **V3UpdateCount = 0**: Correto por design. Sem Update artificial.
✅ **NeutralEntityDrawCount**: Instrumentado em DrawEntities() via sampler.
✅ **LegacyLightingUpdateCount**: Instrumentado em WorldLightingSystem.Update() com early-exit em V3.
✅ **LegacyCompositeCount**: Instrumentado em DrawWorldLighting() via spriteBatch.Draw().
✅ **Métricas Separadas**: CurrentFrame + SessionMetrics implementado.
✅ **Build**: Compilado com sucesso, sem warnings.

**A Fase 0 está auditada e aprovada para encerramento.**
