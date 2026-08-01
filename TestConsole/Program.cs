using System;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;

namespace TestConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Phase 3.2A Complete Test Suite Executor");
            Console.WriteLine("=======================================\n");

            try
            {
                // Execute all 28 tests + allocation harness
                Phase3_2ATestRunner.RunAllTests();
                Console.WriteLine("\n✓ Test suite completed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ Test suite failed with exception:");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
