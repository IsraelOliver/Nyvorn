# Phase 3 Roadmap: Directional Lighting Implementation

**Status**: Ready to start (Phase 2 complete, validated)  
**Base**: Double-buffered immutable frame snapshots  
**Strategy**: Incremental subfases, not monolithic implementation

---

## Why Subfases?

Phase 2 taught us: trying to birth 5 systems at once creates cascading failures.

**Phase 2 failure mode**: Attempted to implement double-buffering + rendering + testing + logging + validation all together → divergent changes, hard to debug, unclear what actually fixed the drift.

**Phase 3 approach**: One subphase at a time, each with:
- ✅ Pass/fail criteria
- ✅ Regression tests
- ✅ Metrics baseline
- ✅ Cleanup before next phase

---

## Subphase 1: Sun Direction & Color

**Goal**: Store directional sun properties in frame data; verify they remain immutable

**Work**:
1. Add fields to LightingV3FrameSlot:
   - `Vector3 SunDirection` (normalized world direction)
   - `Color SunColor` (RGB + intensity)
   - `float SunIntensity` (0-1 scale)

2. Update Foundation.Update():
   - Query sun direction from game state
   - Store in back slot
   - Validate immutability in probe logs

3. Extend LightingV3FrameData:
   - Add corresponding readonly fields
   - No new allocations (just struct fields)

4. Update validation:
   - Extend Phase2Tests to verify sun properties persist
   - Add probe check: SunDirection != default

**Pass Criteria**:
- ✅ Sun properties logged in FoundationPublish
- ✅ RendererConsume sees identical values
- ✅ No allocation regressions
- ✅ All Phase 2 tests still passing
- ✅ Builds clean

**Time Estimate**: 1-2 hours  
**Risk**: Low (pure data addition, no logic)

---

## Subphase 2: Visibility Testing via Ray Marching

**Goal**: For each sample point, determine if sun is visible (ray marching through tiles)

**Work**:
1. Create `SunVisibilityBuffer[]` in LightingV3FrameSlot:
   - One float per sample (0.0 = blocked, 1.0 = visible)
   - Pre-allocate alongside opacity buffers

2. Implement ray march algorithm:
   - From sample world position
   - Follow SunDirection backwards to sky
   - Test tile classifications for occlusion
   - Return visibility factor

3. Update BuildOpacityFieldsToSlot():
   - After opacity calculation
   - For each sample: ray march and store visibility
   - Track march time separately

4. Add probe for visibility:
   - `ProbeSunVisibility` in frame data
   - Log alongside opacity values

**Pass Criteria**:
- ✅ Ray march produces sensible results (high noon = more visible)
- ✅ Shadowed areas show < 1.0 visibility
- ✅ No all-black or all-white regressions
- ✅ March time < 5 ms (should be ~1-2 ms)
- ✅ Probe logs show varying visibility (not constant)
- ✅ Phase 2 tests still passing

**Time Estimate**: 3-4 hours  
**Risk**: Medium (algorithm validation, edge cases)

---

## Subphase 3: Direct Surface Impact

**Goal**: Apply sun visibility + color to surface lighting values

**Work**:
1. Create `DirectSunLighting[]` buffer in slot:
   - One Color per sample
   - R,G,B for color, A for intensity

2. Calculate direct lighting in new method:
   - Directsun_sample = SunColor × SunIntensity × SunVisibility[sample]
   - Store in DirectSunLighting buffer

3. Update renderer to visualize:
   - Debug mode: show DirectSunLighting as colored view
   - Should show yellow/orange for lit areas, dark for shadows

4. Verify composition:
   - Direct lighting + existing opacity = correct appearance
   - No visual artifacts at tile boundaries

**Pass Criteria**:
- ✅ Direct lighting buffer populated
- ✅ Debug view shows expected colors
- ✅ Lit areas brighter than shadowed areas
- ✅ No allocation regressions
- ✅ Phase 2 tests still passing
- ✅ Probe includes sample direct lighting

**Time Estimate**: 2-3 hours  
**Risk**: Medium (visual validation required)

---

## Subphase 4: Directional Shadows

**Goal**: Propagate shadow information from occluders to affected samples

**Work**:
1. Extend BuildOpacityFieldsToSlot():
   - For each occluder tile
   - Calculate shadow cast in sun direction
   - Reduce visibility for affected samples

2. Create `ShadowCaster[]` utility:
   - Determine shadow range from tile + direction
   - Apply falloff (sharp edge for hard shadows)

3. Update visibility calculation:
   - Apply shadow casters before final visibility
   - Result: shadowed areas have partial/zero visibility

4. Add shadow metrics:
   - `ShadowCasterCount` in frame data
   - Track shadow calculation time

**Pass Criteria**:
- ✅ Shadows appear in direction of sun
- ✅ Multiple occluders cast multiple shadows (no overlap bugs)
- ✅ Shadow falloff is smooth (not stepped)
- ✅ Shadow time < 3 ms
- ✅ Phase 2 tests + Subphase 3 visual still valid
- ✅ Probe shows reduced visibility in shadows

**Time Estimate**: 4-5 hours  
**Risk**: High (shadow geometry complex, easy to get wrong)

---

## Subphase 5: Light Shafts in Fissures

**Goal**: Enhance fissure visibility by accumulating sun light through gaps

**Work**:
1. Detect fissure samples:
   - Mark samples at fissure boundaries
   - Flag as "shaft candidate"

