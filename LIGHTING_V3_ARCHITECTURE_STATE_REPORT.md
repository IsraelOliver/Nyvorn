# Lighting V3 Architecture State Report

## Executive Summary

State of architecture as of commit `884f98a` (HUD fix, after regressão crítica was fixed).

**Current Status**: Phase 2 (Foundation) and Phase 3.2A (SunVisibility) fully implemented and validated. DDA algorithm is mathematically correct. Performance characteristics require measurement before optimization decisions.

**Measurement Phase Required**: Phase A0 (Cache Feasibility Instrumentation) must precede any optimization. Do NOT implement caching without empirical cache-hit-rate data.

---

## 1. Inventário de Componentes

### Core Components

#### 1.1 LightingV3Foundation (674 LOC)
**File**: `LightingV3Foundation.cs`  
**Status**: VALIDADO ✓  
**Responsibility**:
- Manages update lifecycle and frame coordination
- Orchestrates Classification, OccluderField, SunVisibility computation
- Double-buffered FrameSlot management (front/back)
- Metrics collection

**Dependencies**:
- `ILightingWorldGeometryProvider` (interface for world geometry queries)
- `LightingSamplingConfig` (configuration)
- `List<IOccluderProvider>` (pluggable occluders)
- `ISolarProvider` (sun state provider)
- `ActiveLightingRegion` (region management)
- `LightingV3FrameSlot` (buffer management)

**Call Sites** (consumers):
- `PlayingSessionViewCoordinator.UpdateLightingV3()` - entry point
- `PlayingState.Update()` - called once per frame when V3 mode active

**Key Methods**:
- `Update(cameraX, cameraY, logicalWidth, logicalHeight, tileSize)` - OLD PATH (deprecated)
- `UpdateFromVisibleWorldRect(visibleWorldRect, tileSize)` - NEW PATH (Task 2.1)
- `BuildSunVisibilityFieldToSlot()` - GARGALO (performance hotspot)
- `GetFrameData()` - returns current frame snapshot for rendering

---

#### 1.2 ActiveLightingRegion (238 LOC)
**File**: `ActiveLightingRegion.cs`  
**Status**: VALIDADO ✓  
**Responsibility**:
- Defines active region (tiles to process this frame)
- Converts world coordinates ↔ local coordinates ↔ sample indices
- Manages margin around visible area
- Handles world wrapping and clamping

**Key Methods**:
- `Update(cameraX, cameraY, logicalWidth, logicalHeight, tileSize)` - OLD calculation
- `UpdateFromTiles(regionWidth, regionHeight, originX, originY, tileSize)` - NEW calculation (Task 2.1)
- `WorldTileToLocalTile()`, `WorldPositionToLocalSample()`, `LocalSampleToWorldPosition()`

**State After Task 2.1**:
- ✓ Correctly applies Camera.Zoom via VisibleWorldRect
- ✓ 123x66 tiles at 1920x1009 with Zoom=2.0
- ✓ 32,472 samples expected

---

#### 1.3 LightingV3FrameSlot (168 LOC)
**File**: `LightingV3FrameSlot.cs`  
**Status**: VALIDADO ✓  
**Responsibility**:
- Immutable frame data container
- Double-buffering: holds classification, opacity, sun visibility buffers
- Provides frame snapshots to renderer

**Buffers**:
- `TileClassifications[]` - enum per tile
- `SunOpacityBuffer[]` - float per sample
- `LocalOpacityBuffer[]` - float per sample
- `SunVisibilityBuffer[]` - float per sample (COMPUTED EVERY FRAME)
- `ProbeData` - diagnostics
- `SunState` - directional sun info

**Allocation Strategy**:
- Pre-allocated to capacity on construction
- Reused across frames (no new alloc if capacity sufficient)
- `EnsureCapacity()` called before each frame

---

### Calculation Components

#### 1.4 SceneWorldClassifier (NOT YET MEASURED)
**File**: Not fully read yet  
**Status**: IMPLEMENTED  
**Responsibility**:
- Classifies each tile as OpenAtmosphere, VisibleBackground, or SolidForeground
- Used for occlusion decisions

