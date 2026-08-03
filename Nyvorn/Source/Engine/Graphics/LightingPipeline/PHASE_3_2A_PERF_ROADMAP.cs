/*
 * PHASE 3.2A-PERF: Sun Visibility Performance Optimization
 *
 * APPROVED BEHAVIOR (Phase 3.2A - DO NOT REGRESS):
 * - SunVisibility follows solar direction correctly
 * - Foreground solid blocks, background does NOT block
 * - 28/28 tests passing
 * - GuardLimitHits = 0
 * - Visual: white (free) -> gray (partial) -> black (blocked)
 * - No NaN/Inf values
 *
 * CURRENT PERFORMANCE (REJECTED):
 * Active Region:    242x129 tiles
 * Sample Grid:      484x258
 * Total Samples:    124,872
 * DDA Rays Started: 20,120
 * Cells Visited:    1,913,968
 * Avg Cells/Ray:    95.13
 * Max Cells/Ray:    199
 * Build Time:       17.122 ms  (FAIL: target ≤3ms avg)
 *
 * ========================================
 * TASK 1: CREATE CHECKPOINT
 * ========================================
 * Status: COMPLETED (commit b1305cd)
 *
 * Reference DDA available for:
 * - Regression testing
 * - Visual comparison
 * - Numerical comparison
 * - Performance baseline
 *
 * ========================================
 * TASK 2: AUDIT ACTIVE REGION RESOLUTION
 * ========================================
 * Status: PENDING
 *
 * Question: Why is resolution 242x129 now vs 122x63 previously?
 *
 * Investigate which dimension is used:
 * [ ] Physical backbuffer resolution
 * [ ] Logical viewport dimension
 * [ ] RenderTarget dimension
 * [ ] Window dimension
 * [ ] Camera dimension
 *
 * Outcome Required:
 * - Confirm which dimension is correct
 * - Do NOT artificially reduce region
 * - If real coverage is 242x129, optimize the algorithm
 * - Presentation: conclusion + recommendation before changes
 *
 * ========================================
 * TASK 3: SEPARATE CALCULATION vs DEBUG COST
 * ========================================
 * Status: PENDING
 *
 * Measure separately over 300 frames:
 * [ ] A. Legacy pipeline (baseline)
 * [ ] B. V3 active, debug None
 * [ ] C. V3 active, SunVisibility debug enabled
 * [ ] D. BuildSunVisibility isolated cost
 * [ ] E. RenderSunVisibility isolated cost
 *
 * Collect:
 * - average (ms)
 * - p95 (ms)
 * - maximum (ms)
 *
 * Expected: Debug renderer currently draws 124,872 samples per frame.
 *
 * ========================================
 * TASK 4: OPTIMIZE DEBUG RENDERER
 * ========================================
 * Status: PENDING
 *
 * Current: SpriteBatch.Draw() per sample = 124,872 draw calls/frame
 * Target:  Single textured quad using point sampling
 *
 * Implementation:
 * [ ] Create reusable texture (484x258 RGBA or similar)
 * [ ] Reusable CPU buffer for upload
 * [ ] Update texture only when SunVisibility field changes
 * [ ] Single quad draw call with point sampling
 * [ ] Preserve world wrapping alignment
 * [ ] Preserve V3DebugWorldRT source/destination
 * [ ] Add metrics: DebugTextureUploads, UploadTime, DrawTime, FieldVersion
 *
 * Visual must remain:
 * - 0.0 = black, 1.0 = white, partial = gray
 * - Exact alignment with world coordinates
 *
 * ========================================
 * TASK 5: IMPLEMENT INVALIDATION SYSTEM
 * ========================================
 * Status: PENDING
 *
 * SunVisibility currently rebuilt every update.
 * Rebuild only when these change:
 *
 * [ ] ActiveRegion origin
 * [ ] ActiveRegion size
 * [ ] Solar direction (with configurable tolerance, e.g., 0.25°)
 * [ ] IsAboveHorizon state
 * [ ] SunIntensity (threshold-based)
 * [ ] Foreground geometry version
 * [ ] SunOpacity provider version
 * [ ] Sampling configuration
 *
 * Metrics to add:
 * - SunVisibilityFieldVersion (incremented on rebuild)
 * - SunVisibilityRebuildReason (enum or string)
 * - SunVisibilityRebuildCount
 * - SunVisibilityReuseCount
 *
 * When stable:
 * - Do NOT execute DDA
 * - Do NOT copy buffer unnecessarily
 * - Reuse last valid field
 * - Preserve immutability and double-buffering
 * - Preserve atomic FrameData publication
 *
 * ========================================
 * TASK 6: GEOMETRY INVALIDATION
 * ========================================
 * Status: PENDING
 *
 * Hook into existing world change detection:
 * [ ] MapChangeListener or equivalent
 * [ ] Foreground alteration triggers rebuild
 * [ ] Background ignored (does not block)
 * [ ] No full-world scanning
 * [ ] Use version/dirty flag
 *
 * First iteration: Complete rebuild on geometry change.
 * Partial updates deferred to later phase.
 *
 * ========================================
 * TASK 7: TILE-FIRST ADAPTIVE REFINEMENT
 * ========================================
 * Status: PENDING
 *
 * Two-stage algorithm (preserves final 2x2 sample grid):
 *
 * Coarse Stage:
 * [ ] One ray per tile (center)
 * [ ] Solid foreground returns blocked immediately
 * [ ] Reusable coarse buffer
 *
 * Refinement Stage:
 * [ ] Four sub-tile samples only where needed
 * [ ] Refinement criteria:
 *     - Coarse visibility differs from neighbor
 *     - Adjacent to foreground
 *     - On shadow boundary
 *     - Partial transmission
 *     - Difference > tolerance
 *
 * Fully-free & fully-blocked tiles reuse coarse value.
 * Preserve precision on shadow edges.
 *
 * Metrics:
 * - CoarseTileRays
 * - RefinedTiles
 * - RefinedSamples
 * - SamplesReusedFromCoarse
 * - DdaCallsSaved
 * - CellsVisitedSaved
 *
 * ========================================
 * TASK 8: COMPARE vs REFERENCE DDA
 * ========================================
 * Status: PENDING
 *
 * Run both algorithms on same fixtures:
 * [ ] ReferenceDda (current implementation)
 * [ ] OptimizedAdaptive (tile-first refinement)
 *
 * Compare every sample.
 * Metrics:
 * - ExactMatches
 * - DifferentSamples
 * - DifferencePercentage
 * - MaximumAbsoluteDifference
 * - BoundaryDifferenceCount
 *
 * Target: DifferentSamples = 0
 * (Field is binary, so outside refined regions must be identical)
 *
 * Test scenarios:
 * [ ] Sun at 85°
 * [ ] Sun at 45°
 * [ ] Sun at 15°
 * [ ] Solid platform
 * [ ] Narrow fissure
 * [ ] Blocker outside ActiveRegion
 * [ ] World seam
 * [ ] Underground
 * [ ] Open surface
 *
 * ========================================
 * TASK 9: PROTOTYPE DIRECTIONAL SWEEP (IF NEEDED)
 * ========================================
 * Status: BLOCKED (pending Tasks 7-8)
 *
 * Only if tile-first + invalidation still miss budget.
 * Do NOT replace approved DDA immediately.
 *
 * Requirements if implemented:
 * [ ] Process field opposite to LightTravelDirection
 * [ ] Use halo in solar direction
 * [ ] Consult SunOpacity
 * [ ] Respect world wrapping
 * [ ] Background transparent
 * [ ] Directional shadow preservation
 * [ ] Approximately O(sampleCount)
 *
 * Do NOT use BFS, flood fill, or blur.
 * Compare against reference DDA.
 *
 * ========================================
 * TASK 10: DO NOT PARALLELIZE YET
 * ========================================
 * Status: CONSTRAINT
 *
 * Do NOT use:
 * [ ] Task
 * [ ] Parallel.For
 * [ ] Multiple threads
 * [ ] Async patterns
 *
 * First: Remove redundant work.
 * Then: Profile the optimized algorithm.
 * Later: Consider parallelization if bottleneck remains.
 *
 * Reason: Premature parallelization complicates:
 * - Buffer publication
 * - Zero-allocation guarantee
 * - Race conditions
 * - Masks expensive algorithms
 *
 * ========================================
 * TASK 11: ALLOCATION GATE (VALIDATED HARNESS)
 * ========================================
 * Status: PENDING
 *
 * Run corrected harness (commit 277df38) with 300 updates each:
 *
 * [ ] Scenario 0: Empty loop (baseline)
 * [ ] Scenario A: Foundation without SunVisibility
 * [ ] Scenario B: Foundation with SunVisibility
 * [ ] Scenario C: SunVisibility isolated
 *
 * Required Invariants:
 * - SampleCountBefore == SampleCountAfter
 * - CapacityBefore == CapacityAfter
 * - BufferReferencesBefore == BufferReferencesAfter
 * - ThreadIdBefore == ThreadIdAfter
 * - MeasurementValid == true
 *
 * Allocation Target:
 * - Scenario 0: 0 bytes/update
 * - Scenario A: 0 bytes/update
 * - Scenario B: 0 bytes/update
 * - Scenario C: 0 bytes/update
 *
 * Do NOT accept resizing, rebuild, or region changes during window.
 *
 * ========================================
 * TASK 12: PERFORMANCE GATE (1920x1009)
 * ========================================
 * Status: PENDING
 *
 * Real resolution: 242x129 tiles, 124,872 samples
 * Minimum 300 updates per condition:
 *
 * [ ] Sun at 85° (high elevation)
 * [ ] Sun at 45° (mid elevation)
 * [ ] Sun at 15° (low elevation)
 * [ ] Open surface
 * [ ] Underground
 * [ ] Stationary camera
 * [ ] Moving camera
 *
 * Collect: average, p95, maximum
 *
 * PRODUCTION TARGETS:
 *
 * SunVisibility Build:
 * [ ] average ≤ 3.0 ms
 * [ ] p95 ≤ 5.0 ms
 * [ ] maximum ≤ 8.0 ms
 *
 * Foundation Total:
 * [ ] average ≤ 5.0 ms
 * [ ] p95 ≤ 8.0 ms
 * [ ] maximum ≤ 10.0 ms
 *
 * Allocation:
 * [ ] bytes/update = 0
 * [ ] GuardLimitHits = 0
 *
 * Debug visual cost reported separately (not included in pipeline budget).
 *
 * ========================================
 * TASK 13: PRESERVE VISUAL BEHAVIOR
 * ========================================
 * Status: CONSTRAINT
 *
 * Approved visual:
 * [ ] White where line-of-sight to Sun exists
 * [ ] Black behind solid foreground
 * [ ] Background does NOT block
 * [ ] Shadow follows solar direction
 * [ ] Night is black
 * [ ] Blocker outside screen continues blocking
 * [ ] Horizontal seam continuous
 * [ ] Mask aligned to tiles
 *
 * Do NOT reduce SamplesPerAxis without:
 * - Visual comparison screenshot
 * - User authorization
 *
 * ========================================
 * TASK 14: HOTKEY VERIFICATION
 * ========================================
 * Status: PENDING
 *
 * Preserve:
 * [ ] Ctrl+Shift+J: Toggle Legacy/V3 pipeline
 * [ ] Ctrl+Alt+1: Cycle debug modes
 * [ ] Ctrl+Alt+2: Print metrics
 * [ ] Ctrl+Shift+Alt+2: Reset metrics
 * [ ] Ctrl+Alt+T: Run tests + allocation harness
 *
 * Note: Project uses J, NOT L (deliberate change).
 *
 * ========================================
 * TASK 15: DELIVERY CHECKLIST
 * ========================================
 * Status: IN PROGRESS
 *
 * [ ] 1. ActiveRegion audit report
 * [ ] 2. Calculation vs debug cost breakdown
 * [ ] 3. Debug renderer with texture (optimized)
 * [ ] 4. Invalidation/reuse system implemented
 * [ ] 5. Tile-first adaptive refinement
 * [ ] 6. Comparison against ReferenceDda (8 scenarios)
 * [ ] 7. Allocation harness results (4 scenarios)
 * [ ] 8. Performance metrics (7 conditions, avg/p95/max)
 * [ ] 9. DDA calls saved, cells saved counts
 * [ ] 10. Screenshots: before/after visual comparison
 * [ ] 11. Tests: 28/28 passing
 * [ ] 12. Builds: 0 errors, 0 warnings (Debug + Release)
 * [ ] 13. Commits: Separate per optimization (5-7 commits)
 *
 * CONSTRAINTS (DO NOT VIOLATE):
 * [ ] Do NOT start Phase 3.2B
 * [ ] Do NOT apply lighting to world
 * [ ] Do NOT implement surface impact, glow, shafts, soft shadows, bloom
 * [ ] Do NOT regress visual behavior
 * [ ] Do NOT mark Phase 3.2A sealed before performance gate passes
 *
 * ========================================
 * CURRENT STATUS SUMMARY
 * ========================================
 *
 * Phase 3.2A Reference Implementation: APPROVED (behavior)
 * Phase 3.2A Reference Implementation: REJECTED (performance)
 *
 * Phase 3.2A-PERF: INITIATED
 * Checkpoint commit: b1305cd
 *
 * Next immediate task: TASK 2 (Audit ActiveRegion)
 *
 * ========================================
 */
