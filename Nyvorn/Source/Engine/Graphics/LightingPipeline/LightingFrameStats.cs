using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Steady-state frame timing for one lighting pipeline. Reset on every activation: the frame spanning
    /// the switch is never added, and a warmup (both frames AND seconds) is discarded, so one-time
    /// initialization never mixes with continuous cost. Keeps the most recent <see cref="Capacity"/> samples.
    /// </summary>
    public sealed class LightingFrameStats
    {
        public const int WarmupFrames = 60;
        public const double WarmupSeconds = 1.0;
        public const int Capacity = 1800;

        private readonly List<float> frameMs = new(Capacity);
        private readonly List<float> drawCpuMs = new(Capacity);
        private readonly List<float> updateCpuMs = new(Capacity);
        private readonly List<float> updatesPerFrame = new(Capacity);
        private readonly List<float> restMs = new(Capacity);
        private int next;
        private double warmupElapsedSeconds;

        public LightingFrameStats(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public int DiscardedFrames { get; private set; }
        public float FirstFrameMs { get; private set; } = -1f;
        public int Count => frameMs.Count;
        public bool WarmingUp => DiscardedFrames < WarmupFrames || warmupElapsedSeconds < WarmupSeconds;

        public void Reset()
        {
            frameMs.Clear();
            drawCpuMs.Clear();
            updateCpuMs.Clear();
            updatesPerFrame.Clear();
            restMs.Clear();
            next = 0;
            warmupElapsedSeconds = 0;
            DiscardedFrames = 0;
            FirstFrameMs = -1f;
        }

        /// <param name="frame">Wall time between consecutive Draw calls.</param>
        /// <param name="drawCpu">CPU time of the gameplay Draw call; excludes Present.</param>
        /// <param name="updateCpu">CPU time of every Update that ran in this frame (fixed timestep may run several).</param>
        /// <param name="updates">Number of Update calls in this frame.</param>
        public void Add(float frame, float drawCpu, float updateCpu, int updates)
        {
            if (FirstFrameMs < 0f)
                FirstFrameMs = frame;

            if (WarmingUp)
            {
                DiscardedFrames++;
                warmupElapsedSeconds += frame / 1000.0;
                return;
            }

            // Rest = Present, GPU/driver waits, VSync and scheduling outside Update and Draw.
            float rest = frame - drawCpu - updateCpu;
            if (frameMs.Count < Capacity)
            {
                frameMs.Add(frame);
                drawCpuMs.Add(drawCpu);
                updateCpuMs.Add(updateCpu);
                updatesPerFrame.Add(updates);
                restMs.Add(rest);
                return;
            }

            frameMs[next] = frame;
            drawCpuMs[next] = drawCpu;
            updateCpuMs[next] = updateCpu;
            updatesPerFrame[next] = updates;
            restMs[next] = rest;
            next = (next + 1) % Capacity;
        }

        public string Summary()
        {
            if (Count == 0)
                return $"{Name} warming up ({DiscardedFrames}/{WarmupFrames} frames, {warmupElapsedSeconds:0.0}/{WarmupSeconds:0.0} s discarded)";

            var (frameMedian, frameP95) = Percentiles(frameMs);
            var (drawMedian, drawP95) = Percentiles(drawCpuMs);
            var (updateMedian, updateP95) = Percentiles(updateCpuMs);
            var (updatesMedian, updatesP95) = Percentiles(updatesPerFrame);
            var (restMedian, restP95) = Percentiles(restMs);
            return $"{Name} frame median {frameMedian:0.00} ms / p95 {frameP95:0.00} ms (~{1000f / frameMedian:0} FPS) | " +
                   $"draw CPU {drawMedian:0.00}/{drawP95:0.00} | update CPU {updateMedian:0.00}/{updateP95:0.00} " +
                   $"({updatesMedian:0.#}/{updatesP95:0.#} updates) | rest {restMedian:0.00}/{restP95:0.00} ms (median/p95, n={Count})";
        }

        /// <summary>Median (mean of the two middle values when even) and nearest-rank p95.</summary>
        public static (float Median, float P95) Percentiles(IReadOnlyList<float> values)
        {
            if (values.Count == 0)
                return (float.NaN, float.NaN);

            var sorted = new float[values.Count];
            for (int i = 0; i < sorted.Length; i++)
                sorted[i] = values[i];
            Array.Sort(sorted);

            int n = sorted.Length;
            float median = n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) * 0.5f;
            float p95 = sorted[Math.Max(0, (int)Math.Ceiling(n * 0.95) - 1)];
            return (median, p95);
        }
    }
}
