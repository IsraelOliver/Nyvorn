# Phase A0: Cache Feasibility Instrumentation

## Objective

Measure whether cached DDA (reusing SunVisibility field between frames) is viable and beneficial, WITHOUT implementing the cache yet.

The DDA algorithm continues to execute every frame as normal. We collect telemetry to determine: **if the field HAD been cached, when would it have been reused vs. rebuilt?**

---

## 1. Corrections to Previous Report

### Factual Corrections

- ✓ **123×66 tiles at 1920×1009 ONLY when Zoom=2.0**
  - At Zoom=1.0: expected ~245×129 tiles
  - Task 2.1 correctly applies zoom; this was verified

- ✓ **12ms represents ~72% of 16.67ms frame budget** (not 20%)
  - At 60 FPS: 16.67ms per frame
  - 12ms SunVisibility = 72% budget
  - This is significant but within acceptable range

- ✓ **DebugRenderer is NOT production-ready without runtime gate**
  - Currently renders all frames if V3 mode enabled
  - Needs conditional: only if debug mode active
  - Status downgraded: PROTOTYPE (not PRODUCTION)

- ✓ **Allocation harness is BROKEN (divide by zero at line 363)**
  - Cannot trust allocation measurements
  - Must fix before claiming "zero allocation" or "pre-allocation validated"
  - Status: DO NOT RELY ON ALLOCATION DATA from harness

- ✓ **Cached DDA recommendation removed**
  - Previous claim: "resolves 90% of problem"
  - Unsupported without cache-hit-rate measurement
  - Phase A0 exists to gather this data

### Missing Audit Items

The following remain unmeasured and must be analyzed before declaring audit complete:

- [ ] SceneWorldClassifier.cs (full implementation)
- [ ] Geometry provider implementation (gameplay-side)
- [ ] All IOccluderProvider implementations
- [ ] GameSolarProvider integration with day/night cycle
- [ ] WorldMap versioning/change notification system
- [ ] World Renderer call sites and VisibleWorldRect usage
- [ ] ActiveRegion origin change frequency during normal gameplay

---

## 2. Invalidation Simulation Strategy

### Without Modifying BuildSunVisibilityFieldToSlot()

We add parallel tracking (no performance cost):

```csharp
public class SunVisibilityCacheSimulation
{
    // Current frame invalidation state
    private SunVisibilityInvalidationReason _currentInvalidationReason;
    private bool _wouldRebuildThisFrame;
    
    // Aggregated telemetry (120-frame rolling window)
    private int _simulatedCacheHits;
    private int _simulatedCacheMisses;
    private int _consecutiveReusableFrames;
    private int _maxConsecutiveReusableFrames;
    
    // Per-invalidation-reason counters
    private Dictionary<SunVisibilityInvalidationReason, int> _invalidationCounts;
    
    // Angular tolerance variants (tested in parallel)
    private float _currentAngularErrorRaw;
    private float _currentAngularErrorTolerance01;
    private float _currentAngularErrorTolerance025;
    private float _currentAngularErrorTolerance05;
}

public enum SunVisibilityInvalidationReason
{
    None = 0,
    FirstFrame = 1,
    ActiveRegionOriginChanged = 2,
    ActiveRegionSizeChanged = 4,
    SunDirectionChanged = 8,
    HorizonChanged = 16,
    IntensityChanged = 32,
    GeometryVersionChanged = 64,
    SamplingConfigChanged = 128,
    ProviderVersionChanged = 256,
}
```

### Per-Frame Simulation Logic

