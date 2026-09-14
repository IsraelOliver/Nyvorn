using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingV8;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;

namespace Nyvorn.Source.Game.States;

public partial class PlayingState
{
    private readonly string[] v8WorldCases = { "surface-day", "surface-night", "shallow-day", "shallow-night", "shallow-sealed",
        "cavern-torch", "cavern-off", "deep-torch", "deep-off", "wall-before", "wall-mined", "wall-rebuilt",
        "door-closed", "door-open", "many-sources", "camera-shift", "zoom", "wide-view", "camera-pan", "wrap", "sand" };
    private int v8WorldCase, v8CaseFrames;
    private bool v8CasePrepared;
    private Vector2 v8Spawn, v8CaptureCenter;
    private Point v8Room;
    private readonly List<double> v8CpuSamples = new(), v8FrameSamples = new();
    private readonly List<string> v8WorldChecks = new();
    private Color[] v8WallBaseline;
    private Rectangle v8WallBounds;
    private Color[] v8ManyDirect, v8ManyLocal;
    private Rectangle v8ManyBounds;
    private Point v8ManyRoom;
    private int v8StableResources;
    private long v8DaySky;
    private string V8WorldCaptureLabel => V8GameplayOptions.CaptureWorld ? v8WorldCases[Math.Min(v8WorldCase, v8WorldCases.Length - 1)] : "gameplay";
    private bool ShouldCaptureV8WorldFrame => V8GameplayOptions.CaptureWorld && v8CaseFrames == 29;

    private void InitializeV8WorldCapture()
    {
        v8Spawn = session.Player.Position;
        session.Player.SetDebugFly(true);
        // Simulation still advances through PlayingState.Update; captures reposition only the camera/player.
        session.Camera.Zoom = 3;
        session.Camera.UseBounds = false;
        File.WriteAllText(Path.Combine(V8GameplayOptions.Output, "run.txt"),
            $"V8 procedural gameplay capture. Seed=V8-GAMEPLAY-2026; Small; {session.WorldMap.Width}x{session.WorldMap.Height} tiles.\n" +
            "Transient session; no user saves loaded or written. Surface unchanged; underground test chambers are scripted construction in the generated world.\n" +
            "30 rendered frames/case; first 10 warmup; CPU timer covers collection, geometry, lighting and driver submission. GPU duration not measured.\n" +
            string.Join("\n", session.LayerDefinitions.Select(l => $"{l.LayerType}: {l.StartY}..{l.EndY}")));
    }

    private void PrepareV8WorldCapture(int width, int height)
    {
        string name = V8WorldCaptureLabel;
        if (!v8CasePrepared)
        {
            session.Camera.Zoom = name == "zoom" ? 4 : 3;
            session.SetWorldTimeOfDay(name.EndsWith("night") ? .02f : .5f);
            if (name.StartsWith("surface"))
            {
                v8CaptureCenter = v8Spawn - new Vector2(0, 40);
            }
            else
            {
                WorldLayerType type = name.StartsWith("shallow") ? WorldLayerType.ShallowUnderground :
                    name.StartsWith("deep") ? WorldLayerType.DeepCavern : WorldLayerType.Cavern;
                var layer = session.LayerDefinitions.First(l => l.LayerType == type);
                bool keepRoom = name is "wall-mined" or "wall-rebuilt" or "door-open" or "camera-shift" or "zoom" or "wide-view" or "camera-pan" or "shallow-sealed";
                if (!keepRoom)
                {
                    v8Room = new Point(name == "wrap" ? -24 : 140, layer.StartY + 12);
                    BuildV8CaptureRoom(name);
                }
                int size = session.WorldMap.TileSize;
                if (name == "shallow-sealed")
                    for (int y = 8; y < 14; y++) for (int x = 10; x < 14; x++)
                        session.WorldMap.TryPlaceBackgroundTile(v8Room.X + x, v8Room.Y + y, TileType.Stone);
                if (name == "wall-mined")
                    for (int y = 10; y < 21; y++) session.WorldMap.TryBreakTile(v8Room.X + 36, v8Room.Y + y, out _);
                if (name == "wall-rebuilt")
                    for (int y = 10; y < 21; y++) session.WorldMap.TryPlaceTile(v8Room.X + 36, v8Room.Y + y, TileType.Stone);
                if (name == "door-open")
                {
                    var door = session.DoorRuntimeSystem.Doors[0];
                    session.Player.TeleportTo(door.InteractionPosition - new Vector2(16, 0));
                    V8WorldCheck("door interaction toggled", door.TryToggle(session.Player) && door.IsOpen);
                }
                v8CaptureCenter = new Vector2((v8Room.X + 30) * size, (v8Room.Y + 19) * size);
                if (name == "camera-shift") v8CaptureCenter += new Vector2(13.25f, -5.5f);
                if (name == "sand") v8CaptureCenter += new Vector2(0, 6 * size);
            }
            v8CpuSamples.Clear(); v8FrameSamples.Clear(); v8CasePrepared = true;
            Console.WriteLine("[V8 capture] " + name);
        }
        // Keep benchmark input deterministic, while normal session simulation continues between draws.
        session.Camera.Zoom = name == "zoom" ? 4 : name == "wide-view" ? 2 : 3;
        if (!name.StartsWith("surface")) session.Player.TeleportTo(new Vector2((v8Room.X + 27) * 8, (v8Room.Y + 26) * 8));
        else session.Player.TeleportTo(v8Spawn);
        session.Camera.CenterOn(v8CaptureCenter + (name == "camera-pan" ? new Vector2(v8CaseFrames * 2.25f, 0) : Vector2.Zero), width, height);
    }

