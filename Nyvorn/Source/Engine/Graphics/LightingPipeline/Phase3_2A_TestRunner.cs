using System;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Phase 3.2A Complete Test Runner
    /// Executes all 28 tests + allocation measurement harness
    /// </summary>
    public static class Phase3_2ATestRunner
    {
        public static void RunAllTests()
        {
            System.Console.WriteLine("\n");
            System.Console.WriteLine(new string('╔', 70));
            System.Console.WriteLine("║ PHASE 3.2A - COMPLETE TEST SUITE RUNNER");
            System.Console.WriteLine(new string('╚', 70));
            System.Console.WriteLine();

            var stopwatch = Stopwatch.StartNew();

            // Step 1: Run 28 nominal tests
            System.Console.WriteLine("[STEP 1/2] Running 28 Nominal Tests...");
            System.Console.WriteLine(new string('-', 70));
            Phase3_2ACompleteTests.RunAll();

            // Step 2: Prepare and run allocation harness
            System.Console.WriteLine("[STEP 2/2] Running Allocation Measurement Harness...");
            System.Console.WriteLine(new string('-', 70));
            RunAllocationHarness();

            stopwatch.Stop();
            System.Console.WriteLine($"\n✓ Complete suite finished in {stopwatch.ElapsedMilliseconds}ms");
        }

        private static void RunAllocationHarness()
        {
            var harness = new AllocationMeasurementHarness();
            harness.Start();

            // Create a mock foundation for measurement
            var mock = new MockGeometryProvider();
            var foundation = new LightingV3Foundation(mock, LightingSamplingConfig.Default2x2);

            const int tileSize = 16;
            const float cameraWorldX = 512f;
            const float cameraWorldY = 512f;
            const int renderWidth = 1280;
            const int renderHeight = 800;

            // Run harness updates (120 warmup + 300 measured)
            for (int i = 0; i < 420; i++)
            {
                foundation.Update(cameraWorldX, cameraWorldY, renderWidth, renderHeight, tileSize);
                harness.RecordUpdate();
            }

            System.Console.WriteLine("\n✓ Allocation measurement complete");
        }
    }
}
