using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;
using Nyvorn.Source.Engine.Graphics.LightingV9Probe;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;

namespace Nyvorn.Source.Game.States;

/// <summary>
/// --v9-gameplay-smoke: the default V9 gameplay path (PlayingState.V9.cs, without the probe's instrumentation) on a
/// transient world (seed V8-GAMEPLAY-2026, Small; nothing saved). The world clock runs; the camera, its interior zoom and
/// the HUD are the game's own. The steps only place the player, set torches and edit FG/BG tiles through the world's own
/// APIs. On each step's last frame the lit frame is saved and the field is checked against V9ProbeField.Verify (a
/// from-scratch evaluation over the world's own solidity); CPU times per step are recorded. Diagnostic only; then exit.
/// </summary>
public partial class PlayingState
{
    private static readonly (string Name, int Frames)[] V9SmokeSteps =
    {
        ("surface", 90), ("surface-walk", 180), ("torches-3", 60), ("torches-1", 60), ("fg-placed", 60), ("bg-placed", 60),
        ("edits-removed", 60), ("shallow", 150), ("shallow-torch", 60), ("cavern", 150)
    };
    private const int V9SmokeSettle = 20; // frames after a step's action left out of its timing (camera and zoom settle)
    private int v9SmokeStep, v9SmokeFrame;
    private Vector2 v9SmokeStand;
    private Point v9SmokeShallowFloor, v9SmokeCavernFloor;
    private bool v9SmokeShallowFound, v9SmokeCavernFound;
    private List<TorchSaveData> v9SmokeWorldTorches;
    private readonly List<Point> v9SmokeFg = new(), v9SmokeBg = new();
    private readonly List<(double Field, double Draw, double Frame)> v9SmokeTimes = new();
    private (double Field, double Draw) v9SmokeActionTimes; // the step's first frame: its edit, torches or teleport
    private readonly List<string> v9SmokeChecks = new();
    private readonly List<object> v9SmokeResults = new();
    private readonly Dictionary<string, (string Hash, int Torches)> v9SmokeStates = new();
    private string v9SmokeSavesBefore;
    private float v9SmokeClockStart;

    private void InitializeV9Smoke()
    {
        Directory.CreateDirectory(V9Gameplay.SmokeOutput);
        v9SmokeSavesBefore = V9SmokeSavesListing();
        v9SmokeClockStart = session.TimeOfDay01;
        session.Player.SetDebugFly(true); // scripted poses without gravity; the lighting never reads it
        v9SmokeStand = session.Player.Position;
        v9SmokeWorldTorches = session.TorchRuntimeSystem.Torches.Select(t => new TorchSaveData
            { PositionX = t.Position.X, PositionY = t.Position.Y, PoleFrameIndex = t.PoleFrameIndex, FacingLeft = t.FacingLeft }).ToList();
        V9SmokeCheck($"state built without lighting flags draws {ActiveLightingLabel} (V9Gameplay.Enabled={V9Gameplay.Enabled}, probe={V9ProbeOptions.Enabled}, V8={v8Active})",
            ActiveLightingLabel == "V9" && v9Active && !V9ProbeOptions.Enabled && !v8Active);
        V9SmokeCheck($"shipped configuration: {v9Field.NaturalModel}, glow {v9Field.GlowSettings}, FG sky {v9Field.ForegroundSky}",
            v9Field.NaturalModel == V9Gameplay.NaturalModel && v9Field.GlowSettings == V9Gameplay.Glow && v9Field.ForegroundSky);

        // A Shallow cave floor beside background openings and a Cavern floor, nearest to the spawn (world data only).
        var map = session.WorldMap;
        var shallow = v9Field.ShallowLayer;
        var cavern = session.LayerDefinitions.First(l => l.LayerType == WorldLayerType.Cavern);
        int spawnColumn = (int)MathF.Floor(v9SmokeStand.X / 8);
        bool Hole(int x, int y) => V9Air(map, x, y) && map.GetBackgroundTile(x, y) == TileType.Empty;
        for (int radius = 0; radius <= 600 && !(v9SmokeShallowFound && v9SmokeCavernFound); radius++)
            foreach (int x in radius == 0 ? new[] { spawnColumn } : new[] { spawnColumn + radius, spawnColumn - radius })
            {
                for (int y = shallow.StartY + 8; y <= shallow.EndY - 24 && !v9SmokeShallowFound; y++)
                {
                    if (!V9CanStand(map, x, y)) continue;
                    int holes = 0;
                    for (int dy = -6; dy <= 0; dy++)
                        for (int dx = -6; dx <= 6; dx++) if (Hole(x + dx, y + dy)) holes++;
                    if (holes >= 12) { v9SmokeShallowFloor = new Point(x, y + 1); v9SmokeShallowFound = true; }
                }
                for (int y = cavern.StartY + 12; y <= cavern.StartY + 80 && !v9SmokeCavernFound; y++)
                    if (V9CanStand(map, x, y)) { v9SmokeCavernFloor = new Point(x, y + 1); v9SmokeCavernFound = true; }
            }
        Console.WriteLine($"[V9 smoke] spawn {v9SmokeStand}; Shallow floor {(v9SmokeShallowFound ? v9SmokeShallowFloor.ToString() : "none")}; Cavern floor {(v9SmokeCavernFound ? v9SmokeCavernFloor.ToString() : "none")}");
    }

