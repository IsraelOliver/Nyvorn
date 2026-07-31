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
        }

        public void ClearBuffers(int tileCount, int sampleCount)
        {
            Array.Clear(TileClassifications, 0, tileCount);
            Array.Clear(SunOpacityBuffer, 0, sampleCount);
            Array.Clear(LocalOpacityBuffer, 0, sampleCount);
        }
    }

    /// <summary>
    /// Public immutable view of frame data for renderer consumption (value type).
    /// Returned by value - no heap allocation beyond initial assignment.
    /// Renderer receives this and uses it exclusively during Draw.
    /// </summary>
    public readonly struct LightingV3FrameData
    {
        public readonly int UpdateId;
        public readonly ActiveRegionSnapshot Region;
        public readonly LightingCellClassification[] TileClassifications;
        public readonly float[] SunOpacityBuffer;
        public readonly float[] LocalOpacityBuffer;

        // Probe values
        public readonly int ProbeLocalSampleX;
        public readonly int ProbeLocalSampleY;
        public readonly float ProbeSampleWorldX;
        public readonly float ProbeSampleWorldY;
        public readonly float ProbeSunOpacity;
        public readonly float ProbeLocalOpacity;

        public LightingV3FrameData(LightingV3FrameSlot slot)
        {
            UpdateId = slot.FrameId;
            Region = slot.Region;
            TileClassifications = slot.TileClassifications;
            SunOpacityBuffer = slot.SunOpacityBuffer;
            LocalOpacityBuffer = slot.LocalOpacityBuffer;
            ProbeLocalSampleX = slot.ProbeLocalSampleX;
            ProbeLocalSampleY = slot.ProbeLocalSampleY;
            ProbeSampleWorldX = slot.ProbeSampleWorldX;
            ProbeSampleWorldY = slot.ProbeSampleWorldY;
            ProbeSunOpacity = slot.ProbeSunOpacity;
            ProbeLocalOpacity = slot.ProbeLocalOpacity;
        }
    }
}
