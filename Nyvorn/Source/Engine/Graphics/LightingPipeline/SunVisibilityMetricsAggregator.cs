using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Zero-allocation metrics aggregator for Phase 3.2A SunVisibility.
    /// Ring buffers (circular) + scratch buffers (sort only) - no corruption.
    /// P95 calculated only when requested, not every update.
    /// </summary>
    public class SunVisibilityMetricsAggregator
    {
        private const int MaxSamples = 300;

        // Ring buffers: preserve circular data
        private readonly float[] cellsPerRayRing = new float[MaxSamples];
        private readonly float[] foundationTimeRing = new float[MaxSamples];
        private readonly float[] sunVisibilityTimeRing = new float[MaxSamples];

        // Scratch buffers: used ONLY for sorting (p95 calculation)
        private readonly float[] cellsPerRayScratch = new float[MaxSamples];
        private readonly float[] foundationTimeScratch = new float[MaxSamples];
        private readonly float[] sunVisibilityTimeScratch = new float[MaxSamples];

        private int ringBufferIndex = 0;
        private int samplesCollected = 0;

        // Accumulators (reset per window)
        public int UpdateCount { get; private set; }
        public int RaysComputed { get; private set; }
        public int RaysSkipped { get; private set; }
        public int CellsTraversed { get; private set; }
        public int EarlyOuts { get; private set; }
        public int FreeRays { get; private set; }
        public int BlockedRays { get; private set; }
        public int BelowHorizonSkips { get; private set; }
        public int LowElevationSkips { get; private set; }
        public int GuardLimitHits { get; private set; }

        // Derived
        public float AverageCellsPerRay => RaysComputed > 0 ? (float)CellsTraversed / RaysComputed : 0f;
        public float AverageFoundationTimeMs => UpdateCount > 0 ? (float)AccumulatedFoundationTimeMs / UpdateCount : 0f;
        public float AverageSunVisibilityTimeMs => UpdateCount > 0 ? (float)AccumulatedSunVisibilityTimeMs / UpdateCount : 0f;
        public double AccumulatedFoundationTimeMs { get; private set; }
        public double AccumulatedSunVisibilityTimeMs { get; private set; }
        public int ActiveSampleCount { get; set; }
        public float SunElevationDegrees { get; set; }

        public void Reset()
        {
            UpdateCount = 0;
            RaysComputed = 0;
            RaysSkipped = 0;
            CellsTraversed = 0;
            EarlyOuts = 0;
            FreeRays = 0;
            BlockedRays = 0;
            BelowHorizonSkips = 0;
            LowElevationSkips = 0;
            GuardLimitHits = 0;
            AccumulatedFoundationTimeMs = 0;
            AccumulatedSunVisibilityTimeMs = 0;
            ringBufferIndex = 0;
            samplesCollected = 0;
        }

        public void RecordUpdate(double foundationTimeMs, double sunVisibilityTimeMs)
        {
            UpdateCount++;
            AccumulatedFoundationTimeMs += foundationTimeMs;
            AccumulatedSunVisibilityTimeMs += sunVisibilityTimeMs;

            if (samplesCollected < MaxSamples)
                samplesCollected++;

            foundationTimeRing[ringBufferIndex] = (float)foundationTimeMs;
            sunVisibilityTimeRing[ringBufferIndex] = (float)sunVisibilityTimeMs;
            ringBufferIndex = (ringBufferIndex + 1) % MaxSamples;
        }

        public void RecordRayStats(int cellsVisited, bool wasSkipped, bool wasFree, bool wasBlocked,
            bool belowHorizon, bool lowElevation, bool guardHit)
        {
            if (wasSkipped)
            {
                RaysSkipped++;
                if (belowHorizon) BelowHorizonSkips++;
                if (lowElevation) LowElevationSkips++;
            }
            else
            {
                RaysComputed++;
                CellsTraversed += cellsVisited;

                if (samplesCollected < MaxSamples)
                    samplesCollected++;

                cellsPerRayRing[ringBufferIndex] = cellsVisited;

                if (wasFree) FreeRays++;
                if (wasBlocked) BlockedRays++;
                if (guardHit) GuardLimitHits++;
                if (wasBlocked && cellsVisited > 0) EarlyOuts++;
            }
        }

        private float CalculateP95(float[] ringBuffer)
        {
            if (samplesCollected < 3)
                return 0f;

            // Copy ring buffer to scratch buffer (preserving ring, not sorting it)
            Array.Copy(ringBuffer, 0, foundationTimeScratch, 0, samplesCollected);

            // Sort ONLY scratch buffer
            Array.Sort(foundationTimeScratch, 0, samplesCollected);

            int p95Index = (int)Math.Ceiling(samplesCollected * 0.95f);
            return foundationTimeScratch[Math.Min(p95Index - 1, samplesCollected - 1)];
        }

        private float CalculateMax(float[] ringBuffer)
        {
            float max = 0f;
            for (int i = 0; i < samplesCollected; i++)
            {
                if (ringBuffer[i] > max) max = ringBuffer[i];
            }
            return max;
        }

        // P95 calculated only when requested (not every update)
        public float FoundationTimeP95Ms => CalculateP95(foundationTimeRing);
        public float FoundationTimeMaxMs => CalculateMax(foundationTimeRing);
        public float SunVisibilityTimeP95Ms => CalculateP95(sunVisibilityTimeRing);
        public float SunVisibilityTimeMaxMs => CalculateMax(sunVisibilityTimeRing);
        public float CellsPerRayP95 => CalculateP95(cellsPerRayRing);
        public float CellsPerRayMax => CalculateMax(cellsPerRayRing);
    }
}
