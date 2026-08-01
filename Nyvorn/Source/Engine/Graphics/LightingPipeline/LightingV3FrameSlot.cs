using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Immutable snapshot of active region (copied by value, not reference).
    /// </summary>
    public readonly struct ActiveRegionSnapshot
    {
        public readonly int WorldOriginX;
        public readonly int WorldOriginY;
        public readonly int TileWidth;
        public readonly int TileHeight;
        public readonly int SampleWidth;
        public readonly int SampleHeight;
        public readonly int TileSize;

        public ActiveRegionSnapshot(ActiveLightingRegion region, int tileSize)
        {
            WorldOriginX = (int)region.WorldOriginX;
            WorldOriginY = (int)region.WorldOriginY;
            TileWidth = region.RegionWidthTiles;
            TileHeight = region.RegionHeightTiles;
            SampleWidth = region.RegionWidthSamples;
            SampleHeight = region.RegionHeightSamples;
            TileSize = tileSize;
        }
    }

    /// <summary>
    /// Single frame slot for double-buffered foundation data.
    /// Each slot owns its buffers to ensure true immutability.
    /// </summary>
    public class LightingV3FrameSlot
    {
        public int FrameId { get; set; }
        public ActiveRegionSnapshot Region { get; set; }

        // Independent buffers owned by this slot
        public LightingCellClassification[] TileClassifications { get; set; }
        public float[] SunOpacityBuffer { get; set; }
        public float[] LocalOpacityBuffer { get; set; }
        public float[] SunVisibilityBuffer { get; set; }  // Phase 3.2A: Sun visibility field (0.0 = blocked, 1.0 = free)

        // Directional sun state (Phase 3)
        public LightingV3SunState SunState { get; set; }

        // Probe for validation
        public int ProbeLocalSampleX { get; set; }
        public int ProbeLocalSampleY { get; set; }
        public float ProbeSampleWorldX { get; set; }
        public float ProbeSampleWorldY { get; set; }
        public float ProbeSunOpacity { get; set; }
        public float ProbeLocalOpacity { get; set; }

        public LightingV3FrameSlot(int initialCapacityTiles, int initialCapacitySamples)
        {
            FrameId = 0;
            TileClassifications = new LightingCellClassification[initialCapacityTiles];
            SunOpacityBuffer = new float[initialCapacitySamples];
            LocalOpacityBuffer = new float[initialCapacitySamples];
            SunVisibilityBuffer = new float[initialCapacitySamples];
        }

        public void EnsureCapacity(int tileCount, int sampleCount)
        {
            if (TileClassifications.Length < tileCount)
            {
                var newArray = new LightingCellClassification[Math.Max(256, tileCount * 2)];
                Array.Copy(TileClassifications, newArray, Math.Min(TileClassifications.Length, newArray.Length));
                TileClassifications = newArray;
            }
            if (SunOpacityBuffer.Length < sampleCount)
            {
                var newArray = new float[Math.Max(256, sampleCount * 2)];
                Array.Copy(SunOpacityBuffer, newArray, Math.Min(SunOpacityBuffer.Length, newArray.Length));
                SunOpacityBuffer = newArray;
            }
            if (LocalOpacityBuffer.Length < sampleCount)
            {
                var newArray = new float[Math.Max(256, sampleCount * 2)];
                Array.Copy(LocalOpacityBuffer, newArray, Math.Min(LocalOpacityBuffer.Length, newArray.Length));
                LocalOpacityBuffer = newArray;
            }
            if (SunVisibilityBuffer.Length < sampleCount)
            {
                var newArray = new float[Math.Max(256, sampleCount * 2)];
                Array.Copy(SunVisibilityBuffer, newArray, Math.Min(SunVisibilityBuffer.Length, newArray.Length));
                SunVisibilityBuffer = newArray;
            }
        }

        public void ClearBuffers(int tileCount, int sampleCount)
        {
            Array.Clear(TileClassifications, 0, tileCount);
            Array.Clear(SunOpacityBuffer, 0, sampleCount);
            Array.Clear(LocalOpacityBuffer, 0, sampleCount);
            // NOTE: SunVisibilityBuffer is NOT cleared here because every sample is written in BuildSunVisibilityFieldToSlot.
            // Clearing the entire buffer would waste CPU when all sampleCount values are overwritten.
            // The unused capacity (beyond sampleCount) is not published to renderer.
        }
    }

    /// <summary>
    /// Public immutable view of frame data for renderer consumption (value type).
    /// Returned by value - no heap allocation beyond initial assignment.
    /// Renderer receives this and uses it exclusively during Draw.
    ///
    /// SEMÂNTICA READ-ONLY REAL:
    /// - TileClassifications, SunOpacityBuffer, LocalOpacityBuffer: expostos como array (legacy)
    /// - SunVisibility (Phase 3.2A): ReadOnlySpan (não pode ser modificado)
    /// - SampleCount define a janela válida para leitura.
    /// - Dados fora dessa janela não participam do frame publicado.
    /// - Nenhuma cópia é feita por frame.
    /// - Renderer NÃO pode acessar arrays internos diretamente para SunVisibility.
    /// </summary>
    public readonly struct LightingV3FrameData
    {
        private readonly float[] _sunVisibilityBuffer;
        public readonly int UpdateId;
        public readonly ActiveRegionSnapshot Region;
        public readonly LightingCellClassification[] TileClassifications;
        public readonly float[] SunOpacityBuffer;
        public readonly float[] LocalOpacityBuffer;

        // Directional sun state (Phase 3)
        public readonly LightingV3SunState SunState;

        // Probe values
        public readonly int ProbeLocalSampleX;
        public readonly int ProbeLocalSampleY;
        public readonly float ProbeSampleWorldX;
        public readonly float ProbeSampleWorldY;
        public readonly float ProbeSunOpacity;
        public readonly float ProbeLocalOpacity;

        // Sample count for read-only window
        public readonly int SampleCount;

        public LightingV3FrameData(LightingV3FrameSlot slot)
        {
            UpdateId = slot.FrameId;
            Region = slot.Region;
            TileClassifications = slot.TileClassifications;
            SunOpacityBuffer = slot.SunOpacityBuffer;
            LocalOpacityBuffer = slot.LocalOpacityBuffer;
            _sunVisibilityBuffer = slot.SunVisibilityBuffer;
            SunState = slot.SunState;
            ProbeLocalSampleX = slot.ProbeLocalSampleX;
            ProbeLocalSampleY = slot.ProbeLocalSampleY;
            ProbeSampleWorldX = slot.ProbeSampleWorldX;
            ProbeSampleWorldY = slot.ProbeSampleWorldY;
            ProbeSunOpacity = slot.ProbeSunOpacity;
            ProbeLocalOpacity = slot.ProbeLocalOpacity;
            SampleCount = Region.SampleWidth * Region.SampleHeight;
        }

        /// <summary>
        /// Read-only view of sun visibility buffer (Phase 3.2A).
        /// Returns only the valid sample window [0, SampleCount).
        /// No copy, no allocation per frame.
        /// </summary>
        public ReadOnlySpan<float> SunVisibility
        {
            get => _sunVisibilityBuffer.AsSpan(0, SampleCount);
        }
    }
}
