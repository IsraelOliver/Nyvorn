# V5 Sky Ambient Prototype 0.1 - Experimental Lighting Mode

## Overview

This is an **experimental prototype** for sky-driven ambient lighting in cavern systems. It is NOT a final architectural decision and NOT integrated into the main rendering pipeline yet.

The prototype implements:
- Manual sky portal placement (debug-only)
- Portal-guided light propagation through caverns
- Sky color tinting based on time of day (dawn/day/sunset/night)
- Depth-based foreground illumination
- Multiple debug visualization modes

## Status

**Phase 1 (Core):** ✅ Structure files created
- Config (parameters)
- Portal (data structure)
- Field computation (background light propagation)
- Foreground lighting (depth-based attenuation)
- Compositor (combine + sky color)
- Renderer (upload to texture)
- Debugger (portal tool + vis modes)

**Phase 2 (Integration):** 🚧 TODO
- Integrate into PlayingState rendering
- Add LightingPipelineMode enum entry
- Hook keyboard controls

**Phase 3 (Debug Visualizer):** 🚧 TODO
- Implement 5 visualization modes rendering

## Architecture

### Key Classes

1. **V5SkyAmbientConfig** - Centralized parameter storage
   - Falloff curves (forward, lateral, direction change)
   - Foreground layer weights (5 depths)
   - Saturation curve
   - Camera margin

2. **V5SkyPortal** - Data: tile position of a sky opening

3. **V5SkyAmbientField** - Core algorithm
   - BFS-style propagation from portals
   - Directionally-guided falloff (heuristic)
   - Soft saturation accumulation
   - Reusable buffer

4. **V5ForegroundLighting** - Depth attenuation
   - Precomputes distance from each solid tile to nearest open space
   - Applies depth-based weights (0.0 at layer 5+)

5. **V5SkyAmbientCompositor** - Combines field + foreground
   - Samples both layers
   - Applies sky color tint (configurable strength)

6. **V5SkyAmbientRenderer** - Uploads to texture
   - Prepares Color[] buffer
   - Uploads each frame
   - Provides Draw() method

7. **V5SkyAmbientDebugger** - Input + visualization toggle
   - Portal add/remove by mouse
   - Keyboard shortcuts for modes
   - Tracks which visualization is active

## Debug Controls (To Be Integrated)

```
Shift+V               Toggle between Legacy and V5 mode
LMB on background     Add sky portal at mouse tile
RMB on portal         Remove portal at mouse tile
Ctrl+Shift+P          Clear all portals
1 (key)               Visualization: Portal positions
2 (key)               Visualization: Background field intensity (grayscale)
3 (key)               Visualization: Accumulated contributions
4 (key)               Visualization: Foreground light map
5 (key)               Visualization: Final colored result
Shift+D               Toggle debug overlay on/off
```

## Implementation Notes

### Falloff Heuristic (Temporary)

The propagation algorithm uses a directional falloff to guide light follow natural cave shapes:

- **Forward continuation**: Low falloff (spreads gently)
- **Lateral spread**: Higher falloff (restricted)
- **Direction reversal**: Highest falloff (penalized)

This is a **temporary heuristic** chosen for visual clarity, not a final solution.

### Soft Saturation

Light accumulation from multiple portals uses a smooth saturation curve instead of hard clamp:
- Prevents blown-out white regions
- Maintains contrast in shadow areas
- Ceiling is configurable (default 1.2)

### Foreground Depth Tiers

Solid tiles are classified by distance from the surface:

```
Layer 0 (surface):  1.00x intensity
Layer 1 (1 deep):   0.65x intensity
Layer 2 (2 deep):   0.35x intensity
Layer 3 (3 deep):   0.15x intensity
Layer 4+ (4+ deep): 0.00x intensity (black)
```

These weights are configurable per the spec.

### Sky Color Tint

Sky color is sampled from `SkyState.AmbientLight` (provided by WorldEnvironmentSystem):
- Dawn: cool + warm blend
- Day: bright cool blue
- Sunset: warm orange
- Night: deep cool blue

The tint strength is configurable (default 0.85 = mostly colored, 0.15 white).

## Limitations & Known Issues

1. **Not yet integrated**: The prototype classes exist but are not wired into the render pipeline.
2. **Portals are manual**: No automatic detection of open spaces. Debug-only for now.
3. **No directional sun**: Only ambient color, no strong sun direction or ray shafts.
4. **No performance optimizations yet**: Priorities correctness + adjustability first.
5. **Background/foreground not yet separated**: The compositor assumes both can be sampled at any tile. Actual integration will need to clarify rendering layer structure.

## Validation Checklist (To Complete)

- [ ] One portal isolates and illuminates a background region
- [ ] Two nearby portals create overlap with additive light
- [ ] Foreground darkens correctly over 5 layers
- [ ] Day/sunset/night color changes are visible
- [ ] Instant toggle between Legacy and V5 works
- [ ] Portal add/remove updates result immediately
- [ ] All 5 debug vis modes render without crash

## Files Created

```
Nyvorn/Source/Engine/Graphics/LightingPipeline/
  V5SkyAmbientConfig.cs
  V5SkyPortal.cs
  V5SkyAmbientField.cs
  V5ForegroundLighting.cs
  V5SkyAmbientCompositor.cs
  V5SkyAmbientRenderer.cs
  V5SkyAmbientDebugger.cs
  V5_SKY_AMBIENT_PROTOTYPE_README.md (this file)
```

## Next Steps

1. **Phase 2**: Integrate into PlayingState (add mode selection, update call, render call)
2. **Phase 3**: Implement debug visualization rendering
3. **Phase 4**: Test with validation scenes
4. **Phase 5**: Measure performance (field computation time, upload time)
5. **Phase 6**: Document findings + limitations for architectural review

---

**Created**: Experimental prototype, August 2026  
**Status**: Not for production, not final architecture  
**Priority**: Visual iteration + parameter tuning
