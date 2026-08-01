using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Zero-allocation metrics aggregator for Phase 3.2A SunVisibility.
    /// Uses ring buffer for p95 calculation without allocating per update.
    /// </summary>
    public class SunVisibilityMetricsAggregator
    {
        private const int MaxSamples = 300;  // Ring buffer size for p95 calculation
        private readonly float[] cellsPerRayBuffer = new float[MaxSamples];
        private readonly float[] foundationTimeBuffer = new float[MaxSamples];
        private readonly float[] sunVisibilityTimeBuffer = new float[MaxSamples];

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

            // Add to ring buffer for p95
            if (samplesCollected < MaxSamples)
                samplesCollected++;

            foundationTimeBuffer[ringBufferIndex] = (float)foundationTimeMs;
            sunVisibilityTimeBuffer[ringBufferIndex] = (float)sunVisibilityTimeMs;
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

                // Store for p95 calculation
                if (samplesCollected < MaxSamples)
                    samplesCollected++;

                cellsPerRayBuffer[ringBufferIndex] = cellsVisited;

                if (wasFree) FreeRays++;
                if (wasBlocked) BlockedRays++;
                if (guardHit) GuardLimitHits++;

                // Count early outs (if cellsVisited > 0 but blocked, likely early exit)
                if (wasBlocked && cellsVisited > 0) EarlyOuts++;
            }
        }

        public float GetP95(float[] buffer)
        {
            if (samplesCollected < 3)
                return 0f;

            int p95Index = (int)Math.Ceiling(samplesCollected * 0.95f);
            Array.Sort(buffer, 0, samplesCollected);
            return buffer[Math.Min(p95Index, samplesCollected - 1)];
        }

        public float GetMax(float[] buffer)
        {
            float max = 0f;
            for (int i = 0; i < samplesCollected; i++)
            {
                if (buffer[i] > max) max = buffer[i];
            }
            return max;
        }

        public float FoundationTimeP95Ms => GetP95(foundationTimeBuffer);
        public float FoundationTimeMaxMs => GetMax(foundationTimeBuffer);
        public float SunVisibilityTimeP95Ms => GetP95(sunVisibilityTimeBuffer);
        public float SunVisibilityTimeMaxMs => GetMax(sunVisibilityTimeBuffer);
        public float CellsPerRayP95 => GetP95(cellsPerRayBuffer);
        public float CellsPerRayMax => GetMax(cellsPerRayBuffer);
    }
}
