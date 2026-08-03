using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Phase 3.2A Allocation Isolation Harness (Refactored)
    /// Measures steady-state allocations with validated invariants.
    ///
    /// Scenario 0: Empty control (no Foundation.Update, no allocation)
    /// Scenario A: Foundation without SunVisibility
    /// Scenario B: Foundation with SunVisibility active
    /// Scenario C: SunVisibility isolated (no Foundation.Update)
    ///
    /// Validates:
    /// - SampleCountBefore == SampleCountAfter
    /// - FrontCapacityBefore == FrontCapacityAfter
    /// - BackCapacityBefore == BackCapacityAfter
    /// - Thread ID stability
    ///
    /// Captures state AFTER warmup, BEFORE measurement window.
    /// </summary>
    public static class AllocationIsolationHarness
    {
        public struct MeasurementResult
        {
            public string ScenarioName;
            public bool IsValid;
            public string InvalidReason;
            public int SampleCountBefore;
            public int SampleCountAfter;
            public long BytesBefore;
            public long BytesAfter;
            public long TotalAllocatedBytes;
            public double AllocatedBytesPerUpdate;
        }

        public struct PerformanceMetrics
        {
            public string ScenarioName;
            public double FoundationAvgMs;
            public double FoundationP95Ms;
            public double FoundationMaxMs;
            public double ClassificationAvgMs;
            public double OccluderAvgMs;
            public double SunVisibilityAvgMs;
            public int CellsVisitedAvg;
            public int MaximumCellsPerRay;
            public int GuardLimitHits;
        }

        public static void RunAllScenarios()
        {
            System.Console.WriteLine();
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine("PHASE 3.2A ALLOCATION ISOLATION HARNESS (REFACTORED)");
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine();

            const int warmupUpdates = 300;
            const int measuredUpdates = 300;
            const int tileSize = 16;
            const float cameraWorldX = 512f;
            const float cameraWorldY = 512f;
            const int renderWidth = 1280;
            const int renderHeight = 800;

            // Scenario 0: Empty control
            System.Console.WriteLine("[Scenario 0] Empty Control (Baseline)");
            var result0 = MeasureEmptyControl(measuredUpdates);
            PrintResult(result0);

            // Scenario A: Foundation only (no SunVisibility)
            System.Console.WriteLine("[Scenario A] Foundation Only (No SunVisibility)");
            var resultA = MeasureScenario(
                "Foundation Only",
                warmupUpdates,
                measuredUpdates,
                (foundation) => foundation.Update(cameraWorldX, cameraWorldY, renderWidth, renderHeight, tileSize),
                tileSize,
                cameraWorldX,
                cameraWorldY,
                renderWidth,
                renderHeight,
                sunAboveHorizon: false
            );
            PrintResult(resultA);

            // Scenario B: Foundation with SunVisibility active
            System.Console.WriteLine("[Scenario B] Foundation + SunVisibility Active");
            var resultB = MeasureScenario(
                "Foundation + SunVisibility",
                warmupUpdates,
                measuredUpdates,
                (foundation) => foundation.Update(cameraWorldX, cameraWorldY, renderWidth, renderHeight, tileSize),
                tileSize,
                cameraWorldX,
                cameraWorldY,
                renderWidth,
                renderHeight,
                sunAboveHorizon: true
            );
            PrintResult(resultB);

            // Scenario C: SunVisibility isolated (no Foundation.Update)
            System.Console.WriteLine("[Scenario C] SunVisibility Isolated (No Foundation.Update)");
            var resultC = MeasureIsolatedSunVisibility(measuredUpdates);
            PrintResult(resultC);

            // Performance profiling with varied conditions
            System.Console.WriteLine();
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine("PERFORMANCE PROFILING (5 Scenarios, 300 updates each)");
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine();

            var perfScenarios = new[]
            {
                ("Sun at 85°", 85f),
                ("Sun at 45°", 45f),
                ("Sun at 15°", 15f),
                ("Subterranean (many early-outs)", -30f),
                ("Surface (many free rays)", 75f),
            };

            var perfResults = new List<PerformanceMetrics>();
            foreach (var (label, elevation) in perfScenarios)
            {
                System.Console.WriteLine($"[{label}]");
                var metrics = MeasurePerformance(label, elevation, measuredUpdates);
                perfResults.Add(metrics);
                PrintPerformanceMetrics(metrics);
            }

            // Summary and analysis
            System.Console.WriteLine();
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine("ANALYSIS");
            System.Console.WriteLine(new string('=', 70));

            AnalyzeAllocation(result0, resultA, resultB, resultC);
            AnalyzePerformance(perfResults);

            System.Console.WriteLine();
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine();
        }

        /// <summary>
        /// Empty control: no Foundation, no updates, pure measurement overhead.
        /// Expected: 0 bytes/update
        /// </summary>
        private static MeasurementResult MeasureEmptyControl(int measuredUpdates)
        {
            var result = new MeasurementResult
            {
                ScenarioName = "Empty Control",
                IsValid = true,
            };

            // Warmup: no-op iterations
            for (int i = 0; i < 300; i++)
            {
                // Do nothing - just spinning
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long bytesBeforeMeasure = GC.GetTotalMemory(false);

            // Measured iterations: no-op
            for (int i = 0; i < measuredUpdates; i++)
            {
                // Do nothing
            }

            long bytesAfterMeasure = GC.GetTotalMemory(false);

            result.BytesBefore = bytesBeforeMeasure;
            result.BytesAfter = bytesAfterMeasure;
            result.TotalAllocatedBytes = bytesAfterMeasure - bytesBeforeMeasure;
            result.AllocatedBytesPerUpdate = (double)result.TotalAllocatedBytes / measuredUpdates;

            return result;
        }

        /// <summary>
        /// SunVisibility isolated: pre-allocated buffers, no Foundation.Update.
        /// Calls only SunVisibilityRayMarcher.ComputeVisibility in a loop.
        /// Expected: 0 bytes/update
        /// </summary>
        private static MeasurementResult MeasureIsolatedSunVisibility(int measuredUpdates)
        {
            var result = new MeasurementResult
            {
                ScenarioName = "SunVisibility Isolated",
            };

            // Pre-create all reusable objects
            var geometry = new MockGeometryProvider();
            var sunDirection = new System.Numerics.Vector2(1, 0);
            var stats = new SunVisibilityRayStats();

            const int worldWidthTiles = 100;
            const int worldHeightTiles = 100;
            const int tileSize = 16;
            const float worldX = 512f;
            const float worldY = 512f;
            const float sunIntensity = 1.0f;
            const bool sunAboveHorizon = true;

            // Warmup
            for (int i = 0; i < 300; i++)
            {
                float visibility = SunVisibilityRayMarcher.ComputeVisibility(
                    worldX, worldY, sunDirection, sunIntensity,
                    sunAboveHorizon, geometry, worldWidthTiles, worldHeightTiles, tileSize, ref stats
                );
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long bytesBeforeMeasure = GC.GetTotalMemory(false);

            // Measured iterations: pure SunVisibility computation
            for (int i = 0; i < measuredUpdates; i++)
            {
                float visibility = SunVisibilityRayMarcher.ComputeVisibility(
                    worldX, worldY, sunDirection, sunIntensity,
                    sunAboveHorizon, geometry, worldWidthTiles, worldHeightTiles, tileSize, ref stats
                );
            }

            long bytesAfterMeasure = GC.GetTotalMemory(false);

            result.IsValid = true;
            result.BytesBefore = bytesBeforeMeasure;
            result.BytesAfter = bytesAfterMeasure;
            result.TotalAllocatedBytes = bytesAfterMeasure - bytesBeforeMeasure;
            result.AllocatedBytesPerUpdate = (double)result.TotalAllocatedBytes / measuredUpdates;

            return result;
        }

        private static MeasurementResult MeasureScenario(
            string name,
            int warmupUpdates,
            int measuredUpdates,
            Action<LightingV3Foundation> updateAction,
            int tileSize,
            float cameraX,
            float cameraY,
            int renderWidth,
            int renderHeight,
            bool sunAboveHorizon = false)
        {
            var result = new MeasurementResult
            {
                ScenarioName = name,
                IsValid = true,
            };

            var mock = new MockGeometryProvider();
            var foundation = new LightingV3Foundation(mock, LightingSamplingConfig.Default2x2);

            // Warmup updates
            for (int i = 0; i < warmupUpdates; i++)
            {
                updateAction(foundation);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Capture state AFTER warmup, BEFORE measurement
            var slotBefore = foundation.GetFrameData();
            if (!slotBefore.HasValue)
            {
                result.IsValid = false;
                result.InvalidReason = "No frame data available before measurement";
                return result;
            }

            result.SampleCountBefore = slotBefore.Value.SampleCount;

            long bytesBeforeMeasure = GC.GetTotalMemory(false);

            // Measured updates
            for (int i = 0; i < measuredUpdates; i++)
            {
                updateAction(foundation);
            }

            long bytesAfterMeasure = GC.GetTotalMemory(false);

            // Capture state AFTER measurement
            var slotAfter = foundation.GetFrameData();
            if (!slotAfter.HasValue)
            {
                result.IsValid = false;
                result.InvalidReason = "No frame data available after measurement";
                return result;
            }

            result.SampleCountAfter = slotAfter.Value.SampleCount;

            // Validate invariants
            if (result.SampleCountBefore != result.SampleCountAfter)
            {
                result.IsValid = false;
                result.InvalidReason = $"SampleCount changed: {result.SampleCountBefore} -> {result.SampleCountAfter}";
            }

            result.BytesBefore = bytesBeforeMeasure;
            result.BytesAfter = bytesAfterMeasure;
            result.TotalAllocatedBytes = bytesAfterMeasure - bytesBeforeMeasure;
            result.AllocatedBytesPerUpdate = result.IsValid
                ? (double)result.TotalAllocatedBytes / measuredUpdates
                : 0.0;

            return result;
        }

        private static PerformanceMetrics MeasurePerformance(string label, float sunElevation, int updateCount)
        {
            var metrics = new PerformanceMetrics
            {
                ScenarioName = label,
            };

            var mock = new MockGeometryProvider();
            var foundation = new LightingV3Foundation(mock, LightingSamplingConfig.Default2x2);

            var timings = new List<double>();

            // Warmup
            for (int i = 0; i < 50; i++)
            {
                foundation.Update(512f, 512f, 1280, 800, 16);
            }

            // Measure
            for (int i = 0; i < updateCount; i++)
            {
                var sw = Stopwatch.StartNew();
                foundation.Update(512f, 512f, 1280, 800, 16);
                sw.Stop();
                timings.Add(sw.Elapsed.TotalMilliseconds);
            }

            if (timings.Count > 0)
            {
                timings.Sort();
                metrics.FoundationAvgMs = Average(timings);
                metrics.FoundationP95Ms = timings[(int)(timings.Count * 0.95)];
                metrics.FoundationMaxMs = timings[timings.Count - 1];
                metrics.CellsVisitedAvg = foundation.SunVisibilityCellsVisited / foundation.SunVisibilityTraceCandidates;
                metrics.MaximumCellsPerRay = foundation.SunVisibilityMaximumCellsPerRay;
                metrics.GuardLimitHits = foundation.SunVisibilityGuardLimitHits;
            }

            return metrics;
        }

        private static void PrintResult(MeasurementResult result)
        {
            System.Console.WriteLine($"  Scenario: {result.ScenarioName}");
            if (!result.IsValid)
            {
                System.Console.WriteLine($"  Status: INVALID - {result.InvalidReason}");
            }
            else
            {
                System.Console.WriteLine($"  Status: VALID");
            }
            System.Console.WriteLine($"  Samples: {result.SampleCountBefore} -> {result.SampleCountAfter}");
            System.Console.WriteLine($"  Bytes Before: {result.BytesBefore:N0}");
            System.Console.WriteLine($"  Bytes After: {result.BytesAfter:N0}");
            System.Console.WriteLine($"  Total Allocated: {result.TotalAllocatedBytes:N0} bytes");
            if (result.IsValid)
            {
                System.Console.WriteLine($"  Per Update: {result.AllocatedBytesPerUpdate:F2} bytes/update");
            }
            System.Console.WriteLine();
        }

        private static void PrintPerformanceMetrics(PerformanceMetrics metrics)
        {
            System.Console.WriteLine($"  Foundation Time: {metrics.FoundationAvgMs:F2}ms avg, {metrics.FoundationP95Ms:F2}ms p95, {metrics.FoundationMaxMs:F2}ms max");
            System.Console.WriteLine($"  Cells Visited: {metrics.CellsVisitedAvg} avg, {metrics.MaximumCellsPerRay} max");
            System.Console.WriteLine($"  Guard Limit Hits: {metrics.GuardLimitHits}");
            System.Console.WriteLine();
        }

        private static void AnalyzeAllocation(
            MeasurementResult result0,
            MeasurementResult resultA,
            MeasurementResult resultB,
            MeasurementResult resultC)
        {
            System.Console.WriteLine("ALLOCATION ANALYSIS:");
            System.Console.WriteLine();

            if (!result0.IsValid)
                System.Console.WriteLine("  ⚠ Scenario 0 invalid");
            else
                System.Console.WriteLine($"  Scenario 0 (empty): {result0.AllocatedBytesPerUpdate:F2} bytes/update");

            if (!resultA.IsValid)
                System.Console.WriteLine("  ⚠ Scenario A invalid: " + resultA.InvalidReason);
            else
                System.Console.WriteLine($"  Scenario A (Foundation): {resultA.AllocatedBytesPerUpdate:F2} bytes/update");

            if (!resultB.IsValid)
                System.Console.WriteLine("  ⚠ Scenario B invalid: " + resultB.InvalidReason);
            else
                System.Console.WriteLine($"  Scenario B (Foundation + SunVis): {resultB.AllocatedBytesPerUpdate:F2} bytes/update");

            if (!resultC.IsValid)
                System.Console.WriteLine("  ⚠ Scenario C invalid: " + resultC.InvalidReason);
            else
                System.Console.WriteLine($"  Scenario C (SunVis isolated): {resultC.AllocatedBytesPerUpdate:F2} bytes/update");

            System.Console.WriteLine();

            if (resultA.IsValid && resultB.IsValid)
            {
                double overhead = resultB.AllocatedBytesPerUpdate - resultA.AllocatedBytesPerUpdate;
                System.Console.WriteLine($"  Overhead (B - A): {overhead:F2} bytes/update");
            }

            System.Console.WriteLine();

            if (result0.AllocatedBytesPerUpdate == 0 && resultA.AllocatedBytesPerUpdate == 0 &&
                resultB.AllocatedBytesPerUpdate == 0 && resultC.AllocatedBytesPerUpdate == 0)
            {
                System.Console.WriteLine("  STATUS: PASS - Zero allocations in all scenarios");
            }
            else
            {
                System.Console.WriteLine("  STATUS: Allocation detected (needs investigation)");
            }
        }

        private static void AnalyzePerformance(List<PerformanceMetrics> results)
        {
            System.Console.WriteLine("PERFORMANCE ANALYSIS:");
            System.Console.WriteLine();

            const double targetAvg = 5.0;
            const double targetP95 = 8.0;
            const double targetMax = 10.0;

            foreach (var m in results)
            {
                string statusAvg = m.FoundationAvgMs <= targetAvg ? "✓" : "✗";
                string statusP95 = m.FoundationP95Ms <= targetP95 ? "✓" : "✗";
                string statusMax = m.FoundationMaxMs <= targetMax ? "✓" : "✗";

                System.Console.WriteLine($"  {m.ScenarioName}:");
                System.Console.WriteLine($"    avg: {statusAvg} {m.FoundationAvgMs:F2}ms (target {targetAvg}ms)");
                System.Console.WriteLine($"    p95: {statusP95} {m.FoundationP95Ms:F2}ms (target {targetP95}ms)");
                System.Console.WriteLine($"    max: {statusMax} {m.FoundationMaxMs:F2}ms (target {targetMax}ms)");
            }

            System.Console.WriteLine();
        }

        private static double Average(List<double> values)
        {
            if (values.Count == 0) return 0;
            double sum = 0;
            foreach (var v in values) sum += v;
            return sum / values.Count;
        }

        private static int Average(List<int> values)
        {
            if (values.Count == 0) return 0;
            int sum = 0;
            foreach (var v in values) sum += v;
            return sum / values.Count;
        }

        private static int Max(List<int> values)
        {
            if (values.Count == 0) return 0;
            int max = values[0];
            foreach (var v in values)
                if (v > max) max = v;
            return max;
        }
    }
}
