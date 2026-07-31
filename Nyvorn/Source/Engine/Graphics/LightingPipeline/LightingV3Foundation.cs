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

        private ActiveLightingRegion _activeRegion;
        private LightingCellClassification[] _tileClassifications;
        private OccluderField _occluderField;

        // Metrics
        public int ActiveTileCount { get; private set; }
        public int ActiveSampleCount { get; private set; }
        public long ClassificationTimeMs { get; private set; }
        public long OccluderBuildTimeMs { get; private set; }
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
        {
            _geometryProvider = geometryProvider ?? throw new ArgumentNullException(nameof(geometryProvider));
            _samplingConfig = samplingConfig ?? throw new ArgumentNullException(nameof(samplingConfig));
            _occluderProviders = new List<IOccluderProvider>();

            _activeRegion = new ActiveLightingRegion(_samplingConfig);
            _occluderField = new OccluderField();
            _tileClassifications = new LightingCellClassification[256]; // Initial capacity

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
            // Update active region
            _activeRegion.Update(cameraWorldX, cameraWorldY, logicalRenderWidth, logicalRenderHeight, tileSize);

            ActiveTileCount = _activeRegion.RegionWidthTiles * _activeRegion.RegionHeightTiles;
            ActiveSampleCount = _activeRegion.TotalSamples;

            // Ensure classification buffer capacity
            if (_tileClassifications.Length < ActiveTileCount)
            {
                System.Array.Resize(ref _tileClassifications, System.Math.Max(256, ActiveTileCount * 2));
            }

            // Classify tiles in region
            ClassifyRegion(tileSize);

            // Build occluder field
            BuildOccluderField();
        }

        /// <summary>
        /// Classify all tiles in the active region.
        /// </summary>
        private void ClassifyRegion(int tileSize)
        {
            var startTime = System.Diagnostics.Stopwatch.StartNew();

            var classifier = new SceneWorldClassifier(_geometryProvider);

            int leftTile = (int)System.Math.Floor(_activeRegion.WorldOriginX / tileSize);
            int topTile = (int)System.Math.Floor(_activeRegion.WorldOriginY / tileSize);

            classifier.ClassifyRegion(leftTile, topTile, _activeRegion.RegionWidthTiles, _activeRegion.RegionHeightTiles,
                                     _tileClassifications);

            startTime.Stop();
            ClassificationTimeMs = startTime.ElapsedMilliseconds;
        }

        /// <summary>
        /// Build occluder field from classifications and provider data.
        /// </summary>
        private void BuildOccluderField()
        {
            var startTime = System.Diagnostics.Stopwatch.StartNew();

            int prevCapacity = _occluderField.SampleCount;

            _occluderField.Initialize(_activeRegion.RegionWidthSamples, _activeRegion.RegionHeightSamples);

            if (_occluderField.SampleCount > prevCapacity)
                BufferResizeCount++;

            // Apply all providers
            int tileSize = 16; // TODO: Get from world/config
            foreach (var provider in _occluderProviders)
            {
                provider.ApplyOcclusion(_occluderField, _activeRegion, tileSize);
            }

            startTime.Stop();
            OccluderBuildTimeMs = startTime.ElapsedMilliseconds;
        }

        /// <summary>
        /// Get the active lighting region.
        /// </summary>
        public ActiveLightingRegion GetActiveRegion() => _activeRegion;

        /// <summary>
        /// Get the occluder field (sun and local light opacity).
        /// </summary>
        public OccluderField GetOccluderField() => _occluderField;

        /// <summary>
        /// Get tile classifications (for debugging).
        /// </summary>
        public LightingCellClassification[] GetTileClassifications() => _tileClassifications;

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
            System.Console.WriteLine($"Classification Time: {ClassificationTimeMs}ms");
            System.Console.WriteLine($"Occluder Build Time: {OccluderBuildTimeMs}ms");
            System.Console.WriteLine($"Buffer Resize Count: {BufferResizeCount}");
            System.Console.WriteLine($"Occluder Field: {_occluderField}");
            System.Console.WriteLine($"Active Providers: {_occluderProviders.Count}");
            foreach (var provider in _occluderProviders)
            {
                System.Console.WriteLine($"  - {provider.ProviderName}");
            }
        }
    }
}
