# Phase 3.2A: Runtime Failures & Corrections

**Date:** 2026-08-03  
**Status:** IN PROGRESS - Addressing 4 Objective Runtime Failures  
**Authority:** User Runtime Validation

---

## 4 Objective Runtime Failures Identified

### 1. TEST RUNNER HARDCODED COUNTS (FAILED)
**Symptom:**
- Reported: "28/28 tests"
- Actual executed: 27
- Missing test: `GuardLimitHitsZeroInValidTests`

**Root Cause:**
- Test runner used hardcoded count (`28/28`)
- Did not derive from actual test case collection
- No way to detect missing tests

**Correction Applied:**
- ✅ Created static test case list: `List<TestCase> TestCases`
- ✅ Derived counts: `Executed`, `Passed`, `Failed`, `Skipped` (not hardcoded)
- ✅ Added missing test 28: `Test_GuardLimitHitsZeroInValidTests()`
- ✅ Output format: `01 PASS CompletelyFreePath ... 28 PASS GuardLimitHitsZeroInValidTests`
- ✅ ASCII PASS/FAIL (no corrupted symbols)
- ✅ Summary: `Executed = 28, Passed = 28, Failed = 0, Skipped = 0`

**Files Changed:**
- Phase3_2A_TestRunner.cs (completely refactored)
- Phase3_2A_CompleteTests.cs (added public wrappers + Test_GuardLimitHitsZeroInValidTests)

**Status:** ✅ COMPLETE

---

### 2. METRICS SEMANTICS BROKEN (IN PROGRESS)
**Symptom:**
```
RaysComputed = 30744
CellsTraversed = 0
EarlyOuts = 0
```
This is mathematically impossible. 30k rays computed with zero cells traversed?

**Root Cause:**
- No distinction between `RaysComputed` (entered DDA) vs `RaysSkipped` (pre-DDA rejection)
- CellsTraversed never aggregated per ray
- EarlyOuts never recorded

**Correction Needed:**
Define clearly:
- `RaysComputed`: Raios que realmente entraram no DDA
- `RaysSkipped`: Samples encerrados antes do DDA (sun below horizon, low elevation, zero intensity)
- `BelowHorizonSkips`: Subset of RaysSkipped
- `LowElevationSkips`: Subset of RaysSkipped
- `CellsTraversed`: Agregado de todas as células visitadas em RaysComputed
- `EarlyOuts`: Count de raios que saíram cedo (transmittance ≤ 0.001)
- `MaximumCellsPerRay`: Maior número de células em um único raio

**SunVisibilityRayStats Enhancement:**
```csharp
public struct SunVisibilityRayStats
{
    public int CellsVisited;        // Células visitadas neste raio
    public bool EarlyOut;            // Saída antecipada por transmittance
    public bool WasFree;             // transmittance > 0.5
    public bool WasBlocked;          // transmittance < 0.1
    public bool WasSkipped;          // Rejeitado pré-DDA
    public SunVisibilitySkipReason SkipReason;
    public float FinalTransmittance;
    public bool GuardLimitHit;       // [NEW] Limite guardião atingido
}
```

**Expected Output (Diurnal, No Blockers):**
```
SunIsAboveHorizon = true
SunIntensity = 0.95
SunElevation = 45.0 degrees
SampleCount = 30744
RaysComputed = 30744
RaysSkipped = 0
BelowHorizonSkips = 0
LowElevationSkips = 0
CellsTraversed = 61488 (approximately 2 cells/ray)
AverageCellsPerRay = 2.0
MaximumCellsPerRay = 3
EarlyOuts = 0
FreeRays = 30744
BlockedRays = 0
GuardLimitHits = 0
```

**Files to Modify:**
- SunVisibilityRayMarcher.cs (enhance struct, aggregate properly)
- SunVisibilityMetricsAggregator.cs (track per-ray and aggregate)
- LightingV3Foundation.cs (BuildSunVisibilityFieldToSlot accumulates stats)

**Status:** 🔄 IN PROGRESS

---

### 3. DEBUG RENDERER NOT INSTRUMENTED (TODO)
**Symptom:**
- No visual confirmation of sun visibility field drawing
- No counters for samples drawn
- No bounds tracking (world X/Y min/max)
- Cannot verify debug visual actually renders

**Required Metrics:**
```csharp
public int SunVisibilitySamplesDrawn { get; set; }
public float SunVisibilityMinimumWorldX { get; set; }
public float SunVisibilityMaximumWorldX { get; set; }
public float SunVisibilityMinimumWorldY { get; set; }
public float SunVisibilityMaximumWorldY { get; set; }
```

