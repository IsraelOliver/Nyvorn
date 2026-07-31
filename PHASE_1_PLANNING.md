# FASE 1 - PREPARAÇÃO DE RENDERTARGETS

## STATUS: PLANEJAMENTO (AGUARDANDO AUTORIZAÇÃO)

---

## OBJETIVO

Estruturar a infraestrutura de composição para V3, preparando RenderTargets separados para cada etapa de renderização.

**Importante**: Nenhuma implementação de iluminação será feita na Fase 1.

---

## ESCOPO FASE 1

### Será Implementado

#### 1. AtmosphereRenderTarget
- Renderização de céu, sol, luas, montanhas
- Resultado: textura com atmosfera sem iluminação
- Blend: Opaco (sem composição)

#### 2. WorldRenderTarget
- Renderização de terreno, água, decorações
- Resultado: textura com mundo sem iluminação
- Blend: Opaco

#### 3. EntitiesRenderTarget
- Renderização de jogador, inimigos, NPCs, itens
- Resultado: textura com entidades com cores neutras
- Blend: Opaco (sem sampler de iluminação)

#### 4. EmissiveRenderTarget
- Dedicado para futuros objetos luminosos
- Resultado: vazio na Fase 1 (pronto para Phase 2)
- Blend: Aditivo (para composição futura)

#### 5. Composição Estruturada
```
BackBuffer ← Composite(
    AtmosphereRT,
    WorldRT,
    EntitiesRT,
    EmissiveRT (vazio),
    Lighting (zero aplicado)
)
```

---

## O QUE NÃO SERÁ FEITO NA FASE 1

❌ Luz solar direcional  
❌ Propagação de luz em grades  
❌ Glow de tochas  
❌ Cavern darkness (escurecimento de cavernas)  
❌ Tochas renderizadas em V3  
❌ Nenhuma implementação de iluminação  
❌ Nenhum algoritmo de luz  

---

## ESTRUTURA DE RENDERIZAÇÃO ESPERADA

### Pseudocódigo Phase 1

```csharp
if (IsV3Mode)
{
    // Prepare RenderTargets
    graphicsDevice.SetRenderTarget(sceneRT);
    graphicsDevice.Clear(Color.Black);
    
    // PASS 1: Atmosphere
    DrawAtmosphere();  // → sceneRT
    
    // PASS 2: World
    DrawWorld();  // → sceneRT (additive)
    
    // PASS 3: Entities (Neutral)
    DrawEntities(neutralSampler);  // → sceneRT (additive)
    
    // PASS 4: Emissive (empty in Phase 1)
    // DrawEmissive();  // → sceneRT (additive)
    
    // Composite to backbuffer
    graphicsDevice.SetRenderTarget(null);
    DrawRT(sceneRT);  // No lighting multiplication
}
else  // Legacy
{
    // Existing direct-to-backbuffer rendering
    // Unchanged
}
```

---

## INFRAESTRUTURA NECESSÁRIA

### PlayingSessionViewCoordinator Modifications
- Add `atmosphereRenderTarget` field
- Add `worldRenderTarget` field  
- Add `entitiesRenderTarget` field
- Add `emissiveRenderTarget` field
- Methods: `EnsureAtmosphereRenderTarget()`, etc.
- Disposal in `DisposeSceneRenderTarget()`

### PlayingState Modifications
- Conditional RenderTarget setup based on mode
- Sequential passes with correct render targets
- Composition logic (no lighting yet)

---

## VALIDATION CRITERIA

Phase 1 will be complete when:

✅ All RenderTargets allocated and deallocated correctly  
✅ Atmosphere renders to AtmosphereRT  
✅ World renders to WorldRT  
✅ Entities render to EntitiesRT (with neutral sampler)  
✅ Composition combines all passes to backbuffer  
✅ Visual output identical to Phase 0 composition-neutral  
✅ Legacy mode still renders correctly  
✅ Mode switching works without visual artifacts  
✅ No memory leaks from RenderTarget allocation  
✅ Build: zero warnings, zero errors  

---

## DEPENDENCIES

- ✅ Phase 0 (isolamento de modo) - COMPLETE
- ✅ LightingPipelineCoordinator - COMPLETE
- ✅ Neutral entity lighting - COMPLETE
- ⏳ Phase 1 infrastructure - PENDING

---

## RESTRICTIONS FOR PHASE 1

### Architectural

- Do NOT implement any lighting algorithm
- Do NOT modify Legacy rendering path
- Do NOT remove fallback to Legacy
- Do NOT add conditional logic inside subsystems

### Code Quality

- No magic numbers for RT dimensions
- No RenderTarget leaks (must dispose)
- Type-safe rendering calls
- Clear separation of concerns

### Testing

- Runtime validation: V3 neutral composition matches Phase 0
- Metrics: No additional isolation violations
- Legacy mode: Zero regressions

---

## NEXT STEPS (PENDING AUTHORIZATION)

1. Review this plan
2. Confirm scope boundaries
3. Authorize Phase 1 start
4. Implementation begins

**Until authorized: NO CODE CHANGES**

---

## TIMELINE ESTIMATE

Phase 1 implementation: 1-2 sessions (if authorized)
- RenderTarget infrastructure: 30 min
- Pass reorganization: 60 min  
- Composition logic: 30 min
- Testing & validation: 30 min

---

## PHASE 2+ PREVIEW (NOT STARTED)

Once Phase 1 is complete, Phase 2 will add:
- DirectionalSunlightMap
- BFS light propagation
- Light composition (multiply blend)
- Cavern darkness
- Torch glow (additive)
- etc.

---

**Ready for Phase 1 authorization.**
