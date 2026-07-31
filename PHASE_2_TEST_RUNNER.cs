// PHASE 2 DETERMINISTIC TESTS
// Run via: dotnet run --project Nyvorn -- --test-phase2

using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using System;

public class Phase2TestRunner
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test-phase2")
        {
            Console.WriteLine("\n========== PHASE 2 DETERMINISTIC TESTS ==========\n");
            Phase2Tests.RunAll();
            Console.WriteLine("\n========== TEST RUN COMPLETE ==========\n");
            Environment.Exit(0);
        }
    }
}