    private void BuildV8CaptureRoom(string name)
    {
        var map = session.WorldMap;
        int size = map.TileSize;
        session.SandSystem.RemoveSandInRectangle(v8Room.X * size, v8Room.Y * size, 64 * size, 40 * size);
        for (int y = 0; y < 40; y++) for (int x = 0; x < 64; x++)
        {
            bool solid = x == 0 || x >= 62 || y == 0 || y >= 38 || x == 36 || (x >= 6 && x < 13 && y >= 29);
            map.SetTile(v8Room.X + x, v8Room.Y + y, solid ? TileType.Stone : TileType.Empty);
            map.SetBackgroundTile(v8Room.X + x, v8Room.Y + y, TileType.Stone);
        }
        if (name.StartsWith("shallow"))
            for (int y = 8; y < 14; y++) for (int x = 10; x < 14; x++)
                map.TryBreakBackgroundTile(v8Room.X + x, v8Room.Y + y, out _);
        var platforms = new List<PlatformSaveData>();
        for (int x = 20; x < 29; x++) platforms.Add(new PlatformSaveData { PositionX = (v8Room.X + x) * size, PositionY = (v8Room.Y + 19) * size });
        session.PlatformRuntimeSystem.Restore(platforms);
        var torches = new List<TorchSaveData>();
        if (!name.EndsWith("off") && !name.StartsWith("shallow")) AddTorch(14, 14);
        if (name is "many-sources" or "wrap") { AddTorch(29, 30); AddTorch(48, 18); AddTorch(55, 30); }
        if (name == "sand") AddTorch(31, 30);
        session.TorchRuntimeSystem.Restore(torches);
        void AddTorch(int x, int y)
        {
            map.SetTile(v8Room.X + x, v8Room.Y + y + 1, TileType.Stone);
            var torch = new TorchInstance(new Point(map.WrapTileX(v8Room.X + x), v8Room.Y + y), 0, size);
            torches.Add(new TorchSaveData { PositionX = torch.Position.X, PositionY = torch.Position.Y, PoleFrameIndex = 0 });
        }
        session.DoorRuntimeSystem.Restore(Array.Empty<DoorSaveData>());
        if (name == "door-closed")
        {
            for (int y = 16; y < 19; y++) map.SetTile(v8Room.X + 36, v8Room.Y + y, TileType.Empty);
            session.DoorRuntimeSystem.Restore(new[] { new DoorSaveData { TileX = map.WrapTileX(v8Room.X + 36), TileY = v8Room.Y + 16 } });
        }
        if (name == "sand") session.SandSystem.AddSettledSandRectangle((v8Room.X + 20) * size, (v8Room.Y + 34) * size, 8 * size, 4 * size);
    }

    private void FinishV8WorldFrame(float frameSeconds, bool captured)
    {
        if (v8CaseFrames == 10) v8StableResources = v8Renderer.ResourceCreations;
        if (v8CaseFrames >= 10 && !captured) { v8CpuSamples.Add(v8CpuMs); v8FrameSamples.Add(frameSeconds * 1000); }
        if (captured)
        {
            ObserveV8WorldCapture();
            v8CpuSamples.Sort(); v8FrameSamples.Sort();
            string line = $"{V8WorldCaptureLabel}: n={v8CpuSamples.Count}; {v8Final.Width}x{v8Final.Height}; zoom={session.Camera.Zoom}; sources={v8Sources.Count}; " +
                $"CPU+submission median={v8CpuSamples[v8CpuSamples.Count / 2]:F3}ms p95={v8CpuSamples[(int)((v8CpuSamples.Count - 1) * .95)]:F3}ms; " +
                $"frame median={v8FrameSamples[v8FrameSamples.Count / 2]:F3}ms; GPU=unmeasured; " +
                $"last stages geometry={v8Renderer.LastGeometryMs:F3}, direct={v8Renderer.LastDirectMs:F3}, ambient={v8Renderer.LastAmbientMs:F3}, faces={v8Renderer.LastFacesMs:F3}ms\n";
            File.AppendAllText(Path.Combine(V8GameplayOptions.Output, "timings.txt"), line);
            v8WorldCase++; v8CaseFrames = 0; v8CasePrepared = false;
            if (v8WorldCase == v8WorldCases.Length)
            {
                File.WriteAllLines(Path.Combine(V8GameplayOptions.Output, "checks.txt"), v8WorldChecks);
                V8GameplayOptions.CaptureFinished = true;
                if (v8WorldChecks.Any(s => s.StartsWith("FAIL"))) Environment.ExitCode = 1;
            }
        }
        else v8CaseFrames++;
    }

