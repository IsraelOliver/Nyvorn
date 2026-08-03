using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Writes RenderedFrameProfiler results to formatted files.
    /// Separates OBSERVED (from metrics), INFERIDO (derived), and AINDA NÃO COMPROVADO (hypothetical).
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
                    writer.WriteLine("MEASUREMENT INVALID");
                    writer.WriteLine("==========================================================");
                    writer.WriteLine();
                    writer.WriteLine($"Invalid Reason: {ReasonToString(result.InvalidReason)}");
                    writer.WriteLine($"BeginDraw Rejected Count: {result.BeginDrawRejectedCount}");
                    writer.WriteLine($"NoRenderedFrameTimeout Count: {result.NoRenderedFrameTimeoutCount}");
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
                writer.WriteLine($"  Rendered Frames: {result.FramesRecorded}");
                writer.WriteLine($"  BeginDraw Rejected Count: {result.BeginDrawRejectedCount}");
                writer.WriteLine($"  Max Updates Before Rendered Frame: {result.MaxUpdatesBeforeRenderedFrame}");
                writer.WriteLine();

                // Observed metrics
                writer.WriteLine("==========================================================");
                writer.WriteLine("OBSERVED (from measurements):");
                writer.WriteLine("==========================================================");
                writer.WriteLine();

                writer.WriteLine("Frame Timing:");
                WriteStat(writer, "  WallClockFrameIntervalMs", ExtractMetric(metrics, m => m.WallClockFrameIntervalMs));

                var fpsStats = ExtractMetric(metrics, m => m.FPS);
                writer.WriteLine($"  FPS:");
                writer.WriteLine($"    Average: {fpsStats.avg:F1}");
                writer.WriteLine($"    Minimum: {metrics.Min(m => m.FPS):F1}");
                writer.WriteLine();

                writer.WriteLine("CPU Timing:");
                WriteStat(writer, "  TotalUpdateCpuMs", ExtractMetric(metrics, m => m.TotalUpdateCpuMs));
                WriteStat(writer, "  DrawCpuMs", ExtractMetric(metrics, m => m.DrawCpuMs));
                WriteStat(writer, "  PresentMs", ExtractMetric(metrics, m => m.PresentMs));
                writer.WriteLine();

                writer.WriteLine("Update Accumulation:");
                WriteIntStat(writer, "  UpdateCallsSinceLastDraw", ExtractIntMetric(metrics, m => m.UpdateCallsSinceLastDraw));
                WriteIntStat(writer, "  CatchUpUpdateCount", ExtractIntMetric(metrics, m => m.CatchUpUpdateCount));
                WriteIntStat(writer, "  IsRunningSlowlyUpdateCount", ExtractIntMetric(metrics, m => m.IsRunningSlowlyUpdateCount));
                writer.WriteLine();

                writer.WriteLine("Foundation Stages (nested in Update):");
                WriteStat(writer, "  FoundationUpdateMs", ExtractMetric(metrics, m => m.FoundationUpdateMs));
                WriteStat(writer, "    ClassificationMs", ExtractMetric(metrics, m => m.ClassificationMs));
                WriteStat(writer, "    OccluderBuildMs", ExtractMetric(metrics, m => m.OccluderBuildMs));
                WriteStat(writer, "    SunVisibilityBuildMs", ExtractMetric(metrics, m => m.SunVisibilityBuildMs));
                WriteIntStat(writer, "    SunVisibilityBuildCalls", ExtractIntMetric(metrics, m => calls[Array.IndexOf(result.Metrics, m)].SunVisibilityBuildCalls));
                writer.WriteLine();

                writer.WriteLine("Draw Stages:");
                WriteStat(writer, "  WorldRenderMs", ExtractMetric(metrics, m => m.WorldRenderMs));
                WriteStat(writer, "  DebugRendererMs", ExtractMetric(metrics, m => m.DebugRendererMs));
                WriteStat(writer, "    RenderSunVisibilityMs", ExtractMetric(metrics, m => m.RenderSunVisibilityMs));
                WriteStat(writer, "    DebugCompositeMs", ExtractMetric(metrics, m => m.DebugCompositeMs));
                WriteStat(writer, "  HudDrawMs", ExtractMetric(metrics, m => m.HudDrawMs));
                WriteStat(writer, "  BeginDrawGateMs", ExtractMetric(metrics, m => m.BeginDrawGateMs));
                writer.WriteLine();

                // Derived metrics
                writer.WriteLine("==========================================================");
                writer.WriteLine("INFERIDO (derived from observations):");
                writer.WriteLine("==========================================================");
                writer.WriteLine();

                var unaccountedStats = ExtractMetric(metrics, m => m.UnaccountedWallTimeMs);
                WriteStat(writer, "UnaccountedWallTimeMs", unaccountedStats);
                writer.WriteLine("  This is wall-clock time not accounted by CPU (GPU waits, VSync, etc).");
                writer.WriteLine();

                var totalCpuStats = ExtractMetric(metrics, m => m.TotalCpuMs);
                WriteStat(writer, "TotalCpuMs", totalCpuStats);
                writer.WriteLine("  This is TotalUpdateCpuMs + DrawCpuMs (CPU-only measurement).");
                writer.WriteLine();

                // Anomalies and diagnostic interpretation
                writer.WriteLine("==========================================================");
                writer.WriteLine("ANOMALIES & DIAGNOSTICS:");
                writer.WriteLine("==========================================================");
                writer.WriteLine();

                var maxUnaccounted = metrics.Max(m => m.UnaccountedWallTimeMs);
                if (maxUnaccounted > 5.0)
                {
                    writer.WriteLine($"⚠ High Unaccounted Time: max {maxUnaccounted:F2}ms");
                    writer.WriteLine("  Possible: GPU waits, VSync stalls, scheduler delays.");
                    writer.WriteLine();
                }

                var minFps = metrics.Min(m => m.FPS);
                if (minFps < 30.0)
                {
                    writer.WriteLine($"⚠ Low FPS: minimum {minFps:F1} FPS");
                    writer.WriteLine("  System may be overloaded or blocked.");
                    writer.WriteLine();
                }

                var maxUpdateCalls = metrics.Max(m => m.UpdateCallsSinceLastDraw);
                if (maxUpdateCalls > 2)
                {
                    writer.WriteLine($"⚠ Multiple Updates Per Rendered Frame: max {maxUpdateCalls}");
                    writer.WriteLine($"  MonoGame IsRunningSlowly triggered, executing catch-up Updates.");
                    writer.WriteLine();
                }

                var avgSunVisibilityCalls = calls.Average(c => c.SunVisibilityBuildCalls);
                if (avgSunVisibilityCalls > 1.1)
                {
                    writer.WriteLine($"⚠ Multiple SunVisibility Builds: avg {avgSunVisibilityCalls:F2} per rendered frame");
                    writer.WriteLine($"  SunVisibility rebuilt during catch-up Updates.");
                    writer.WriteLine();
                }

                var runningSlowlyCount = metrics.Count(m => m.IsRunningSlowlyUpdateCount > 0);
                if (runningSlowlyCount > 0)
                {
                    writer.WriteLine($"⚠ IsRunningSlowly: triggered in {runningSlowlyCount} rendered frames");
                    writer.WriteLine($"  Fixed timestep not keeping up with actual frame time.");
                    writer.WriteLine();
                }

                if (result.BeginDrawRejectedCount > 0)
                {
                    writer.WriteLine($"⚠ BeginDraw Rejected: {result.BeginDrawRejectedCount} times");
                    writer.WriteLine($"  Render target or graphics device unavailable.");
                    writer.WriteLine();
                }

                // Hypotheses to test
                writer.WriteLine("==========================================================");
                writer.WriteLine("AINDA NÃO COMPROVADO (hypotheses to validate):");
                writer.WriteLine("==========================================================");
                writer.WriteLine();

                writer.WriteLine("Hypothesis: <1 FPS caused by Update loop cascade");
                writer.WriteLine("  Would require: UpdateCallsSinceLastDraw >> 1");
                writer.WriteLine("               SunVisibilityBuildCalls >> 1 (proportional)");
                writer.WriteLine("               IsRunningSlowlyUpdateCount high");
                writer.WriteLine("               RenderedFrameInterval >> 16.67ms");
                if (maxUpdateCalls > 2 && avgSunVisibilityCalls > 1.1)
                {
                    writer.WriteLine("  Status: DATA CONSISTENT WITH HYPOTHESIS (but not proven until full test)");
                }
                else
                {
                    writer.WriteLine("  Status: NO EVIDENCE FOR THIS HYPOTHESIS");
                }
                writer.WriteLine();

                writer.WriteLine("Next Steps:");
                writer.WriteLine("  1. Run all three modes (Legacy, V3 None, V3 SunVisibility)");
                writer.WriteLine("  2. Compare metrics across modes");
                writer.WriteLine("  3. Identify which mode shows <1 FPS signature");
                writer.WriteLine("  4. Correlate with SunVisibilityBuildCalls to confirm hypothesis");
                writer.WriteLine();

                writer.WriteLine("==========================================================");
            }
        }

        private static void WriteStat(StreamWriter writer, string metricName, (double avg, double p95, double max) stats)
        {
            writer.WriteLine($"{metricName}:");
            writer.WriteLine($"    Average: {stats.avg:F3} ms");
            writer.WriteLine($"    P95:     {stats.p95:F3} ms");
            writer.WriteLine($"    Max:     {stats.max:F3} ms");
        }

        private static void WriteIntStat(StreamWriter writer, string metricName, (double avg, int max) stats)
        {
            writer.WriteLine($"{metricName}:");
            writer.WriteLine($"    Average: {stats.avg:F3}");
            writer.WriteLine($"    Max:     {stats.max}");
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
                RenderedFrameProfiler.InvalidReasonEnum.ModeChanged => "Lighting mode changed",
                RenderedFrameProfiler.InvalidReasonEnum.ResolutionChanged => "Resolution changed",
                RenderedFrameProfiler.InvalidReasonEnum.ZoomChanged => "Camera.Zoom changed",
                RenderedFrameProfiler.InvalidReasonEnum.ActiveRegionSizeChanged => "ActiveRegion size changed",
                RenderedFrameProfiler.InvalidReasonEnum.SampleCountChanged => "SampleCount changed",
                RenderedFrameProfiler.InvalidReasonEnum.NoRenderedFrameTimeout => "No rendered frame for 2+ seconds",
                RenderedFrameProfiler.InvalidReasonEnum.BeginDrawRejected => "BeginDraw returned false",
                RenderedFrameProfiler.InvalidReasonEnum.EndDrawFailed => "EndDraw failed",
                _ => "Unknown"
            };
        }
    }
}
