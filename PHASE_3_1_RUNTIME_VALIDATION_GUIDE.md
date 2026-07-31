# Phase 3.1 Runtime Validation Guide

**Objective**: Validate that the real solar provider (GameSolarProvider) correctly exposes sun state at 4 points in the cycle: Morning, Noon, Evening, Night.

---

## Setup

The following classes are now available:

1. **GameSolarProvider** - Real implementation of ISolarProvider
   - Bridges V3 lighting to game's day/night cycle (ISunCycleProvider)
   - Converts sun color from gamma to linear
   - Maps time of day to elevation angle
   - Calculates intensity based on sun position

2. **Phase3_1RuntimeValidator** - Diagnostic dumper
   - RecordDump(label, timeOfDay, state, updateId) - capture state
   - PrintDumps() - display all captured dumps
   - Validate() - run validation checks

---

## How to Execute Runtime Validation

### Step 1: Integrate GameSolarProvider into PlayingState

In PlayingState (where LightingV3Foundation is created):

```csharp
// Get the ISunCycleProvider from game systems
var sunCycleProvider = session.ViewCoordinator.SunCycleProvider;  // Or however it's accessed
var solarProvider = new GameSolarProvider(sunCycleProvider);

// Pass to Foundation
var foundation = new LightingV3Foundation(
    geometryProvider,
    samplingConfig,
    solarProvider);
```

### Step 2: Add Dump Points

In PlayingState.Draw() or Update(), capture state at each cycle point:

```csharp
// In game loop where you can check timeOfDay01
var frameData = foundation.GetFrameData();
if (frameData.HasValue)
{
    float timeOfDay01 = solarProvider.GetTimeOfDay01();
    
    // Morning (~6:00 AM) - timeOfDay01 ≈ 0.25
    if (timeOfDay01 > 0.23f && timeOfDay01 < 0.27f)
    {
        Phase3_1RuntimeValidator.RecordDump("Morning", timeOfDay01, frameData.Value.SunState, frameData.Value.UpdateId);
    }
    
    // Noon (~12:00 PM) - timeOfDay01 ≈ 0.5
    if (timeOfDay01 > 0.48f && timeOfDay01 < 0.52f)
    {
        Phase3_1RuntimeValidator.RecordDump("Noon", timeOfDay01, frameData.Value.SunState, frameData.Value.UpdateId);
    }
    
    // Evening (~6:00 PM) - timeOfDay01 ≈ 0.75
    if (timeOfDay01 > 0.73f && timeOfDay01 < 0.77f)
    {
        Phase3_1RuntimeValidator.RecordDump("Evening", timeOfDay01, frameData.Value.SunState, frameData.Value.UpdateId);
    }
    
    // Night (~0:00 AM) - timeOfDay01 ≈ 0.0 or 1.0
    if (timeOfDay01 < 0.1f || timeOfDay01 > 0.9f)
    {
        Phase3_1RuntimeValidator.RecordDump("Night", timeOfDay01, frameData.Value.SunState, frameData.Value.UpdateId);
    }
}
```

### Step 3: Run Validation

When you have collected all 4 dumps (by playing through 1 full cycle in accelerated time):

```csharp
Phase3_1RuntimeValidator.PrintDumps();
Phase3_1RuntimeValidator.Validate();
```

Or add a hotkey (e.g., Ctrl+Alt+3):

```csharp
if (inputService.IsKeyJustPressed(Keys.D3) && (inputService.IsKeyHeld(Keys.LeftControl) && inputService.IsKeyHeld(Keys.LeftAlt)))
{
    Phase3_1RuntimeValidator.PrintDumps();
    Phase3_1RuntimeValidator.Validate();
}
```

---

## Expected Validation Output

