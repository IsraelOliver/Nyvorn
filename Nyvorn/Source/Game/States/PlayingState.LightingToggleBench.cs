using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using Nyvorn.Source.Engine.Graphics.LightingV8;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;

namespace Nyvorn.Source.Game.States;

/// <summary>--lighting-toggle-bench: transient procedural session (nothing saved). Validates repeated V7/V8 switches
/// with mining, sources and doors changed while a pipeline is inactive, then measures each pipeline's steady-state
/// frame time under the same camera, zoom, hour and sources. Switches go through SetLightingPipeline, as F4 does.</summary>
public partial class PlayingState
{
    private sealed record TogglePhase(string Name, string Scene, bool V8, Action Before, Action<string> Check, bool Measure, bool Rapid = false);

    private const int ToggleValidationFrames = 45, ToggleRapidFrames = 30, ToggleBenchSamples = 300;
    private readonly List<TogglePhase> togglePhases = new();
    private readonly List<string> toggleChecks = new(), toggleResults = new();
    private readonly List<Point> toggleManyTorches = new();
    private readonly double[] toggleV8StageSum = new double[8];
    private int togglePhase = -1, toggleFrames, toggleStageSamples, toggleResources = -1, toggleV7SourcesWithTorch = -1;
    private bool toggleAdvance = true;
    private long toggleV7Start, toggleV6Start, toggleV8Start;
    private double toggleV7CpuSum;
    private Point toggleTorch, toggleTarget, toggleBlocker;
    private Vector2 toggleSpawn;
    private float toggleZoom;
    private V8LightingRenderer toggleRenderer;
    private Color[] toggleDirectBaseline;
    private Rectangle toggleDirectBounds;

    private string TogglePhaseName => togglePhases[togglePhase].Name;

