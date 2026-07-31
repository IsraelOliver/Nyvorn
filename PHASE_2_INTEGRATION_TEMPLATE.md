# PHASE 2 INTEGRATION TEMPLATE

This template shows how to integrate `LightingV3Foundation` into `PlayingState`.

---

## STEP 1: Add Fields to PlayingState

```csharp
// In PlayingState.cs class declaration

private LightingV3Foundation _lightingV3Foundation;
private LightingV3DebugController _debugController;
private LightingV3DebugRenderer _debugRenderer;
```

---

## STEP 2: Initialize in Constructor/Setup

```csharp
// In PlayingState constructor or initialization method

// Get the geometry provider (adapt to your actual access pattern)
var worldDataProvider = /* ... from PlayingSession or ViewCoordinator ... */;
var geometryAdapter = new WorldDataGeometryAdapter(worldDataProvider);

// Create foundation
_lightingV3Foundation = new LightingV3Foundation(geometryAdapter);

// Debug system (DEBUG builds only)
#if DEBUG
_debugController = new LightingV3DebugController();
_debugRenderer = new LightingV3DebugRenderer(graphicsDevice);
#endif
```

---

## STEP 3: Update in Draw() - V3 Branch Only

```csharp
// In PlayingState.Draw() within the V3 branch (not Legacy)

if (!IsLegacyMode)
{
    // Update foundation BEFORE rendering
    // Parameters must be:
    // - cameraX: camera world position X (center or top-left, clarify)
    // - cameraY: camera world position Y
    // - logicalWidth: render width in pixels (screenW or LogicalRenderWidth)
    // - logicalHeight: render height in pixels (screenH or LogicalRenderHeight)
    // - tileSize: tile size in pixels (typically 16)
    
    _lightingV3Foundation.Update(
        session.Camera.Position.X,
        session.Camera.Position.Y,
        screenW,
        screenH,
        16  // TileSize
    );

#if DEBUG
    // Handle debug controller input
    _debugController?.Update();
    
    // Check for Ctrl+Shift+F (dump metrics)
    var keyboard = Keyboard.GetState();
    if (keyboard.IsKeyDown(Keys.LeftControl) && keyboard.IsKeyDown(Keys.LeftShift) && 
        keyboard.IsKeyDown(Keys.F))
    {
        LightingV3DebugRenderer.DumpMetrics(_lightingV3Foundation);
    }
#endif
}
```

---

## STEP 4: Render Debug Visualization

```csharp
// In PlayingState.Draw() after V3 composition (but before HUD)
// Within spriteBatch.Begin/End scope

#if DEBUG
if (!IsLegacyMode && _debugRenderer != null)
{
    var debugMode = _debugController?.GetCurrentMode() ?? LightingDebugMode.None;
    
    spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
    _debugRenderer.Render(spriteBatch, _lightingV3Foundation, debugMode, 16);
    spriteBatch.End();
}
#endif
```

---

## STEP 5: Cleanup on Exit

```csharp
// In PlayingState.OnExit()

_lightingV3Foundation?.Dispose();
_debugRenderer?.Dispose();
```

---

## QUICK REFERENCE: Parameter Meanings

| Parameter | Source | Meaning |
|-----------|--------|---------|
| `cameraX` | `camera.Position.X` | Camera horizontal position (world pixels) |
| `cameraY` | `camera.Position.Y` | Camera vertical position (world pixels) |
| `logicalWidth` | `screenW` or `LogicalRenderWidth` | Visible screen width (pixels) |
| `logicalHeight` | `screenH` or `LogicalRenderHeight` | Visible screen height (pixels) |
| `tileSize` | Usually `16` | Tile size in pixels |

---

## HOTKEYS

| Hotkey | Action | Effect |
|--------|--------|--------|
| `Ctrl+Shift+V` | Cycle debug modes | Changes visualization (Classification → SunOpacity → LocalLight → SampleGrid → None) |
| `Ctrl+Shift+F` | Dump metrics | Prints foundation stats to console |

**These only work in DEBUG builds.**

---

## DEBUG OUTPUT EXAMPLE

