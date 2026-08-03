using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Writes ProfileResult to formatted text files.
    /// Call OFFLINE (after profiler is inactive), not during measurements.
    /// </summary>
    public static class ProfileResultWriter
    {
        public static void WriteToFile(FrameProfiler.ProfileResult result, string outputPath)
        {
            if (!result.IsValid)
            {
                using (var writer = new StreamWriter(outputPath))
                {
                    writer.WriteLine("==========================================================");
                    writer.WriteLine($"MEASUREMENT INVALID: {result.InvalidReason}");
                    writer.WriteLine("==========================================================");
                }
                return;
            }

            var metrics = result.Metrics;
            var calls = result.Calls;

            if (metrics.Length == 0)
            {
                using (var writer = new StreamWriter(outputPath))
                {
                    writer.WriteLine("No frames recorded.");
                }
                return;
            }

            using (var writer = new StreamWriter(outputPath))
            {
                // Header
                writer.WriteLine("==========================================================");
                writer.WriteLine($"BASELINE MEASUREMENT: {result.Mode}");
                writer.WriteLine("==========================================================");
                writer.WriteLine();

                // Configuration
                writer.WriteLine("CONFIGURATION:");
                writer.WriteLine($"  Mode: {result.Mode}");
                writer.WriteLine($"  Resolution: {result.ConfiguredResolutionWidth}x{result.ConfiguredResolutionHeight}");
                writer.WriteLine($"  Camera.Zoom: {result.ConfiguredZoom:F2}");
                writer.WriteLine($"  ActiveRegion: {result.ConfiguredActiveRegionWidth}x{result.ConfiguredActiveRegionHeight}");
                writer.WriteLine($"  SampleCount: {result.ConfiguredSampleCount}");
                writer.WriteLine();

                writer.WriteLine("CAPTURE:");
                writer.WriteLine($"  Frames Measured: {metrics.Length}");
                writer.WriteLine();

                // Timing statistics (all in milliseconds)
                WriteStat(writer, "WallClockFrameIntervalMs", ExtractMetric(metrics, m => m.WallClockFrameIntervalMs));
                WriteStat(writer, "CpuUpdateDrawMs", ExtractMetric(metrics, m => m.CpuUpdateDrawMs));
                WriteStat(writer, "UnaccountedWallTimeMs", ExtractMetric(metrics, m => m.UnaccountedWallTimeMs));
                writer.WriteLine();

                WriteStat(writer, "TotalUpdateMs", ExtractMetric(metrics, m => m.TotalUpdateMs));
                WriteStat(writer, "TotalDrawMs", ExtractMetric(metrics, m => m.TotalDrawMs));
                writer.WriteLine();

                WriteStat(writer, "FoundationUpdateMs", ExtractMetric(metrics, m => m.FoundationUpdateMs));
                WriteStat(writer, "  ClassificationMs", ExtractMetric(metrics, m => m.ClassificationMs));
                WriteStat(writer, "  OccluderBuildMs", ExtractMetric(metrics, m => m.OccluderBuildMs));
                WriteStat(writer, "  SunVisibilityBuildMs", ExtractMetric(metrics, m => m.SunVisibilityBuildMs));
                writer.WriteLine();

                WriteStat(writer, "WorldRenderMs", ExtractMetric(metrics, m => m.WorldRenderMs));
                WriteStat(writer, "DebugRendererMs", ExtractMetric(metrics, m => m.DebugRendererMs));
                WriteStat(writer, "  RenderSunVisibilityMs", ExtractMetric(metrics, m => m.RenderSunVisibilityMs));
                WriteStat(writer, "  DebugCompositeMs", ExtractMetric(metrics, m => m.DebugCompositeMs));
                WriteStat(writer, "HudDrawMs", ExtractMetric(metrics, m => m.HudDrawMs));
                writer.WriteLine();

                WriteStat(writer, "FPS", ExtractMetric(metrics, m => m.FPS));
                writer.WriteLine();

                // Call count statistics
                writer.WriteLine("CALL COUNT STATISTICS (per frame, should be ≤1 typically):");
                writer.WriteLine();

                WriteCallCountStat(writer, "FoundationUpdateCalls", ExtractCallMetric(calls, c => c.FoundationUpdateCalls));
                WriteCallCountStat(writer, "BuildSunVisibilityCalls", ExtractCallMetric(calls, c => c.BuildSunVisibilityCalls));
                WriteCallCountStat(writer, "DebugRendererCalls", ExtractCallMetric(calls, c => c.DebugRendererCalls));
                WriteCallCountStat(writer, "DebugRendererEarlyReturns", ExtractCallMetric(calls, c => c.DebugRendererEarlyReturns));
                WriteCallCountStat(writer, "RenderSunVisibilityCalls", ExtractCallMetric(calls, c => c.RenderSunVisibilityCalls));
                WriteCallCountStat(writer, "DebugCompositeCalls", ExtractCallMetric(calls, c => c.DebugCompositeCalls));
                WriteCallCountStat(writer, "HudDrawCalls", ExtractCallMetric(calls, c => c.HudDrawCalls));
                writer.WriteLine();

                // Anomalies
                writer.WriteLine("ANOMALIES:");
                writer.WriteLine();

                var maxUnaccountedWallTime = metrics.Max(m => m.UnaccountedWallTimeMs);
                if (maxUnaccountedWallTime > 5.0)
                {
                    writer.WriteLine($"⚠ WARNING: UnaccountedWallTime exceeded 5ms (max: {maxUnaccountedWallTime:F2}ms)");
                    writer.WriteLine("  This may indicate GPU waits, VSync stalls, or scheduler delays.");
                }

                var minFps = metrics.Min(m => m.FPS);
                if (minFps < 30.0)
                {
                    writer.WriteLine($"⚠ WARNING: Minimum FPS dropped below 30 ({minFps:F1} FPS)");
                    writer.WriteLine("  Measurement may be unreliable or system overloaded.");
                }

                var maxSunVisibilityBuild = metrics.Max(m => m.SunVisibilityBuildMs);
                if (maxSunVisibilityBuild > 10.0)
                {
                    writer.WriteLine($"⚠ WARNING: SunVisibilityBuildMs exceeded 10ms (max: {maxSunVisibilityBuild:F2}ms)");
                }

                bool duplicateFoundation = calls.Any(c => c.FoundationUpdateCalls > 1);
                bool duplicateSunVisibility = calls.Any(c => c.BuildSunVisibilityCalls > 1);

                if (duplicateFoundation)
                {
                    writer.WriteLine("⚠ WARNING: FoundationUpdateCalls > 1 detected in some frames");
                    writer.WriteLine("  This indicates the Foundation was updated multiple times per frame.");
                }

                if (duplicateSunVisibility)
                {
                    writer.WriteLine("⚠ WARNING: BuildSunVisibilityCalls > 1 detected in some frames");
                    writer.WriteLine("  This indicates SunVisibility was rebuilt multiple times per frame.");
                }

                writer.WriteLine();
                writer.WriteLine("==========================================================");
            }
        }

        private static void WriteStat(StreamWriter writer, string metricName, (double avg, double p95, double max) stats)
        {
            writer.WriteLine($"{metricName}:");
            writer.WriteLine($"  Average: {stats.avg:F3} ms");
            writer.WriteLine($"  P95:     {stats.p95:F3} ms");
            writer.WriteLine($"  Max:     {stats.max:F3} ms");
        }

        private static void WriteCallCountStat(StreamWriter writer, string metricName, (double avg, int max) stats)
        {
            writer.WriteLine($"{metricName}:");
            writer.WriteLine($"  Average: {stats.avg:F3}");
            writer.WriteLine($"  Max:     {stats.max}");
        }

        private static (double avg, double p95, double max) ExtractMetric(
            FrameProfiler.FrameMetrics[] metrics, Func<FrameProfiler.FrameMetrics, double> selector)
        {
            if (metrics.Length == 0)
                return (0.0, 0.0, 0.0);

            var values = metrics.Select(selector).Where(v => v >= 0).OrderBy(v => v).ToArray();
            if (values.Length == 0)
                return (0.0, 0.0, 0.0);

            double avg = values.Average();
            double max = values.Last();

            // P95: 95th percentile
            int p95Index = (int)Math.Ceiling(values.Length * 0.95) - 1;
            double p95 = values[Math.Max(0, p95Index)];

            return (avg, p95, max);
        }

        private static (double avg, int max) ExtractCallMetric(
            FrameProfiler.CallCounts[] calls, Func<FrameProfiler.CallCounts, int> selector)
        {
            if (calls.Length == 0)
                return (0.0, 0);

            var values = calls.Select(selector).ToArray();
            double avg = values.Average();
            int max = values.Max();

            return (avg, max);
        }
    }
}