    private void PrepareV9SmokeFrame()
    {
        var map = session.WorldMap;
        string name = V9SmokeSteps[v9SmokeStep].Name;
        int column = (int)MathF.Floor(v9SmokeStand.X / 8), floor = V9SmokeFloorRow(column);
        if (v9SmokeFrame == 0)
        {
            Console.WriteLine("[V9 smoke] " + name);
            switch (name)
            {
                case "torches-3": RestoreV9SmokeTorches(new Point(column - 6, V9SmokeFloorRow(column - 6) - 1), new Point(column + 5, V9SmokeFloorRow(column + 5) - 1), new Point(column + 11, V9SmokeFloorRow(column + 11) - 1)); break;
                case "torches-1": RestoreV9SmokeTorches(new Point(column - 6, V9SmokeFloorRow(column - 6) - 1)); break;
                case "fg-placed":
                    // A 2x3 stone pillar beside the player, bottom-up so each block has a solid neighbour (gameplay rule).
                    for (int y = floor - 1; y >= floor - 3; y--)
                        for (int x = column + 2; x <= column + 3; x++)
                            if (map.TryPlaceTile(x, y, TileType.Stone)) v9SmokeFg.Add(new Point(x, y));
                    break;
                case "bg-placed":
                    // A dirt background wall behind open air over the ground, left of the player.
                    for (int y = floor - 6; y <= floor - 2; y++)
                        for (int x = column - 10; x <= column - 4; x++)
                            if (V9Air(map, x, y) && map.TryPlaceBackgroundTile(x, y, TileType.Dirt)) v9SmokeBg.Add(new Point(x, y));
                    break;
                case "edits-removed":
                    foreach (Point p in v9SmokeFg) map.TryBreakTile(p.X, p.Y, out _);
                    foreach (Point p in v9SmokeBg) map.TryBreakBackgroundTile(p.X, p.Y, out _);
                    break;
                case "shallow": if (v9SmokeShallowFound) v9SmokeStand = V9Stand(v9SmokeShallowFloor); break;
                case "shallow-torch":
                    var shallowTorch = new[] { 3, -3, 5, -5, 2, -2 }.Select(d => new Point(v9SmokeShallowFloor.X + d, v9SmokeShallowFloor.Y - 1))
                        .FirstOrDefault(p => V9Air(map, p.X, p.Y) && !V9Air(map, p.X, p.Y + 1), new Point(v9SmokeShallowFloor.X, v9SmokeShallowFloor.Y - 1));
                    RestoreV9SmokeTorches(shallowTorch);
                    break;
                case "cavern":
                    RestoreV9SmokeTorches();
                    if (v9SmokeCavernFound) v9SmokeStand = V9Stand(v9SmokeCavernFloor);
                    break;
            }
        }
        if (name == "surface-walk") { v9SmokeStand.X += 3; v9SmokeStand.Y = V9SmokeFloorRow((int)MathF.Floor(v9SmokeStand.X / 8)) * 8 - 1; }
        session.Player.TeleportTo(v9SmokeStand);
    }

