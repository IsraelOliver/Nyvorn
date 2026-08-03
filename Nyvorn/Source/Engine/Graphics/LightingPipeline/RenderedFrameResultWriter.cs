using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Writes RenderedFrameProfiler results to formatted text files.
    /// Call OFFLINE (after profiler is inactive), not during measurements.
    /// Handles rendered frames (not update calls).
    /// </summary>
    public static class RenderedFrameResultWriter
    {
        public static void WriteToFile(RenderedFrameProfiler.ProfileResult result, string outputPath)
        {
            if (!result.IsValid)
            {
                using (var writer = new StreamWriter(outputPath))
                {
                    writer.WriteLine("==========================================================");
                    writer.WriteLine($"MEASUREMENT INVALID");
                    writer.WriteLine("==========================================================");
                    writer.WriteLine();
                    writer.WriteLine($"Invalid Reason: {ReasonToString(result.InvalidReason)}");
                    writer.WriteLine($"Draw Skipped Count: {result.DrawSkippedCount}");
                    writer.WriteLine($"IsRunningSlowly Count: {result.IsRunningSlowlyCount}");
                    writer.WriteLine();
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
                writer.WriteLine($"RENDERED FRAME BASELINE: {result.Mode}");
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
                writer.WriteLine($"  Rendered Frames: {metrics.Length}");
                writer.WriteLine($"  Draw Skipped Count: {result.DrawSkippedCount}");
                writer.WriteLine($"  IsRunningSlowly Count: {result.IsRunningSlowlyCount}");
                writer.WriteLine();

                // Timing statistics
                WriteStat(writer, "WallClockFrameIntervalMs", ExtractMetric(metrics, m => m.WallClockFrameIntervalMs));
                WriteStat(writer, "TotalCpuMs", ExtractMetric(metrics, m => m.TotalCpuMs));
                WriteStat(writer, "UnaccountedWallTimeMs", ExtractMetric(metrics, m => m.UnaccountedWallTimeMs));
                writer.WriteLine();

                WriteStat(writer, "TotalUpdateCpuMs", ExtractMetric(metrics, m => m.TotalUpdateCpuMs));
                WriteStat(writer, "DrawCpuMs", ExtractMetric(metrics, m => m.DrawCpuMs));
                WriteStat(writer, "PresentMs", ExtractMetric(metrics, m => m.PresentMs));
                writer.WriteLine();

                WriteStat(writer, "FPS", ExtractMetric(metrics, m => m.FPS));
                writer.WriteLine();

                // Update call statistics
                writer.WriteLine("UPDATE CALLS STATISTICS (per rendered frame):");
                WriteCallStat(writer, "UpdateCallsSinceLastDraw", ExtractIntMetric(metrics, m => m.UpdateCallsSinceLastDraw));
                writer.WriteLine();

                // SunVisibility call statistics
                writer.WriteLine("SUN VISIBILITY BUILD STATISTICS (per rendered frame):");
                WriteCallStat(writer, "SunVisibilityBuildCallsThisFrame", ExtractIntMetric(metrics, m => m.SunVisibilityBuildCallsThisFrame));
                writer.WriteLine();

                // Anomalies
                writer.WriteLine("ANOMALIES:");
                writer.WriteLine();

                var maxUnaccounted = metrics.Max(m => m.UnaccountedWallTimeMs);
                if (maxUnaccounted > 5.0)
                {
                    writer.WriteLine($"⚠ WARNING: UnaccountedWallTime max exceeded 5ms ({maxUnaccounted:F2}ms)");
                    writer.WriteLine("  This may indicate GPU waits, VSync stalls, or scheduler delays.");
                    writer.WriteLine();
                }

                var minFps = metrics.Min(m => m.FPS);
                if (minFps < 30.0)
                {
                    writer.WriteLine($"⚠ WARNING: Minimum FPS dropped below 30 ({minFps:F1} FPS)");
                    writer.WriteLine("  Measurement may be unreliable or system overloaded.");
                    writer.WriteLine();
                }

                var maxUpdateCalls = metrics.Max(m => m.UpdateCallsSinceLastDraw);
                if (maxUpdateCalls > 2)
                {
                    writer.WriteLine($"⚠ WARNING: Multiple Update calls per rendered frame ({maxUpdateCalls} max)");
                    writer.WriteLine("  System may be running slow or fixed timestep is not synchronized.");
                    writer.WriteLine();
                }

                var maxSunVisibilityCalls = metrics.Max(m => m.SunVisibilityBuildCallsThisFrame);
                if (maxSunVisibilityCalls > 1)
                {
                    writer.WriteLine($"⚠ WARNING: SunVisibility rebuilt multiple times per frame ({maxSunVisibilityCalls} max)");
                    writer.WriteLine("  This indicates potential loop duplication or replay during renders.");
                    writer.WriteLine();
                }

                var runningSlowlyFrames = metrics.Count(m => m.IsRunningSlowly);
                if (runningSlowlyFrames > 0)
                {
                    writer.WriteLine($"⚠ WARNING: IsRunningSlowly=true in {runningSlowlyFrames} frames");
                    writer.WriteLine("  MonoGame executed multiple Updates to catch up with fixed timestep.");
                    writer.WriteLine();
                }

                if (result.DrawSkippedCount > 0)
                {
                    writer.WriteLine($"⚠ CRITICAL: Draw was skipped {result.DrawSkippedCount} times");
                    writer.WriteLine("  This indicates rendering was suppressed or blocked.");
                    writer.WriteLine();
                }

                writer.WriteLine("==========================================================");
            }
        }

        private static void WriteStat(StreamWriter writer, string metricName, (double avg, double p95, double max) stats)
        {
            writer.WriteLine($"{metricName}:");
            writer.WriteLine($"  Average: {stats.avg:F3}");
            writer.WriteLine($"  P95:     {stats.p95:F3}");
            writer.WriteLine($"  Max:     {stats.max:F3}");
        }

        private static void WriteCallStat(StreamWriter writer, string metricName, (double avg, int max) stats)
        {
            writer.WriteLine($"{metricName}:");
            writer.WriteLine($"  Average: {stats.avg:F3}");
            writer.WriteLine($"  Max:     {stats.max}");
        }

        private static (double avg, double p95, double max) ExtractMetric(
            RenderedFrameProfiler.RenderedFrameMetrics[] metrics, Func<RenderedFrameProfiler.RenderedFrameMetrics, double> selector)
        {
            if (metrics.Length == 0)
                return (0.0, 0.0, 0.0);

            var values = metrics.Select(selector).Where(v => v >= 0).OrderBy(v => v).ToArray();
            if (values.Length == 0)
                return (0.0, 0.0, 0.0);

            double avg = values.Average();
            double max = values.Last();

            int p95Index = (int)Math.Ceiling(values.Length * 0.95) - 1;
            double p95 = values[Math.Max(0, p95Index)];

            return (avg, p95, max);
        }

        private static (double avg, int max) ExtractIntMetric(
            RenderedFrameProfiler.RenderedFrameMetrics[] metrics, Func<RenderedFrameProfiler.RenderedFrameMetrics, int> selector)
        {
            if (metrics.Length == 0)
                return (0.0, 0);

            var values = metrics.Select(selector).ToArray();
            double avg = values.Average();
            int max = values.Max();

            return (avg, max);
        }

        private static string ReasonToString(RenderedFrameProfiler.InvalidReasonEnum reason)
        {
            return reason switch
            {
                RenderedFrameProfiler.InvalidReasonEnum.None => "None",
                RenderedFrameProfiler.InvalidReasonEnum.ModeChanged => "Lighting mode changed during measurement",
                RenderedFrameProfiler.InvalidReasonEnum.ResolutionChanged => "Resolution changed during measurement",
                RenderedFrameProfiler.InvalidReasonEnum.ZoomChanged => "Camera.Zoom changed during measurement",
                RenderedFrameProfiler.InvalidReasonEnum.ActiveRegionSizeChanged => "ActiveRegion size changed during measurement",
                RenderedFrameProfiler.InvalidReasonEnum.SampleCountChanged => "SampleCount changed during measurement",
                RenderedFrameProfiler.InvalidReasonEnum.NoRenderedFrameTimeout => "No rendered frame for 2+ seconds (watchdog timeout)",
                RenderedFrameProfiler.InvalidReasonEnum.DrawSkipped => "BeginDraw returned false (render target unavailable)",
                RenderedFrameProfiler.InvalidReasonEnum.UnknownFailure => "Unknown failure",
                _ => "Unknown"
            };
        }
    }
}