2. Accumulate directional light:
   - For shaft samples, trace upward
   - Collect sun light from unobstructed path
   - Multiply by sun intensity

3. Create `LightShaftIntensity[]`:
   - One float per sample
   - 0 = no shaft, 1.0 = strong shaft

4. Update renderer:
   - Blend direct lighting + shaft intensity
   - Shafts should glow in fissures during day

**Pass Criteria**:
- ✅ Shafts appear in fissure cracks
- ✅ Shaft brightness varies with sun angle
- ✅ No shafts at night (sun below horizon)
- ✅ Shaft time < 2 ms
- ✅ All previous tests still passing

**Time Estimate**: 3-4 hours  
**Risk**: Medium (artistic validation, performance tuning)

---

## Subphase 6: Performance & Polish

**Goal**: Optimize previous work; stabilize metrics; cleanup

**Work**:
1. Profile all subphases:
   - Measure total update time
   - Identify bottlenecks (likely ray march or shadows)
   - Target: < 15 ms per update

2. Optimize critical paths:
   - Cache ray march results
   - Use spatial partitioning for shadow lookup
   - Consider multi-threaded classification/visibility

3. Remove debug probes:
   - Keep critical metrics
   - Remove verbose logging
   - Clean up console output

4. Stabilize metrics:
   - Document final timing/memory
   - Create performance baseline
   - Implement regression test

5. Final validation:
   - Run Phase2Tests + all visual checks
   - Verify builds clean
   - Document Phase 3 completion

**Pass Criteria**:
- ✅ Update time < 15 ms (budget for 60 FPS)
- ✅ Memory usage stable
- ✅ No memory leaks (buffer growth stops)
- ✅ Console clean (no spam)
- ✅ All tests passing
- ✅ Builds clean (Debug + Release)
- ✅ Visual quality approved

**Time Estimate**: 2-3 hours  
**Risk**: Low (optimization only, no new features)

---

## Integration Points

### After Subphase 1
- Sun data available in frame data
- Ready for visibility testing

### After Subphase 2
- Visibility buffer complete
- Ready for surface lighting

### After Subphase 3
- Direct lighting applied
- Ready for shadow implementation

### After Subphase 4
- Shadows working
- Ready for artistic polish

### After Subphase 5
- Shafts in fissures
- Artistic goals complete

### After Subphase 6
- Performance validated
- Phase 3 complete

---

## Regression Prevention

**Each subphase must**:
1. Run Phase2Tests.RunAll() → all pass
2. Call ValidateSlotIndependence() → all independent
3. Check probe logs match → UpdateId identical
4. Verify visualization still pinned → no drift
5. Build clean → 0 errors, 0 warnings

**If any check fails**:
- Revert subphase immediately
- Debug root cause
- Restart subphase after fix

---

## Metrics Baseline

**Phase 2 metrics** (for comparison):
- Classification: ~1 ms
- Opacity: ~2 ms
- Swap: ~0.1 ms
- **Total**: ~3 ms per update

**Phase 3 target** (after all subphases):
- +Sun direction: +0.1 ms
- +Ray march: +2 ms
- +Direct lighting: +0.5 ms
- +Shadows: +1 ms
- +Shafts: +1 ms
- -Optimization: -2 ms (hopefully)
- **Target total**: ~8 ms per update (well under budget)

---

## Commit Strategy

**One commit per subphase** (not per hour):
```
feat(lighting-v3): phase 3 subphase N - <feature name>

- Added <data>
- Implemented <algorithm>
- Verified <metrics>
- Tests: <result>

Subphase metrics:
- <timing>
- <memory>
- <visual quality>
```

---

## Exit Criteria (Phase 3 Complete)

- ✅ All 6 subphases complete
- ✅ All Phase 2 tests still passing
- ✅ New Phase 3 validation tests added
- ✅ Performance < 15 ms per update
- ✅ Visual quality approved
- ✅ Console output clean
- ✅ Builds clean (Debug + Release)
- ✅ Regression tests documented
- ✅ Commit history clean
- ✅ Phase 4 (local lighting) can begin

---

## What NOT to Do

❌ Implement all subphases at once  
❌ Skip validation between subphases  
❌ Remove Phase 2 guarantees  
❌ Add continuous logging per frame  
❌ Let performance creep beyond budget  
❌ Ignore test failures (commit anyway)  
❌ Break immutability for convenience  

✅ Do implement one subphase at a time  
✅ Do run tests after each subphase  
✅ Do preserve double-buffering  
✅ Do log only on change  
✅ Do measure timing constantly  
✅ Do revert if tests fail  
✅ Do maintain atomic guarantees  

---

## Timeline Estimate

| Subphase | Hours | Status |
|----------|-------|--------|
| 1: Sun direction | 1-2 | 🚀 Ready |
| 2: Ray marching | 3-4 | 🚀 Ready |
| 3: Direct surface | 2-3 | 🚀 Ready |
| 4: Shadows | 4-5 | 🚀 Ready |
| 5: Light shafts | 3-4 | 🚀 Ready |
| 6: Optimization | 2-3 | 🚀 Ready |
| **Total** | **15-21 hours** | 🚀 Ready |

**Phase 3: Ready to start, one subphase at a time.**

---

**Remember**: Phase 2 proved incremental, validated approach works. Do not skip subphases. Do not combine them. One at a time, with tests passing between each.

Phase 2: Complete ✅  
Phase 3: Roadmap clear 🗺️  
Ready to proceed: Yes 🚀
