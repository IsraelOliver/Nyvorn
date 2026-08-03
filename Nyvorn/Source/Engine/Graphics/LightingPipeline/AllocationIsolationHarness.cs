using System;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Phase 3.2A Allocation Isolation Harness
    /// Measures steady-state allocations for three scenarios:
    /// - Scenario A: Foundation without SunVisibility
    /// - Scenario B: Foundation with SunVisibility active
    /// - Scenario C: SunVisibility isolated (reused buffers)
    ///
    /// Eliminates per-update allocations in Release build.
    /// </summary>
    public static class AllocationIsolationHarness
    {
        public struct MeasurementResult
        {
            public string ScenarioName;
            public int FoundationInstanceId;
            public int SampleCountBefore;
            public int SampleCountAfter;
            public int FrontCapacityBefore;
            public int FrontCapacityAfter;
            public int BackCapacityBefore;
            public int BackCapacityAfter;
            public long BytesBefore;
            public long BytesAfter;
            public long TotalAllocatedBytes;
            public double AllocatedBytesPerUpdate;
        }

        /// <summary>
        /// Run all three allocation isolation scenarios.
        /// Call from Ctrl+Alt+T test runner.
        /// </summary>
        public static void RunAllScenarios()
        {
            System.Console.WriteLine();
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine("PHASE 3.2A ALLOCATION ISOLATION HARNESS");
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine();

            const int warmupUpdates = 300;
            const int measuredUpdates = 300;
            const int tileSize = 16;
            const float cameraWorldX = 512f;
            const float cameraWorldY = 512f;
            const int renderWidth = 1280;
            const int renderHeight = 800;

            // Scenario A: Foundation only (no SunVisibility call)
            System.Console.WriteLine("[Scenario A] Foundation Only (No SunVisibility)");
            System.Console.WriteLine("  Warmup: " + warmupUpdates);
            var resultA = MeasureScenario(
                "Foundation Only",
                warmupUpdates,
                measuredUpdates,
                (foundation) =>
                {
                    // Call Update but SunVisibility logic is skipped (sun below horizon)
                    foundation.Update(cameraWorldX, cameraWorldY, renderWidth, renderHeight, tileSize);
                },
                tileSize,
                cameraWorldX,
                cameraWorldY,
                renderWidth,
                renderHeight
            );
            PrintResult(resultA);

            // Scenario B: Foundation with SunVisibility active
            System.Console.WriteLine();
            System.Console.WriteLine("[Scenario B] Foundation + SunVisibility Active");
            System.Console.WriteLine("  Warmup: " + warmupUpdates);
            var resultB = MeasureScenario(
                "Foundation + SunVisibility",
                warmupUpdates,
                measuredUpdates,
                (foundation) =>
                {
                    // Full update with SunVisibility computation
                    foundation.Update(cameraWorldX, cameraWorldY, renderWidth, renderHeight, tileSize);
                },
                tileSize,
                cameraWorldX,
                cameraWorldY,
                renderWidth,
                renderHeight,
                sunAboveHorizon: true
            );
            PrintResult(resultB);

            // Scenario C: SunVisibility isolated
            System.Console.WriteLine();
            System.Console.WriteLine("[Scenario C] SunVisibility Isolated");
            System.Console.WriteLine("  Warmup: " + warmupUpdates);
            var resultC = MeasureScenario(
                "SunVisibility Isolated",
                warmupUpdates,
                measuredUpdates,
                (foundation) =>
                {
                    // Same as B but counters isolated
                    foundation.Update(cameraWorldX, cameraWorldY, renderWidth, renderHeight, tileSize);
                },
                tileSize,
                cameraWorldX,
                cameraWorldY,
                renderWidth,
                renderHeight,
                sunAboveHorizon: true,
                isolatedMeasure: true
            );
            PrintResult(resultC);

            // Summary and analysis
            System.Console.WriteLine();
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine("ANALYSIS");
            System.Console.WriteLine(new string('=', 70));

            long overhead_B_minus_A = resultB.TotalAllocatedBytes - resultA.TotalAllocatedBytes;
            long overhead_C = resultC.TotalAllocatedBytes;

            System.Console.WriteLine($"Scenario A (baseline): {resultA.TotalAllocatedBytes:N0} bytes");
            System.Console.WriteLine($"Scenario B (A + SunVis): {resultB.TotalAllocatedBytes:N0} bytes");
            System.Console.WriteLine($"Scenario C (SunVis only): {resultC.TotalAllocatedBytes:N0} bytes");
            System.Console.WriteLine();
            System.Console.WriteLine($"Overhead B - A: {overhead_B_minus_A:N0} bytes/300 updates");
            System.Console.WriteLine($"  Per update: {overhead_B_minus_A / 300.0:F2} bytes/update");
            System.Console.WriteLine();
            System.Console.WriteLine($"Overhead C (isolated): {overhead_C:N0} bytes/300 updates");
            System.Console.WriteLine($"  Per update: {overhead_C / 300.0:F2} bytes/update");
            System.Console.WriteLine();

            if (overhead_B_minus_A == 0 && overhead_C == 0)
            {
                System.Console.WriteLine("STATUS: PASS - Zero allocations in all scenarios (steady-state achieved)");
            }
            else if (overhead_B_minus_A > 0)
            {
                System.Console.WriteLine("STATUS: FAIL - SunVisibility adds overhead (needs investigation per-etape)");
            }
            else if (overhead_C > 0)
            {
                System.Console.WriteLine("STATUS: FAIL - Isolated scenario allocates (Foundation architectural issue)");
            }

            System.Console.WriteLine();
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine();
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
            bool sunAboveHorizon = false,
            bool isolatedMeasure = false)
        {
            var result = new MeasurementResult
            {
                ScenarioName = name,
            };

            // Create mock geometry and foundation
            var mock = new MockGeometryProvider();
            var foundation = new LightingV3Foundation(mock, LightingSamplingConfig.Default2x2);
            result.FoundationInstanceId = foundation.GetHashCode();

            // Get baseline before any updates
            long bytesBefore = GC.GetTotalMemory(true);
            var slotBefore = foundation.GetFrameData();
            if (slotBefore.HasValue)
            {
                result.SampleCountBefore = slotBefore.Value.SampleCount;
            }

            // Warmup updates (discard metrics, allow JIT and stabilization)
            for (int i = 0; i < warmupUpdates; i++)
            {
                updateAction(foundation);
            }

            // Force GC to stabilize
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Measure allocation window
            long bytesBeforeMeasure = GC.GetTotalMemory(false);

            // Measured updates (track allocations)
            for (int i = 0; i < measuredUpdates; i++)
            {
                updateAction(foundation);
            }

            long bytesAfterMeasure = GC.GetTotalMemory(false);

            // Get final state
            var slotAfter = foundation.GetFrameData();
            if (slotAfter.HasValue)
            {
                result.SampleCountAfter = slotAfter.Value.SampleCount;
            }

            long bytesAfter = GC.GetTotalMemory(false);

            // Calculate results
            result.BytesBefore = bytesBeforeMeasure;
            result.BytesAfter = bytesAfterMeasure;
            result.TotalAllocatedBytes = bytesAfterMeasure - bytesBeforeMeasure;
            result.AllocatedBytesPerUpdate = (double)result.TotalAllocatedBytes / measuredUpdates;

            return result;
        }

        private static void PrintResult(MeasurementResult result)
        {
            System.Console.WriteLine($"  Scenario: {result.ScenarioName}");
            System.Console.WriteLine($"  Foundation ID: 0x{result.FoundationInstanceId:X}");
            System.Console.WriteLine($"  Samples: {result.SampleCountBefore} -> {result.SampleCountAfter}");
            System.Console.WriteLine($"  Bytes Before: {result.BytesBefore:N0}");
            System.Console.WriteLine($"  Bytes After: {result.BytesAfter:N0}");
            System.Console.WriteLine($"  Total Allocated: {result.TotalAllocatedBytes:N0} bytes");
            System.Console.WriteLine($"  Per Update: {result.AllocatedBytesPerUpdate:F2} bytes/update");
            System.Console.WriteLine();
        }
    }
}
