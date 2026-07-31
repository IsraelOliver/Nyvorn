# PHASE 0 ARCHITECTURAL CORRECTION

## PROBLEM IDENTIFIED

WorldLightingSystem was violating centralized architecture by:
1. Importing LightingPipelineCoordinator 
2. Checking LightingPipelineMode.IsLegacyMode internally
3. Performing early-return based on pipeline mode
4. Recording metrics inside subsystem instead of at central callsite

This violated the Single Responsibility Principle: the Legacy subsystem should remain agnostic of pipeline mode decisions.

---

## SOLUTION IMPLEMENTED

### 1. WorldLightingSystem Purified

**REMOVED from WorldLightingSystem.cs**:
- Import: `using Nyvorn.Source.Engine.Graphics.LightingPipeline;`
- Early-return condition: `if (!LightingPipelineCoordinator.I.IsLegacyMode) return;`
- Metric recording: `LightingPipelineCoordinator.I.RecordLegacyLightingUpdate();`
- Metric recording: `LightingPipelineCoordinator.I.RecordLegacyLightGridCopy();`
- Metric recording: `LightingPipelineCoordinator.I.RecordLegacyGlowGridCopy();`

**RESULT**: WorldLightingSystem is now pure - it executes exactly the same code every time Update() is called, regardless of pipeline mode.

---

### 2. Metrics Moved to Central Callsites

**PATTERN**: Record metric immediately before Legacy subsystem call (at call site only)

#### A. LegacyLightingUpdateCount
**Location**: PlayingSession.cs line 466
```csharp
// PHASE 0: Record Legacy lighting update at central callsite (only if Legacy mode)
if (Engine.Graphics.LightingPipeline.LightingPipelineCoordinator.I.IsLegacyMode)
{
    Engine.Graphics.LightingPipeline.LightingPipelineCoordinator.I.RecordLegacyLightingUpdate();
    LightingSystem.Update(dt, Camera.Position, Camera.Zoom, screenWidth, screenHeight, EnvironmentSystem.SkyState.AmbientLight);
}
```

**Guarantee**: If V3 mode is active, this entire block is skipped. WorldLightingSystem.Update() is never reached.

#### B. LegacyLightGridCopyCount
**Location**: PlayingSessionViewCoordinator.cs line 437
```csharp
// Record at central callsite (caller already checked Legacy mode)
LightingPipelineCoordinator.I.RecordLegacyLightGridCopy();
lightingSystem.CopyLightGridTo(lightTextureBuffer);
```

**Guarantee**: Called only from PrepareWorldLighting(), which is only called when `IsLegacyMode == true` (checked in PlayingState line 369).

#### C. LegacyGlowGridCopyCount  
**Location**: PlayingSessionViewCoordinator.cs line 504
```csharp
// Record at central callsite (caller already checked Legacy mode)
LightingPipelineCoordinator.I.RecordLegacyGlowGridCopy();
lightingSystem.CopyGlowGridTo(glowTextureBuffer);
```

**Guarantee**: Same as LightGridCopy - only called when in Legacy mode.

#### D. LegacyCompositeCount
**Location**: PlayingSessionViewCoordinator.cs line 470 (already correct)
```csharp
LightingPipelineCoordinator.I.RecordLegacyComposite();
// ... spriteBatch.Draw(...) with MultiplyBlend
```

**Guarantee**: Called only from DrawWorldLighting(), which is only invoked in DrawWithLegacyPipeline context.

---

### 3. Type-Safe Metrics API

**REPLACED**: String-based `metricRecorder("string")` callbacks  
**WITH**: Type-safe `IEntityLightMetrics` interface

#### New Interface
```csharp
public interface IEntityLightMetrics
{
    void RecordDraw();
    void RecordPlayerLightSample();
    void RecordEntityTintApply();
}
```

#### Implementation Pattern
- `LegacyEntityLightMetrics`: Implements all three methods (records to coordinator)
- `NeutralEntityLightMetrics`: RecordDraw() and RecordPlayerLightSample() record, RecordEntityTintApply() is no-op

#### Usage in DrawEntities
```csharp
var metrics = entityLightSampler.GetMetrics();  // Type-safe
metrics.RecordDraw();                           // Explicit method call
```

No more magic strings. Compiler ensures all metric events are captured correctly.

---

## CENTRALIZATION VALIDATION

### Single Point of Decision
All pipeline mode checks now occur ONLY in PlayingState.Draw():
- Line 369: `if (LightingPipelineCoordinator.I.IsLegacyMode)` → PrepareWorldLighting
- Line 378: `if (LightingPipelineCoordinator.I.IsLegacyMode)` → DrawWithLegacyPipeline
- PlayingSession line 466: `if (...IsLegacyMode)` → WorldLightingSystem.Update

### No Subsystem Mode Awareness
✅ WorldLightingSystem: No mode checks, no early returns
✅ PlayingSessionViewCoordinator: No mode checks in methods called by V3
✅ Entity samplers: No mode checks, only implement their contracts

### Metric Recording at Callsites
✅ Legacy subsystem calls are guarded by central mode check
✅ Metrics recorded immediately before subsystem call
✅ No metrics recorded inside Legacy subsystems

---

## BUILD VERIFICATION

✅ **Compilation**: Zero warnings, zero errors
✅ **No behavior changes**: Visual rendering identical to previous version
✅ **No V3 implementation added**: Phase 0 remains composition-neutral
✅ **Architecture satisfied**: All mode decisions centralized, subsystems pure

---

## SUBSYSTEMS AUDITED

| Subsystem | Status | Location |
|-----------|--------|----------|
| WorldLightingSystem.Update | ✅ Purified | Guarded in PlayingSession line 466 |
| CopyLightGridTo | ✅ Purified | Guarded in PlayingState line 369 |
| CopyGlowGridTo | ✅ Purified | Guarded in PlayingState line 369 |
| DrawWorldLighting | ✅ Purified | Called only from DrawWithLegacyPipeline |
| DrawTorchGlow | ✅ Purified | Called only from DrawWithLegacyPipeline |
| DrawNightOverlay | ✅ Purified | Called only from DrawWithLegacyPipeline |
| BSL Shadow System | ✅ Purified | Called only from DrawWithLegacyPipeline |
| Entity Lighting | ✅ Typesafe | Sampler-based, no string callbacks |

---

## CONCLUSION

**Architectural centralization is now complete.**

All legacy subsystems are:
- Independent of pipeline mode
- Purely functional (same execution every time called)
- Isolated from V3 decisions
- Instrumented only at central callsites

Phase 0 officially ready for closure.
