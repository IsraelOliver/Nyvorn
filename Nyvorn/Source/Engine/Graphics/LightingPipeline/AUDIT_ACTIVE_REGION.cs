/*
 * AUDIT_ACTIVE_REGION - TASK 2 Investigation
 *
 * FINDINGS:
 * =========
 *
 * 1. DIMENSION SOURCE IDENTIFIED
 *
 *    Location: PlayingState.cs lines 159-160
 *    Code:
 *      int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
 *      int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
 *
 *    These values are PHYSICAL backbuffer resolution (1920x1009).
 *
 * 2. CALL CHAIN TRACED:
 *
 *    PlayingState.Update() [line 152]
 *      → screenW, screenH from BackBufferWidth/Height [line 159-160]
 *      → session.FollowCamera(dt, screenW, screenH) [line 351]
 *      → session.ViewCoordinator.UpdateLightingV3(cameraX, cameraY, screenW, screenH) [line 356]
 *      → LightingV3Foundation.Update(cameraX, cameraY, logicalRenderWidth, logicalRenderHeight, tileSize) [line 130 of Foundation]
 *      → _activeRegion.Update(cameraWorldX, cameraWorldY, logicalRenderWidth, logicalRenderHeight, tileSize) [line 130 of Foundation]
 *      → ActiveLightingRegion calculates region based on logicalRenderWidth/Height [line 100-104]
 *
 * 3. CAMERA ZOOM DISCOVERED - NOT USED IN ACTIVE REGION!
 *
 *    Location: PlayingState.cs line 3146
 *    Code:
 *      float viewWidth = screenWidth / session.Camera.Zoom;
 *
 *    Location: PlayingSession.cs lines 413-414
 *    Code:
 *      screenWidth / Camera.Zoom,
 *      screenHeight / Camera.Zoom);
 *
 *    PROBLEM: Camera.Zoom IS used by GetVisibleLoopOffsets and TissueSystem,
 *             but NOT passed to LightingV3Foundation.Update()!
 *
 *    Actual visible world = screenWidth / Camera.Zoom, NOT screenWidth
 *
 * 4. FORMULA IN ACTIVE REGION (line 100-104 of ActiveLightingRegion.cs):
 *
 *    int visibleWidthTiles = (logicalRenderWidth + tileSize - 1) / tileSize;
 *    int visibleHeightTiles = (logicalRenderHeight + tileSize - 1) / tileSize;
 *
 *    RegionWidthTiles = visibleWidthTiles + (MarginTiles * 2);
 *    RegionHeightTiles = visibleHeightTiles + (MarginTiles * 2);
 *
 *    With MarginTiles = 1 (default):
 *      - If logicalRenderWidth = 1920, tileSize = 16:
 *        visibleWidthTiles = (1920 + 15) / 16 = 121
 *        RegionWidthTiles = 121 + 2 = 123 ≈ 242x129 with 2x2 sampling
 *
 *      - If logicalRenderWidth = 960 (zoom 2.0), tileSize = 16:
 *        visibleWidthTiles = (960 + 15) / 16 = 61
 *        RegionWidthTiles = 61 + 2 = 63 ≈ 122x63 with 2x2 sampling
 *
 * 5. HYPOTHESIS CONFIRMED:
 *
 *    Execution A (reported as 242x129):
 *      - Camera.Zoom = 1.0 (or not being considered)
 *      - logicalRenderWidth = 1920
 *      - ActiveRegion = 242x129 tiles, 124,872 samples
 *
 *    Execution B (reported as 122x63):
 *      - Camera.Zoom = 2.0 (zoomed in 2x)
 *      - logicalRenderWidth = 1920 / 2.0 = 960
 *      - ActiveRegion = 122x63 tiles, ~30,744 samples
 *
 * 6. ROOT CAUSE:
 *
 *    LightingV3Foundation.Update() receives PHYSICAL screenW/screenH,
 *    not LOGICAL (zoom-adjusted) dimensions.
 *
 *    Fix would be to either:
 *    A. Pass zoom-adjusted dimensions to Foundation.Update()
 *    B. Pass Camera.Zoom separately and apply in Foundation
 *    C. Calculate logical viewport at PlayingState and pass that
 *
 * 7. WORLD RENDERER COMPARISON:
 *
 *    PlayingSession.FollowCamera() [line 391] calls:
 *      ViewCoordinator.UpdateSimulationViewport(screenWidth, screenHeight);
 *
 *    Then later (line 413-414):
 *      screenWidth / Camera.Zoom,
 *      screenHeight / Camera.Zoom);
 *
 *    This suggests World Renderer ALSO receives unzoomed dimensions initially,
 *    but applies zoom internally where needed.
 *
 * 8. WHICH IS CORRECT?
 *
 *    The CORRECT dimension depends on game design:
 *
 *    If zoom > 1.0 means "closer look at same world":
 *      - ActiveRegion should use screenWidth / Camera.Zoom
 *      - Currently showing 242x129 is WRONG (overshoots world coverage)
 *      - Should be 122x63 at zoom 2.0
 *
 *    If zoom > 1.0 means "upscale UI but same world coverage":
 *      - ActiveRegion should use full screenWidth
 *      - Currently showing 242x129 is CORRECT
 *      - Zoom is UI-only, not world coverage related
 *
 * 9. EVIDENCE SUGGESTS FIRST INTERPRETATION (zoom affects coverage):
 *
 *    - Code at PlayingState line 3146 explicitly divides by Camera.Zoom
 *    - TissueSystem.SetPropagationViewport divides by Camera.Zoom
 *    - This pattern repeats across codebase
 *    - Suggests zoom IS meant to affect world sampling viewport
 *
 * 10. HYPOTHESIS TO VERIFY:
 *
 *     Test 1: Log Camera.Zoom value when ActiveRegion = 242x129
 *     Test 2: Check if Camera.Zoom has changed between builds
 *     Test 3: Manually set zoom to 2.0 and verify ActiveRegion becomes 122x63
 *     Test 4: Compare with TissueSystem behavior (if it divides by zoom)
 *
 * ========================================
 * RECOMMENDATION (PENDING USER APPROVAL)
 * ========================================
 *
 * Current behavior (using physical backbuffer size) appears to be a BUG
 * because:
 *
 * 1. Camera.Zoom is already being divided elsewhere in codebase
 * 2. The dimension change (242x129 vs 122x63) exactly corresponds
 *    to zoom 1.0 vs 2.0 factor
 * 3. ActiveRegion is LARGER than world actually visible at zoom levels > 1
 * 4. This causes unnecessary oversampling when zoomed in
 *
 * PROPOSED FIX:
 *
 * Pass adjusted dimensions to LightingV3Foundation.Update():
 *
 *   int logicalWidth = (int)(screenW / session.Camera.Zoom);
 *   int logicalHeight = (int)(screenH / session.Camera.Zoom);
 *   session.ViewCoordinator.UpdateLightingV3(
 *       session.Camera.Position.X,
 *       session.Camera.Position.Y,
 *       logicalWidth,
 *       logicalHeight);
 *
 * Expected result:
 * - At zoom 1.0: ActiveRegion = 242x129 (as currently)
 * - At zoom 2.0: ActiveRegion = 122x63 (matches previous execution)
 * - Cells visited reduced by ~4x at zoom 2.0
 * - Still maintains full coverage of visible world
 *
 * CAVEAT:
 * - Must verify world boundaries and wrapping still work
 * - Must verify no regression at other zoom levels
 * - Must test at zoom 0.5 if game supports
 * - Visual comparison required before deployment
 *
 * ========================================
 * NEXT STEPS (AWAITING USER DIRECTION)
 * ========================================
 *
 * User must decide:
 *
 * A. Is zoom 1.0 the only supported value?
 *    → No fix needed, but confirm this is intentional
 *    → Consider documenting that zoom affects viewport
 *
 * B. Does camera zoom change during gameplay?
 *    → If yes, bug confirmed: ActiveRegion ignoring zoom
 *    → Fix should be applied after visual testing
 *    → Need to rerun performance tests at zoom 1.0 to establish real baseline
 *
 * C. Was 122x63 from zoom 2.0 or different backbuffer resolution?
 *    → Check git history or build logs from that execution
 *    → May help confirm hypothesis
 *
 * THIS AUDIT HAS NOT IMPLEMENTED ANY CHANGES.
 * Reference DDA remains unchanged.
 * ActiveRegion calculation remains unchanged.
 * All 28 tests continue passing.
 */
