using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Stores occlusion data at sub-tile sampling resolution.
    /// Each sample contains opacity values for different light types.
    ///
    /// Opacity: 0 = fully transparent (no blocking), 1 = fully opaque (complete blocking).
    ///
    /// Fields stored per sample:
    /// - SunOpacity: How much this sample blocks sunlight (directional)
    /// - LocalLightOpacity: How much this sample blocks local light sources (omnidirectional)
    /// - MaterialId: (optional) Material category for future extensions
    ///
    /// Rules (Phase 2):
    /// - SolidForeground: SunOpacity=1, LocalLightOpacity=1 (blocks everything)
    /// - BackgroundWall: SunOpacity=0, LocalLightOpacity=0 (blocks nothing)
    /// - OpenAtmosphere: SunOpacity=0, LocalLightOpacity=0 (blocks nothing)
    /// - Trees, structures, dynamic objects: Provided by IOccluderProvider
    /// </summary>
    public class OccluderField
    {
        // Reusable buffers
        private float[] _sunOpacityBuffer;
        private float[] _localLightOpacityBuffer;
        private byte[] _materialIdBuffer;

        /// <summary>
        /// Total number of samples in this field.
        /// </summary>
        public int SampleCount { get; private set; }

        /// <summary>
        /// Width in samples.
        /// </summary>
        public int WidthSamples { get; private set; }

        /// <summary>
        /// Height in samples.
        /// </summary>
        public int HeightSamples { get; private set; }

        public OccluderField()
        {
            SampleCount = 0;
            WidthSamples = 0;
            HeightSamples = 0;
            _sunOpacityBuffer = new float[256]; // Initial capacity
            _localLightOpacityBuffer = new float[256];
            _materialIdBuffer = new byte[256];
        }

        /// <summary>
        /// Resize buffers to hold the specified sample count.
        /// Grows if needed, never shrinks.
        /// </summary>
        public void EnsureCapacity(int requiredSampleCount)
        {
            if (_sunOpacityBuffer.Length >= requiredSampleCount)
                return;

            // Grow to next power of 2
            int newCapacity = 256;
            while (newCapacity < requiredSampleCount)
                newCapacity *= 2;

            System.Array.Resize(ref _sunOpacityBuffer, newCapacity);
            System.Array.Resize(ref _localLightOpacityBuffer, newCapacity);
            System.Array.Resize(ref _materialIdBuffer, newCapacity);
        }

        /// <summary>
        /// Initialize field for a region of given dimensions.
        /// </summary>
        public void Initialize(int widthSamples, int heightSamples)
        {
            WidthSamples = widthSamples;
            HeightSamples = heightSamples;
            SampleCount = widthSamples * heightSamples;

            EnsureCapacity(SampleCount);

            // Clear all buffers
            System.Array.Clear(_sunOpacityBuffer, 0, SampleCount);
            System.Array.Clear(_localLightOpacityBuffer, 0, SampleCount);
            System.Array.Clear(_materialIdBuffer, 0, SampleCount);
        }

        /// <summary>
        /// Set opacities for a single sample.
        /// </summary>
        public void SetSample(int flatIndex, float sunOpacity, float localLightOpacity, byte materialId = 0)
        {
            if (flatIndex < 0 || flatIndex >= SampleCount)
                throw new ArgumentOutOfRangeException(nameof(flatIndex));

            _sunOpacityBuffer[flatIndex] = System.Math.Clamp(sunOpacity, 0f, 1f);
            _localLightOpacityBuffer[flatIndex] = System.Math.Clamp(localLightOpacity, 0f, 1f);
            _materialIdBuffer[flatIndex] = materialId;
        }

        /// <summary>
        /// Get sun opacity for a sample.
        /// </summary>
        public float GetSunOpacity(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= SampleCount)
                return 0f;
            return _sunOpacityBuffer[flatIndex];
        }

        /// <summary>
        /// Get local light opacity for a sample.
        /// </summary>
        public float GetLocalLightOpacity(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= SampleCount)
                return 0f;
            return _localLightOpacityBuffer[flatIndex];
        }

        /// <summary>
        /// Get material ID for a sample.
        /// </summary>
        public byte GetMaterialId(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= SampleCount)
                return 0;
            return _materialIdBuffer[flatIndex];
        }

        /// <summary>
        /// Apply classification-based occlusion to a region.
        /// </summary>
        public void ApplyClassifications(LightingCellClassification[] classifications, int widthTiles,
                                        int heightTiles, int samplesPerAxis)
        {
            int index = 0;
            for (int ty = 0; ty < heightTiles; ty++)
            {
                for (int tx = 0; tx < widthTiles; tx++)
                {
                    var classification = classifications[index];

                    float sunOpacity, localLightOpacity;

                    if (classification == LightingCellClassification.SolidForeground)
                    {
                        // Solid foreground blocks everything
                        sunOpacity = 1f;
                        localLightOpacity = 1f;
                    }
                    else if (classification == LightingCellClassification.VisibleBackground)
                    {
                        // Background wall blocks nothing
                        sunOpacity = 0f;
                        localLightOpacity = 0f;
                    }
                    else // OpenAtmosphere
                    {
                        // Open atmosphere blocks nothing
                        sunOpacity = 0f;
                        localLightOpacity = 0f;
                    }

                    // Apply to all samples in this tile
                    int baseSampleX = tx * samplesPerAxis;
                    int baseSampleY = ty * samplesPerAxis;

                    for (int sy = 0; sy < samplesPerAxis; sy++)
                    {
                        for (int sx = 0; sx < samplesPerAxis; sx++)
                        {
                            int sampleX = baseSampleX + sx;
                            int sampleY = baseSampleY + sy;
                            int flatIndex = sampleY * WidthSamples + sampleX;

                            SetSample(flatIndex, sunOpacity, localLightOpacity);
                        }
                    }

                    index++;
                }
            }
        }

        /// <summary>
        /// Get flat index from 2D sample coordinates.
        /// </summary>
        public int SampleToFlatIndex(int sampleX, int sampleY)
        {
            if (sampleX < 0 || sampleX >= WidthSamples || sampleY < 0 || sampleY >= HeightSamples)
                return -1;

            return sampleY * WidthSamples + sampleX;
        }

        /// <summary>
        /// Get 2D coordinates from flat index.
        /// </summary>
        public (int X, int Y) FlatIndexToSample(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= SampleCount)
                return (-1, -1);

            int sampleY = flatIndex / WidthSamples;
            int sampleX = flatIndex % WidthSamples;

            return (sampleX, sampleY);
        }

        /// <summary>
        /// Get a read-only view of sun opacity buffer (for debugging).
        /// </summary>
        public float[] GetSunOpacityBuffer() => _sunOpacityBuffer;

        /// <summary>
        /// Get a read-only view of local light opacity buffer (for debugging).
        /// </summary>
        public float[] GetLocalLightOpacityBuffer() => _localLightOpacityBuffer;

        public override string ToString()
        {
            return $"OccluderField({WidthSamples}x{HeightSamples}={SampleCount} samples, capacity={_sunOpacityBuffer.Length})";
        }
    }
}
