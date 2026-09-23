using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using Nyvorn.Source.Engine.Graphics.LightingV8;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.Game.States;

/// <summary>--v8-perf-bench: V8 frame cost in a transient procedural session (nothing saved). A static camera and a fixed
/// walk (same path every run, advanced per rendered frame so it does not depend on FPS), each with 0, 1 and several
/// torches. Decoration light sources are suppressed so source counts are exact. Every frame after the scenario warmup is
/// kept, including geometry and face rebuild frames. No capture or GPU readback happens during collection.</summary>
public partial class PlayingState
{
    private sealed record PerfScenario(string Name, int Torches, bool Walk);

    private static readonly string[] PerfColumns =
    {
        "frame_ms", "draw_cpu_ms", "update_cpu_ms", "rest_ms", "v8_cpu_ms",
        "geometry_ms", "geometry_sand_hash_ms", "geometry_fill_ms", "geometry_edges_ms",
        "direct_ms", "ambient_ms", "ambient_grid_ms", "ambient_upload_ms",
        "faces_ms", "faces_build_ms", "faces_upload_ms", "faces_draw_ms",
        "geometry_rebuilt", "faces_rebuilt", "face_vertices", "face_weighted_pixels", "edges", "sources", "camera_x",
        "face_chunks_built", "face_chunks_visible", "face_arena_rewrites", "face_chunks_uncovered",
        "present_ms", "window_active"
    };
    private int perfPreviousArenaRewrites;
    private const int PerfTimingColumns = 17, PerfGeometryRebuilt = 17, PerfFacesRebuilt = 18;

    private const int PerfWarmupFrames = 60, PerfStaticFrames = 600, PerfWalkTiles = 160, PerfWalkPixelsPerFrame = 2;
    private readonly List<PerfScenario> perfScenarios = new();
    private readonly List<double[]> perfSamples = new();
    private readonly List<string> perfResults = new();
    private readonly List<Point> perfTorchSpots = new();
    private readonly StringBuilder perfCsv = new();
    private int perfScenario = -1, perfFrame;
    private double[] perfPending;
    private bool perfScenarioDone, perfFinished;
    private bool v8SuppressDecorationSources;
    private Point perfTorch;
    private float perfZoom;

    private void InitializeV8PerfBench()
    {
        var layer = session.LayerDefinitions.First(l => l.LayerType == WorldLayerType.ShallowUnderground);
        session.Player.SetDebugFly(true);
        session.Camera.UseBounds = false;
        perfZoom = session.Camera.Zoom; // gameplay default zoom
        Directory.CreateDirectory(V8GameplayOptions.Output);
        if (!FindV8BackgroundSite(layer, TileType.Dirt, false, out perfTorch, out _, out _))
            throw new InvalidOperationException("No generated Shallow cave site for the V8 perf bench");

        FindPerfTorchSpots();
        v8SuppressDecorationSources = true;
        foreach (int torches in new[] { 0, 1, perfTorchSpots.Count })
        {
            perfScenarios.Add(new PerfScenario($"static-{torches}-torches", torches, false));
            perfScenarios.Add(new PerfScenario($"walk-{torches}-torches", torches, true));
        }

        perfCsv.AppendLine("scenario,sample," + string.Join(",", PerfColumns));
    }

    private void FindPerfTorchSpots()
    {
        var map = session.WorldMap;
        perfTorchSpots.Add(perfTorch);
        for (int x = perfTorch.X - PerfWalkTiles / 2; x <= perfTorch.X + PerfWalkTiles / 2 && perfTorchSpots.Count < 8; x++)
            for (int y = perfTorch.Y - 15; y <= perfTorch.Y + 15 && perfTorchSpots.Count < 8; y++)
                if (map.GetTile(x, y) == TileType.Empty && map.GetTile(x, y - 1) == TileType.Empty &&
                    map.GetTile(x, y + 1) is not (TileType.Empty or TileType.Platform) &&
                    perfTorchSpots.All(p => Math.Abs(p.X - x) >= 18))
                    perfTorchSpots.Add(new Point(x, y));
    }

