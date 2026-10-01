> DOCUMENTO HISTÓRICO — descreve o estado de 2026-08-11, quando a V6 era o
> pipeline único. Não reflete o estado atual (V7 padrão, V8 em desenvolvimento).
> Ver [[00-system-map]].

# Ny'vorn Lighting System — Baseline Checkpoint

**Status**: LIGHTING BASELINE — QUASE PRONTA  
**Revision**: Phase 3 Consolidation Complete  
**Date**: 2026-08-11

## Architecture

### Core System
- **V6LightingSystem**: 5-step unified pipeline
  1. Classify terrain/foreground/water cells
  2. Compute surface light (direct propagation)
  3. Render background glow (sky-exposed cells)
  4. Propagate background → foreground indirect coupling
  5. Apply artificial point lights (torches)

### Storage
- **V6LightMap**: Float RGB arrays + CellMedium classification
- **V6LightMapRenderer**: Converts RGB to ProductionTexture (GPU texture)
- **Active Buffer**: Dynamic region around camera with configurable margin

### Sampling
- **V6LightSampler**: Bilinear interpolation for entity tinting
- **IEntityLightSampler**: Interface implemented by V6LightSampler
- **Consumer Pattern**: DrawEntities (player), DrawLoopedWorldEntities (enemies/items)

## Lighting Sources

### Natural
- **Sky Ambient**: Color from SkyState.AmbientLight (day/night dependent)
- **Background Glow**: Propagates from sky-exposed cells downward
- **Foreground Response**: Attenuates based on terrain density
- **Background → Foreground Indirect**: Weak coupling (0.20 strength)

### Artificial
- **Torch Point Light**: Single source per torch furniture
- **Algorithm**: Weighted line attenuation (Xiaolin-Wu-like ray coverage distribution)
- **Energy Model**: remainingEnergy = intensity - (airAttenuation + obstacleAttenuation)

## Occlusion

- **Foreground Tiles**: CellMedium classification blocks/attenuates light
- **Closed Doors**: Special handling for door occlusion (high attenuation)
- **Propagation**: 4-pass bidirectional sweep for stable convergence

## Current Parameters (Baseline 090ab3b — Approved)

| Parameter | Value | Role |
|-----------|-------|------|
| **TorchLightColor** | (1.0, 0.60, 0.20) | Warm orange flame |
| **TorchLightIntensity** | 1.0 | Multiplier on RGB |
| **TorchLightRadiusTiles** | 9 | Maximum reach |
| **PointLightAirAttenuationPerTile** | 0.08 | Linear decay in open air |
| **PointLightForegroundObstacleAttenuation** | 0.70 | Per-tile penetration cost |
| **PointLightDoorObstacleAttenuation** | 0.95 | Closed door blocks nearly all |
| **PropagationPasses** | 4 | Convergence iterations per frame |
| **BackgroundSeedIntensity** | 0.90 | Sky-open initialization |
| **ForegroundIndirectFromBackgroundStrength** | 0.20 | Weak coupling factor |

## Consumers

| Entity | Sampler | Grid |
|--------|---------|------|
| **Player** | V6LightSampler | 2×3 hurtbox points → averaged |
| **Enemy** | V6LightSampler | Point sample at center |
| **WorldItem** | V6LightSampler | Point sample at center |
| **Static Objects** | V6LightMap directly | Tile-based classification |

## Draw Pipeline (V6 Consolidated)

```
PlayingState.Draw()
  → v6LightingSystem.Update()           # Compute lighting
  → v6LightMapRenderer.Update()         # Render RGB texture
  → DrawGameplayWorld()                 # Single unified pipeline
      → DrawWorldLitObjects()           # Static world objects
      → Draw V6 ProductionTexture       # Apply multiply-blend lighting
      → DrawEntities(v6LightSampler)   # Player with V6 tint
      → DrawLoopedWorldEntities()       # Enemies, items, torches
      → DrawNightOverlay()              # Final darkness layer
```

## Known Limitations

- **Tile-Based Resolution**: Final lighting tied to tile grid (no sub-tile precision)
- **Static Object Shadows**: Not implemented; objects receive ambient only
- **Furnace Light**: Not active; placeholder for future expansion
- **Bloom/Emissive**: Polish pending (torches have no halo yet)
- **Directional Sunlight**: Simplified to ambient color; no angle-dependent shading
- **Performance**: Linear with buffer size; camera view determines cost

## No Regressions vs. Baseline 090ab3b

- ✅ Natural lighting identical
- ✅ Torch point light identical
- ✅ Door occlusion identical  
- ✅ Entity tinting identical
- ✅ Draw order frozen
- ✅ FPS stable
- ✅ No visual artifacts

## Future Work (Out of Scope)

- Pixel-perfect light with sub-tile precision (WorldColorRenderTarget)
- Object-space lighting and shadows (stencil-based)
- Furnace light as point source
- Bloom / emissive polish
- Sun shafts / directional effects
- Advanced specular / parallax lighting

## Consolidation Changes (Phase 3)

**Removed**:
- 7 V5 sky-ambient files
- WorldLightingSystem and all Legacy paths
- isV6TerrariaMode flag and branches
- Pipeline mode switching infrastructure
- Legacy entity sampler + metrics
- All DrawWorldLighting, PrepareWorldLighting, PrepareTorchGlow code

**Preserved**:
- V6 core (LightingSystem, LightMap, LightSampler, LightMapRenderer)
- V6 configuration (LightingConfig)
- Entity lighting abstraction (IEntityLightSampler)
- Geometry/occlusion infrastructure (used by V6)

**Compiles**: ✅ (16 warnings: Legacy buffer fields not used — safe to ignore)

## Update Frequency

- **v6LightingSystem.Update()**: Once per frame in PlayingState.Draw()
- **v6LightMapRenderer.Update()**: Once per frame after LightingSystem
- **Torch point lights**: Updated via SetArtificialLightSources() each frame

## Safety Checkpoints

1. ✅ V6 Update called exactly once per frame
2. ✅ No entity duplication in draw order
3. ✅ No V5 or Legacy references remain
4. ✅ All toggles removed (isV6TerrariaMode, IsLegacyMode)
5. ✅ Draw order frozen and verified
6. ✅ Parameters locked (no ad-hoc tuning)
7. ✅ Compilation successful with 0 errors

---

**Checkpoint Status**: READY FOR CONSOLIDATION COMMIT

V6 is the sole, official lighting pipeline. No alternatives exist. Visual presentation locked.
