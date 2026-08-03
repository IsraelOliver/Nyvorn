using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Phase 2 Foundation: Spatial and geometric foundation for V3 lighting.
    /// Manages active region, classification, and occlusion fields.
    ///
    /// Does NOT implement visual lighting yet.
    /// Only describes:
    /// - Where elements are (ActiveLightingRegion)
    /// - What type they are (SceneWorldClassifier)
    /// - How much they block (OccluderField)
    ///
    /// Depends on ILightingWorldGeometryProvider for foreground/background queries.
    /// Does NOT depend on WorldMap directly (decoupled via interface).
    /// </summary>
    public class LightingV3Foundation
    {
        private readonly LightingSamplingConfig _samplingConfig;
        private readonly ILightingWorldGeometryProvider _geometryProvider;
        private readonly List<IOccluderProvider> _occluderProviders;
        private readonly ISolarProvider _solarProvider;

        private ActiveLightingRegion _activeRegion;
        private RenderedFrameProfiler _profiler;  // Optional profiler for Phase A0.0

        // Double-buffered frame slots (true immutability via independent buffers)
        private LightingV3FrameSlot _backSlot;  // Being built during Update
        private LightingV3FrameSlot _frontSlot; // Consumed by renderer during Draw
        private int _updateId;

        // Metrics
        public int ActiveTileCount { get; private set; }
        public int ActiveSampleCount { get; private set; }
        public double ClassificationTimeMs { get; private set; }
        public double OccluderBuildTimeMs { get; private set; }
        public double SunVisibilityBuildTimeMs { get; private set; }
        public int BufferResizeCount { get; private set; }

        // Phase 3.2A metrics - Semantically consistent
        public int SunVisibilitySampleCount { get; private set; }
        public int SunVisibilityPreTraceSkips { get; private set; }
        public int SunVisibilityBelowHorizonSkips { get; private set; }
        public int SunVisibilityLowIntensitySkips { get; private set; }
        public int SunVisibilityLowElevationSkips { get; private set; }
        public int SunVisibilityTraceCandidates { get; private set; }
        public int SunVisibilityStartingCellBlocks { get; private set; }
        public int SunVisibilityDdaRaysStarted { get; private set; }
        public int SunVisibilityCellsVisited { get; private set; }
        public int SunVisibilityEarlyOuts { get; private set; }
        public int SunVisibilityFullyFreeRays { get; private set; }
        public int SunVisibilityPartiallyTransmittedRays { get; private set; }
        public int SunVisibilityFullyBlockedRays { get; private set; }
        public int SunVisibilityGuardLimitHits { get; private set; }
        public int SunVisibilityMaximumCellsPerRay { get; private set; }

        // Derived
        public float SunVisibilityAverageCellsPerTrace =>
            SunVisibilityTraceCandidates > 0 ? (float)SunVisibilityCellsVisited / SunVisibilityTraceCandidates : 0f;
        public float SunVisibilityAverageDdaCellsPerRay =>
            SunVisibilityDdaRaysStarted > 0 ? (float)SunVisibilityCellsVisited / SunVisibilityDdaRaysStarted : 0f;

        /// <summary>
        /// Create foundation with default 2x2 sampling (4 samples per tile).
        /// </summary>
        public LightingV3Foundation(ILightingWorldGeometryProvider geometryProvider)
            : this(geometryProvider, LightingSamplingConfig.Default2x2)
        {
        }

        /// <summary>
        /// Create foundation with custom sampling configuration.
        /// </summary>
        public LightingV3Foundation(ILightingWorldGeometryProvider geometryProvider, LightingSamplingConfig samplingConfig)
            : this(geometryProvider, samplingConfig, null)
        {
        }

        /// <summary>
        /// Create foundation with sampling configuration and solar provider.
        /// </summary>
        public LightingV3Foundation(ILightingWorldGeometryProvider geometryProvider, LightingSamplingConfig samplingConfig, ISolarProvider solarProvider)
        {
            _geometryProvider = geometryProvider ?? throw new ArgumentNullException(nameof(geometryProvider));
            _samplingConfig = samplingConfig ?? throw new ArgumentNullException(nameof(samplingConfig));
            _solarProvider = solarProvider;
            _occluderProviders = new List<IOccluderProvider>();

            _activeRegion = new ActiveLightingRegion(_samplingConfig);

            // Create double-buffered slots (256 initial capacity)
            _backSlot = new LightingV3FrameSlot(256, 1024);
            _frontSlot = new LightingV3FrameSlot(256, 1024);
            _updateId = 0;

            RegisterDefaultProviders();
        }

        /// <summary>
        /// Register the mandatory foreground tile provider.
        /// </summary>
        private void RegisterDefaultProviders()
        {
            _occluderProviders.Add(new ForegroundTileOccluderProvider(_geometryProvider));
        }

        /// <summary>
        /// Set profiler for Phase A0.0 Emergency Starvation Capture.
        /// </summary>
        public void SetProfiler(RenderedFrameProfiler profiler)
        {
            _profiler = profiler;
        }

        /// <summary>
        /// Register an additional occluder provider (trees, structures, etc).
        /// </summary>
        public void RegisterOccluderProvider(IOccluderProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            _occluderProviders.Add(provider);
        }

        /// <summary>
        /// Update foundation from authoritative visible world rect (Task 2.1).
        /// This is the preferred path - uses shared VisibleWorldRect from renderer.
        /// </summary>
        public void UpdateFromVisibleWorldRect(VisibleWorldRect visibleWorldRect, int tileSize)
        {
            // Capture previous origin for change detection
            float prevOriginX = _activeRegion.WorldOriginX;
            float prevOriginY = _activeRegion.WorldOriginY;

            // Convert float boundaries to tile indices (floor/ceil preserve precision)
            float minTileXFloat = visibleWorldRect.Left / tileSize;
            float maxTileXFloat = visibleWorldRect.Right / tileSize;
            float minTileYFloat = visibleWorldRect.Top / tileSize;
            float maxTileYFloat = visibleWorldRect.Bottom / tileSize;

            int minTileX = (int)System.Math.Floor(minTileXFloat);
            int maxTileXInclusive = (int)System.Math.Ceiling(maxTileXFloat) - 1;
            int minTileY = (int)System.Math.Floor(minTileYFloat);
            int maxTileYInclusive = (int)System.Math.Ceiling(maxTileYFloat) - 1;

            // Calculate region dimensions before margin
            int visibleTileCountX = maxTileXInclusive - minTileX + 1;
            int visibleTileCountY = maxTileYInclusive - minTileY + 1;

            // Apply margin (1 tile default) exactly once
            int marginTiles = 1;
            int regionWidthTiles = visibleTileCountX + (marginTiles * 2);
            int regionHeightTiles = visibleTileCountY + (marginTiles * 2);

            // Calculate final region origin (top-left corner in world pixels)
            int leftTileWithMargin = minTileX - marginTiles;
            int topTileWithMargin = minTileY - marginTiles;
            float regionOriginX = leftTileWithMargin * tileSize;
            float regionOriginY = topTileWithMargin * tileSize;

            // Update active region dimensions using direct assignment
            // NOTE: We pass the values to Update() which sets them internally
            _activeRegion.UpdateFromTiles(regionWidthTiles, regionHeightTiles, regionOriginX, regionOriginY, tileSize);

            ActiveTileCount = regionWidthTiles * regionHeightTiles;
            ActiveSampleCount = _activeRegion.TotalSamples;

            // Reset Phase 3.2A metrics
            SunVisibilitySampleCount = 0;
            SunVisibilityPreTraceSkips = 0;
            SunVisibilityBelowHorizonSkips = 0;
            SunVisibilityLowIntensitySkips = 0;
            SunVisibilityLowElevationSkips = 0;
            SunVisibilityTraceCandidates = 0;
            SunVisibilityStartingCellBlocks = 0;
            SunVisibilityDdaRaysStarted = 0;
            SunVisibilityCellsVisited = 0;
            SunVisibilityEarlyOuts = 0;
            SunVisibilityFullyFreeRays = 0;
            SunVisibilityPartiallyTransmittedRays = 0;
            SunVisibilityFullyBlockedRays = 0;
            SunVisibilityGuardLimitHits = 0;
            SunVisibilityMaximumCellsPerRay = 0;

            // Ensure back slot capacity
            _backSlot.EnsureCapacity(ActiveTileCount, ActiveSampleCount);
            _backSlot.ClearBuffers(ActiveTileCount, ActiveSampleCount);

            // Classify tiles in back slot
            if (_profiler != null)
                _profiler.OnClassificationStart();
            try
            {
                ClassifyRegionToSlot(_backSlot, tileSize);
            }
            finally
            {
                if (_profiler != null)
                    _profiler.OnClassificationEnd();
            }

            // Build opacities in back slot
            if (_profiler != null)
                _profiler.OnOccluderBuildStart();
            try
            {
                BuildOpacityFieldsToSlot(_backSlot, tileSize);
            }
            finally
            {
                if (_profiler != null)
                    _profiler.OnOccluderBuildEnd();
            }

            // Build sun visibility field (Phase 3.2A)
            if (_profiler != null)
                _profiler.OnSunVisibilityBuildStart();
            try
            {
                BuildSunVisibilityFieldToSlot(_backSlot, tileSize);
            }
            finally
            {
                if (_profiler != null)
                    _profiler.OnSunVisibilityBuildEnd();
            }

            // Calculate probe position (sample at 20,20 local coordinates)
            int probeLocalSampleX = 20;
            int probeLocalSampleY = 20;
            int probeIndex = probeLocalSampleY * _activeRegion.RegionWidthSamples + probeLocalSampleX;

            float sampleSpacingX = _activeRegion.RegionWidthTiles > 0
                ? (float)(_activeRegion.RegionWidthTiles * tileSize) / _activeRegion.RegionWidthSamples
                : 1f;
            float sampleSpacingY = _activeRegion.RegionHeightTiles > 0
                ? (float)(_activeRegion.RegionHeightTiles * tileSize) / _activeRegion.RegionHeightSamples
                : 1f;
            float sampleCenterOffsetX = sampleSpacingX / 2f;
            float sampleCenterOffsetY = sampleSpacingY / 2f;

            float probeWorldX = _activeRegion.WorldOriginX + probeLocalSampleX * sampleSpacingX + sampleCenterOffsetX;
            float probeWorldY = _activeRegion.WorldOriginY + probeLocalSampleY * sampleSpacingY + sampleCenterOffsetY;

            float probeSunOpacity = probeIndex >= 0 && probeIndex < _backSlot.SunOpacityBuffer.Length
                ? _backSlot.SunOpacityBuffer[probeIndex]
                : 0f;
            float probeLocalOpacity = probeIndex >= 0 && probeIndex < _backSlot.LocalOpacityBuffer.Length
                ? _backSlot.LocalOpacityBuffer[probeIndex]
                : 0f;

            // Store probe data in back slot
            _backSlot.ProbeLocalSampleX = probeLocalSampleX;
            _backSlot.ProbeLocalSampleY = probeLocalSampleY;
            _backSlot.ProbeSampleWorldX = probeWorldX;
            _backSlot.ProbeSampleWorldY = probeWorldY;
            _backSlot.ProbeSunOpacity = probeSunOpacity;
            _backSlot.ProbeLocalOpacity = probeLocalOpacity;

            // Get solar state from provider (Phase 3.1)
            if (_solarProvider != null)
            {
                _backSlot.SunState = _solarProvider.GetSunState();
            }
            else
            {
                _backSlot.SunState = LightingV3SunState.Night();  // Default if no provider
            }

            // CRITICAL: Publish frame atomically after both buffers are ready
            _updateId++;
            _backSlot.FrameId = _updateId;
            _backSlot.Region = new ActiveRegionSnapshot(_activeRegion, tileSize);

            // Log probe when WorldOrigin changes (disabled during profiling)
            bool regionChanged = prevOriginX != _activeRegion.WorldOriginX ||
                               prevOriginY != _activeRegion.WorldOriginY;

            // Swap buffers: front becomes the published data, back is next to build
            var temp = _frontSlot;
            _frontSlot = _backSlot;
            _backSlot = temp;
        }

        /// <summary>
        /// Update foundation for current camera position and render size.
        /// Called once per frame before rendering.
        /// DEPRECATED: Use UpdateFromVisibleWorldRect instead.
        /// </summary>
        public void Update(float cameraWorldX, float cameraWorldY, int logicalRenderWidth, int logicalRenderHeight, int tileSize)
        {
            // Capture previous origin for change detection
            float prevOriginX = _activeRegion.WorldOriginX;
            float prevOriginY = _activeRegion.WorldOriginY;

            // Update active region (old path - ignores zoom)
            _activeRegion.Update(cameraWorldX, cameraWorldY, logicalRenderWidth, logicalRenderHeight, tileSize);

            ActiveTileCount = _activeRegion.RegionWidthTiles * _activeRegion.RegionHeightTiles;
            ActiveSampleCount = _activeRegion.TotalSamples;


            // Reset Phase 3.2A metrics
            SunVisibilitySampleCount = 0;
            SunVisibilityPreTraceSkips = 0;
            SunVisibilityBelowHorizonSkips = 0;
            SunVisibilityLowIntensitySkips = 0;
            SunVisibilityLowElevationSkips = 0;
            SunVisibilityTraceCandidates = 0;
            SunVisibilityStartingCellBlocks = 0;
            SunVisibilityDdaRaysStarted = 0;
            SunVisibilityCellsVisited = 0;
            SunVisibilityEarlyOuts = 0;
            SunVisibilityFullyFreeRays = 0;
            SunVisibilityPartiallyTransmittedRays = 0;
            SunVisibilityFullyBlockedRays = 0;
            SunVisibilityGuardLimitHits = 0;
            SunVisibilityMaximumCellsPerRay = 0;

            // Ensure back slot capacity
            _backSlot.EnsureCapacity(ActiveTileCount, ActiveSampleCount);
            _backSlot.ClearBuffers(ActiveTileCount, ActiveSampleCount);

            // Classify tiles in back slot
            if (_profiler != null)
                _profiler.OnClassificationStart();
            try
            {
                ClassifyRegionToSlot(_backSlot, tileSize);
            }
            finally
            {
                if (_profiler != null)
                    _profiler.OnClassificationEnd();
            }

            // Build opacities in back slot
            if (_profiler != null)
                _profiler.OnOccluderBuildStart();
            try
            {
                BuildOpacityFieldsToSlot(_backSlot, tileSize);
            }
            finally
            {
                if (_profiler != null)
                    _profiler.OnOccluderBuildEnd();
            }

            // Build sun visibility field (Phase 3.2A)
            if (_profiler != null)
                _profiler.OnSunVisibilityBuildStart();
            try
            {
                BuildSunVisibilityFieldToSlot(_backSlot, tileSize);
            }
            finally
            {
                if (_profiler != null)
                    _profiler.OnSunVisibilityBuildEnd();
            }

            // Calculate probe position (sample at 20,20 local coordinates)
            int probeLocalSampleX = 20;
            int probeLocalSampleY = 20;
            int probeIndex = probeLocalSampleY * _activeRegion.RegionWidthSamples + probeLocalSampleX;

            float sampleSpacingX = _activeRegion.RegionWidthTiles > 0
                ? (float)(_activeRegion.RegionWidthTiles * tileSize) / _activeRegion.RegionWidthSamples
                : 1f;
            float sampleSpacingY = _activeRegion.RegionHeightTiles > 0
                ? (float)(_activeRegion.RegionHeightTiles * tileSize) / _activeRegion.RegionHeightSamples
                : 1f;
            float sampleCenterOffsetX = sampleSpacingX / 2f;
            float sampleCenterOffsetY = sampleSpacingY / 2f;

            float probeWorldX = _activeRegion.WorldOriginX + probeLocalSampleX * sampleSpacingX + sampleCenterOffsetX;
            float probeWorldY = _activeRegion.WorldOriginY + probeLocalSampleY * sampleSpacingY + sampleCenterOffsetY;

            float probeSunOpacity = probeIndex >= 0 && probeIndex < _backSlot.SunOpacityBuffer.Length
                ? _backSlot.SunOpacityBuffer[probeIndex]
                : 0f;
            float probeLocalOpacity = probeIndex >= 0 && probeIndex < _backSlot.LocalOpacityBuffer.Length
                ? _backSlot.LocalOpacityBuffer[probeIndex]
                : 0f;

            // Store probe data in back slot
            _backSlot.ProbeLocalSampleX = probeLocalSampleX;
            _backSlot.ProbeLocalSampleY = probeLocalSampleY;
            _backSlot.ProbeSampleWorldX = probeWorldX;
            _backSlot.ProbeSampleWorldY = probeWorldY;
            _backSlot.ProbeSunOpacity = probeSunOpacity;
            _backSlot.ProbeLocalOpacity = probeLocalOpacity;

            // Get solar state from provider (Phase 3.1)
            if (_solarProvider != null)
            {
                _backSlot.SunState = _solarProvider.GetSunState();
            }
            else
            {
                _backSlot.SunState = LightingV3SunState.Night();  // Default if no provider
            }

            // CRITICAL: Publish frame atomically after both buffers are ready
            _updateId++;
            _backSlot.FrameId = _updateId;
            _backSlot.Region = new ActiveRegionSnapshot(_activeRegion, tileSize);

            // Log probe when WorldOrigin changes (disabled during profiling)
            bool regionChanged = prevOriginX != _activeRegion.WorldOriginX ||
                               prevOriginY != _activeRegion.WorldOriginY;

            // Swap buffers: front becomes the published data, back is next to build
            var temp = _frontSlot;
            _frontSlot = _backSlot;
            _backSlot = temp;
        }

        /// <summary>
        /// Classify all tiles in the active region to back slot.
        /// </summary>
        private void ClassifyRegionToSlot(LightingV3FrameSlot slot, int tileSize)
        {
            var startTime = System.Diagnostics.Stopwatch.StartNew();

            var classifier = new SceneWorldClassifier(_geometryProvider);

            int leftTile = (int)System.Math.Floor(_activeRegion.WorldOriginX / tileSize);
            int topTile = (int)System.Math.Floor(_activeRegion.WorldOriginY / tileSize);

            classifier.ClassifyRegion(leftTile, topTile, _activeRegion.RegionWidthTiles, _activeRegion.RegionHeightTiles,
                                     slot.TileClassifications);

            startTime.Stop();
            ClassificationTimeMs = startTime.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Build opacity buffers from classifications and provider data to back slot.
        /// </summary>
        private void BuildOpacityFieldsToSlot(LightingV3FrameSlot slot, int tileSize)
        {
            var startTime = System.Diagnostics.Stopwatch.StartNew();

            // Create temporary occluder field for this build
            var tempField = new OccluderField();
            tempField.Initialize(_activeRegion.RegionWidthSamples, _activeRegion.RegionHeightSamples);

            // Apply all providers to temp field
            foreach (var provider in _occluderProviders)
            {
                provider.ApplyOcclusion(tempField, _activeRegion, tileSize);
            }

            // Copy opacities to slot buffers
            for (int i = 0; i < _activeRegion.RegionWidthSamples * _activeRegion.RegionHeightSamples; i++)
            {
                slot.SunOpacityBuffer[i] = tempField.GetSunOpacity(i);
                slot.LocalOpacityBuffer[i] = tempField.GetLocalLightOpacity(i);
            }

            startTime.Stop();
            OccluderBuildTimeMs = startTime.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Build sun visibility field using DDA ray marching (Phase 3.2A).
        /// Computes visibility for each sample: 0.0 = blocked, 1.0 = free.
        /// Maintains mathematically consistent metrics with strict invariants.
        /// </summary>
        private void BuildSunVisibilityFieldToSlot(LightingV3FrameSlot slot, int tileSize)
        {
            var startTime = System.Diagnostics.Stopwatch.StartNew();

            // Get sun state
            var sunState = slot.SunState;
            int totalSamples = _activeRegion.RegionWidthSamples * _activeRegion.RegionHeightSamples;
            SunVisibilitySampleCount = totalSamples;

            // Pre-trace skip checks (no DDA)
            if (!sunState.IsAboveHorizon)
            {
                SunVisibilityBelowHorizonSkips = totalSamples;
                SunVisibilityPreTraceSkips = totalSamples;
                startTime.Stop();
                SunVisibilityBuildTimeMs = startTime.Elapsed.TotalMilliseconds;
                return;
            }

            if (sunState.Intensity <= 0.001f)
            {
                SunVisibilityLowIntensitySkips = totalSamples;
                SunVisibilityPreTraceSkips = totalSamples;
                startTime.Stop();
                SunVisibilityBuildTimeMs = startTime.Elapsed.TotalMilliseconds;
                return;
            }

            // Calculate sample grid parameters
            float sampleSpacingX = _activeRegion.RegionWidthTiles > 0
                ? (float)(_activeRegion.RegionWidthTiles * tileSize) / _activeRegion.RegionWidthSamples
                : 1f;
            float sampleSpacingY = _activeRegion.RegionHeightTiles > 0
                ? (float)(_activeRegion.RegionHeightTiles * tileSize) / _activeRegion.RegionHeightSamples
                : 1f;
            float sampleCenterOffsetX = sampleSpacingX / 2f;
            float sampleCenterOffsetY = sampleSpacingY / 2f;

            // Compute visibility for each sample with comprehensive metrics
            for (int i = 0; i < totalSamples; i++)
            {
                // Convert linear index to 2D coordinates
                int localSampleY = i / _activeRegion.RegionWidthSamples;
                int localSampleX = i % _activeRegion.RegionWidthSamples;

                // Calculate world position at sample center
                float worldX = _activeRegion.WorldOriginX + localSampleX * sampleSpacingX + sampleCenterOffsetX;
                float worldY = _activeRegion.WorldOriginY + localSampleY * sampleSpacingY + sampleCenterOffsetY;

                // Compute visibility using ray marcher with stats
                var stats = default(SunVisibilityRayStats);
                float visibility = SunVisibilityRayMarcher.ComputeVisibility(
                    worldX, worldY,
                    sunState.DirectionToSun,
                    sunState.Intensity,
                    sunState.IsAboveHorizon,
                    _geometryProvider,
                    _geometryProvider.WorldWidthTiles,
                    _geometryProvider.WorldHeightTiles,
                    tileSize,
                    ref stats);

                slot.SunVisibilityBuffer[i] = visibility;

                // Aggregate metrics per ray
                if (stats.WasSkipped)
                {
                    SunVisibilityPreTraceSkips++;
                    if (stats.SkipReason == SunVisibilitySkipReason.LowElevation)
                        SunVisibilityLowElevationSkips++;
                }
                else
                {
                    // Ray entered DDA or was immediately blocked
                    SunVisibilityTraceCandidates++;

                    if (stats.CellsVisited == 0)
                    {
                        // Starting cell was solid
                        SunVisibilityStartingCellBlocks++;
                        SunVisibilityFullyBlockedRays++;
                    }
                    else
                    {
                        // DDA was executed
                        SunVisibilityDdaRaysStarted++;
                        SunVisibilityCellsVisited += stats.CellsVisited;

                        if (stats.EarlyOut)
                            SunVisibilityEarlyOuts++;

                        if (stats.GuardLimitHit)
                        {
                            SunVisibilityGuardLimitHits++;
                            SunVisibilityFullyBlockedRays++;
                        }
                        else
                        {
                            // Classify by visibility: fully free, partially transmitted, or fully blocked
                            const float VisibilityEpsilon = 0.001f;
                            const float FullyFreeThreshold = 1.0f - VisibilityEpsilon;  // >= 0.999

                            if (visibility >= FullyFreeThreshold)
                                SunVisibilityFullyFreeRays++;
                            else if (visibility <= VisibilityEpsilon)  // Early-out threshold
                                SunVisibilityFullyBlockedRays++;
                            else
                                SunVisibilityPartiallyTransmittedRays++;
                        }

                        if (stats.CellsVisited > SunVisibilityMaximumCellsPerRay)
                            SunVisibilityMaximumCellsPerRay = stats.CellsVisited;
                    }
                }
            }

            // Validate invariants (debug only)
            if (LightingV3Diagnostics.EnablePhase31RuntimeValidation)
            {
                bool invariant1 = (SunVisibilityPreTraceSkips ==
                    SunVisibilityBelowHorizonSkips + SunVisibilityLowIntensitySkips + SunVisibilityLowElevationSkips);
                bool invariant2 = (SunVisibilitySampleCount == SunVisibilityPreTraceSkips + SunVisibilityTraceCandidates);
                bool invariant3 = (SunVisibilityTraceCandidates == SunVisibilityStartingCellBlocks + SunVisibilityDdaRaysStarted);
                bool invariant4 = (SunVisibilityFullyFreeRays + SunVisibilityPartiallyTransmittedRays + SunVisibilityFullyBlockedRays == SunVisibilityTraceCandidates);

                if (!invariant1 || !invariant2 || !invariant3 || !invariant4)
                {
                    System.Console.WriteLine("[Phase3_2A] WARNING: Metric invariant violation detected!");
                    System.Console.WriteLine($"  Invariant 1 (PreTraceSkips): {invariant1}");
                    System.Console.WriteLine($"  Invariant 2 (SampleCount): {invariant2}");
                    System.Console.WriteLine($"  Invariant 3 (TraceCandidates): {invariant3}");
                    System.Console.WriteLine($"  Invariant 4 (FullyFree+Partial+FullyBlocked): {invariant4}");
                }
            }

            startTime.Stop();
            SunVisibilityBuildTimeMs = startTime.Elapsed.TotalMilliseconds;

            // Log metrics if diagnostics enabled
            if (LightingV3Diagnostics.EnablePhase31RuntimeValidation)
            {
                System.Console.WriteLine($"[Phase3_2A] Sun Elevation={sunState.Elevation:F1}deg Intensity={sunState.Intensity:F3}");
                System.Console.WriteLine($"  Samples={SunVisibilitySampleCount} PreSkips={SunVisibilityPreTraceSkips} Candidates={SunVisibilityTraceCandidates}");
                System.Console.WriteLine($"  StartingCellBlocks={SunVisibilityStartingCellBlocks} DdaStarted={SunVisibilityDdaRaysStarted}");
                System.Console.WriteLine($"  CellsVisited={SunVisibilityCellsVisited} Avg={SunVisibilityAverageCellsPerTrace:F2} Max={SunVisibilityMaximumCellsPerRay}");
                System.Console.WriteLine($"  FullyFree={SunVisibilityFullyFreeRays} Partial={SunVisibilityPartiallyTransmittedRays} FullyBlocked={SunVisibilityFullyBlockedRays} EarlyOuts={SunVisibilityEarlyOuts} GuardHits={SunVisibilityGuardLimitHits}");
                System.Console.WriteLine($"  Time={SunVisibilityBuildTimeMs:F2}ms");
            }
        }

        /// <summary>
        /// Audit data for Active Region diagnostics.
        /// </summary>
        public struct ActiveRegionAuditData
        {
            public int RegionWidthTiles;
            public int RegionHeightTiles;
            public int RegionWidthSamples;
            public int RegionHeightSamples;
            public int TotalSamples;
            public float OriginX;
            public float OriginY;
        }

        /// <summary>
        /// Get immutable frame data for current frame (value type returned by value, no allocation).
        /// Renderer MUST capture this ONCE at start of Draw and use exclusively.
        /// </summary>
        public LightingV3FrameData? GetFrameData() => _frontSlot != null ? new LightingV3FrameData(_frontSlot) : null;

        /// <summary>
        /// Get Active Region audit data for diagnostics (TASK 2).
        /// </summary>
        public ActiveRegionAuditData GetActiveRegionAuditData()
        {
            return new ActiveRegionAuditData
            {
                RegionWidthTiles = _activeRegion.RegionWidthTiles,
                RegionHeightTiles = _activeRegion.RegionHeightTiles,
                RegionWidthSamples = _activeRegion.RegionWidthSamples,
                RegionHeightSamples = _activeRegion.RegionHeightSamples,
                TotalSamples = _activeRegion.TotalSamples,
                OriginX = _activeRegion.WorldOriginX,
                OriginY = _activeRegion.WorldOriginY
            };
        }

        /// <summary>
        /// Get the active lighting region (for compatibility only, use GetFrameData).
        /// </summary>
        public ActiveLightingRegion GetActiveRegion() => _activeRegion;

        /// <summary>
        /// Get sampling configuration.
        /// </summary>
        public LightingSamplingConfig GetSamplingConfig() => _samplingConfig;

        /// <summary>
        /// Dump metrics to console (for debugging).
        /// </summary>
        public void DumpMetricsToConsole()
        {
            System.Console.WriteLine($"=== LightingV3Foundation Metrics ===");
            System.Console.WriteLine($"Sampling: {_samplingConfig}");
            System.Console.WriteLine($"Active Region: {_activeRegion}");
            System.Console.WriteLine($"Active Tiles: {ActiveTileCount}");
            System.Console.WriteLine($"Active Samples: {ActiveSampleCount}");
            System.Console.WriteLine($"Classification Time: {ClassificationTimeMs:F3}ms");
            System.Console.WriteLine($"Occluder Build Time: {OccluderBuildTimeMs:F3}ms");
            System.Console.WriteLine($"Sun Visibility Build Time: {SunVisibilityBuildTimeMs:F3}ms");
            System.Console.WriteLine($"Buffer Resize Count: {BufferResizeCount}");
            System.Console.WriteLine($"Frame Update Id: {_updateId}");
            System.Console.WriteLine($"=== Phase 3.2A Metrics (Semantically Consistent) ===");
            System.Console.WriteLine($"Sample Count: {SunVisibilitySampleCount}");
            System.Console.WriteLine($"Pre-Trace Skips: {SunVisibilityPreTraceSkips}");
            System.Console.WriteLine($"  Below Horizon: {SunVisibilityBelowHorizonSkips}");
            System.Console.WriteLine($"  Low Intensity: {SunVisibilityLowIntensitySkips}");
            System.Console.WriteLine($"  Low Elevation: {SunVisibilityLowElevationSkips}");
            System.Console.WriteLine($"Trace Candidates: {SunVisibilityTraceCandidates}");
            System.Console.WriteLine($"  Starting Cell Blocks: {SunVisibilityStartingCellBlocks}");
            System.Console.WriteLine($"  DDA Rays Started: {SunVisibilityDdaRaysStarted}");
            System.Console.WriteLine($"Cells Visited: {SunVisibilityCellsVisited}");
            System.Console.WriteLine($"  Average Per Trace: {SunVisibilityAverageCellsPerTrace:F2}");
            System.Console.WriteLine($"  Average Per DDA Ray: {SunVisibilityAverageDdaCellsPerRay:F2}");
            System.Console.WriteLine($"  Maximum: {SunVisibilityMaximumCellsPerRay}");
            System.Console.WriteLine($"Early Outs: {SunVisibilityEarlyOuts}");
            System.Console.WriteLine($"Ray Classification (Visibility):");
            System.Console.WriteLine($"  Fully Free Rays (≥0.999): {SunVisibilityFullyFreeRays}");
            System.Console.WriteLine($"  Partially Transmitted Rays (0.001-0.999): {SunVisibilityPartiallyTransmittedRays}");
            System.Console.WriteLine($"  Fully Blocked Rays (≤0.001): {SunVisibilityFullyBlockedRays}");
            System.Console.WriteLine($"Guard Limit Hits: {SunVisibilityGuardLimitHits}");
            System.Console.WriteLine($"Active Providers: {_occluderProviders.Count}");
            foreach (var provider in _occluderProviders)
            {
                System.Console.WriteLine($"  - {provider.ProviderName}");
            }
        }

        /// <summary>
        /// Validate that front and back slots have independent buffers.
        /// Prints validation results to console.
        /// </summary>
        public void ValidateSlotIndependence()
        {
            bool tilesIndependent = !ReferenceEquals(_frontSlot.TileClassifications, _backSlot.TileClassifications);
            bool sunIndependent = !ReferenceEquals(_frontSlot.SunOpacityBuffer, _backSlot.SunOpacityBuffer);
            bool localIndependent = !ReferenceEquals(_frontSlot.LocalOpacityBuffer, _backSlot.LocalOpacityBuffer);

            bool allIndependent = tilesIndependent && sunIndependent && localIndependent;

            System.Console.WriteLine($"=== Slot Independence Validation ===");
            System.Console.WriteLine($"TileClassifications: {(tilesIndependent ? "✓ INDEPENDENT" : "✗ SHARED")}");
            System.Console.WriteLine($"SunOpacityBuffer: {(sunIndependent ? "✓ INDEPENDENT" : "✗ SHARED")}");
            System.Console.WriteLine($"LocalOpacityBuffer: {(localIndependent ? "✓ INDEPENDENT" : "✗ SHARED")}");
            System.Console.WriteLine($"Result: {(allIndependent ? "✓ ALL INDEPENDENT" : "✗ VALIDATION FAILED")}");
            System.Console.WriteLine($"=====================================");
        }

        /// <summary>
        /// Dispose foundation resources (if any).
        /// Safe to call multiple times.
        /// </summary>
        public void Dispose()
        {
            // Currently no unmanaged resources, but available for future extensions
            // (CUDA buffers, native memory pools, etc)
        }
    }
}
