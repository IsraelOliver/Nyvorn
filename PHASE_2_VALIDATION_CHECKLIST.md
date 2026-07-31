# Phase 2 Double-Buffered Immutable Snapshots - Validation Checklist

**Status**: Implementation complete, ready for validation

## 1. PlayingState Integration ✓

**Location**: `Nyvorn\Source\Game\States\PlayingState.cs` lines 1242-1283

**Validation**:
- [ ] Confirm frameData captured ONCE before visibleLoopOffsets loop
- [ ] Confirm frameData passed to renderer.Render()
- [ ] Confirm renderer.Render() called with frameData parameter
- [ ] Confirm NO additional GetFrameData() calls inside loop
- [ ] Confirm logs removed (lines 1247-1255 deleted)

**Code**:
```csharp
// Single capture point (line ~1250)
var frameData = debugFoundation?.GetFrameData();

// Used in loop (line ~1272)
viewCoord.LightingV3DebugRenderer.Render(
    spriteBatch,
    frameData,  // <-- NOT foundation
    ...);
```

## 2. Renderer Signature Updated ✓

**Location**: `Nyvorn\Source\Engine\Graphics\LightingPipeline\LightingV3DebugRenderer.cs` line 77

**Validation**:
- [ ] Render() accepts LightingV3FrameData parameter (not LightingV3Foundation)
- [ ] Render() does NOT call GetFrameData() internally
- [ ] Render() does NOT have modeConfirmationText parameter
- [ ] Renderer has no reference to Foundation object

**Signature**:
```csharp
public void Render(
    SpriteBatch spriteBatch,
    LightingV3FrameData frameData,  // <-- Direct data, not foundation
    LightingDebugMode mode,
    int tileSize,
    Matrix worldViewTransform)
```

## 3. Zero Allocations Per Update ✓

**Validation**:
- [ ] LightingV3FrameData constructor (LightingV3FrameSlot.cs line 110) does NOT create new arrays
- [ ] LightingV3FrameData only copies references from slot
- [ ] Foundation.Update() does NOT call `new LightingV3FrameData()` per frame
- [ ] GetFrameData() is called only in PlayingState (once per frame)
- [ ] No LINQ, ToArray(), or temporary collections in Update()

**Code to verify**:
```csharp
// In LightingV3FrameData constructor
public LightingV3FrameData(LightingV3FrameSlot slot)
{
    UpdateId = slot.FrameId;  // Copy by value
    Region = slot.Region;     // Copy value struct
    TileClassifications = slot.TileClassifications;  // Reference only, no new[]
    // ... other references, no allocations
}
```

## 4. Slot Independence Validation ✓

**Test Method**: `LightingV3Foundation.ValidateSlotIndependence()`

**Validation Steps**:
1. Run game once to initialize lighting
2. Call `foundation.ValidateSlotIndependence()` manually or via debug hotkey
3. Verify output:
```
TileClassifications: ✓ INDEPENDENT
SunOpacityBuffer: ✓ INDEPENDENT
LocalOpacityBuffer: ✓ INDEPENDENT
Result: ✓ ALL INDEPENDENT
```

**Guarantees**:
- Front slot never written during Update()
- Back slot buffers differ from front buffers
- ReferenceEquals returns false for all three buffer pairs

## 5. Atomic Publishing Order ✓

**Location**: `LightingV3Foundation.cs` Update() method lines 108-155

**Order (MUST be exact)**:
1. Line 92: Update active region
2. Line 98: EnsureCapacity on back slot
3. Line 99: ClearBuffers on back slot
4. Line 102: ClassifyRegionToSlot (build tiles in back)
5. Line 105: BuildOpacityFieldsToSlot (build opacities in back)
6. Lines 112-141: Calculate and store probe data in back slot
7. Line 147: Increment UpdateId
8. Line 148: Assign UpdateId to back slot FrameId
9. Line 149: Create ActiveRegionSnapshot and assign to back slot
10. Lines 151-156: Swap front ← back (atomic publish)

**Critical**: WorldOrigin only changes after all buffers are ready (step 9 before step 10)

## 6. Probe Consistency Logging ✓

**Foundation Logging** (`LightingV3Foundation.cs` line 153):
- Logs when WorldOriginX or WorldOriginY changes
- Format: `[V3FoundationPublish] UpdateId=X WorldOrigin=(x,y) ProbeIndex=index ProbeWorld=(x,y) Sun=opacity Local=opacity`
- Includes probe position and opacities

**Renderer Logging** (`LightingV3DebugRenderer.cs` line 85):
- Logs on every Render() call
- Format: `[V3RendererConsume] UpdateId=X WorldOrigin=(x,y) ProbeIndex=index ProbeWorld=(x,y) Sun=opacity Local=opacity`
- Uses frameData provided by caller

**Validation**:
- [ ] Run game and move camera to change WorldOrigin
- [ ] Check console for matching FoundationPublish and RendererConsume logs
- [ ] Verify UpdateId is identical in paired logs
- [ ] Verify WorldOrigin coordinates match
- [ ] Verify Probe coordinates match
- [ ] Verify opacity values match

