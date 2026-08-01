using System;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;

namespace TestConsole
{
    public static class AllocProfiling
    {
        public static void ProfileAllocation()
        {
            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("ALLOCATION PROFILING");
            Console.WriteLine(new string('=', 70) + "\n");

            // Test 1: Just Foundation creation
            Console.WriteLine("Test 1: Foundation Creation Only");
            var before1 = GC.GetAllocatedBytesForCurrentThread();
            var mock = new MockGeometryProvider();
            var foundation = new LightingV3Foundation(mock, LightingSamplingConfig.Default2x2);
            var after1 = GC.GetAllocatedBytesForCurrentThread();
            Console.WriteLine($"  Allocation: {after1 - before1} bytes\n");

            // Test 2: Single Update
            Console.WriteLine("Test 2: Single Foundation Update");
            const int tileSize = 16;
            const float cameraWorldX = 512f;
            const float cameraWorldY = 512f;
            const int renderWidth = 1280;
            const int renderHeight = 800;

            before1 = GC.GetAllocatedBytesForCurrentThread();
            foundation.Update(cameraWorldX, cameraWorldY, renderWidth, renderHeight, tileSize);
            after1 = GC.GetAllocatedBytesForCurrentThread();
            Console.WriteLine($"  Allocation: {after1 - before1} bytes\n");

            // Test 3: Repeated updates
            Console.WriteLine("Test 3: 10 Foundation Updates");
            before1 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++)
            {
                foundation.Update(cameraWorldX, cameraWorldY, renderWidth, renderHeight, tileSize);
            }
            after1 = GC.GetAllocatedBytesForCurrentThread();
            Console.WriteLine($"  Total Allocation: {after1 - before1} bytes");
            Console.WriteLine($"  Per Update: {(after1 - before1) / 10.0:F2} bytes\n");
        }
    }
}