```csharp
public void SimulateInvalidationCheck(
    in VisibleWorldRect visibleWorldRect,
    int tileSize,
    in LightingV3SunState sunState,
    int geometryVersion,
    int providerVersion)
{
    _currentInvalidationReason = SunVisibilityInvalidationReason.None;
    _wouldRebuildThisFrame = false;
    
    // Check 1: First frame ever
    if (_lastSimulationFrameId == -1)
    {
        _currentInvalidationReason |= SunVisibilityInvalidationReason.FirstFrame;
        _wouldRebuildThisFrame = true;
    }
    
    // Check 2: ActiveRegion changed
    if (visibleWorldRect.Left != _lastValidVisibleWorldRect.Left ||
        visibleWorldRect.Top != _lastValidVisibleWorldRect.Top)
    {
        _currentInvalidationReason |= SunVisibilityInvalidationReason.ActiveRegionOriginChanged;
        _wouldRebuildThisFrame = true;
    }
    
    if (visibleWorldRect.Width != _lastValidVisibleWorldRect.Width ||
        visibleWorldRect.Height != _lastValidVisibleWorldRect.Height)
    {
        _currentInvalidationReason |= SunVisibilityInvalidationReason.ActiveRegionSizeChanged;
        _wouldRebuildThisFrame = true;
    }
    
    // Check 3: Sun Direction (multiple tolerances)
    float angularDifference = Vector2.Distance(
        sunState.DirectionToSun,
        _lastValidSunState.DirectionToSun);
    _currentAngularErrorRaw = angularDifference;
    
    if (angularDifference > 0.001f)  // Raw comparison
    {
        _currentInvalidationReason |= SunVisibilityInvalidationReason.SunDirectionChanged;
        _wouldRebuildThisFrame = true;
    }
    
    // Check 4: Horizon changed
    if (sunState.IsAboveHorizon != _lastValidSunState.IsAboveHorizon)
    {
        _currentInvalidationReason |= SunVisibilityInvalidationReason.HorizonChanged;
        _wouldRebuildThisFrame = true;
    }
    
    // Check 5: Intensity changed
    if (Math.Abs(sunState.Intensity - _lastValidSunState.Intensity) > 0.001f)
    {
        _currentInvalidationReason |= SunVisibilityInvalidationReason.IntensityChanged;
        _wouldRebuildThisFrame = true;
    }
    
    // Check 6: Geometry version changed
    if (geometryVersion != _lastValidGeometryVersion)
    {
        _currentInvalidationReason |= SunVisibilityInvalidationReason.GeometryVersionChanged;
        _wouldRebuildThisFrame = true;
    }
    
    // Aggregation
    if (_wouldRebuildThisFrame)
    {
        _simulatedCacheMisses++;
        _consecutiveReusableFrames = 0;
        _invalidationCounts[_currentInvalidationReason]++;
    }
    else
    {
        _simulatedCacheHits++;
        _consecutiveReusableFrames++;
        _maxConsecutiveReusableFrames = Math.Max(
            _maxConsecutiveReusableFrames,
            _consecutiveReusableFrames);
    }
    
    // Store for next frame
    _lastValidVisibleWorldRect = visibleWorldRect;
    _lastValidSunState = sunState;
    _lastValidGeometryVersion = geometryVersion;
    _lastSimulationFrameId++;
}
```

---

## 3. Solar Direction Tolerance Testing

### Four Parallel Measurements

No code changes - only telemetry. Each frame compute:

```csharp
enum SolarToleranceLevel
{
    RawComparison = 0,          // Vector distance > 0.001
    Tolerance01Degree = 1,      // ~0.01745 radians
    Tolerance025Degree = 2,     // ~0.00436 radians
    Tolerance05Degree = 3,      // ~0.00873 radians
}

struct SolarDirectionMetrics
{
    public float AngularErrorDegrees;
    public float AngularErrorRadians;
    public int HitCountRaw;
    public int HitCountTol01;
    public int HitCountTol025;
    public int HitCountTol05;
    public float HitRateRaw;
    public float HitRateTol01;
    public float HitRateTol025;
    public float HitRateTol05;
    public int RebuildCountRaw;
    public int RebuildCountTol01;
    public int RebuildCountTol025;
    public int RebuildCountTol05;
    public float AvgAngularError;
    public float MaxAngularError;
    public float MinAngularError;
}
```

### Validation Metrics Per Tolerance

For each tolerance level, track:
- **SimulatedCacheHitRate** (%)
- **RebuildsPerSecond** (Hz)
- **MaximumAngularError** (degrees)
- **AverageAngularError** (degrees)

Decision criteria:
- If HitRate < 80% → tolerance is too strict
- If HitRate > 95% → tolerance may be loose (validate against visual artifacts)
- Optimal: 85-92% hit rate with acceptable angular error

---

## 4. Gameplay Scenarios (60+ seconds each, Release build)