    private void CaptureV9SmokeFrame()
    {
        var (name, frames) = V9SmokeSteps[v9SmokeStep];
        if (v9SmokeFrame != frames - 1) return;
        var map = session.WorldMap;
        long start = Stopwatch.GetTimestamp();
        var v = v9Field.Verify(map, sourceStride: 7); // also fills NaturalEnergy / ArtificialEnergy
        double verifyMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        V9SmokeCheck($"{name}: field equals a from-scratch evaluation over world solidity (field {v.FieldBitsDifferent}, natural {v.NaturalBitsDifferent}, torches {v.ArtificialBitsDifferent}, glow {v.GlowBitsDifferent} differing float channels; torch squares {v.SourceBitsDifferent}, {v.OutsideBoxNonZero} outside; occupancy {v.MaskMismatches} mismatches)",
            v.FieldBitsDifferent == 0 && v.NaturalBitsDifferent == 0 && v.ArtificialBitsDifferent == 0 && v.GlowBitsDifferent == 0 && v.SourceBitsDifferent == 0 && v.OutsideBoxNonZero == 0 && v.MaskMismatches == 0);
        string hash = V9Hash(v9Field.Energy);
        int torches = v9Field.Last.Torches;
        int naturalLit = v9Field.NaturalEnergy.Count(e => e != Vector3.Zero), torchLit = v9Field.ArtificialEnergy.Count(e => e != Vector3.Zero);
        Vector3 head = V9NaturalAt(session.Player.Position - new Vector2(0, 16)) ?? Vector3.Zero;
        switch (name)
        {
            case "surface": V9SmokeCheck($"open sky lights the Surface: {naturalLit} texels with natural light; at the player's head {head}", naturalLit > 0 && head.Z > 0); break;
            case "surface-walk": V9SmokeCheck($"camera movement recentred the window {v9Field.Recenters} times", v9Field.Recenters > 0); break;
            case "torches-3": V9SmokeCheck($"three torches added: {torches} torches reach the window, {torchLit} texels lit by them", torches >= 3 && torchLit > 0); break;
            case "torches-1": V9SmokeCheck($"two torches removed: {v9SmokeStates["torches-3"].Torches} -> {torches}", torches == v9SmokeStates["torches-3"].Torches - 2); break;
            case "fg-placed": V9SmokeCheck($"FG placed ({v9SmokeFg.Count} stone tiles) changes the field", v9SmokeFg.Count > 0 && hash != v9SmokeStates["torches-1"].Hash); break;
            case "bg-placed":
            {
                // Every texel of the new wall had direct Surface sky (open sky x weight 1); now at most the weaker glow.
                int behind = 0, stillDirect = 0;
                float direct = V9LabSettings.SkyLinearRgb.Z * V9ProbeField.OpenSky;
                foreach (Point p in v9SmokeBg)
                    for (int dy = 0; dy < 8; dy++)
                        for (int dx = 0; dx < 8; dx++)
                            if (V9NaturalAt(new Vector2(p.X * 8 + dx, p.Y * 8 + dy)) is Vector3 e) { behind++; if (e.Z >= direct) stillDirect++; }
                V9SmokeCheck($"BG placed ({v9SmokeBg.Count} tiles) hides direct sky: {stillDirect} of {behind} texels behind it keep open-sky light (limit {direct:0.###})",
                    v9SmokeBg.Count > 0 && behind > 0 && stillDirect == 0 && hash != v9SmokeStates["fg-placed"].Hash);
                break;
            }
            case "edits-removed": V9SmokeCheck("FG and BG removed: the field returns bit for bit to the state before the edits", hash == v9SmokeStates["torches-1"].Hash); break;
            case "shallow":
            {
                int skyTiles = 0, fgSky = v9Field.BackplaneForegroundGlow.Count(g => g > 0) + v9Field.ForegroundSkyDiagnostic().Affected;
                Rectangle b = v9Field.Bounds;
                for (int y = V9ProbeSky.FloorDiv(b.Top, 8); y < V9ProbeSky.FloorDiv(b.Bottom, 8); y++)
                    for (int x = V9ProbeSky.FloorDiv(b.Left, 8); x < V9ProbeSky.FloorDiv(b.Right, 8); x++) if (v9Field.SkyVisibleAt(x, y) > 0) skyTiles++;
                V9SmokeCheck($"Shallow ({session.GetPlayerWorldLayer()}, floor {v9SmokeShallowFloor}): sky tiles {skyTiles}, glowing texels {v9Field.Last.SkyGlowPixels}, FG texels lit by sky/glow {fgSky}, zoom {session.Camera.Zoom:0.##}",
                    v9SmokeShallowFound && skyTiles > 0 && v9Field.Last.SkyGlowPixels > 0 && fgSky > 0);
                break;
            }
            case "shallow-torch": V9SmokeCheck($"torch in the Shallow: {torches} torches reach the window, {torchLit} texels lit", torches >= 1 && torchLit > 0); break;
            case "cavern":
            {
                int cavernStart = v9Field.ShallowLayer.EndY + 1, deep = 0;
                for (int y = 0, i = 0; y < v9Field.Height; y++)
                    for (int x = 0; x < v9Field.Width; x++, i++)
                        if (v9Field.Origin.Y + y >= (cavernStart + 8) * 8 && v9Field.NaturalEnergy[i] != Vector3.Zero) deep++;
                V9SmokeCheck($"Cavern ({session.GetPlayerWorldLayer()}, floor {v9SmokeCavernFloor}): {deep} texels with natural light 8+ tiles below the Cavern start",
                    v9SmokeCavernFound && session.GetPlayerWorldLayer() == WorldLayerType.Cavern && deep == 0);
                break;
            }
        }
        v9SmokeStates[name] = (hash, torches);
        string prefix = Path.Combine(V9Gameplay.SmokeOutput, $"{v9SmokeStep:00}-{name}");
        using (var stream = File.Create(prefix + "_final.png")) v9Final.SaveAsPng(stream, v9Final.Width, v9Final.Height);
        var times = v9SmokeTimes.ToList();
        v9SmokeTimes.Clear();
        v9SmokeResults.Add(new
        {
            name, frames, player = new { x = session.Player.Position.X, y = session.Player.Position.Y, layer = session.GetPlayerWorldLayer().ToString() },
            zoom = session.Camera.Zoom, timeOfDay = session.TimeOfDay01,
            field = new { origin = new { x = v9Field.Origin.X, y = v9Field.Origin.Y }, v9Field.Width, v9Field.Height, v9Field.Recenters, torches, naturalLit, torchLit },
            hashes = new { energy = hash, natural = V9Hash(v9Field.NaturalEnergy), artificial = V9Hash(v9Field.ArtificialEnergy) },
            verify = new { v.FieldBitsDifferent, v.NaturalBitsDifferent, v.ArtificialBitsDifferent, v.GlowBitsDifferent, v.SourceBitsDifferent, v.OutsideBoxNonZero, v.MaskMismatches, verifyMs },
            cpuMs = new { samples = times.Count, field = Stats(times.Select(t => t.Field)), draw = Stats(times.Select(t => t.Draw)), frameInterval = Stats(times.Select(t => t.Frame)),
                actionFrame = new { field = Math.Round(v9SmokeActionTimes.Field, 3), draw = Math.Round(v9SmokeActionTimes.Draw, 3) } }
        });

        static object Stats(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(x => x).ToArray();
            if (sorted.Length == 0) return null;
            return new { median = Math.Round(sorted[sorted.Length / 2], 3), p95 = Math.Round(sorted[(int)(sorted.Length * .95)], 3), max = Math.Round(sorted[^1], 3) };
        }
    }