    private void InitializeLightingToggleBench()
    {
        var layer = session.LayerDefinitions.First(l => l.LayerType == WorldLayerType.ShallowUnderground);
        session.Player.SetDebugFly(true);
        session.Camera.UseBounds = false;
        toggleZoom = session.Camera.Zoom; // gameplay default, not a capture zoom
        toggleSpawn = session.Player.Position;
        Directory.CreateDirectory(V8GameplayOptions.Output);
        if (!FindV8BackgroundSite(layer, TileType.Dirt, false, out toggleTorch, out toggleTarget, out toggleBlocker))
            throw new InvalidOperationException("No generated Shallow cave site for the toggle bench");
        FindToggleTorchSpots();

        ToggleCheck("init", "V7 is the default pipeline", ActiveLightingLabel == "V7");
        ToggleCheck("init", "V8 renderer not created before its first activation", v8Renderer == null);
        var none = new KeyboardState();
        var held = new KeyboardState(LightingToggleKey);
        ToggleCheck("init", "F4 toggles on press; held and release do not repeat",
            IsLightingTogglePress(held, none) && !IsLightingTogglePress(held, held) && !IsLightingTogglePress(none, held));

        // Validation: each edit runs while the pipeline about to be activated is inactive.
        Add("v7-open", "cave", false, () => SetToggleTorches(toggleTorch), name =>
        {
            toggleV7SourcesWithTorch = v7Lighting.SourceCount; // torch + cave mushrooms in the region
            ToggleCheck(name, $"V7 direct reaches target ({V7DirectAtTarget():0.000})", V7DirectAtTarget() > 0);
        });
        Add("v8-first-activation", "cave", true, null, name =>
        {
            toggleDirectBaseline = V8Direct(out toggleDirectBounds);
            toggleRenderer = v8Renderer;
            toggleResources = v8Renderer.ResourceCreations;
            ToggleCheck(name, $"V8 direct reaches target ({V8DirectAtTarget(toggleDirectBaseline, toggleDirectBounds)})", V8DirectAtTarget(toggleDirectBaseline, toggleDirectBounds) > 0);
        });
        Add("v7-after-column-built-in-v8", "cave", false, PlaceToggleColumn, name =>
            ToggleCheck(name, $"V7 reflects column built while inactive: direct at target {V7DirectAtTarget():0.000}", V7DirectAtTarget() == 0));
        Add("v8-after-column-mined-in-v7", "cave", true, MineToggleColumn, name =>
        {
            var direct = V8Direct(out var bounds);
            int differing = bounds == toggleDirectBounds ? direct.Where((c, i) => c != toggleDirectBaseline[i]).Count() : -1;
            ToggleCheck(name, $"V8 reflects column mined while inactive: direct at target {V8DirectAtTarget(direct, bounds)}", V8DirectAtTarget(direct, bounds) > 0);
            ToggleCheck(name, $"V8 direct buffer identical to first activation ({differing} texels differ)", differing == 0);
            ToggleCheck(name, "same V8 renderer instance and no new GPU resources", ReferenceEquals(v8Renderer, toggleRenderer) && v8Renderer.ResourceCreations == toggleResources);
        });
        Add("v7-after-torch-removed-in-v8", "cave", false, () => SetToggleTorches(), name =>
            // Mushrooms are V7 sources too, so the torch removal is exactly one source fewer than with the torch.
            ToggleCheck(name, $"V7 reflects torch removed while inactive: sources {toggleV7SourcesWithTorch} -> {v7Lighting.SourceCount}, direct {V7DirectAtTarget():0.000}",
                v7Lighting.SourceCount == toggleV7SourcesWithTorch - 1 && V7DirectAtTarget() == 0));
        Add("v8-after-torch-and-closed-door-in-v7", "cave", true, () => { SetToggleTorches(toggleTorch); PlaceToggleDoor(); }, name =>
        {
            var direct = V8Direct(out var bounds);
            ToggleCheck(name, $"V8 reflects torch restored ({v8Sources.Count} sources) and closed door placed while inactive (direct {V8DirectAtTarget(direct, bounds)})",
                v8Sources.Count >= 1 && V8DirectAtTarget(direct, bounds) == 0);
        });
        Add("v7-with-door", "cave", false, null, name =>
            toggleResults.Add($"{name}: V7 direct at target {V7DirectAtTarget():0.000} (V7 has no door occlusion; informative only)"));
        Add("v8-after-door-opened-in-v7", "cave", true, OpenToggleDoor, name =>
        {
            var direct = V8Direct(out var bounds);
            ToggleCheck(name, $"V8 reflects door opened while inactive (direct {V8DirectAtTarget(direct, bounds)})", V8DirectAtTarget(direct, bounds) > 0);
        });
        togglePhases.Add(new TogglePhase("rapid-toggle-every-update", "cave", false,
            () => session.DoorRuntimeSystem.Restore(Array.Empty<DoorSaveData>()),
            name => ToggleCheck(name, "renderer kept and no new GPU resources after rapid toggling",
                ReferenceEquals(v8Renderer, toggleRenderer) && v8Renderer.ResourceCreations == toggleResources),
            false, Rapid: true));

        // Measurements: alternate V7/V8 twice per scene under identical conditions.
        foreach (string scene in new[] { "surface", "cave", "cave-8-torches" })
            foreach (bool v8 in new[] { false, true, false, true })
                togglePhases.Add(new TogglePhase($"bench-{scene}-{(v8 ? "v8" : "v7")}", scene, v8,
                    () => { if (scene == "cave-8-torches") SetToggleTorches(toggleManyTorches.ToArray()); else SetToggleTorches(toggleTorch); },
                    null, true));

        void Add(string name, string scene, bool v8, Action before, Action<string> check) =>
            togglePhases.Add(new TogglePhase(name, scene, v8, before, check, false));
    }