### Scenario A: Player Stationary on Surface
**Setup**: Player in forest/plains, no movement, normal sun cycle
**Expected**: High cache hit rate (only sun changes)
**Measures**:
- Cache hit rate
- Most common invalidation reason
- Consecutive reusable frames

### Scenario B: Player Walking Continuously
**Setup**: Player walking around terrain, sun cycle normal
**Expected**: Lower hit rate (ActiveRegion changes frequently)
**Measures**:
- Cache hit rate vs. active region change frequency
- Average reusable streak length
- Invalidation reason distribution

### Scenario C: Player Underground
**Setup**: Player in deep cave, exploring
**Expected**: Moderate (cave geometry static, sun doesn't matter below)
**Measures**:
- How often background wall geometry differs (should not invalidate)
- Foreground changes (should invalidate)
- Hit rate stability

### Scenario D: Player Mining and Placing Blocks
**Setup**: Player actively destroying/building terrain
**Expected**: Frequent invalidation (geometry version changes)
**Measures**:
- Geometry version change frequency
- How often invalidation is due to geometry vs. camera
- Estimated rebuild cost per block change

### Scenario E: Accelerated Sun Cycle
**Setup**: Time runs 10× faster, player stationary
**Expected**: Very low hit rate (sun changes every ~1.6 frames)
**Measures**:
- Cache hit rate with fast time
- How many frames between sun direction changes
- Estimated unnecessary rebuilds per day/night cycle

### Scenario F: Normal Sun Cycle
**Setup**: Time runs normal speed, player stationary
**Expected**: High hit rate (sun changes slowly)
**Measures**:
- Cache hit rate with normal day/night
- Frames between rebuilds (should be 30-60+)
- Cost per "day" in game time

### Scenario G: Full HD (1920×1080)
**Setup**: Scenario B on 1920×1080
**Expected**: Similar hit rate to G (resolution independent)
**Measures**:
- Verify resolution doesn't affect cache logic
- Performance scaling

### Scenario H: 2K Resolution (2560×1440)
**Setup**: Scenario B on 2560×1440
**Expected**: Similar hit rate (resolution independent)
**Measures**:
- Verify resolution doesn't affect cache logic
- Performance scaling with higher sample count

---

## 5. Output Format Per Scenario

```
SCENARIO: [Name]
DURATION: 60 seconds
RESOLUTION: [WxH]

STATISTICS:
  Total Frames Simulated: N
  Would-Rebuild Count: N
  Would-Reuse Count: N
  Cache Hit Rate: X.X%
  Average Reusable Streak: N frames
  Maximum Reusable Streak: N frames

INVALIDATION REASONS (cumulative):
  None: N (N%)
  FirstFrame: N (N%)
  ActiveRegionOriginChanged: N (N%)
  ActiveRegionSizeChanged: N (N%)
  SunDirectionChanged: N (N%)
  HorizonChanged: N (N%)
  IntensityChanged: N (N%)
  GeometryVersionChanged: N (N%)
  Other: N (N%)

SOLAR TOLERANCE COMPARISON:
  Raw Comparison:    N hits (X.X%) from N frames
  0.1° Tolerance:    N hits (X.X%) from N frames
  0.25° Tolerance:   N hits (X.X%) from N frames
  0.5° Tolerance:    N hits (X.X%) from N frames
  
  Average Angular Error: X.XXX degrees
  Max Angular Error: X.XXX degrees
  Min Angular Error: X.XXX degrees

ESTIMATED TIME IMPACT:
  DDA Time Executed This Run: X.X ms total
  If Cache Implemented (at current hit rate): X.X ms avoided
  Rebuild Cost Per Cache Miss: X.X ms
  Net Benefit: X.X% FPS improvement potential
```

---

## 6. ActiveRegion Origin Change Frequency

### Key Measurement

Track when `_activeRegion.WorldOriginX/Y` changes during normal gameplay:

```csharp
struct ActiveRegionChangeMetrics
{
    public int OriginChangeCount;
    public int OriginX_ChangeCount;
    public int OriginY_ChangeCount;
    public float AvgFramesBetweenOriginChange;
    public int MaxFramesBetweenOriginChange;
    public int FramesWithoutOriginChange;  // Longest stable streak
}
```

**Critical**: If origin changes frequently (< 5 frames), field cache has limited benefit during exploration.

If origin is stable (> 20 frames typically), field cache can be very effective.

---

## 7. Geometry Change Detection

### WorldMap Versioning

**TODO**: Verify with gameplay code author:
1. Does WorldMap have `public int Version { get; }`?
2. Is there an event/notification on geometry change?
3. Does background tile change trigger version bump?
4. Does foreground tile change trigger version bump?

**Current Assumption** (verify):
- Background walls DO NOT invalidate SunVisibility (they don't block sun)
- Foreground blocks DO invalidate (they block sun)
- Opaque decorations might block sun (depends on geometry provider)

**Action**: Do NOT scan entire geometry. Rely on WorldMap versioning or provider change notification.

---

## 8. Fix Allocation Harness

### Issue at Line 363

```csharp
metrics.CellsVisitedAvg = foundation.SunVisibilityCellsVisited / foundation.SunVisibilityTraceCandidates;
```

**Fix**: Guard against division by zero
```csharp
metrics.CellsVisitedAvg = foundation.SunVisibilityTraceCandidates > 0
    ? foundation.SunVisibilityCellsVisited / (float)foundation.SunVisibilityTraceCandidates
    : 0f;
```

**Before Phase A0**: Harness must be fixed so allocation data can be collected reliably.

---

## 9. Decision Criteria

After collecting all data, CHOOSE ONE:

### Option A: Cached DDA is Sufficient
**If**:
- Cache hit rate >= 85% in at least 5/8 scenarios
- Maximum invalidation cost is acceptable (<5ms per rebuild)
- Most common invalidation is sun direction, NOT active region changes

**Action**: Implement Phase A (Caching)

---

### Option B: Cached DDA + Incremental Update Needed
**If**:
- Cache hit rate is 70-85%
- Active region origin changes frequently
- Benefit would improve with incremental buffer shifts

**Action**: Design Phase B (Incremental with overlapping region buffer)

---

### Option C: Directional Transmittance Sweep is Better
**If**:
- Cache hit rate < 70% even with optimal tolerance
- Active region changes invalidate cache > 40% of frames
- Camera movement is faster than sun movement

**Action**: Prototype Option C (different algorithm)

---

### Option D: Hybrid Approach
**If**:
- Cache works well when stationary (Option A)
- But fails during fast camera movement (Option B needed)

**Action**: Implement Phase A (Caching) + Phase B (Incremental) as combined solution

---

## 10. Constraints & Limitations

### What Phase A0 Will NOT Do

- ❌ Implement the cache (still execute DDA every frame)
- ❌ Change visual output
- ❌ Modify DDA algorithm
- ❌ Apply lighting to world
- ❌ Start Phase 3.2B

### What Phase A0 MUST Do

- ✅ Add instrumentation (zero performance cost in steady state)
- ✅ Collect 60+ seconds per scenario
- ✅ Measure 4 solar tolerances in parallel
- ✅ Fix allocation harness before claiming results
- ✅ Verify WorldMap versioning exists
- ✅ Complete audit of missing components
- ✅ Present data-driven recommendation

---

## Estimation

**Implementation Time**: 3-5 days
- Add instrumentation to Foundation (1-2 days)
- Run scenarios and collect data (1-2 days)
- Fix harness (0.5 day)
- Complete missing audit (0.5 day)
- Analysis and recommendation (0.5 day)

**No Code Production Impacts**: All changes are telemetry-only.

---

## Success Criteria for Phase A0

**Complete when**:
- ✓ All scenarios executed 60+ seconds in Release mode
- ✓ Cache hit rates calculated for all 4 solar tolerances
- ✓ Invalidation reason distribution clear
- ✓ ActiveRegion origin change frequency documented
- ✓ WorldMap versioning verified
- ✓ Allocation harness fixed and validated
- ✓ Missing audit components (SceneWorldClassifier, providers, etc.) fully read
- ✓ Data-driven recommendation (A/B/C/D) presented with confidence intervals
- ✓ Next phase (A, B, C, or D) clearly defined

**Do NOT proceed to Phase 3.2B until Phase A0 complete.**

---

**Status**: Phase A0 Definition Ready  
**Next**: Implement instrumentation in Foundation