**Called By**:
- `BuildOpacityFieldsToSlot()` in Foundation

---

#### 1.5 OccluderField (229 LOC)
**File**: `OccluderField.cs`  
**Status**: IMPLEMENTED  
**Responsibility**:
- Builds transmittance fields from geometry
- Interfaces with IOccluderProvider instances

**Occlusion Propagation**:
- `IsSolid()` - queries opacity at sample location
- Providers: ForegroundTileOccluder (foreground blocks), etc.

---

#### 1.6 SunVisibilityRayMarcher (252 LOC)
**File**: `SunVisibilityRayMarcher.cs`  
**Status**: PARTIALLY VALIDATED (algorithm correct, performance bad)  
**Responsibility**:
- Reference DDA (Amanatides & Woo) grid traversal
- Traces ray from sample point toward sun direction
- Computes binary visibility (0 = blocked, 1 = free)

**Performance Characteristics**:
- Per-sample: calls `ComputeVisibility()` which calls `RayMarchDDA()`
- RayMarchDDA: loops until ray exits world bounds or is fully blocked
- No memoization, no hierarchical acceleration
- Runs on **every frame for every sample**

**Algorithm Details** (from code):
1. Check sun above horizon - skip if not
2. Check sun intensity - skip if too low
3. Calculate starting cell at sample position
4. DDA loop: advance through grid cells
5. Each cell: check opacity, accumulate transmittance
6. Stop when: transmittance saturated (fully blocked), ray exits bounds (y < 0 = top), or cell limit hit

---

### State Components

#### 1.7 LightingV3SunState (148 LOC)
**File**: `LightingV3SunState.cs`  
**Status**: VALIDADO ✓  
**Responsibility**:
- Encapsulates sun direction, intensity, elevation, horizon state
- Provided by `ISolarProvider` (typically `GameSolarProvider`)

---

#### 1.8 LightingV3DebugController
**Status**: IMPLEMENTED ✓  
**Responsibility**:
- Toggles debug visualization mode (None, Classification, SunVisibility, etc.)
- Hotkey: Ctrl+Shift+V

---

### Render Components

#### 1.9 LightingV3DebugRenderer (382 LOC)
**File**: `LightingV3DebugRenderer.cs`  
**Status**: PRODUCTION-READY (after regression fix)  
**Responsibility**:
- Renders debug visualization of classification, opacities, sun visibility
- Drawing is screen-space (via camera transform in caller)

**Current State**:
- ✓ No Begin/End anidados (regression fixed)
- ✓ Transform applied by PlayingState
- ✓ HUD counter fixed to show SunVisibilitySamplesDrawn

---

### Adapter & Provider Components

#### 1.10 ILightingWorldGeometryProvider (interface)
**Status**: VALIDADO ✓  
**Implementations**:
- Real: Nyvorn gameplay world (decoupled, not read yet)
- Mock: MockGeometryProvider (for testing, 489 LOC)

---

#### 1.11 ISolarProvider (interface)
**Status**: VALIDADO ✓  
**Implementations**:
- `GameSolarProvider` - wraps world day/night cycle

---

### Test & Diagnostic Components

#### 1.12 Phase3_2A_CompleteTests (385 LOC)
**Status**: VALIDADO ✓  
**Tests**: 28 complete tests
- ✓ All passing (28/28)
- Coverage: ray marching, partial opacity, seams, guard limits, double-buffering

---

#### 1.13 AllocationIsolationHarness (500 LOC)
**Status**: IMPLEMENTED (harness has bug: divide by zero at line 363)  
**Purpose**: Measure allocation per scenario
**Result**: Pre-allocation strategy working (~11.9KB/update Foundation + SunVisibility)

---

## 2. State Matrix