    private void ObserveV8WorldCapture()
    {
        var sky = V8Capture.Read(v8Renderer.Ambient.SkyTexture);
        var direct = V8Capture.Read(v8Renderer.Direct);
        var local = V8Capture.Read(v8Renderer.Ambient.LocalTexture);
        var bounds = v8Renderer.LightBounds;
        bool zero = true;
        for (int y = 0; y < bounds.Height; y++)
            if (v8Renderer.AmbientContext.SkyWeight((bounds.Y + y) / 8) == 0)
                for (int x = 0; x < bounds.Width; x++)
                { var c = sky[y * bounds.Width + x]; zero &= c.R == 0 && c.G == 0 && c.B == 0; }
        V8WorldCheck("sky zero in forbidden layers", zero);
        V8WorldCheck("stable graphics resources", v8StableResources == v8Renderer.ResourceCreations);
        string name = V8WorldCaptureLabel;
        if (name.EndsWith("off"))
        {
            V8WorldCheck("no direct after removing torches", direct.All(c => c.R == 0 && c.G == 0 && c.B == 0));
            V8WorldCheck("no residual local inside room", InteriorTotal(local) == 0);
        }
        if (name == "shallow-day") { v8DaySky = InteriorTotal(sky); V8WorldCheck("background opening admits sky", v8DaySky > 0); }
        if (name == "shallow-night") V8WorldCheck("real night sky weaker than day", InteriorTotal(sky) < v8DaySky);
        if (name == "shallow-sealed") V8WorldCheck("background construction removes aperture", InteriorTotal(sky) == 0);
        if (name == "sand")
        {
            int sandPixels = 0; bool classified = true;
            for (int y = (v8Room.Y + 30) * 8; y < (v8Room.Y + 38) * 8; y++)
                for (int x = (v8Room.X + 16) * 8; x < (v8Room.X + 32) * 8; x++)
                    if (session.SandSystem.HasSandAt(x, y)) { sandPixels++; classified &= v8Renderer.Geometry.IsSolid(x, y); }
            V8WorldCheck($"dynamic sand classified ({sandPixels} pixels)", sandPixels > 100 && classified);
        }
        if (name == "many-sources") { v8ManyDirect = direct; v8ManyLocal = local; v8ManyBounds = bounds; v8ManyRoom = v8Room; }
        if (name is "camera-shift" or "zoom" or "wide-view" or "camera-pan" or "wrap")
        {
            Point delta = (v8Room - v8ManyRoom) * new Point(8, 8);
            int directError = 0, localError = 0, samples = 0;
            for (int y = 3; y < 37; y++) for (int x = 3; x < 60; x++)
            {
                Point p = new((v8Room.X + x) * 8 + 4, (v8Room.Y + y) * 8 + 4), old = p - delta;
                if (!bounds.Contains(p) || !v8ManyBounds.Contains(old)) continue;
                int i = (p.Y - bounds.Y) * bounds.Width + p.X - bounds.X;
                int j = (old.Y - v8ManyBounds.Y) * v8ManyBounds.Width + old.X - v8ManyBounds.X;
                directError = Math.Max(directError, Error(direct[i], v8ManyDirect[j]));
                localError = Math.Max(localError, Error(local[i], v8ManyLocal[j])); samples++;
            }
            V8WorldCheck($"world-anchored direct/local ({samples} samples; errors {directError}/{localError})", samples > 200 && directError <= 1 && localError <= 1);
        }
        if (V8WorldCaptureLabel == "wall-before") { v8WallBaseline = direct; v8WallBounds = bounds; }
        if (V8WorldCaptureLabel == "wall-mined") V8WorldCheck("mining changes direct", bounds == v8WallBounds && !direct.SequenceEqual(v8WallBaseline));
        if (V8WorldCaptureLabel == "wall-rebuilt") V8WorldCheck("construction restores direct", bounds == v8WallBounds && direct.SequenceEqual(v8WallBaseline));
        long InteriorTotal(Color[] field)
        {
            long sum = 0;
            for (int y = 2; y < 37; y++) for (int x = 2; x < 61; x++)
            {
                Point p = new((v8Room.X + x) * 8 + 4, (v8Room.Y + y) * 8 + 4);
                if (!bounds.Contains(p)) continue;
                var c = field[(p.Y - bounds.Y) * bounds.Width + p.X - bounds.X]; sum += c.R + c.G + c.B;
            }
            return sum;
        }
        static int Error(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    }
    private void V8WorldCheck(string label, bool passed) => v8WorldChecks.Add($"{(passed ? "PASS" : "FAIL")} {V8WorldCaptureLabel}: {label}");
}