```
╔════════════════════════════════════════════════════════════════════════════════╗
║ PHASE 3.1 RUNTIME VALIDATION - SOLAR PROVIDER STATE DUMPS                     ║
╚════════════════════════════════════════════════════════════════════════════════╝

═══════════════════════════════════════════════════════════
TIME: Morning (TimeOfDay=0.250)
───────────────────────────────────────────────────────────
UpdateId:               42
DirectionToSun:        (-0.6523, -0.7580)
LightTravelDirection:  (0.6523, 0.7580)
Elevation:             49.24°
Intensity:             0.8500
IsAboveHorizon:        true
LinearColor:           (0.8000, 0.6500, 0.4500)
IsValid:               true

[... Noon, Evening, Night similar format ...]

╔════════════════════════════════════════════════════════════════════════════════╗
║ PHASE 3.1 VALIDATION RESULTS                                                  ║
╚════════════════════════════════════════════════════════════════════════════════╝

VALIDATION 1: All states valid (no NaN/infinity)
  Morning: ✓
  Noon: ✓
  Evening: ✓
  Night: ✓

VALIDATION 2: DirectionToSun normalized
  Morning: ✓
  Noon: ✓
  Evening: ✓
  Night: ✓

VALIDATION 3: LightTravelDirection is exactly opposite
  Morning: ✓
  Noon: ✓
  Evening: ✓
  Night: ✓

VALIDATION 4: Noon - DirectionToSun points upward
  Y component: -0.9990 ✓

VALIDATION 5: Morning/Evening have opposite horizontal components
  Morning X: -0.6523
  Evening X: 0.6523
  Opposite signs: ✓

VALIDATION 6: Night - IsAboveHorizon=false
  IsAboveHorizon: false ✓

VALIDATION 7: Night - Intensity is low (0.0-0.2)
  Intensity: 0.0500 ✓

VALIDATION 8: All dumps in same frame (same UpdateId)
  UpdateId consistency: ✓

╔════════════════════════════════════════════════════════════════════════════════╗
║ ✓ ALL VALIDATIONS PASSED - SOLAR PROVIDER READY FOR PHASE 3.2                 ║
╚════════════════════════════════════════════════════════════════════════════════╝
```

---

## What Validations Check

1. **All states valid**: No NaN or infinity in any field
2. **DirectionToSun normalized**: Unit vector with |length - 1| < tolerance
3. **LightTravelDirection opposite**: Exactly -DirectionToSun
4. **Noon upward**: Y component should be < -0.5 (negative Y = up)
5. **Morning/Evening opposite**: Horizontal X signs should differ
6. **Night not above horizon**: IsAboveHorizon must be false
7. **Night intensity low**: Should be in range [0, 0.2]
8. **Frame consistency**: All 4 dumps must have same UpdateId (proven immutability)

---

## Cycle Times (Typical Configuration)

Assuming standard 24-hour cycle mapped to [0, 1]:

- **Morning**: TimeOfDay01 ≈ 0.25 (6:00 AM)
- **Noon**: TimeOfDay01 ≈ 0.50 (12:00 PM)
- **Evening**: TimeOfDay01 ≈ 0.75 (6:00 PM)  
- **Night**: TimeOfDay01 ≈ 0.0 or 1.0 (midnight)

Exact times depend on the game's WorldDayNightCycle configuration.

---

## Acceleration for Testing

To speed up the cycle during testing:

1. Find the time acceleration factor in game config
2. Increase it to ~10-100x normal speed
3. Play through 1 full cycle (~15-30 seconds at 100x)
4. Dumps will be captured automatically
5. Call validation when cycle is complete

---

## Troubleshooting

**No dumps recorded**:
- Verify the if-conditions match timeOfDay01 values for your game
- Check that frameData.HasValue is true (Foundation initialized?)
- Verify SolarProvider is passed to Foundation constructor

**Dumps show wrong values**:
- Check that GameSolarProvider is using correct ISunCycleProvider
- Verify ISunCycleProvider.SunDirection matches sun's visual position
- Confirm color conversion from gamma to linear is working

**Validation fails**:
- If NaN/infinity: check sun direction normalization
- If not opposite: verify LightTravelDirection calculation
- If noon not upward: check elevation mapping formula
- If night intensity high: verify NightStrength is being used

---

## Next Steps

Once validation **✓ PASSES**:
- ✅ Phase 3.1 is approved
- ✅ GameSolarProvider proven correct
- ✅ Ready for Phase 3.2 (Ray Marching)

Do NOT proceed to Phase 3.2 until all 8 validations pass.