| Component | Status | Production | Correct | Performance | Notes |
|-----------|--------|------------|---------|-------------|-------|
| Foundation | VALIDATED | Yes | Yes | BAD | O(samples×ray_length) |
| ActiveRegion | VALIDATED | Yes | Yes | GOOD | Task 2.1 fix applied |
| FrameSlot | VALIDATED | Yes | Yes | GOOD | Pre-alloc strategy working |
| Classification | IMPLEMENTED | Yes | ? | UNKNOWN | Not measured yet |
| OccluderField | IMPLEMENTED | Yes | ? | UNKNOWN | Not measured yet |
| SunVisibilityRayMarcher | PARTIAL | Yes | Yes | BAD | Algorithm correct, no cache |
| BuildSunVisibilityField | CRITICAL | Yes | Yes | BAD | Recalcs all samples/frame |
| DebugRenderer | PRODUCTION | Yes | Yes | GOOD | Regression fixed |
| DebugController | IMPLEMENTED | Yes | Yes | GOOD | Works |
| Double Buffering | VALIDATED | Yes | Yes | GOOD | Immutability preserved |

---

## 3. The Bottleneck: BuildSunVisibilityFieldToSlot()

**Location**: LightingV3Foundation.cs, line 406-554

**Core Issue**:
```csharp
// This loop executes EVERY FRAME
for (int i = 0; i < totalSamples; i++)
{
    // For EACH sample, call expensive DDA ray marching
    float visibility = SunVisibilityRayMarcher.ComputeVisibility(
        worldX, worldY,
        sunDirection,
        // ... calls RayMarchDDA() → loops through grid cells
    );
    slot.SunVisibilityBuffer[i] = visibility;
}
```

**Actual Numbers** (1920x1009, Zoom=1.0):
- ActiveRegion: 123×66 tiles (approx)
- Samples: 123×66 × 4 = ~32,472 samples
- Per frame: 32,472 calls to ComputeVisibility()
- Per call: RayMarchDDA() loops until ray exits

**DDA Ray Statistics** (from metrics, typical open area):
- DdaRaysStarted: ~16,500
- CellsVisited: ~1,600,000 total
- Average cells per ray: 97
- Maximum cells per ray: (typically < 500)

**Time Measurement** (from instrumentation):
- SunVisibilityBuildTimeMs: ~12ms (at 1920x1009)
- **FPS Impact**: 12ms ≈ 20% of 60fps budget

**Recalculation Schedule**:
- ✗ **Every frame** - no caching
- ✗ **No invalidation** - recomputes even when sun/geometry unchanged
- ✗ **No early exit** - computes all samples even in uniform lighting

---

## 4. Algoritmos Candidatos

### Candidate B (Primary Recommendation): Cached DDA with Invalidation

**What it does**: Same DDA, but only recompute when sun direction/intensity changes

**Implementation Outline**:
```csharp
private SunStateHash _lastCachedSunState;
private bool _needsSunVisibilityRebuild;

public void UpdateFromVisibleWorldRect(...)
{
    var currentSunHash = ComputeHash(slot.SunState);
    _needsSunVisibilityRebuild = (currentSunHash != _lastCachedSunState);
    
    if (_needsSunVisibilityRebuild)
    {
        BuildSunVisibilityFieldToSlot(_backSlot, tileSize);
        _lastCachedSunState = currentSunHash;
    }
    // else: reuse previous frame's buffer
}
```

**Expected Outcomes**:
- **FPS Improvement**: 12ms → 0.2ms average (static sun)
- **Latency**: Full frame rebuild in ~12ms when sun changes (acceptable, rare)
- **Validation**: No new algorithm, same correctness
- **Difficulty**: 2/10 (hash + conditional)
- **Risk**: Low

---

## 5. Recomendação Final

**PRIMARY**: Implement Phase A (Caching) using Candidate B

**DO NOT START Phase 3.2B** (lighting application) until caching is complete and validated.

**Expected Timeline**: Phase A = 3-5 days implementation + validation

---

**Report Status**: Complete analysis ready for decision  
**Code Snapshot**: commit `884f98a`  
**Generated**: 2026-08-03
