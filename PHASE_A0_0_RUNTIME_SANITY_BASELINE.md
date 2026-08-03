# Phase A0.0: Runtime Sanity Baseline

## Objective

Establish ground truth for frame timing before Phase A (cache feasibility measurement).

**Critical Issue**: Contradiction between reported timings (2-5ms vs 12ms) and unresolved <1 FPS regression.

---

## 1. Resolve Timing Contradiction

### Documented Measurements

From conversation history:

**Measurement 1** (implied 12ms):
- Context: "12ms SunVisibility (vs ~17ms before)"
- Status: Edge case or typical?
- Conditions: UNKNOWN

**Measurement 2** (agent report 2-5ms):
- Context: "2-5ms típico (dia claro), 50+ subterrâneo"
- Status: Claimed as "typical"
- Conditions: UNKNOWN

### Required Clarification

For EACH measurement, document:
- Commit hash
- Resolution (1920x1009, 2560x1440, etc.)
- Camera.Zoom value
- ActiveRegion tile count (e.g., 123x66)
- SampleCount value
- World position (surface, underground, etc.)
- Solar time (sunrise, noon, sunset, night)
- Debugger: ON or OFF
- Metric type: Single frame instantaneous, or 120-frame average?
- Full frame timing or just SunVisibility component
- ActiveRegion origin change frequency during measurement
- Is this measurement from audited code or from memory?

### Expected Outcome

Clear explanation of:
- Why 12ms appears in history
- Why 2-5ms appears in audit
- Which is correct for which scenario
- Why they differ (if both valid)

**Formula Verification**:
- 2ms = ~12% of 16.67ms ✓
- 5ms = ~30% of 16.67ms ✓
- 12ms = ~72% of 16.67ms ✓

---

## 2. Sanity Baseline Measurement

### Test Setup

**Environment**:
- Release build (no Debug symbols)
- NO debugger attached
- NO telemetry yet (baseline = current code)
- 1920x1009 resolution
- Camera.Zoom = 1.0
- Player at world origin
- Normal sun cycle
- No player movement
- No debug visualization
- No active audit helpers

### Three Test Cases

#### Test A: Legacy Pipeline (600 frames)
```
Preconditions:
  - Press Ctrl+Shift+J to toggle to Legacy mode
  - Wait 30 frames for stabilization
  - Start measurement (frame 0)
  - Run for 600 frames (10 seconds @ 60fps)
  - No input during test
```

#### Test B: V3 Mode, Debug=None (600 frames)
```
Preconditions:
  - Press Ctrl+Shift+J to toggle to V3 mode
  - Verify debug mode is off (Ctrl+Shift+V to cycle to None)
  - Wait 30 frames for stabilization
  - Start measurement (frame 0)
  - Run for 600 frames
  - No input during test
```

#### Test C: V3 Mode, Debug=SunVisibility (600 frames)
```
Preconditions:
  - V3 mode already active
  - Press Ctrl+Shift+V until SunVisibility mode active
  - Wait 30 frames for stabilization
  - Start measurement (frame 0)
  - Run for 600 frames
  - No input during test
```

### Metrics Collected Per Frame

**Timing (milliseconds)**:
```csharp
public struct FrameTimingMetrics
{
    public double TotalUpdateMs;           // Update() total time
    public double TotalDrawMs;             // Draw() total time
    
    public double FoundationUpdateMs;      // Foundation.Update() or UpdateFromVisibleWorldRect()
    public double ClassificationMs;        // ClassifyRegionToSlot()
    public double OccluderBuildMs;         // BuildOpacityFieldsToSlot()
    public double SunVisibilityBuildMs;    // BuildSunVisibilityFieldToSlot()
    
    public double DebugRendererMs;         // LightingV3DebugRenderer.Render()
    public double RenderSunVisibilityMs;   // RenderSunVisibility() within renderer
    public double DebugCompositeMs;        // Composition of debug RT
    
    public double HudDrawMs;               // HUD text rendering
    
    public double FPS;                     // 1000.0 / (TotalUpdateMs + TotalDrawMs)
}
```

**Call Counts Per Frame**:
```csharp
public struct CallCountMetrics
{
    public int FoundationUpdateCalls;      // Should be 1
    public int BuildSunVisibilityCalls;    // Should be 1
    public int DebugRendererCalls;         // Should be 0 or 1
    public int RenderSunVisibilityCalls;   // Should be 0 (None) or 1 (SunVisibility)
    public int DebugCompositeCalls;        // Should be 0 or 1
    public int HudDrawCalls;               // Should be 1
}
```

### Collection Strategy

**NO console output per frame** (too slow).

Use circular buffer (600 slots):
```csharp
private FrameTimingMetrics[] _frameMetrics = new FrameTimingMetrics[600];
private CallCountMetrics[] _frameCallCounts = new CallCountMetrics[600];
private int _currentFrameIndex = 0;
private bool _baselineMeasurementActive = false;
```

After 600 frames, dump to file:
```
BASELINE_LEGACY.txt
BASELINE_V3_NONE.txt
BASELINE_V3_SUNVISIBILITY.txt
```

