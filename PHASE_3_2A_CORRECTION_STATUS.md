# Phase 3.2A Runtime Failure Corrections - Current Status

**Date:** 2026-08-03  
**Authority:** User Runtime Validation  
**Completion Target:** All 4 corrections before Phase 3.2B

---

## Correction 1: Test Runner (COMPLETE ✅)

**Status:** ✅ DONE  
**Commit:** `dc22f50`  

### What Was Fixed
- ❌ **Was:** Reported "28/28 tests" but only executed 27
- ✅ **Now:** Derived counts from static test registry
  - Executed = TestCases.Count
  - Passed = actual PASS count
  - Failed = actual FAIL count  
  - Skipped = actual SKIP count

### Changes Made
- Phase3_2A_TestRunner.cs: Complete refactor with `List<TestCase>` registry
- Phase3_2A_CompleteTests.cs: Added public wrappers + missing `Test_GuardLimitHitsZeroInValidTests`
- Output: ASCII PASS/FAIL (no corrupted symbols), numbered 01-28
- Summary: `Executed=28, Passed=28, Failed=0, Skipped=0`

### Build Status
✅ Debug: 0 errors, 0 warnings  
✅ Release: 0 errors, 0 warnings  

---

## Correction 2: Metrics Semantics (TODO ⏳)

**Status:** ⏳ IN PROGRESS  
**Blocking:** Yes (cannot verify correctness without this)  

### Problem Statement
```
RaysComputed = 30744
CellsTraversed = 0  ← IMPOSSIBLE
EarlyOuts = 0       ← IMPOSSIBLE
```

No distinction between:
- Rays that **entered** DDA (RaysComputed)
- Rays **rejected pre-DDA** (RaysSkipped)
  - Below horizon
  - Low elevation (<5°)
  - Zero intensity

### Required Implementation

**Add fields to Foundation metrics:**
```csharp
public int SunVisibilityRaysComputed { get; private set; }      // Entered DDA
public int SunVisibilityRaysSkipped { get; private set; }        // Pre-DDA rejection
public int SunVisibilityBelowHorizonSkips { get; private set; }  // Subset of skipped
public int SunVisibilityLowElevationSkips { get; private set; }  // Subset of skipped
public int SunVisibilityCellsTraversed { get; private set; }     // Total cells across all rays
public int SunVisibilityMaxCellsPerRay { get; private set; }     // Peak in single ray
public int SunVisibilityEarlyOuts { get; private set; }          // Early terminations
public int SunVisibilityFreeRays { get; private set; }           // Transmittance > 0.5
public int SunVisibilityBlockedRays { get; private set; }        // Transmittance < 0.1
public int SunVisibilityGuardLimitHits { get; private set; }     // Guard limit hit
```

**Refactor BuildSunVisibilityFieldToSlot:**
1. Use `ref SunVisibilityRayStats` for each ray
2. Aggregate: `SunVisibilityCellsTraversed += stats.CellsVisited`
3. Count: `if (stats.EarlyOut) SunVisibilityEarlyOuts++`
4. Track: `if (stats.GuardLimitHit) SunVisibilityGuardLimitHits++`

**Expected Output (Diurnal):**
```
Sun Is Above Horizon = True
Sun Elevation = 45.0 degrees
Sun Intensity = 0.95
Sample Count = 30744
Rays Computed = 30744      ← All entered DDA
Rays Skipped = 0           ← None rejected
Below Horizon Skips = 0
Low Elevation Skips = 0
Cells Traversed = 61488    ← ~2 cells per ray (typical)
Average Cells Per Ray = 2.0
Maximum Cells Per Ray = 3
Early Outs = 0             ← No transmittance blocking
Free Rays = 30744          ← All free (no blockers)
Blocked Rays = 0
Guard Limit Hits = 0
```

**Files to Modify:**
- LightingV3Foundation.cs (BuildSunVisibilityFieldToSlot)
- SunVisibilityRayMarcher.cs (track GuardLimitHit in stats)
- DumpMetricsToConsole() - add new fields

---

## Correction 3: Debug Renderer Instrumentation (TODO ⏳)

**Status:** ⏳ PENDING  
**Blocking:** Yes (cannot visually validate without this)  

### Problem Statement
No counters to verify SunVisibility debug mode is actually drawing anything.