## 7. Frame Snapshot Stability ✓

**Validation**:
- [ ] frameData captured in PlayingState at start of debug pass (line ~1250)
- [ ] Same frameData reference used for ALL visibleLoopOffsets iterations
- [ ] No call to GetFrameData() inside loop or inside renderer
- [ ] UpdateId from start == UpdateId at end (because same object)

**To verify**:
```csharp
// At START of pass
var frameData = debugFoundation?.GetFrameData();
int startUpdateId = frameData.UpdateId;

// In loop - frameData never changes
for (int i = 0; i < visibleLoopOffsets.Count; i++)
{
    renderer.Render(..., frameData, ...);  // Same object
}

// At END - would still have same UpdateId
int endUpdateId = frameData.UpdateId;  // Same as startUpdateId
```

## 8. Test Cases Validation ✓

**Location**: `MockGeometryProvider.cs` Phase2Tests

All six test cases updated to use GetFrameData():

1. **Case A: SolidForeground** - line 114
   - [ ] Classification == SolidForeground
   - [ ] SunOpacity[0] ≈ 1.0
   - [ ] LocalOpacity[0] ≈ 1.0

2. **Case B: VisibleBackground** - line 143
   - [ ] Classification == VisibleBackground
   - [ ] SunOpacity[0] ≈ 0.0
   - [ ] LocalOpacity[0] ≈ 0.0

3. **Case C: OpenAtmosphere** - line 172
   - [ ] Classification == OpenAtmosphere

4. **Case D: Opacity Blending** - line 191
   - [ ] 0.5 ⊕ 0.5 = 0.75

5. **Case E: SunOcclusion Rules** - line 209
   - [ ] Solid sun opacity = 1.0
   - [ ] Background sun opacity = 0.0
   - [ ] Atmosphere sun opacity = 0.0

6. **Case F: LocalLight Occlusion** - line 243
   - [ ] Solid local opacity = 1.0
   - [ ] Background local opacity = 0.0
   - [ ] Atmosphere local opacity = 0.0

**To run tests**:
```csharp
Phase2Tests.RunAll();
```

Output should show:
```
=== Phase 2 Foundation Tests ===
[PASS] Case A: SolidForeground
[PASS] Case B: VisibleBackground
[PASS] Case C: OpenAtmosphere
[PASS] Case D: Opacity Blending
[PASS] Case E: Sun Occlusion Rules
[PASS] Case F: Local Light Occlusion Rules
=== All Tests Complete ===
```

## 9. Log Cleanup ✓

**Removed**:
- [ ] PlayingState debug logs (lines 1247-1255)
- [ ] modeConfirmationText from Render() signature
- [ ] Console.WriteLine for mode confirmation

**Retained** (only when needed):
- [ ] Probe logs (FoundationPublish / RendererConsume) - only on WorldOrigin change
- [ ] Validation results from ValidateSlotIndependence()
- [ ] Test results from Phase2Tests.RunAll()
- [ ] Manual metrics dump via Ctrl+Alt+2

## 10. Build Verification ✓

**Commands**:
```bash
# Debug build
dotnet build --no-restore

# Release build
dotnet build --no-restore --configuration Release
```

**Expected**:
```
Compilação com êxito.
    0 Aviso(s)
    0 Erro(s)
```

## 11. Final Checklist - Before Runtime Test

- [ ] 1. PlayingState integration: frameData captured once, passed to renderer
- [ ] 2. Renderer signature: accepts frameData, not foundation
- [ ] 3. Zero allocations: confirmed no new[] per update
- [ ] 4. Slot independence: ReferenceEquals validation passes
- [ ] 5. Atomic order: publish happens after all data ready
- [ ] 6. Probe consistency: FoundationPublish ↔ RendererConsume match
- [ ] 7. Snapshot stability: UpdateId consistent through pass
- [ ] 8. Tests passing: All 6 deterministic tests pass
- [ ] 9. Logs clean: Only meaningful logs remain
- [ ] 10. Build success: Both Debug and Release compile cleanly
- [ ] 11. NO Phase 3 started

## Runtime Test Strategy

Once all 11 items above are checked:

1. Start game in V3 debug mode
2. Move camera to trigger WorldOrigin changes
3. Monitor console for probe logs:
   - FoundationPublish (when region changes)
   - RendererConsume (every render)
4. Verify probe values match between publish and consume
5. Verify drift visualization is PINNED to tiles (no more slowness)
6. Confirm camera movement is smooth, not 8-pixel snaps

**If drift persists**: Check that frameData is same object throughout pass (UpdateId must match)

**If tests fail**: Verify frame slot buffers are truly independent (ValidateSlotIndependence)

## Reference: Key Files

- LightingV3Foundation.cs - Core double-buffer logic
- LightingV3FrameSlot.cs - Slot structure and FrameData wrapper
- LightingV3DebugRenderer.cs - Renderer refactored for frameData
- PlayingState.cs - Single capture point for frameData
- MockGeometryProvider.cs - Deterministic tests
- DebugVisualization.cs - Legacy debug renderer (uses frameData)