    private void UpdateLightingToggleBench()
    {
        if (togglePhase >= 0 && togglePhase < togglePhases.Count && togglePhases[togglePhase].Rapid && !toggleAdvance)
        {
            SetLightingPipeline(!v8Active);
            return;
        }

        if (!toggleAdvance || togglePhase >= togglePhases.Count)
            return;

        toggleAdvance = false;
        togglePhase++;
        if (togglePhase == togglePhases.Count)
        {
            WriteToggleBench();
            V8GameplayOptions.CaptureFinished = true;
            return;
        }

        var phase = togglePhases[togglePhase];
        phase.Before?.Invoke();
        session.SetWorldTimeOfDay(.5f);
        SetLightingPipeline(phase.V8);
        toggleFrames = 0;
        toggleV7Start = v7ComputeFrames; toggleV6Start = v6ComputeFrames; toggleV8Start = v8RenderFrames;
        toggleV7CpuSum = 0; Array.Clear(toggleV8StageSum); toggleStageSamples = 0;
        Console.WriteLine("[Lighting toggle bench] " + phase.Name);
    }

    private void PrepareLightingToggleBench(int width, int height)
    {
        if (togglePhase < 0 || togglePhase >= togglePhases.Count)
            return;

        var phase = togglePhases[togglePhase];
        int size = session.WorldMap.TileSize;
        session.Camera.Zoom = toggleZoom;
        if (phase.Scene == "surface")
        {
            session.Player.TeleportTo(toggleSpawn);
            session.Camera.CenterOn(toggleSpawn - new Vector2(0, 40), width, height);
            return;
        }

        int away = Math.Sign(toggleTarget.X - toggleTorch.X);
        session.Player.TeleportTo(new Vector2((toggleTorch.X - away * 10) * size, (toggleTorch.Y - 2) * size));
        session.Camera.CenterOn(new Vector2((toggleTorch.X + toggleTarget.X + 1) * size / 2f, (toggleTarget.Y + .5f) * size), width, height);
    }

    private void FinishLightingToggleBenchFrame()
    {
        if (togglePhase < 0 || togglePhase >= togglePhases.Count || toggleAdvance)
            return;

        var phase = togglePhases[togglePhase];
        var stats = LightingStats(ActiveLightingLabel);
        toggleFrames++;
        if (phase.Measure && stats.Count > 0)
        {
            if (v8Active)
            {
                toggleV8StageSum[0] += v8Renderer.LastGeometryMs; toggleV8StageSum[1] += v8Renderer.LastDirectMs;
                toggleV8StageSum[2] += v8Renderer.LastAmbientMs; toggleV8StageSum[3] += v8Renderer.LastFacesMs;
                toggleV8StageSum[4] += v8CpuMs;
                toggleV8StageSum[5] += v8Renderer.Ambient.LastFieldMs;
                toggleV8StageSum[6] += v8Renderer.Ambient.LastReconstructMs;
                toggleV8StageSum[7] += v8Renderer.Ambient.LastUploadMs;
            }
            else toggleV7CpuSum += v7Lighting.LastCpuMs;
            toggleStageSamples++;
        }

        bool done = phase.Measure ? stats.Count >= ToggleBenchSamples :
            toggleFrames >= (phase.Rapid ? ToggleRapidFrames : ToggleValidationFrames);
        if (!done)
            return;

        string name = phase.Name;
        long v7 = v7ComputeFrames - toggleV7Start, v6 = v6ComputeFrames - toggleV6Start, v8 = v8RenderFrames - toggleV8Start;
        if (phase.Rapid)
        {
            ToggleCheck(name, $"every frame computed exactly one pipeline (V7 {v7} + V6 {v6} + V8 {v8} = {toggleFrames} frames)", v7 + v6 + v8 == toggleFrames);
        }
        else
        {
            bool onlyActive = phase.V8 ? v8 == toggleFrames && v7 == 0 && v6 == 0 : v7 == toggleFrames && v8 == 0 && v6 == 0;
            ToggleCheck(name, $"only {(phase.V8 ? "V8" : "V7")} computed lighting (V7 {v7}, V6 {v6}, V8 {v8}; {toggleFrames} frames)", onlyActive);
            ToggleCheck(name, $"indicator {ActiveLightingLabel}; background albedo neutral={session.WorldMap.NeutralLightingAlbedo}",
                ActiveLightingLabel == (phase.V8 ? "V8" : "V7") && session.WorldMap.NeutralLightingAlbedo == phase.V8);
        }

        phase.Check?.Invoke(name);
        if (phase.Measure)
        {
            int n = Math.Max(1, toggleStageSamples);
            string work = phase.V8
                ? $"V8 CPU mean: geometry {toggleV8StageSum[0] / n:0.00}, direct {toggleV8StageSum[1] / n:0.00}, ambient {toggleV8StageSum[2] / n:0.00} (grid+propagation {toggleV8StageSum[5] / n:0.00} / per-texel reconstruction {toggleV8StageSum[6] / n:0.00} / texture upload {toggleV8StageSum[7] / n:0.00}), faces {toggleV8StageSum[3] / n:0.00}, collect+render+submission {toggleV8StageSum[4] / n:0.00} ms; {v8Sources.Count} sources; light bounds {v8Renderer.LightBounds.Width}x{v8Renderer.LightBounds.Height}"
                : $"V7 CPU mean {toggleV7CpuSum / n:0.00} ms; {v7Lighting.SourceCount} sources; region {v7Lighting.RegionWidth}x{v7Lighting.RegionHeight} tiles";
            toggleResults.Add($"{name}: {stats.Summary()} | first frame after switch {stats.FirstFrameMs:0.00} ms, {stats.DiscardedFrames} warmup frames discarded | {work}");
        }
        else SaveToggleBackBuffer(name);

        toggleAdvance = true;
    }