### Output Format

```
==========================================================
BASELINE MEASUREMENT: [Test Name]
==========================================================

Duration: 600 frames (10.0 seconds @ 60 FPS nominal)

TIMING STATISTICS (milliseconds):

TotalUpdateMs:
  Average:    X.XXX
  P95:        X.XXX
  Max:        X.XXX

TotalDrawMs:
  Average:    X.XXX
  P95:        X.XXX
  Max:        X.XXX

FoundationUpdateMs:
  Average:    X.XXX
  P95:        X.XXX
  Max:        X.XXX

ClassificationMs:
  Average:    X.XXX
  P95:        X.XXX
  Max:        X.XXX

OccluderBuildMs:
  Average:    X.XXX
  P95:        X.XXX
  Max:        X.XXX

SunVisibilityBuildMs:
  Average:    X.XXX
  P95:        X.XXX
  Max:        X.XXX

DebugRendererMs:
  Average:    X.XXX
  P95:        X.XXX
  Max:        X.XXX

RenderSunVisibilityMs:
  Average:    X.XXX
  P95:        X.XXX
  Max:        X.XXX

DebugCompositeMs:
  Average:    X.XXX
  P95:        X.XXX
  Max:        X.XXX

HudDrawMs:
  Average:    X.XXX
  P95:        X.XXX
  Max:        X.XXX

FPS:
  Average:    X.X
  Minimum:    X.X

CALL COUNT STATISTICS (should be ≤ 1 per frame normally):

FoundationUpdateCalls:
  Average:    X.XXX
  Max:        X
  Warning if > 1

BuildSunVisibilityCalls:
  Average:    X.XXX
  Max:        X
  Warning if > 1

DebugRendererCalls:
  Average:    X.XXX
  Max:        X

RenderSunVisibilityCalls:
  Average:    X.XXX
  Max:        X

DebugCompositeCalls:
  Average:    X.XXX
  Max:        X

HudDrawCalls:
  Average:    X.XXX
  Max:        X
  Warning if != 1

ANOMALIES:

If TotalUpdateMs.P95 > 5ms:
  - Identify which component exceeded budget
  
If TotalDrawMs.P95 > 10ms:
  - Identify which component exceeded budget

If FPS.Minimum < 60:
  - Report worst 5 frames with timestamps
  - Report which metric spiked
  
If CallCount.Max > 1 for Update/SunVisibility:
  - RED FLAG: component called multiple times per frame
  - Identify frame numbers where this occurred

==========================================================
```

---

## 3. Locate <1 FPS Regression

### If Sanity Baseline Shows <1 FPS

**Audit Checklist**:

- [ ] **Loop Duplication**: Is BuildSunVisibilityFieldToSlot() called multiple times per frame?
  - Check: CallCountMetrics.BuildSunVisibilityCalls > 1

- [ ] **Foundation Called Multiple Times**: Foundation.Update() called more than once?
  - Check: CallCountMetrics.FoundationUpdateCalls > 1

- [ ] **Debug Renderer Active in Mode=None**: LightingV3DebugRenderer.Render() running when it shouldn't?
  - Check: CallCountMetrics.DebugRendererCalls > 0 when mode=None
  - Issue: Early return not working

- [ ] **Duplicate Rendering**: RenderSunVisibility() called for same frame twice?
  - Check: CallCountMetrics.RenderSunVisibilityCalls > 1 in debug mode

- [ ] **Begin/End Unbalanced**: SpriteBatch begin/end mismatch causing deadlock?
  - Check: If one of the draw times suddenly freezes high
  - Action: Add BeginCount/EndCount metrics

- [ ] **RenderTarget Thrashing**: SetRenderTarget called multiple times?
  - Track: SetRenderTargetCount per frame (should be constant)

- [ ] **SetData/GetData**: Texture or buffer data uploads/downloads?
  - Check: GPU stalls from ReadData
  - Action: Add metrics for GPU waits

- [ ] **LINQ in Hot Path**: Any .Where(), .Select(), .ToList()?
  - Check: Search code for hot path LINQ
  - Metric: AllocatedBytesPerUpdate

- [ ] **String Creation**: String concatenation in Update/Draw?
  - Check: No debug logging with string format
  - Metric: GC collections per frame

- [ ] **Reflection**: Any reflection calls?
  - Check: GetType(), MethodInfo.Invoke()

- [ ] **Console Output**: Console.WriteLine per frame?
  - Check: Spamming output while measuring
  - Fix: Disable diagnostic logging

- [ ] **Audit Helper Running**: ActiveRegionAuditHelper executing without hotkey?
  - Check: CaptureSnapshot() being called automatically
  - Fix: Should only run on hotkey

- [ ] **Harness Running**: AllocationIsolationHarness or other test harness active?
  - Check: Test code running during gameplay
  - Fix: Disable test harness

- [ ] **Wait/Lock/Sync**: Any Thread.Sleep, Monitor.Wait, or Mutex?
  - Check: No blocking operations
  - Metric: LockContention count

---

## 4. Telemetry Overhead

### Two Measurement Passes