**When SunVisibility Mode Active:**
- SamplesDrawn must equal FrameData.SampleCount
- Bounds must be non-zero (unless region really at origin)
- Iterate only `frameData.SunVisibility`
- Limit to `frameData.SampleCount`
- Use `ActiveRegionSnapshot` from same FrameData
- DO NOT access Foundation during Draw

**Files to Modify:**
- DebugVisualization.cs (add instrumentation)

**Status:** ⏳ TODO

---

### 4. ALLOCATION ISOLATION MISSING (TODO)
**Symptom:**
```
Scenario A (Foundation only):        ~?? bytes/update
Scenario B (Foundation + SunVisibility): 297,585 bytes/update
Scenario C (SunVisibility isolated):     ~?? bytes/update
```
Cannot determine which component allocates.

**Required Measurements:**
Three separate runs in Release mode, same thread:

**Scenario A:** Foundation with SunVisibility DISABLED
```
- Create foundation
- Warmup: 300 updates (no resize)
- Measure: 300 updates
- Report: BytesPerUpdate_FoundationOnly
```

**Scenario B:** Foundation with SunVisibility ENABLED
```
- Create foundation + provider
- Warmup: 300 updates (no resize)
- Measure: 300 updates
- Report: BytesPerUpdate_WithSunVisibility
```

**Scenario C:** SunVisibility isolated (reuse buffers)
```
- Pre-allocate foundation + buffers
- Reuse provider and region
- Warmup: 300 updates (NO resize, NO provider creation)
- Measure: 300 updates
- Report: BytesPerUpdate_SunVisibilityOnly
```

**Expected Output:**
```
Scenario A
  FoundationInstanceId = 0x123456
  SampleCountBefore = 2560
  SampleCountAfter = 2560
  FrontCapacityBefore = 2560
  FrontCapacityAfter = 2560
  BackCapacityBefore = 2560
  BackCapacityAfter = 2560
  BytesBefore = 2,000,000
  BytesAfter = 2,050,000
  TotalAllocatedBytes = 50,000
  AllocatedBytesPerUpdate = 166.67

Scenario B
  ... same as A but with additional SunVisibility overhead
  AllocatedBytesPerUpdate = X

Scenario C
  ... SunVisibility only
  AllocatedBytesPerUpdate = Y
```

**Difference Analysis:**
- Scenario B - Scenario A = SunVisibility overhead
- If C = 0, then all allocation is architectural (rebuilds every frame)

**Files to Modify:**
- Create new: Phase3_2A_AllocationIsolationHarness.cs

**Status:** ⏳ TODO

---

## Hotkey Configuration (MUST VERIFY)

**Current State:**
- Ctrl+Shift+L: Toggle Legacy/V3 (CHECK)
- Ctrl+Alt+1: Cycle debug mode (CHECK)
- Ctrl+Alt+2: Print metrics (CHECK)
- Ctrl+Shift+Alt+2: Reset metrics (CHECK)
- Ctrl+Alt+T: Run tests (CHECK)

**Action:** Verify in PlayingState.cs

---

## Staged Delivery Plan

### Phase 1: Test Runner (DONE)
✅ Static test list  
✅ Test 28 added  
✅ Derived counts  
✅ ASCII output  

### Phase 2: Metrics Semantics (IN PROGRESS)
🔄 RaysComputed vs RaysSkipped distinction  
🔄 CellsTraversed aggregation per ray  
🔄 EarlyOuts tracking  
🔄 GuardLimitHit recording  

### Phase 3: Debug Instrumentation (TODO)
⏳ SunVisibilitySamplesDrawn  
⏳ Bounds tracking  
⏳ Verify visual rendering  

### Phase 4: Allocation Isolation (TODO)
⏳ Scenario A/B/C measurements  
⏳ Zero-allocation verification  
⏳ Determine root cause  

### Phase 5: Hotkey Verification (TODO)
⏳ Confirm all hotkeys  
⏳ Document exact bindings  

---

## Build Status
✅ Debug: 0 errors, 0 warnings  
✅ Release: 0 errors, 0 warnings  

---

## Next Steps (User Authority)
1. Merge Phase 1 (test runner)
2. Wait for Phases 2-4 completion
3. Do NOT approve Phase 3.2A until all 4 failures resolved
4. Do NOT begin Phase 3.2B
5. Do NOT apply rendering yet