    private float V7DirectAtTarget()
    {
        Vector3 direct = v7Lighting.GetDirectAt(toggleTarget.X, toggleTarget.Y);
        return direct.X + direct.Y + direct.Z;
    }

    private Color[] V8Direct(out Rectangle bounds)
    {
        bounds = v8Renderer.LightBounds;
        return V8Capture.Read(v8Renderer.Direct);
    }

    private int V8DirectAtTarget(Color[] direct, Rectangle bounds)
    {
        int size = session.WorldMap.TileSize, sum = 0;
        for (int y = toggleTarget.Y * size; y < (toggleTarget.Y + 1) * size; y++)
            for (int x = toggleTarget.X * size; x < (toggleTarget.X + 1) * size; x++)
                if (bounds.Contains(x, y))
                {
                    Color c = direct[(y - bounds.Y) * bounds.Width + x - bounds.X];
                    sum += c.R + c.G + c.B;
                }
        return sum;
    }

    private void SetToggleTorches(params Point[] tiles)
    {
        int size = session.WorldMap.TileSize;
        session.TorchRuntimeSystem.Restore(tiles.Select(tile =>
        {
            var torch = new TorchInstance(tile, 0, size);
            return new TorchSaveData { PositionX = torch.Position.X, PositionY = torch.Position.Y, PoleFrameIndex = 0 };
        }).ToArray());
    }

    private void PlaceToggleColumn()
    {
        bool placed = true;
        for (int y = toggleBlocker.Y; y >= toggleBlocker.Y - 3; y--)
            placed &= session.WorldMap.TryPlaceTile(toggleBlocker.X, y, TileType.Stone);
        ToggleCheck(TogglePhaseName, "column placed by TryPlaceTile while V8 active", placed && ActiveLightingLabel == "V8");
    }

    private void MineToggleColumn()
    {
        bool mined = true;
        for (int y = toggleBlocker.Y - 3; y <= toggleBlocker.Y; y++)
            mined &= session.WorldMap.TryBreakTile(toggleBlocker.X, y, out _);
        ToggleCheck(TogglePhaseName, "column mined by TryBreakTile while V7 active", mined && ActiveLightingLabel == "V7");
    }

