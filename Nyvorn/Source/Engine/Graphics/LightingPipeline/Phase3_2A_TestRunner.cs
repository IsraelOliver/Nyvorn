using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Phase 3.2A Complete Test Runner
    /// Static test list with derived counts (executed, passed, failed, skipped).
    /// </summary>
    public static class Phase3_2ATestRunner
    {
        // Static test case registry
        private static readonly List<TestCase> TestCases = new()
        {
            new TestCase("01", "CompletelyFreePath", () => Phase3_2ACompleteTests.Test_CompletelyFreePathPublic()),
            new TestCase("02", "ForegroundBlocks", () => Phase3_2ACompleteTests.Test_ForegroundBlocksPublic()),
            new TestCase("03", "BackgroundWallDoesNotBlock", () => Phase3_2ACompleteTests.Test_BackgroundWallDoesNotBlockPublic()),
            new TestCase("04", "DiagonalRay", () => Phase3_2ACompleteTests.Test_DiagonalRayPublic()),
            new TestCase("05", "SeamLeftToRight", () => Phase3_2ACompleteTests.Test_SeamLeftToRightPublic()),
            new TestCase("06", "SeamRightToLeft", () => Phase3_2ACompleteTests.Test_SeamRightToLeftPublic()),
            new TestCase("07", "SunBelowHorizon", () => Phase3_2ACompleteTests.Test_SunBelowHorizonPublic()),
            new TestCase("08", "StartingCellSolid", () => Phase3_2ACompleteTests.Test_StartingCellSolidPublic()),
            new TestCase("09", "StartingCellPartialOnce", () => Phase3_2ACompleteTests.Test_StartingCellPartialOncePublic()),
            new TestCase("10", "SinglePartialOpacity", () => Phase3_2ACompleteTests.Test_SinglePartialOpacityPublic()),
            new TestCase("11", "TwoPartialOpacities", () => Phase3_2ACompleteTests.Test_TwoPartialOpacitiesPublic()),
            new TestCase("12", "NextCellPartialOnce", () => Phase3_2ACompleteTests.Test_NextCellPartialOncePublic()),
            new TestCase("13", "CanonicalBlockerAfterSeam", () => Phase3_2ACompleteTests.Test_CanonicalBlockerAfterSeamPublic()),
            new TestCase("14", "BlockerOutsideActiveRegion", () => Phase3_2ACompleteTests.Test_BlockerOutsideActiveRegionPublic()),
            new TestCase("15", "NearHorizontalSkipped", () => Phase3_2ACompleteTests.Test_NearHorizontalSkippedPublic()),
            new TestCase("16", "NearMinimumTraceElevation", () => Phase3_2ACompleteTests.Test_NearMinimumTraceElevationPublic()),
            new TestCase("17", "TallWorldTraversal", () => Phase3_2ACompleteTests.Test_TallWorldTraversalPublic()),
            new TestCase("18", "MathematicalGuardIsSufficient", () => Phase3_2ACompleteTests.Test_MathematicalGuardIsSufficientPublic()),
            new TestCase("19", "GuardFailureIsConservative", () => Phase3_2ACompleteTests.Test_GuardFailureIsConservativePublic()),
            new TestCase("20", "CustomSunOpacityProvider", () => Phase3_2ACompleteTests.Test_CustomSunOpacityProviderPublic()),
            new TestCase("21", "SlotBuffersIndependent", () => Phase3_2ACompleteTests.Test_SlotBuffersIndependentPublic()),
            new TestCase("22", "SlotBuffersIndependentAfterResize", () => Phase3_2ACompleteTests.Test_SlotBuffersIndependentAfterResizePublic()),
            new TestCase("23", "CapacityGreaterThanSampleCount", () => Phase3_2ACompleteTests.Test_CapacityGreaterThanSampleCountPublic()),
            new TestCase("24", "ConsumersRespectSampleCount", () => Phase3_2ACompleteTests.Test_ConsumersRespectSampleCountPublic()),
            new TestCase("25", "FrameUpdateIdConsistent", () => Phase3_2ACompleteTests.Test_FrameUpdateIdConsistentPublic()),
            new TestCase("26", "NoNaNOrInfinity", () => Phase3_2ACompleteTests.Test_NoNaNOrInfinityPublic()),
            new TestCase("27", "MetricsCollection", () => Phase3_2ACompleteTests.Test_MetricsCollectionPublic()),
            new TestCase("28", "GuardLimitHitsZeroInValidTests", () => Phase3_2ACompleteTests.Test_GuardLimitHitsZeroInValidTestsPublic()),
        };

        private struct TestCase
        {
            public string Number;
            public string Name;
            public Action Test;

            public TestCase(string number, string name, Action test)
            {
                Number = number;
                Name = name;
                Test = test;
            }
        }

        private static int executed = 0;
        private static int passed = 0;
        private static int failed = 0;
        private static int skipped = 0;

        public static void RunAllTests()
        {
            System.Console.WriteLine();
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine("PHASE 3.2A - COMPLETE TEST SUITE RUNNER");
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine();

            var stopwatch = Stopwatch.StartNew();

            executed = 0;
            passed = 0;
            failed = 0;
            skipped = 0;

            foreach (var testCase in TestCases)
            {
                try
                {
                    testCase.Test();
                    executed++;
                    passed++;
                    System.Console.WriteLine($"{testCase.Number} PASS {testCase.Name}");
                }
                catch (Exception ex)
                {
                    executed++;
                    failed++;
                    System.Console.WriteLine($"{testCase.Number} FAIL {testCase.Name}: {ex.Message}");
                }
            }

            stopwatch.Stop();

            System.Console.WriteLine();
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine($"Executed = {executed}");
            System.Console.WriteLine($"Passed = {passed}");
            System.Console.WriteLine($"Failed = {failed}");
            System.Console.WriteLine($"Skipped = {skipped}");
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine($"Suite finished in {stopwatch.ElapsedMilliseconds}ms");
            System.Console.WriteLine();

            // Run allocation isolation harness
            System.Console.WriteLine();
            AllocationIsolationHarness.RunAllScenarios();
        }
    }
}
