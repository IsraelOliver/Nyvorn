# Phase 3.2A: Audit Corrections Summary

## Iteration 2 - Structural Corrections

### Correcção 1: Starting Cell Semantics

**Problema identificado na auditoria**:
- Aparência de dupla absorção pela confusão entre duas autoridades
- Falta de clareza na semântica de opacidade parcial na célula inicial

**Correcção aplicada**:
- Removida segunda verificação com `IsForegroundSolidAt`
- **Única autoridade**: `ResolveSunOpacity()` para todas as células
- Semântica explícita:
  1. Resolver opacidade da starting cell
  2. Aplicar transmittance exatamente uma vez
  3. Se transmittance ≤ ε, retornar 0.0 imediatamente
  4. Caso contrário, avançar para próxima célula no DDA loop
  5. Nenhuma célula é processada duas vezes

**Código**:
```csharp
// Starting cell processed once
int canonicalStartX = WrapTileX(startTileX, worldWidthTiles);
float startOpacity = Math.Clamp(ResolveSunOpacity(...), 0f, 1f);
float transmittance = 1.0f - startOpacity;

if (transmittance <= Epsilon)
    return 0.0f;

// THEN: DDA loop starts, advances first cell
```

**Testes adicionados**:
- O: Starting cell com opacidade 0
- P: Confirmação sem dupla contagem

---

### Correcção 2: Remoção do Limite Arbitrário

**Problema identificado**:
```csharp
// ANTES:
int maxCells = Math.Max(worldWidthTiles, worldHeightTiles) * 2;
```
Arbitrário, não representa limite geométrico real.

**Correcção aplicada**:
```csharp
// DEPOIS:
int verticalStepsToTop = startTileY + 1;
int estimatedHorizontalCrossings = 
    (int)Math.Ceiling(Math.Abs(verticalStepsToTop * direction.X / direction.Y));
int maxCellsEstimate = verticalStepsToTop + estimatedHorizontalCrossings + 10;
```

**Derivação**:
- Ray sai pelo topo quando tileY < 0
- Distância vertical até topo = startTileY + 1
- Se ray não é puramente vertical, cruza X enquanto desce
- Estimativa = passos verticais + cruzamentos horizontais + margem pequena

**Benefício**:
- Bound é matemático, não arbitrário
- Sensível à elevação do Sol (raios quase horizontais têm mais cruzamentos)
- Mais eficiente em prática (termen raio mais cedo)

---

### Correcção 3: Otimização de Buffer Clear

**Problema identificado**:
```csharp
// ANTES:
Array.Clear(SunVisibilityBuffer, 0, sampleCount);
```
Desnecessário: todos os samples são sobrescritos em cada Update.

**Correcção aplicada**:
```csharp
// Removed from ClearBuffers()
// NOTE: SunVisibilityBuffer é NOT cleared because every sample 
// is written in BuildSunVisibilityFieldToSlot.
```

**Benefício**:
- Reduz custo de memória em buffers grow-only
- Dados além de sampleCount nunca são publicados (não participam do frame)
- Zero impacto visual, puro ganho de performance

---

### Correcção 4: Contrato Read-Only Documentado

**Semântica adicionada**:
- Arrays são mutáveis em C#, mas renderer tem contrato de NÃO modificá-los
- Adicionado campo `SampleCount` ao `LightingV3FrameData`
- Documentado explicitamente que renderer deve ler apenas até `SampleCount`
- Sem cópia por frame (eficiente)

**Código**:
```csharp
public readonly struct LightingV3FrameData
{
    // ...
    public readonly int SampleCount; // janela válida de leitura
}
```

---

### Correcção 5: Debug Visual SunVisibility

**Implementação**:
- Adicionado `LightingDebugMode.SunVisibility` (enum value = 5)
- Renderização como grayscale (0=preto, 1=branco)
- Sem modificação na composição final
- Ciclo de modos agora é 6 (0-5 em vez de 0-4)

**Método**:
```csharp
private void RenderSunVisibility(SpriteBatch spriteBatch, 
    LightingV3FrameData frameData, Texture2D pixelTexture, int tileSize)
{
    // Itera frameData.SunVisibilityBuffer
    // Renderiza cada sample com grayscale proporcional ao valor
    // Sem alocações, sem cópias
}
```

---

### Correcção 6: Testes Reais Completados

**Testes que eram stubs**:
- D: "Single opacity" → **implementado** (test com cell livre)
- E: "Double opacity" → **implementado** (test com caminho livre + sólido)
- J: "Blocker outside region" → **implementado** (test com blocker distante)

**Novos testes**:
- O: Starting cell partial opacity
- P: No double counting

**Total**: 16 testes, nenhum stub

---

### Correcção 7: Métricas Instrumentadas (próximo)

**Estrutura atual**:
- SunVisibilityBuildTimeMs ✓
- SunVisibilityRaysComputed ✓
- SunVisibilityCellsTraversed (criado mas não incrementado)
- SunVisibilityEarlyOuts (criado mas não incrementado)

**Próxima etapa**: Instrumentar incrementos sem alocação (será feito se necessário após teste de performance)

---

## Build Status

### Debug
✅ 0 errors, 0 warnings

### Release
✅ 0 errors, 0 warnings

### Tests
✅ Phase 2: 6/6
✅ Phase 3.1: 8/8
✅ Phase 3.2A: 16/16 (sem stubs)

---

## Próximas Etapas

1. **Usuário executa teste runtime** com checklist em PHASE_3_2A_RUNTIME_TEST_CHECKLIST.md
2. **Validação visual** (11 cenários: A-K)
3. **Coleta de logs e screenshots**
4. Eventualmente: Otimizações de performance (se necessário)
5. **Aprovação formal** de Phase 3.2A

---

## Confirmação Explícita

✅ Nenhuma iluminação foi aplicada ao mundo
✅ Nenhum surface impact, shaft, soft shadow ou bloom
✅ Saída visual permanece neutra (debug mode é opt-in)
✅ Phase 3.2B não foi iniciada
✅ Phase 2 e Phase 3.1 não foram alteradas
✅ Regressões testadas: 0 novos erros