    private void PlaceToggleDoor() => session.DoorRuntimeSystem.Restore(new[] {
        new DoorSaveData { TileX = session.WorldMap.WrapTileX(toggleBlocker.X), TileY = toggleBlocker.Y - 2 } });

    private void OpenToggleDoor()
    {
        var door = session.DoorRuntimeSystem.Doors[0];
        session.Player.TeleportTo(door.InteractionPosition - new Vector2(16, 0));
        ToggleCheck(TogglePhaseName, "door opened by TryToggle while V7 active", door.TryToggle(session.Player) && door.IsOpen && ActiveLightingLabel == "V7");
    }

    private void FindToggleTorchSpots()
    {
        var map = session.WorldMap;
        toggleManyTorches.Add(toggleTorch);
        for (int y = toggleTorch.Y - 12; y <= toggleTorch.Y + 12 && toggleManyTorches.Count < 8; y++)
            for (int x = toggleTorch.X - 40; x <= toggleTorch.X + 40 && toggleManyTorches.Count < 8; x++)
                if (map.GetTile(x, y) == TileType.Empty && map.GetTile(x, y - 1) == TileType.Empty &&
                    map.GetTile(x, y + 1) is not (TileType.Empty or TileType.Platform) && x != toggleBlocker.X &&
                    toggleManyTorches.All(p => Math.Abs(p.X - x) + Math.Abs(p.Y - y) >= 6))
                    toggleManyTorches.Add(new Point(x, y));
    }

    private void SaveToggleBackBuffer(string name)
    {
        var pp = graphicsDevice.PresentationParameters;
        var pixels = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
        graphicsDevice.GetBackBufferData(pixels);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i].A = 255;
        using var texture = new Texture2D(graphicsDevice, pp.BackBufferWidth, pp.BackBufferHeight);
        texture.SetData(pixels);
        V8Capture.Save(texture, Path.Combine(V8GameplayOptions.Output, $"toggle-{togglePhase:00}-{name}_final.png"));
    }

    private void WriteToggleBench()
    {
        var pp = graphicsDevice.PresentationParameters;
        var header = new List<string>
        {
            "Lighting toggle bench. Seed=V8-GAMEPLAY-2026, Small; transient session, nothing saved.",
            $"Backbuffer {pp.BackBufferWidth}x{pp.BackBufferHeight}; zoom {toggleZoom}; " +
                (V8GameplayOptions.UncappedFrames ? "VSync off, variable timestep (--uncapped-frames)" : "MonoGame defaults: VSync on, fixed 60 Hz timestep"),
            $"Cave site: torch {toggleTorch}, target {toggleTarget}, blocker column x={toggleBlocker.X}; cave-8-torches: {string.Join(" ", toggleManyTorches)}",
            $"Per activation: transition frame and warmup ({LightingFrameStats.WarmupFrames} frames and {LightingFrameStats.WarmupSeconds:0.0} s) discarded, then {ToggleBenchSamples} samples.",
            "frame = wall time between Draw calls (Update + Draw + Present); draw CPU = PlayingState.Draw body, excludes Present. GPU time not measured.",
            $"V8 one-time initialization (graphics probe + renderer): {v8InitMs:0.0} ms, excluded from every statistic."
        };
        File.WriteAllLines(Path.Combine(V8GameplayOptions.Output, "toggle-checks.txt"), toggleChecks);
        File.WriteAllLines(Path.Combine(V8GameplayOptions.Output, "toggle-bench.txt"), header.Concat(toggleResults));
        if (toggleChecks.Any(c => c.StartsWith("FAIL")))
            Environment.ExitCode = 1;
    }

    private void ToggleCheck(string phase, string label, bool passed)
    {
        string line = $"{(passed ? "PASS" : "FAIL")} {phase}: {label}";
        toggleChecks.Add(line);
        Console.WriteLine("[Lighting toggle bench] " + line);
    }
}