    private void PrepareV8PerfBench(int width, int height, float frameSeconds)
    {
        if (perfFinished)
            return;

        if (perfPending != null)
        {
            // Wall, draw and update time of the previous frame are only known now.
            perfPending[0] = frameSeconds * 1000.0;
            perfPending[1] = frameCpuMs;
            perfPending[2] = lastFrameUpdateMs;
            perfPending[3] = perfPending[0] - perfPending[1] - perfPending[2];
            perfPending[28] = lastPresentMs;       // EndDraw/Present of that same frame
            perfPending[29] = lastWindowActive ? 1 : 0;
            perfSamples.Add(perfPending);
            perfCsv.Append(perfScenarios[perfScenario].Name).Append(',').Append(perfSamples.Count).Append(',')
                .AppendLine(string.Join(",", perfPending.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))));
            perfPending = null;
        }

        if (perfScenario < 0 || perfScenarioDone)
        {
            if (perfScenario >= 0)
                SummarizePerfScenario();

            perfScenario++;
            if (perfScenario == perfScenarios.Count)
            {
                perfFinished = true;
                WritePerfBench();
                V8GameplayOptions.CaptureFinished = true;
                return;
            }

            var next = perfScenarios[perfScenario];
            SetToggleTorches(next.Torches == 0 ? Array.Empty<Point>() : next.Torches == 1 ? new[] { perfTorch } : perfTorchSpots.ToArray());
            session.SetWorldTimeOfDay(.5f);
            perfFrame = 0;
            perfSamples.Clear();
            perfScenarioDone = false;
            Console.WriteLine("[V8 perf bench] " + next.Name);
        }

        var scenario = perfScenarios[perfScenario];
        int size = session.WorldMap.TileSize;
        float x = scenario.Walk
            ? (perfTorch.X - PerfWalkTiles / 2) * size + Math.Max(0, perfFrame - PerfWarmupFrames) * PerfWalkPixelsPerFrame
            : (perfTorch.X + .5f) * size;
        var center = new Vector2(x, (perfTorch.Y + .5f) * size);
        session.Camera.Zoom = perfZoom;
        session.Player.TeleportTo(center - new Vector2(0, 24));
        session.Camera.CenterOn(center, width, height);
    }

    private void FinishV8PerfBench()
    {
        if (perfFinished || perfScenario < 0 || perfScenario >= perfScenarios.Count)
            return;

        perfFrame++;
        if (perfFrame <= PerfWarmupFrames)
            return;

        var geometry = v8Renderer.Geometry;
        var ambient = v8Renderer.Ambient;
        perfPending = new double[]
        {
            0, 0, 0, 0, v8CpuMs,
            v8Renderer.LastGeometryMs, geometry.LastSandHashMs, geometry.LastFillMs, geometry.LastEdgesMs,
            v8Renderer.LastDirectMs, v8Renderer.LastAmbientMs, ambient.LastFieldMs, ambient.LastUploadMs,
            v8Renderer.LastFacesMs, v8Renderer.LastFaceBuildMs, v8Renderer.LastFaceUploadMs, v8Renderer.LastFaceDrawMs,
            geometry.LastRebuilt ? 1 : 0, v8Renderer.LastFacesRebuilt ? 1 : 0, v8Renderer.FaceVertexCount,
            v8Renderer.FaceWeightedPixels, geometry.Edges.Count, v8Sources.Count, session.Camera.Position.X,
            v8Renderer.FaceChunksBuilt, v8Renderer.FaceChunksVisible, v8Renderer.FaceArenaRewrites - perfPreviousArenaRewrites,
            v8Renderer.FaceChunksUncovered, 0, 1
        };
        perfPreviousArenaRewrites = v8Renderer.FaceArenaRewrites;

        var scenario = perfScenarios[perfScenario];
        int frames = scenario.Walk ? PerfWalkTiles * session.WorldMap.TileSize / PerfWalkPixelsPerFrame : PerfStaticFrames;
        if (perfFrame >= PerfWarmupFrames + frames)
            perfScenarioDone = true;
    }

    private void SummarizePerfScenario()
    {
        var scenario = perfScenarios[perfScenario];
        var line = new StringBuilder($"{scenario.Name}: n={perfSamples.Count}");
        for (int column = 0; column < PerfTimingColumns; column++)
            line.Append($" | {PerfColumns[column]} {Stats(perfSamples, column)}");

        var geometryFrames = perfSamples.Where(s => s[PerfGeometryRebuilt] > 0).ToList();
        var faceFrames = perfSamples.Where(s => s[PerfFacesRebuilt] > 0).ToList();
        line.Append($" | geometry rebuild frames {geometryFrames.Count} (fill {Stats(geometryFrames, 7)}, edges {Stats(geometryFrames, 8)})");
        line.Append($" | face rebuild frames {faceFrames.Count} (build {Stats(faceFrames, 14)}, upload {Stats(faceFrames, 15)})");
        line.Append($" | face vertices {Stats(perfSamples, 19)}, weighted face pixels {Stats(perfSamples, 20)}, edges {Stats(perfSamples, 21)}");
        line.Append($" | face chunks built per frame {Stats(perfSamples, 24)} (total {perfSamples.Sum(s => s[24]):0}), visible {Stats(perfSamples, 25)}, " +
            $"arena rewrites {perfSamples.Sum(s => s[26]):0}, uncovered chunks {perfSamples.Sum(s => s[27]):0}");

        int inactive = perfSamples.Count(s => s[29] == 0), slowInactive = perfSamples.Count(s => s[29] == 0 && s[0] > 16.8);
        line.Append($" | present {Stats(perfSamples, 28)}; present on frames >16.8 ms {Stats(perfSamples.Where(s => s[0] > 16.8).ToList(), 28)}");
        line.Append($" | window inactive frames {inactive} (of which >16.8 ms {slowInactive})");
        int Over(double ms) => perfSamples.Count(s => s[0] > ms);
        line.Append($" | frames >16.8 ms {Over(16.8)}, >33.4 ms {Over(33.4)}, >50 ms {Over(50)}, >100 ms {Over(100)}");
        line.Append($" | sources median {LightingFrameStats.Percentiles(perfSamples.Select(s => (float)s[22]).ToList()).Median:0}");
        perfResults.Add(line.ToString());
        Console.WriteLine("[V8 perf bench] " + line);

        static string Stats(List<double[]> samples, int column)
        {
            if (samples.Count == 0) return "-";
            var values = samples.Select(s => (float)s[column]).ToList();
            var (median, p95) = LightingFrameStats.Percentiles(values);
            return $"{median:0.00}/{p95:0.00}/max {values.Max():0.00}";
        }
    }

    private void WritePerfBench()
    {
        var pp = graphicsDevice.PresentationParameters;
        var header = new List<string>
        {
            "V8 performance bench. Seed=V8-GAMEPLAY-2026, Small; transient session, nothing saved; no captures or GPU readback during collection.",
            $"Backbuffer {pp.BackBufferWidth}x{pp.BackBufferHeight}; zoom {perfZoom}; " +
                (V8GameplayOptions.UncappedFrames ? "VSync off, variable timestep (--uncapped-frames, diagnosis only)" : "MonoGame defaults: VSync on, fixed 60 Hz timestep"),
            $"Site torch {perfTorch}; torch spots {string.Join(" ", perfTorchSpots)}; decoration light sources suppressed; hour 0.5.",
            $"static: camera on the site torch, {PerfStaticFrames} frames. walk: from tile x={perfTorch.X - PerfWalkTiles / 2}, +{PerfWalkPixelsPerFrame} world px per rendered frame over {PerfWalkTiles} tiles (path independent of FPS).",
            $"Each scenario discards only its first {PerfWarmupFrames} frames (static, at its start position); every later frame is kept, rebuild frames included.",
            "Values: median/p95/max. frame = wall time between Draw calls; draw CPU = PlayingState.Draw; update CPU = Updates in that frame; rest = frame - draw - update (Present, GPU/driver waits, VSync).",
            "v8 CPU = source collection + V8 render + submission. Stage timers are CPU; GPU time not measured. Rebuild-frame statistics cover only frames where that stage rebuilt.",
            $"V8 one-time initialization {v8InitMs:0.0} ms (not in statistics)."
        };
        File.WriteAllLines(Path.Combine(V8GameplayOptions.Output, "perf.txt"), header.Concat(perfResults));
        File.WriteAllText(Path.Combine(V8GameplayOptions.Output, "perf-frames.csv"), perfCsv.ToString());
    }
}