When pressing `Ctrl+Shift+F`:

```
=== LightingV3Foundation Metrics ===
Sampling: LightingSamplingConfig(2x2, 4 samples/tile, 0.5 tile spacing)
Active Region: ActiveLightingRegion(128x80 tiles, 256x160 samples=40960, origin=(1280, 640))
Active Tiles: 10240
Active Samples: 40960
Classification Time: 0.234ms
Occluder Build Time: 0.512ms
Buffer Resize Count: 0
Occluder Field: OccluderField(256x160=40960 samples, capacity=65536)
Active Providers: 1
  - ForegroundTiles
```

---

## COORDINATE SYSTEM ASSUMPTIONS

**Before integration, CLARIFY:**

1. **Camera Position Origin**
   - Does `camera.Position` represent screen CENTER or TOP-LEFT?
   - This affects how `ActiveLightingRegion` calculates region bounds.

2. **LogicalRenderSize**
   - Is it derived from `screenW`/`screenH` parameters?
   - Or from backbuffer dimensions?
   - Or from camera viewport?

3. **TileSize**
   - Constant value (16)?
   - Or variable per world?

4. **World Wrapping**
   - Is world horizontally wrapping?
   - If yes, what is the wrapping width?

**Current assumption in template:**
```csharp
// Assumes:
camera.Position = center of view
logicalWidth/Height = actual visible pixels
tileSize = 16 (constant)
no horizontal wrapping (for now)
```

---

## ZERO-ALLOCATION VERIFICATION

To verify no per-frame allocations occur:

```csharp
#if DEBUG
static int frameCount = 0;
static long totalAllocations = 0;

in PlayingState.Update():
{
    long memBefore = GC.GetTotalMemory(false);
    
    _lightingV3Foundation.Update(...);
    
    long memAfter = GC.GetTotalMemory(false);
    if (memAfter > memBefore)
    {
        totalAllocations += (memAfter - memBefore);
        System.Console.WriteLine($"[WARNING] Frame {frameCount}: {memAfter - memBefore} bytes allocated");
    }
    frameCount++;
}
```

Expected behavior: **No output** (allocations only on foundation creation or region resize).

---

## COMMON ISSUES & FIXES

### Issue 1: "IWorldDataProvider not accessible from PlayingState"

**Solution:** Ask where to get it from:
- Is it in `PlayingSession`?
- Is it in `ViewCoordinator`?
- Should we store it in `PlayingState`?

### Issue 2: "Camera position doesn't match expected coordinates"

**Solution:** Add debug output:
```csharp
System.Console.WriteLine($"Camera: {session.Camera.Position}, Screen: {screenW}x{screenH}");
System.Console.WriteLine($"Region origin: {_lightingV3Foundation.GetActiveRegion().WorldOriginX}");
```

Compare with expected values.

### Issue 3: "Debug visualization is upside-down or offset"

**Solution:** Verify TileSize and camera origin:
```csharp
var region = _lightingV3Foundation.GetActiveRegion();
System.Console.WriteLine($"Region: {region.RegionWidthTiles}x{region.RegionHeightTiles} tiles");
System.Console.WriteLine($"Origin: ({region.WorldOriginX}, {region.WorldOriginY})");
System.Console.WriteLine($"Samples: {region.RegionWidthSamples}x{region.RegionHeightSamples}");
```

---

## NEXT STEPS AFTER INTEGRATION

1. **Test in V3 mode** (toggle with `Ctrl+Shift+L`)
2. **Verify classification** (press `Ctrl+Shift+V` to cycle visualization)
3. **Check metrics** (press `Ctrl+Shift+F`)
4. **Monitor allocations** (should be zero per frame after initialization)
5. **Verify no Legacy leakage** (switch to Legacy mode, foundation should not update)

---

## BUILD COMMANDS

```bash
# DEBUG build (with debug visualization)
dotnet build --configuration Debug

# RELEASE build (no debug visualization)
dotnet build --configuration Release
```

Debug visualization is only available in DEBUG builds.

---

**Ready for integration. Answer the 5 blocking questions, then implement this template.**