**Pass 1: NO Instrumentation** (baseline code as-is)
- Run Test A, B, C
- Collect baseline FPS

**Pass 2: WITH Instrumentation** (add timing code)
- Run Test A, B, C again
- Collect instrumented FPS

### Overhead Calculation

```
TelemetryOverheadMs = (No-Instr FPS) - (With-Instr FPS)
TelemetryOverheadPercent = TelemetryOverheadMs / FrameBudgetMs
```

**Requirement**: TelemetryOverheadMs < 0.5ms (< 3% of budget)

If overhead > 0.5ms:
- Optimize: Remove expensive timing calls from hot loop
- Use: Less frequent sampling (every 10 frames)
- Goal: Get overhead < 0.5ms

---

## 5. Phase A0 After Sanity Confirmation

ONLY AFTER baseline shows V3 Mode=None performing normally:

Proceed to Phase A (Cache Feasibility Simulation).

### Cache Invalidation Key Components

Must track:
- ActiveRegion.WorldOriginX/Y (origin change)
- ActiveRegion.RegionWidthTiles/RegionHeightTiles (size change)
- _activeRegion.RegionWidthSamples/RegionHeightSamples (sampling change)
- tileSize (should be constant)
- sunState.DirectionToSun (direction change)
- sunState.IsAboveHorizon (horizon change)
- sunState.Intensity (intensity change)
- geometryVersion (foreground blocks changed)
- providerVersion (sun opacity provider changed)
- worldIdentity (loaded different world)

**Angular Tolerance Strategies**:
1. Raw float comparison
2. 0.1° tolerance
3. 0.25° tolerance
4. 0.5° tolerance

### Metrics Collected (Phase A Proper)

```csharp
public struct CacheSimulationMetrics
{
    public float CacheHitRate;              // %
    public int HitCount;
    public int MissCount;
    
    public float RebuildsPerSecond;         // Hz
    public float AverageRebuildMs;
    public float P95RebuildMs;
    public float MaxRebuildMs;
    
    public float EstimatedTimeAvoided;      // ms over 600 frames
    
    public float AverageReusableStreak;     // frames
    public int MaximumReusableStreak;       // frames
    
    public Dictionary<InvalidationReason, int> InvalidationCounts;
    
    public float FrameTimeWithCacheP95;     // ms (theoretical)
    public float FrameTimeWithCacheMax;     // ms (theoretical)
}
```

---

## 6. Scenario Matrix

### Minimum Required Scenarios

**Behaviors**:
- Parado (stationary)
- Andando (walking)
- Subterrâneo (underground)

**Resolutions**:
- Full HD (1920x1009)
- 2K (2560x1440)

**Minimum Runs**:
- [ ] Parado em Full HD (600 frames)
- [ ] Parado em 2K (600 frames)
- [ ] Andando em Full HD (600 frames)
- [ ] Andando em 2K (600 frames)
- [ ] Subterrâneo em Full HD (600 frames)

---

## 7. Decision Criteria (After All Data)

### NOT Just Cache Hit Rate

Consider:
- **Average rebuild cost**: If 5 ms but happens every frame, no win
- **P95 and max**: If cache miss is 50ms, very bad user experience
- **Reusable streaks**: If always 1 frame apart (camera moving), cache useless
- **Invalidation frequency**: If geometry changes constantly, cache useless

### Possible Recommendations

**Option A: Cached DDA is Sufficient**
- Hit rate ≥ 85%
- P95 rebuild ≤ 2ms
- Average reusable streak ≥ 30 frames

**Option B: Cached DDA + Incremental Update**
- Hit rate 70-85%
- Origin change frequent but predictable
- Incremental buffer shifts would help

**Option C: Directional Transmittance Sweep**
- Hit rate < 70%
- Origin changes frequently (camera movement)
- Completely different algorithm needed

**Option D: Hybrid**
- Cache works stationary (90%+ hit)
- Fails during movement (50% hit)
- Use cache when static, sweep when moving

---

## 8. Constraints

### MUST NOT
- ❌ Implement cache yet
- ❌ Start Phase 3.2B
- ❌ Apply lighting to world
- ❌ Change visual output
- ❌ Modify DDA algorithm

### MUST
- ✅ Resolve timing contradiction
- ✅ Run sanity baseline (Release, no debugger)
- ✅ Locate <1 FPS regression
- ✅ Measure telemetry overhead
- ✅ Report call counts per frame
- ✅ Ensure no components called multiple times

---

## Success Criteria

**Phase A0.0 Complete When**:
- ✓ Timing contradiction resolved (2-5ms vs 12ms explained)
- ✓ Sanity baseline shows reasonable FPS (>30fps minimum)
- ✓ <1 FPS regression identified and root cause found
- ✓ Telemetry overhead measured (<0.5ms)
- ✓ Call counts verified (no duplicates)
- ✓ Ready to proceed to Phase A (cache simulation)

**If <1 FPS not resolved**: Debug that issue FIRST before Phase A.

---

**Status**: Phase A0.0 Specification Ready  
**Next**: Implement instrumentation in Foundation and run baseline measurements
