# PHASE 2 DEBUG INTEGRATION - PLAYINGSTATE

Instruções específicas para integrar debug visualization em PlayingState.

---

## HOTKEYS

| Hotkey | Função | Build | Modo |
|--------|--------|-------|------|
| **F6** | Cycle Phase 2 visualization | DEBUG only | V3 only |
| **F7** | Dump Phase 2 foundation metrics | DEBUG only | V3 only |

---

## STEP 1: Add Fields to PlayingState

```csharp
// In PlayingState class declaration

#if DEBUG
private LightingV3DebugController _debugController;
private LightingV3DebugRenderer _debugRenderer;
#endif
```

---

## STEP 2: Initialize in Constructor/SetupGraphics

```csharp
// In PlayingState constructor or where graphics device becomes available

#if DEBUG
_debugController = new LightingV3DebugController();
_debugRenderer = new LightingV3DebugRenderer(graphicsDevice);
#endif
```

---

## STEP 3: Add to Update() - BEFORE Draw

**CRITICAL**: Read input in Update(), NOT in Draw().

```csharp
// In PlayingState.Update() method
// Somewhere after input is read, before Draw is called

#if DEBUG
if (LightingPipelineCoordinator.I.IsV3Mode && _debugController != null)
{
    _debugController.Update(gameTime.ElapsedGameTime.TotalSeconds);

    // F7: Dump metrics
    var keyboard = Keyboard.GetState();
    if (keyboard.IsKeyDown(Keys.F7) && !previousKeyboard.IsKeyDown(Keys.F7))
    {
        if (_lightingV3Foundation != null)
        {
            System.Console.WriteLine("[LightingV3 Foundation Metrics]");
            _lightingV3Foundation.DumpMetricsToConsole();
        }
        else
        {
            System.Console.WriteLine("[LightingV3] Foundation is not initialized.");
        }
    }
    
    previousKeyboard = keyboard;  // Save for next frame edge detection
}
#endif
```

---

## STEP 4: Add to Draw() - After V3 Composition, Before HUD

**CRITICAL**: Only draw after all RenderTarget composition is complete.

```csharp
// In PlayingState.Draw()
// Location: After DrawWithLightingV3NeutralComposition() completes
//           Before DrawHUD() or screen-space effects

#if DEBUG
if (LightingPipelineCoordinator.I.IsV3Mode && _debugRenderer != null && _debugController != null)
{
    // Prepare overlay information
    string modeConfirmation = _debugController.ShowModeConfirmation 
        ? _debugController.GetModeConfirmationText() 
        : "";

    var debugMode = _debugController.GetCurrentMode();

    // Render debug overlay
    spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
    _debugRenderer.Render(spriteBatch, _lightingV3Foundation, debugMode, 16, modeConfirmation);
    spriteBatch.End();
}
#endif
```

---

## STEP 5: Add to OnExit()

```csharp
// In PlayingState.OnExit() method

#if DEBUG
_debugRenderer?.Dispose();
#endif
```

---

## COMPLETE INTEGRATION CHECKLIST

- [ ] Fields added (DEBUG-only)
- [ ] Initialize in graphics setup
- [ ] Update() reads F6/F7 (DEBUG-only)
- [ ] Draw() renders overlay (DEBUG-only, after composition)
- [ ] Dispose() called on exit (DEBUG-only)
- [ ] Edge detection works (one mode change per F6 press)
- [ ] Mode confirmation printed to console on F6
- [ ] F7 prints metrics or "not initialized" message
- [ ] No effect in RELEASE builds
- [ ] No effect in LEGACY mode

---

## COMMON MISTAKES TO AVOID

❌ **Reading hotkeys in Draw()** instead of Update()
- Result: Input lag, may not register

❌ **Drawing overlay before composition is complete**
- Result: Overlay position is wrong, or appears under other layers

❌ **Not checking DEBUG preprocessor**
- Result: Debug code in RELEASE builds (wasted CPU)

❌ **Not checking IsV3Mode**
- Result: Debug runs in Legacy mode, confuses metrics

❌ **Using foundation before initialization**
- Result: Null reference crash

---

## TESTING HOTKEYS

### F6 Cycle Test

1. Press F6 once → Console shows `[LightingV3] Lighting Debug: Classification`
2. Press F6 again → Console shows `[LightingV3] Lighting Debug: SunOpacity`
3. Continue cycling through all 5 modes
4. Verify no mode change while key is held (edge detection)

### F7 Metrics Test

1. Press F7 → Console shows foundation metrics OR "not initialized" message
2. Verify metrics update each press (no caching)
3. Test in Legacy mode → should have no output (if foundation not instantiated)

---

## EXPECTED CONSOLE OUTPUT

### F6 Pressed

```
[LightingV3Debug] Mode → Classification
[LightingV3] Lighting Debug: Classification
```

### F7 Pressed

```
[LightingV3] Foundation is not initialized.
```

OR

```
[LightingV3 Foundation Metrics]
=== LightingV3Foundation Metrics ===
Sampling: LightingSamplingConfig(2x2, 4 samples/tile, 0.5 tile spacing)
Active Region: ActiveLightingRegion(128x80 tiles, 256x160 samples=40960, origin=(1280, 640))
Active Tiles: 10240
Active Samples: 40960
Classification Time: 0.234ms
Occluder Build Time: 0.512ms
...
```

---

## PLACEHOLDER: Foundation Initialization

Until PlayingState creates LightingV3Foundation, it will be `null`.

Current state: `_lightingV3Foundation` does NOT exist yet in PlayingState.

When integrated (via answering 5 blocking questions):
1. Create `_lightingV3Foundation` in PlayingState
2. Initialize with `WorldDataGeometryAdapter`
3. Call `Update()` in V3 mode branch
4. Call `Dispose()` on exit

---

## VALIDATION

After integration, verify:

✅ Build is clean (DEBUG config)
✅ Run in V3 mode
✅ F6 cycles visualization (check console output)
✅ F7 dumps metrics (check console output)
✅ Overlay appears (if ClassificationMode selected, tiles should highlight)
✅ Switch to Legacy mode
✅ F6/F7 have no effect in Legacy

---

**Integration template ready. Awaiting PlayingState modifications.**