    private void FinishV9SmokeFrame(float frameSeconds)
    {
        int frames = V9SmokeSteps[v9SmokeStep].Frames;
        if (v9SmokeFrame == 0) v9SmokeActionTimes = (v9FieldMs, v9DrawMs);
        if (v9SmokeFrame >= V9SmokeSettle && v9SmokeFrame < frames - 1) v9SmokeTimes.Add((v9FieldMs, v9DrawMs, frameSeconds * 1000));
        if (++v9SmokeFrame < frames) return;
        v9SmokeFrame = 0;
        if (++v9SmokeStep < V9SmokeSteps.Length) return;
        V9SmokeCheck($"world clock ran (time of day {v9SmokeClockStart:0.0000} -> {session.TimeOfDay01:0.0000})", session.TimeOfDay01 != v9SmokeClockStart);
        V9SmokeCheck("no user save written (LocalAppData\\Nyvorn Worlds/Players listing unchanged)", V9SmokeSavesListing() == v9SmokeSavesBefore);
        File.WriteAllText(Path.Combine(V9Gameplay.SmokeOutput, "smoke.json"), JsonSerializer.Serialize(new
        {
            run = "V9 gameplay smoke: default V9 path, transient world V8-GAMEPLAY-2026 Small, nothing saved",
            resolution = new { width = v9Final.Width, height = v9Final.Height }, model = v9Field.NaturalModel.ToString(),
            note = "CPU milliseconds (Stopwatch; GPU time not measured). field = V9 field update; draw = field + scene submission; frameInterval = time between draws (VSync unless --uncapped-frames). Capture frames and the settle frames after each action are excluded; actionFrame is the step's first frame (its edit, torches or teleport).",
            steps = v9SmokeResults
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.Combine(V9Gameplay.SmokeOutput, "checks.txt"), v9SmokeChecks.Append(
            $"TOTAL: {v9SmokeChecks.Count(c => c.StartsWith("PASS"))} passed; {v9SmokeChecks.Count(c => c.StartsWith("FAIL"))} failed."));
        if (v9SmokeChecks.Any(c => c.StartsWith("FAIL"))) Environment.ExitCode = 1;
        V9Gameplay.Finished = true;
    }

    private void V9SmokeCheck(string label, bool passed)
    {
        v9SmokeChecks.Add($"{(passed ? "PASS" : "FAIL")} {label}");
        Console.WriteLine($"[V9 smoke] {(passed ? "PASS" : "FAIL")} {label}");
    }

    private int V9SmokeFloorRow(int column)
    {
        int y = 0;
        while (y < session.WorldMap.Height && V9Air(session.WorldMap, column, y)) y++;
        return y;
    }

    // The world's torches plus the smoke's own, through the same restore path as a save load.
    private void RestoreV9SmokeTorches(params Point[] tiles) =>
        session.TorchRuntimeSystem.Restore(v9SmokeWorldTorches.Concat(tiles.Select(t =>
        {
            var torch = new TorchInstance(new Point(session.WorldMap.WrapTileX(t.X), t.Y), 0, session.WorldMap.TileSize);
            return new TorchSaveData { PositionX = torch.Position.X, PositionY = torch.Position.Y, PoleFrameIndex = 0 };
        })).ToList());

    private static string V9SmokeSavesListing()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nyvorn");
        return string.Join("\n", new[] { "Worlds", "Players" }.Select(d => Path.Combine(root, d)).Where(Directory.Exists)
            .SelectMany(Directory.EnumerateFiles).OrderBy(f => f).Select(f => $"{f}|{new FileInfo(f).Length}|{File.GetLastWriteTimeUtc(f):O}"));
    }
}
