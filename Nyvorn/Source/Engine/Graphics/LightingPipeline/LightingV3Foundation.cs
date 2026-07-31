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

        // Double-buffered frame slots (true immutability via independent buffers)
        private LightingV3FrameSlot _backSlot;  // Being built during Update
        private LightingV3FrameSlot _frontSlot; // Consumed by renderer during Draw
        private int _updateId;

        // Metrics
        public int ActiveTileCount { get; private set; }
        public int ActiveSampleCount { get; private set; }
        public double ClassificationTimeMs { get; private set; }
        public double OccluderBuildTimeMs { get; private set; }
        public int BufferResizeCount { get; private set; }

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
        /// Register an additional occluder provider (trees, structures, etc).
        /// </summary>
        public void RegisterOccluderProvider(IOccluderProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            _occluderProviders.Add(provider);
        }

        /// <summary>
        /// Update foundation for current camera position and render size.
        /// Called once per frame before rendering.
        /// </summary>
        public void Update(float cameraWorldX, float cameraWorldY, int logicalRenderWidth, int logicalRenderHeight, int tileSize)
        {
            // Capture previous origin for change detection
            float prevOriginX = _activeRegion.WorldOriginX;
            float prevOriginY = _activeRegion.WorldOriginY;

            // Update active region
            _activeRegion.Update(cameraWorldX, cameraWorldY, logicalRenderWidth, logicalRenderHeight, tileSize);

            ActiveTileCount = _activeRegion.RegionWidthTiles * _activeRegion.RegionHeightTiles;
            ActiveSampleCount = _activeRegion.TotalSamples;

            // Ensure back slot capacity
            _backSlot.EnsureCapacity(ActiveTileCount, ActiveSampleCount);
            _backSlot.ClearBuffers(ActiveTileCount, ActiveSampleCount);

            // Classify tiles in back slot
            ClassifyRegionToSlot(_backSlot, tileSize);

            // Build opacities in back slot
            BuildOpacityFieldsToSlot(_backSlot, tileSize);

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

            // Log probe when WorldOrigin changes
            bool regionChanged = prevOriginX != _activeRegion.WorldOriginX ||
                               prevOriginY != _activeRegion.WorldOriginY;
            if (regionChanged)
            {
                System.Console.WriteLine($"[V3FoundationPublish] UpdateId={_updateId} WorldOrigin=({_activeRegion.WorldOriginX},{_activeRegion.WorldOriginY}) " +
                    $"ProbeIndex={probeIndex} ProbeWorld=({probeWorldX:F1},{probeWorldY:F1}) " +
                    $"Sun={probeSunOpacity:F3} Local={probeLocalOpacity:F3}");
            }

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
        /// Get immutable frame data for current frame (value type returned by value, no allocation).
        /// Renderer MUST capture this ONCE at start of Draw and use exclusively.
        /// </summary>
        public LightingV3FrameData? GetFrameData() => _frontSlot != null ? new LightingV3FrameData(_frontSlot) : null;

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
            System.Console.WriteLine($"Buffer Resize Count: {BufferResizeCount}");
            System.Console.WriteLine($"Frame Update Id: {_updateId}");
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
