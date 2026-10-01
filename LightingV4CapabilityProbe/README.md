# Lighting V4.2B-B: MonoGame Capability Validation

**Status**: Implementation in Progress  
**Phase**: V4.2B-B Capability Validation  
**Manifest**: [LIGHTING_V4_2B_B_TEST_MANIFEST.md](../LIGHTING_V4_2B_B_TEST_MANIFEST.md)

---

## Overview

Isolated test harness for MonoGame.DesktopGL capabilities.

**Important**: This project is SEPARATE from Nyvorn main.
- No integration with PlayingState
- No changes to Legacy or Lighting V3
- No connection to production pipeline

---

## Building

### Prerequisites

- .NET 8.0 or later
- MonoGame 3.8.x (same as Nyvorn)
- MonoGame Content Builder (MGCB)

### Build Commands

```bash
# Build Debug configuration
dotnet build LightingV4CapabilityProbe -c Debug

# Build Release configuration
dotnet build LightingV4CapabilityProbe -c Release
```

---

## Running Tests

### List Available Tests

```bash
dotnet run --project LightingV4CapabilityProbe -c Release -- --list-tests
```

### Run All Tests

```bash
dotnet run --project LightingV4CapabilityProbe -c Release -- --run-all
```

### Run by Category

```bash
# Environment tests only
dotnet run --project LightingV4CapabilityProbe -c Release -- --run-category Environment

# RenderTarget tests only
dotnet run --project LightingV4CapabilityProbe -c Release -- --run-category RenderTarget

# Shader tests only
dotnet run --project LightingV4CapabilityProbe -c Release -- --run-category ShaderLoop
```

### Run Single Test

```bash
dotnet run --project LightingV4CapabilityProbe -c Release -- --run-test CP-ENV-001

dotnet run --project LightingV4CapabilityProbe -c Release -- --run-test CP-SHADER-008
```

### Options

```bash
# Skip visual inspection tests (run only automatics)
-- --skip-visual

# Override resolution (reduced|current|1440p)
-- --resolution reduced

# Run without display (headless; automatics only)
-- --headless

# Set timeout override (milliseconds)
-- --timeout 30000
```

---

## Test Categories

### Automatic Tests

Tests that pass/fail by code logic without manual inspection:
- Environment detection
- Resource creation
- Shader compilation
- API calls
- Exception handling
- CPU timing
- Memory allocation

### Visual Inspection Tests

Tests that generate output requiring human review:
- Render target visual correctness
- Shader output appearance
- Blend state results
- Filtering/wrapping visual quality

Status: `PENDING REVIEW` until inspected

---

## Output Files

After tests complete:

### LIGHTING_V4_CAPABILITY_RESULTS.md

Formatted results with:
- Test summary
- Pass/fail counts by status
- Constraints discovered
- Viable/inviable techniques
- Recommendations for spikes

### LIGHTING_V4_CAPABILITY_RAW.csv

Raw data for analysis:
- TestId, Category, Status, DurationMs
- CPU timing (setup, submission, draw)
- Allocations, resource sizes
- Failure details

### LIGHTING_V4_CAPABILITY_FAILURES.md

Failures and inconclusive results:
- Environment-specific notes
- Hardware limitations discovered
- Risks identified

---

## Implementation Status

- [x] Project structure
- [x] Test manifest (typed with IDs)
- [x] Runner skeleton (isolation, exception handling)
- [x] Environment tests (stub)
- [x] Shader files (Loop8, Loop16, Loop32, Loop64)
- [ ] RenderTarget tests (full implementation)
- [ ] Sampler tests (full implementation)
- [ ] Buffer tests (full implementation)
- [ ] Blend state tests (full implementation)
- [ ] Texture SetData/GetData tests (full implementation)
- [ ] Results export (Markdown, CSV, JSON)

---

## Next Steps

1. Complete test implementations
2. Run locally: `--run-all`
3. Review CAPABILITY_RESULTS.md
4. Verify no platform blockers
5. **STOP** — await approval before starting Spike A/B/C/E

---

## Architecture Notes

### Isolation

Each test runs in a try-catch:
- Failure in one test doesn't stop others
- Exception details captured
- Resource cleanup guaranteed
- Timeout enforcement per test

### Metrics

Only valid metrics recorded:
- ✓ CPU wall time (setup, submission, draw, frame interval)
- ✓ Allocations
- ✓ Draw calls, vertices, triangles
- ✓ Test duration
- ✗ NO "GPU time estimated"
- ✗ NO "generic blockers"

### Resolution Testing

Three resolutions:
- **Reduced** (~960×504): Required; proportional to 16:9
- **Current** (backbuffer): Required; detected at startup
- **1440p** (2560×1440): Optional; only if display supports

Tests marked `NOT TESTED` if resolution unsupported.

---

## Important

**DO NOT START SPIKES UNTIL**:
1. All capability tests run
2. Results reviewed and approved
3. Platform assumptions validated
4. Constraints documented

---

**Phase**: V4.2B-B  
**Status**: Implementation in progress  
**Timeline**: 3-5 days  
**Gate**: Review results before V4.2B-C (spikes)