### Required Metrics
```csharp
public int SunVisibilitySamplesDrawn { get; set; }
public float SunVisibilityMinimumWorldX { get; set; }
public float SunVisibilityMaximumWorldX { get; set; }
public float SunVisibilityMinimumWorldY { get; set; }
public float SunVisibilityMaximumWorldY { get; set; }
```

### When Drawing SunVisibility Mode
- Initialize bounds to `float.MaxValue` / `float.MinValue`
- Iterate only `frameData.SunVisibility` (not SunOpacityBuffer)
- Limit to `frameData.SampleCount`
- Track min/max world coordinates
- Increment `SunVisibilitySamplesDrawn` per pixel
- DO NOT access Foundation (use only FrameData)

**Files to Modify:**
- DebugVisualization.cs

---

## Correction 4: Allocation Isolation (TODO ⏳)

**Status:** ⏳ PENDING  
**Blocking:** Yes (cannot determine root cause without this)  

### Problem Statement
297,585 bytes/update. Where does it come from?
- Foundation only?
- SunVisibility overhead?
- Architectural (rebuild every frame)?

### Three Scenarios (Release mode, same thread)

**Scenario A: Foundation Alone (No SunVisibility)**
```
SunVisibility disabled or not called
300 warmup updates
300 measured updates
Report: AllocatedBytesPerUpdate_FoundationOnly
```

**Scenario B: Foundation + SunVisibility Active**
```
SunVisibility enabled
300 warmup updates
300 measured updates
Report: AllocatedBytesPerUpdate_WithSunVisibility
```

**Scenario C: SunVisibility Isolated**
```
Reuse: same provider, region, buffers from B
No new provider creation during measurements
300 warmup updates (confirm NO resize)
300 measured updates
Report: AllocatedBytesPerUpdate_SunVisibilityOnly
```

**Expected Output:**
```
Scenario A
  AllocatedBytesPerUpdate = X

Scenario B
  AllocatedBytesPerUpdate = X + Y (SunVisibility overhead)

Scenario C
  AllocatedBytesPerUpdate = Y

If Y ≈ 0: Allocation is architectural (rebuilds, not bug)
If Y > 0: SunVisibility adds overhead
```

**Files to Create:**
- Phase3_2A_AllocationIsolationHarness.cs

---

## Hotkey Verification (TODO ⏳)

**Status:** ⏳ PENDING  
**Required Confirms:**
- Ctrl+Shift+L: Toggle Legacy/V3 (CHECK)
- Ctrl+Alt+1: Cycle debug mode (CHECK)
- Ctrl+Alt+2: Print metrics (CHECK)
- Ctrl+Shift+Alt+2: Reset metrics (CHECK)
- Ctrl+Alt+T: Run tests (CHECK)

**File to Verify:**
- PlayingState.cs (input handling)

---

## Build Status
✅ Phase 1 Commit: dc22f50  
✅ Debug: 0 errors, 0 warnings  
✅ Release: 0 errors, 0 warnings  

---

## Timeline

### Phase 1 (DONE)
- Test runner with static registry
- 28 tests derived, not hardcoded
- Build clean

### Phase 2 (NEXT)
- Metrics semantics implementation
- RaysComputed vs RaysSkipped distinction
- Per-ray cell aggregation
- Diagnostic logging

### Phase 3 (PARALLEL)
- Debug renderer instrumentation
- SunVisibilitySamplesDrawn counter
- Bounds tracking
- Visual verification

### Phase 4 (PARALLEL)
- Allocation isolation harness
- Scenario A/B/C measurements
- Root cause identification
- Zero-allocation verification

### Phase 5 (FINAL)
- Hotkey confirmation
- Complete metrics output
- New Phase 3.2A COMPLETE report

---

## Approval Gate

**Phase 3.2A is NOT approved until:**

1. ✅ Test runner executes all 28 tests accurately
2. ⏳ Metrics show CellsTraversed > 0 (with sun active)
3. ⏳ RaysComputed == SampleCount (no unexplained skips)
4. ⏳ Debug visual renders SunVisibility samples
5. ⏳ Allocation baseline established (Scenario A/B/C)
6. ⏳ All hotkeys working correctly
7. ⏳ Build clean (Debug + Release)

**When complete:**
- Report submitted with proofs
- User reviews findings
- THEN: Phase 3.2A marked APPROVED
- THEN: Phase 3.2B may begin

---

## Next Action (User Authority)

Merge Correction 1 commit `dc22f50`.  
Await implementations of Corrections 2-5.  
Do NOT begin Phase 3.2B.  
Do NOT apply rendering yet.
