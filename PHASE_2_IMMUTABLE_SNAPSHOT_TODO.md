# Phase 2: Immutable Frame Data Double-Buffering - TODO

**Status**: Implementation started, compilation blocked due to refactoring scope

## Problem Identified

The current snapshot implementation is **falsely immutable**:
- LightingV3FrameData contains references to mutable objects
- NextFrame can modify objects that previous frame's renderer still uses
- This causes drift when ActiveLightingRegion changes mid-render

## Solution: True Double-Buffering

### Architecture Required

1. **ActiveRegionSnapshot (value struct)**
   - DONE: Copies WorldOriginX/Y by value
   - DONE: Stores TileWidth, TileHeight, SampleWidth, SampleHeight, TileSize

2. **LightingV3FrameSlot (reusable buffer)** - IN PROGRESS
   - DONE: Independent buffers (TileClassifications[], SunOpacityBuffer[], LocalOpacityBuffer[])
   - DONE: Probe data for validation
   - TODO: Fix Array.Resize issue (use explicit array copy)
   - TODO: EnsureCapacity and ClearBuffers methods

3. **LightingV3FrameData (immutable view)** - DONE
   - Wraps front slot data
   - No references to mutable Foundation objects

4. **LightingV3Foundation.Update()** - IN PROGRESS
   - DONE: Build in _backSlot
   - DONE: Atomic swap (front ← back)
   - TODO: Remove _tileClassifications, _occluderField fields
   - TODO: Update GetTileClassifications() and DumpMetricsToConsole()

### Compilation Errors to Fix

1. **MockGeometryProvider.cs** lines 124, 153, 224, 258
   - Calls to GetOccluderField() - should use GetFrameData() instead

2. **LightingV3DebugRenderer.cs** line 194
   - Calls to GetOccluderField() - should use GetFrameData() instead

3. **DebugVisualization.cs** line 140
   - Calls to GetOccluderField() - should use GetFrameData() instead

4. **LightingV3Foundation.cs**
   - Line 179: GetTileClassifications() returns _tileClassifications (now in slot)
   - Line 199: DumpMetricsToConsole() uses _occluderField (now in slot)

### Renderer Integration (NOT YET STARTED)

In LightingV3DebugRenderer.cs, the Render() method must:
```csharp
public void Render(SpriteBatch spriteBatch, LightingV3Foundation foundation, ...)
{
    // CAPTURE ONCE at start
    LightingV3FrameData frameData = foundation.GetFrameData();
    
    // Use frameData exclusively throughout entire render
    // - Do NOT call foundation methods
    // - Do NOT create new snapshots
    // - All wrapping copies use same frameData
}
```

### Probe Validation (NOT YET IMPLEMENTED)

Need to add to LightingV3FrameSlot and verify in renderer:
- UpdateId tracking
- Sample world position calculation
- Opacity values
- Log comparison between build and render

### Build Steps

1. Fix LightingV3Foundation methods to use slots instead of removed fields
2. Update test files (MockGeometryProvider, etc.) to use GetFrameData()
3. Update LightingV3DebugRenderer to capture frameData once per Draw
4. Verify compilation
5. Add probe logging for validation
6. Test runtime to confirm drift is fixed

### NOT Started

- Renderer integration
- Probe implementation
- Runtime validation test
- Frame data logging

**Critical**: Do not ask for runtime test until all structural code is correct.
