using System;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Measures steady-state allocations in SunVisibility ray marching.
    ///
    /// Procedure:
    /// 1. Warm up Foundation for 120+ updates (buffers stabilized)
    /// 2. Capture baseline allocation
    /// 3. Run 300 updates with SunVisibility active
    /// 4. Capture final allocation
    /// 5. Calculate bytes per update
    ///
    /// Conditions:
    /// - No debug visual
    /// - No per-frame logging
    /// - No resize during window
    /// - No include initialization
    /// </summary>
    public class AllocationMeasurementHarness
    {
        private const int WarmupUpdates = 120;
        private const int MeasurementUpdates = 300;

        private enum Phase
        {
            WarmingUp,
            Measuring,
            Complete
        }

        private Phase currentPhase = Phase.WarmingUp;
        private int updateCounter = 0;
        private long baselineBytes = 0;
        private long finalBytes = 0;

        public bool IsRunning => currentPhase != Phase.Complete;
        public int UpdatesPerformed { get; private set; }

        public void Start()
        {
            currentPhase = Phase.WarmingUp;
            updateCounter = 0;
            UpdatesPerformed = 0;
            baselineBytes = 0;
            finalBytes = 0;

            System.Console.WriteLine("[AllocationHarness] Starting measurement...");
            System.Console.WriteLine($"[AllocationHarness] Warmup: {WarmupUpdates} updates");
            System.Console.WriteLine($"[AllocationHarness] Measurement: {MeasurementUpdates} updates");
        }

        public void RecordUpdate()
        {
            if (!IsRunning)
                return;

            updateCounter++;
            UpdatesPerformed++;

            switch (currentPhase)
            {
                case Phase.WarmingUp:
                    if (updateCounter >= WarmupUpdates)
                    {
                        // Transition to measurement phase
                        currentPhase = Phase.Measuring;
                        updateCounter = 0;
                        baselineBytes = GC.GetAllocatedBytesForCurrentThread();
                        System.Console.WriteLine($"[AllocationHarness] Warmup complete. Baseline: {baselineBytes} bytes");
                        System.Console.WriteLine("[AllocationHarness] Starting measurement phase...");
                    }
                    break;

                case Phase.Measuring:
                    if (updateCounter >= MeasurementUpdates)
                    {
                        // Complete measurement
                        finalBytes = GC.GetAllocatedBytesForCurrentThread();
                        currentPhase = Phase.Complete;
                        PrintResults();
                    }
                    break;
            }
        }

        private void PrintResults()
        {
            long totalAllocated = finalBytes - baselineBytes;
            double allocPerUpdate = (double)totalAllocated / MeasurementUpdates;

            System.Console.WriteLine("\n" + new string('=', 60));
            System.Console.WriteLine("[AllocationHarness] RESULTS");
            System.Console.WriteLine(new string('=', 60));
            System.Console.WriteLine($"Baseline:           {baselineBytes} bytes");
            System.Console.WriteLine($"Final:              {finalBytes} bytes");
            System.Console.WriteLine($"Total Allocated:    {totalAllocated} bytes");
            System.Console.WriteLine($"Updates Measured:   {MeasurementUpdates}");
            System.Console.WriteLine($"Per Update:         {allocPerUpdate:F2} bytes/update");
            System.Console.WriteLine();

            if (allocPerUpdate <= 0.1)
                System.Console.WriteLine("✓ PASS: Zero allocations (within tolerance)");
            else
                System.Console.WriteLine($"✗ FAIL: {allocPerUpdate:F2} bytes/update (expected 0)");

            System.Console.WriteLine(new string('=', 60) + "\n");
        }
    }
}
