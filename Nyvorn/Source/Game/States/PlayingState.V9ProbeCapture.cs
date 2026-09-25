using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;
using Nyvorn.Source.Engine.Graphics.LightingV9Probe;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Persistence;

namespace Nyvorn.Source.Game.States;

/// <summary>V9 probe scenes found procedurally in the transient world, scripted evidence states and the bench.</summary>
public partial class PlayingState
{
    private Vector2 v9Spawn;
    private bool v9EntranceFound, v9CaveFound, v9LateralFound;
    private Point v9EntranceMouth, v9EntranceRim, v9CaveFloor, v9BlockTile, v9SeamFloor, v9SeamTorch, v9LateralOpening, v9LateralInside;
    private readonly List<Point> v9EntranceStands = new(), v9CaveTorches = new(), v9CavePocket = new(), v9EntranceSeal = new(), v9EntrancePorch = new();
    private readonly List<(Point Cell, int Distance)> v9EntrancePath = new(), v9LateralPath = new();
    private string v9SceneReport = string.Empty;

    private static bool V9Air(WorldMap map, int x, int y) => !V9ProbeOccupancy.WorldSolid(map, x, y);
    /// <summary>Standing position on a floor tile (the solid tile under the feet): Player.Position is hurtbox centre-x, bottom-y.</summary>
    private static Vector2 V9Stand(Point floor) => new(floor.X * 8 + 4, floor.Y * 8 - 1);
    // Floor under the cell and three tiles of headroom in its column (the 13x23 hurtbox is centred on the column).
    private static bool V9CanStand(WorldMap map, int x, int y) => !V9Air(map, x, y + 1) &&
        V9Air(map, x, y) && V9Air(map, x, y - 1) && V9Air(map, x, y - 2);
    private const float V9ScriptedZoom = 2f; // the gameplay's default camera zoom, pinned for reproducible scripted frames
    private static Point[] V9Neighbours(Point p) =>
        new[] { new Point(p.X + 1, p.Y), new Point(p.X - 1, p.Y), new Point(p.X, p.Y + 1), new Point(p.X, p.Y - 1) };

    private void FindV9ProbeScenes()
    {
        var map = session.WorldMap;
        int width = map.Width;
        v9Spawn = session.Player.Position;
        int spawnColumn = (int)MathF.Floor(v9Spawn.X / 8);
        var floor = new int[width];
        for (int c = 0; c < width; c++) { int y = 0; while (y < map.Height && V9Air(map, c, y)) y++; floor[c] = y; }
        int[] envelope = v9Field.Envelope(map, 0, width);
        int Wrap(int c) => map.WrapTileX(c);
        // Exposure from world data (the same rule as the field's mask), for scene finding only.
        bool Exposed(int c, int y) => V9ProbeField.ExposedInWorld(map, c, y, envelope[Wrap(c)]);
        bool Unexposed(int c, int y) => V9Air(map, c, y) && !Exposed(c, y);

        // Entrance: the deepest drop of the sky floor below the exterior envelope (Small has one natural entrance).
        int mouth = -1, depth = 0;
        for (int c = 0; c < width; c++) if (floor[c] - envelope[c] > depth) { depth = floor[c] - envelope[c]; mouth = c; }
        if (mouth >= 0 && depth >= 4)
        {
            v9EntranceMouth = new Point(mouth, envelope[mouth]);
            for (int d = 1; d < 24 && !v9EntranceFound; d++)
                foreach (int c in new[] { mouth - d, mouth + d })
                    if (!v9EntranceFound && floor[Wrap(c)] == envelope[Wrap(c)] && V9CanStand(map, c, floor[Wrap(c)] - 1))
                    { v9EntranceRim = new Point(c, floor[Wrap(c)]); v9EntranceFound = true; }
            // Air path from the mouth, inside the ground (below the envelope), by 4-connected distance.
            var start = new Point(mouth, envelope[mouth]);
            var distance = new Dictionary<Point, int> { [start] = 0 };
            var queue = new Queue<Point>(); queue.Enqueue(start);
            while (queue.Count > 0 && distance.Count < 30000)
            {
                Point p = queue.Dequeue();
                foreach (Point n in V9Neighbours(p))
                {
                    if (n.Y < envelope[Wrap(n.X)] || Math.Abs(n.X - mouth) > 60 || n.Y > start.Y + 90 || distance.ContainsKey(n) || !V9Air(map, n.X, n.Y)) continue;
                    distance[n] = distance[p] + 1; queue.Enqueue(n);
                }
            }
            foreach (var pair in distance) v9EntrancePath.Add((pair.Key, pair.Value));
            v9EntrancePath.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            // Seal: the air run across the mouth at the rim row, bounded by solid on both sides (the lab's closure).
            int left = mouth, right = mouth;
            while (V9Air(map, left - 1, envelope[mouth]) && mouth - left < 40) left--;
            while (V9Air(map, right + 1, envelope[mouth]) && right - mouth < 40) right++;
            if (!V9Air(map, left - 1, envelope[mouth]) && !V9Air(map, right + 1, envelope[mouth]))
                for (int x = left; x <= right; x++) v9EntranceSeal.Add(new Point(x, envelope[mouth]));
            // Background porch: every exposed cell of the open air above the mouth, 6 columns beyond each side and 6 rows
            // up (a background-walled porch built over the entrance; the foreground stays open).
            for (int y = envelope[mouth] - 6; y < envelope[mouth]; y++)
                for (int x = left - 6; x <= right + 6; x++)
                    if (Exposed(x, y)) v9EntrancePorch.Add(new Point(x, y));
            // Inside the tunnel: a floor cell near the target path distance, else the nearest path cell (the scripted
            // pose flies, so the player may float in the tunnel air).
            foreach (int target in new[] { 12, 26 })
            {
                var near = v9EntrancePath.Where(p => Math.Abs(p.Distance - target) <= 8).OrderBy(p => Math.Abs(p.Distance - target)).ToList();
                var pick = near.Where(p => V9CanStand(map, p.Cell.X, p.Cell.Y)).Concat(near).Select(p => (Point?)new Point(p.Cell.X, p.Cell.Y + 1)).FirstOrDefault();
                if (pick is Point s) v9EntranceStands.Add(s);
            }
        }

        // Closed cave: the shallowest 4-connected air pocket under the exterior envelope near the spawn whose fill never
        // touches the exterior and stays bounded (no geometric communication with the outside).
        var visited = new HashSet<Point>();
        for (int below = 10; below <= 140 && !v9CaveFound; below++)
            for (int radius = 0; radius <= 400 && !v9CaveFound; radius += 2)
                foreach (int column in new[] { spawnColumn + radius, spawnColumn - radius })
                    if (!v9CaveFound) TryClosedPocket(new Point(column, envelope[Wrap(column)] + below));

        void TryClosedPocket(Point seed)
        {
            if (!V9Air(map, seed.X, seed.Y) || !visited.Add(seed)) return;
            var pocket = new List<Point>();
            var queue = new Queue<Point>(); queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                Point p = queue.Dequeue(); pocket.Add(p);
                if (p.Y <= envelope[Wrap(p.X)] || pocket.Count > 20000) return; // reaches the exterior, or unbounded
                foreach (Point n in V9Neighbours(p))
                    if (V9Air(map, n.X, n.Y) && visited.Add(n)) queue.Enqueue(n);
            }
            if (pocket.Count < 60) return;
            var set = new HashSet<Point>(pocket);
            int cx = (int)pocket.Average(p => p.X), cy = (int)pocket.Average(p => p.Y);
            var stands = pocket.Where(p => V9CanStand(map, p.X, p.Y)).OrderBy(p => Math.Abs(p.X - cx) + Math.Abs(p.Y - cy)).ToList();
            if (stands.Count == 0) return;
            Point player = stands[0];
            var torches = pocket.Where(p => !V9Air(map, p.X, p.Y + 1) && Math.Abs(p.X - player.X) >= 2)
                .OrderBy(p => Math.Abs(p.X - player.X) + Math.Abs(p.Y - player.Y)).Take(5).ToList();
            if (torches.Count < 5) return;
            Point block = new(torches[0].X + Math.Sign(player.X - torches[0].X), torches[0].Y);
            if (!set.Contains(block) || Math.Abs(block.X - player.X) < 2 || torches.Contains(block)) return;
            v9CavePocket.AddRange(pocket); v9CaveTorches.AddRange(torches);
            v9CaveFloor = new Point(player.X, player.Y + 1); v9BlockTile = block; v9CaveFound = true;
        }

        // Lateral opening: an exposed cell whose left or right neighbour is unexposed air lying under ground of its own
        // column (light enters sideways through a face, not from above), opening onto at least 30 cells of unexposed air
        // that are not the entrance tunnel. Nearest to the spawn first.
        var entranceCells = new HashSet<Point>(v9EntrancePath.Select(p => new Point(Wrap(p.Cell.X), p.Cell.Y)));
        for (int radius = 0; radius <= width / 2 && !v9LateralFound; radius++)
            foreach (int column in radius == 0 ? new[] { spawnColumn } : new[] { spawnColumn + radius, spawnColumn - radius })
            {
                int e = envelope[Wrap(column)];
                for (int y = Math.Max(1, e - 40); y < e && !v9LateralFound; y++)
                {
                    if (!Exposed(column, y)) continue;
                    foreach (int d in new[] { 1, -1 })
                    {
                        int nx = column + d;
                        if (v9LateralFound || floor[Wrap(nx)] >= y || !Unexposed(nx, y)) continue;
                        var region = new Dictionary<Point, int> { [new Point(nx, y)] = 0 };
                        var queue = new Queue<Point>(); queue.Enqueue(new Point(nx, y));
                        bool tunnel = false;
                        while (queue.Count > 0 && region.Count < 4000)
                        {
                            Point p = queue.Dequeue();
                            if (entranceCells.Contains(new Point(Wrap(p.X), p.Y))) tunnel = true;
                            foreach (Point n in V9Neighbours(p))
                                if (!region.ContainsKey(n) && Unexposed(n.X, n.Y)) { region[n] = region[p] + 1; queue.Enqueue(n); }
                        }
                        if (tunnel || region.Count < 30) continue;
                        v9LateralOpening = new Point(column, y); v9LateralInside = new Point(nx, y); v9LateralFound = true;
                        foreach (var pair in region) v9LateralPath.Add((pair.Key, pair.Value));
                        v9LateralPath.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                    }
                }
            }

        // Built shelter (transient construction, like the entrance seal): on flat open ground left of the spawn, a stone
        // roof 5 rows above the floor over 14 columns and a back wall; the interior (13x4 air cells) is open on its left
        // side only: a lateral opening. Sealing it closes that side (a room under open sky, sealed).
        for (int c = spawnColumn - 6; c > spawnColumn - 400 && !v9ShelterFound; c--)
        {
            int f = floor[Wrap(c)];
            bool flat = true;
            for (int k = -2; k <= 14 && flat; k++)
                flat = floor[Wrap(c + k)] == f && Exposed(c + k, f - 7);
            if (!flat) continue;
            v9ShelterOrigin = new Point(c, f); v9ShelterFound = true;
        }

        // World seam: stand on the surface at column 2; a torch on the surface three columns before the seam.
        v9SeamFloor = new Point(2, floor[2]);
        v9SeamTorch = new Point(width - 3, floor[width - 3] - 1);

        // Sky Background Glow scenes (PlayingState.V9ProbeShallowGlow.cs).
        FindV9ShallowGlowScenes(spawnColumn);

        v9SceneReport =$"Scenes: spawn=({v9Spawn.X:0},{v9Spawn.Y:0}) column {spawnColumn}; seam floor {v9SeamFloor}, seam torch {v9SeamTorch}; " +
            (v9EntranceFound ? $"entrance mouth column {mouth} rim row {envelope[mouth]} sky floor {floor[mouth]} (depth {depth} tiles below the envelope), rim stand {v9EntranceRim}, path cells {v9EntrancePath.Count}, inside stands {string.Join(" ", v9EntranceStands)}, seal {v9EntranceSeal.Count} cells, background porch {v9EntrancePorch.Count} exposed cells; " : $"no entrance found (max depth {depth}); ") +
            (v9CaveFound ? $"closed cave pocket {v9CavePocket.Count} cells near ({v9CaveFloor.X},{v9CaveFloor.Y}), top {v9CavePocket.Min(p => p.Y - envelope[Wrap(p.X)])} tiles below the envelope, torches {string.Join(" ", v9CaveTorches)}, block {v9BlockTile}; " : "no closed cave found; ") +
            (v9LateralFound ? $"lateral opening: exposed cell {v9LateralOpening} beside unexposed air {v9LateralInside} (column sky floor {floor[Wrap(v9LateralInside.X)]}, envelope {envelope[Wrap(v9LateralInside.X)]}), {v9LateralPath.Count} unexposed air cells behind it (BFS cap 4000); " : "no lateral opening found in the whole world; ") +
            (v9ShelterFound ? $"built shelter on flat ground at column {v9ShelterOrigin.X}, floor row {v9ShelterOrigin.Y} (interior columns {v9ShelterOrigin.X}..{v9ShelterOrigin.X + 12}, rows {v9ShelterOrigin.Y - 4}..{v9ShelterOrigin.Y - 1}); " : "no flat ground for the built shelter; ") +
            v9ShallowReport;
    }

    // Built shelter geometry: roof row floor-5 over columns x..x+13, back wall column x+13, interior x..x+12 x floor-4..floor-1;
    // the seal closes column x (the open side).
    private bool v9ShelterFound;
    private Point v9ShelterOrigin;
    private IEnumerable<Point> V9ShelterShell()
    {
        int x = v9ShelterOrigin.X, f = v9ShelterOrigin.Y;
        for (int k = 0; k <= 13; k++) yield return new Point(x + k, f - 5);
        for (int r = f - 4; r <= f - 1; r++) yield return new Point(x + 13, r);
    }
    private IEnumerable<Point> V9ShelterSeal() { for (int r = v9ShelterOrigin.Y - 4; r <= v9ShelterOrigin.Y - 1; r++) yield return new Point(v9ShelterOrigin.X, r); }
    private IEnumerable<Point> V9ShelterInterior(int fromColumn)
    {
        for (int k = fromColumn; k <= 12; k++)
            for (int r = v9ShelterOrigin.Y - 4; r <= v9ShelterOrigin.Y - 1; r++) yield return new Point(v9ShelterOrigin.X + k, r);
    }

    private void RestoreV9Torches(IEnumerable<Point> tiles) =>
        session.TorchRuntimeSystem.Restore(tiles.Select(t =>
        {
            var torch = new TorchInstance(new Point(session.WorldMap.WrapTileX(t.X), t.Y), 0, session.WorldMap.TileSize);
            return new TorchSaveData { PositionX = torch.Position.X, PositionY = torch.Position.Y, PoleFrameIndex = 0 };
        }).ToList());

    private Vector3? V9EnergyAt(Vector2 world) => V9BufferAt(v9Field.Energy, world);
    private Vector3? V9NaturalAt(Vector2 world) => V9BufferAt(v9Field.NaturalEnergy, world);
    private Vector3? V9BufferAt(Vector3[] buffer, Vector2 world)
    {
        int x = (int)MathF.Floor(world.X) - v9Field.Origin.X, y = (int)MathF.Floor(world.Y) - v9Field.Origin.Y;
        return x < 0 || y < 0 || x >= v9Field.Width || y >= v9Field.Height ? null : buffer[y * v9Field.Width + x];
    }
    private static float Sum(Vector3? e) => e is Vector3 v ? v.X + v.Y + v.Z : -1;

    // ---------------------------------------------------------------- scripted evidence states (--v9-probe-capture)
    // The first 14 states are the session-3 set, in the same order (the legacy model must reproduce its captures byte for
    // byte); the natural-light states are appended after them.
    private string[] v9States = { "surface", "entrance-rim", "entrance-inside", "entrance-deep", "entrance-sealed",
        "entrance-reopened", "cave-dark", "cave-torch-1", "cave-torch-2", "cave-torch-5", "cave-block-placed", "cave-block-removed",
        "wrap-seam", "wrap-seam-shifted",
        "entrance-recentre-a", "entrance-recentre-b", "entrance-recentre-c", "entrance-bg-closed", "entrance-bg-reopened", "lateral-opening",
        "shelter-open", "shelter-sealed",
        // Sky Background Glow (appended; the states above keep their order).
        "shallow-fissure-noglow", "shallow-fissure", "shallow-fissure-reach-24", "shallow-fissure-reach-64",
        "shallow-fissure-peak-2", "shallow-fissure-peak-4",
        "shallow-fissure-sun-morning", "shallow-fissure-sun-afternoon", "shallow-bottom",
        "shallow-room-closed", "shallow-room-torch-5", "shallow-room-small-hole", "shallow-room-wide-hole",
        // Sky cover: a background hole in a Cavern cave stays covered (only Space/Surface and Shallow openings show the sky).
        "cavern-bg-hole",
        // Foreground response to visible sky: openings touching the sealed room's wall (narrow) and its wall and ceiling (wide).
        "shallow-room-edge-small", "shallow-room-edge-wide",
        "shallow-room-offset-open", "shallow-room-offset-blocked", "shallow-room-offset-closed", "shallow-room-offset-reopened" };
    private Vector3[] v9SeamEnergy;
    private Point v9SeamOrigin;
    // Snapshots compared in world coordinates: the window origin depends on camera history (recentre hysteresis).
    private (Vector3[] Energy, Rectangle Bounds) v9EntranceInsideEnergy, v9RecentreEnergy;
    private Dictionary<Point, float> v9EntranceInsideNatural;
    private const int V9CaptureFrame = 6;
    private int v9StateIndex, v9StateFrames;
    private bool v9StatePrepared;
    private Vector2 v9StateStand, v9StateCenter;
    private V9ProbeFrameStats v9StateFirst;
    private double v9StateFirstFieldMs;
    private (Vector3[] Energy, Rectangle Bounds) v9TorchOneEnergy;
    private readonly List<string> v9Checks = new();
    private string V9StateLabel => v9States[Math.Min(v9StateIndex, v9States.Length - 1)];
    private bool ShouldCaptureV9State => V9ProbeOptions.Capture && v9StateFrames == V9CaptureFrame;
    private bool V9Exposure => v9Field.NaturalModel == V9NaturalModel.Exposure;

    private void InitializeV9ProbeCapture()
    {
        // Sun and artistic parameter sweeps belong to the previous experiment, not this architecture comparison.
        if (V9ProbeOptions.SkyBackplane) v9States = v9States.Where(n => !n.Contains("-sun-") && !n.Contains("-peak-") && !n.Contains("-reach-")).ToArray();
        session.Player.SetDebugFly(true); // scripted poses: no gravity while the camera and player are placed
        File.WriteAllText(Path.Combine(V9ProbeOptions.Output, "run.txt"),
            $"V9 gameplay probe capture. Seed=V8-GAMEPLAY-2026; Small; {session.WorldMap.Width}x{session.WorldMap.Height} tiles; transient session, nothing saved.\n" +
            $"Resolution {graphicsDevice.PresentationParameters.BackBufferWidth}x{graphicsDevice.PresentationParameters.BackBufferHeight}; zoom {session.Camera.Zoom}; noon fixed.\n" +
            $"Field margins {V9ProbeField.MarginX}x{V9ProbeField.MarginY} px; sky spacing {V9ProbeSky.Spacing} px, height {V9ProbeSky.HeightAbove} px, envelope window {2 * V9ProbeSky.HalfWindow + 1} columns.\n" +
            $"Natural model: {v9Field.NaturalModel}" + (v9Field.IsBackplane ? $" (FG empty + BG empty; continuous depth rows {v9Field.SurfaceStartRow}..{v9Field.ShallowLayer.EndY + 1}; no point entries, no sun)" : V9Exposure ? $" (open sky {V9ProbeField.OpenSky:R} x sky RGB; opening power {V9ProbeField.EntryPower:R} per cell, radius {V9LabSettings.SkyRadiusPixels})" : " (legacy sky point samples, diagnostic)") + ".\n" +
            v9SceneReport + "\n" + string.Join("\n", session.LayerDefinitions.Select(l => $"{l.LayerType}: {l.StartY}..{l.EndY}")) + "\n");
    }

    private void PrepareV9ProbeState(int width, int height)
    {
        string name = V9StateLabel;
        if (!v9StatePrepared)
        {
            v9StatePrepared = true;
            bool cave = name.StartsWith("cave"), entrance = name.StartsWith("entrance");
            if ((cave && !v9CaveFound) || (entrance && (!v9EntranceFound || (name != "entrance-rim" && v9EntranceStands.Count < (name == "entrance-deep" ? 2 : 1)))) ||
                (name is "entrance-sealed" or "entrance-reopened" && v9EntranceSeal.Count == 0) ||
                (name is "entrance-bg-closed" or "entrance-bg-reopened" && v9EntrancePorch.Count == 0) ||
                (name == "lateral-opening" && !v9LateralFound) || (name.StartsWith("shelter") && !v9ShelterFound) ||
                (V9IsGlowState(name) && !V9ShallowSceneAvailable(name)))
            {
                v9Checks.Add($"SKIP {name}: scene not found procedurally ({v9SceneReport})");
                v9StateSkipped = true;
                return;
            }
            v9StateSkipped = false;
            var map = session.WorldMap;
            Vector2 mouthCentre = new(v9EntranceMouth.X * 8 + 4, v9EntranceMouth.Y * 8 + 40);
            switch (name)
            {
                case "surface": v9StateStand = v9Spawn; v9StateCenter = v9Spawn - new Vector2(0, 12); break;
                case "entrance-rim":
                    v9StateStand = V9Stand(v9EntranceRim);
                    v9StateCenter = mouthCentre; break;
                case "entrance-inside" or "entrance-sealed" or "entrance-reopened" or "entrance-bg-closed" or "entrance-bg-reopened":
                    v9StateStand = V9Stand(v9EntranceStands[0]); v9StateCenter = v9StateStand - new Vector2(0, 12); break;
                case "entrance-deep": v9StateStand = V9Stand(v9EntranceStands[1]); v9StateCenter = v9StateStand - new Vector2(0, 12); break;
                // Recentre sequence across the mouth: each step moves the camera 240 px (> the 128 px margin).
                case "entrance-recentre-a" or "entrance-recentre-b" or "entrance-recentre-c":
                    v9StateCenter = mouthCentre + new Vector2(240 * (name[^1] - 'b'), 0); v9StateStand = V9Stand(v9EntranceRim); break;
                case "shelter-open" or "shelter-sealed":
                    v9StateCenter = new Vector2((v9ShelterOrigin.X + 6) * 8 + 4, (v9ShelterOrigin.Y - 3) * 8);
                    v9StateStand = V9Stand(new Point(v9ShelterOrigin.X - 2, v9ShelterOrigin.Y)); break;
                case "lateral-opening":
                    v9StateCenter = new Vector2(v9LateralOpening.X * 8 + 4, v9LateralOpening.Y * 8 + 4);
                    v9StateStand = v9StateCenter + new Vector2(-24 * Math.Sign(v9LateralInside.X - v9LateralOpening.X), 12); break;
                case "wrap-seam": v9StateStand = V9Stand(v9SeamFloor); v9StateCenter = v9StateStand - new Vector2(0, 12); break;
                case "wrap-seam-shifted":
                    // The same place one world later: exactly what the wrap system does to player and camera.
                    v9StateStand = V9Stand(v9SeamFloor) + new Vector2(map.PixelWidth, 0); v9StateCenter = v9StateStand - new Vector2(0, 12); break;
                default: v9StateStand = V9Stand(v9CaveFloor); v9StateCenter = v9StateStand - new Vector2(0, 12); break;
            }
            int torches = name switch { "cave-torch-1" or "cave-block-placed" or "cave-block-removed" => 1, "cave-torch-2" => 2, "cave-torch-5" => 5, _ => 0 };
            RestoreV9Torches(name.StartsWith("wrap") ? new[] { v9SeamTorch } : v9CaveTorches.Take(torches));
            // Scripted construction across the mouth (same TileRevision path as player building), then removed.
            if (name == "entrance-sealed") foreach (Point p in v9EntranceSeal) map.SetTile(p.X, p.Y, TileType.Stone);
            if (name == "entrance-reopened") foreach (Point p in v9EntranceSeal) map.SetTile(p.X, p.Y, TileType.Empty);
            // Background only: walls behind the open air above the mouth, then removed (foreground untouched).
            if (name == "entrance-bg-closed") foreach (Point p in v9EntrancePorch) map.SetBackgroundTile(p.X, p.Y, TileType.Dirt);
            if (name == "entrance-bg-reopened") foreach (Point p in v9EntrancePorch) map.SetBackgroundTile(p.X, p.Y, TileType.Empty);
            // Built shelter: roof and back wall (left side open), then that side closed. Last states: left in place.
            if (name == "shelter-open") foreach (Point p in V9ShelterShell()) map.SetTile(p.X, p.Y, TileType.Stone);
            if (name == "shelter-sealed") foreach (Point p in V9ShelterSeal()) map.SetTile(p.X, p.Y, TileType.Stone);
            if (name == "cave-block-placed" && !map.TryPlaceTile(v9BlockTile.X, v9BlockTile.Y, TileType.Stone))
                map.SetTile(v9BlockTile.X, v9BlockTile.Y, TileType.Stone);
            if (name == "cave-block-removed" && !map.TryBreakTile(v9BlockTile.X, v9BlockTile.Y, out _))
                map.SetTile(v9BlockTile.X, v9BlockTile.Y, TileType.Empty);
            // Every state starts from the run's glow settings; the Shallow states set their own pose and variant.
            v9Field.GlowSettings = V9BaseGlow();
            if (V9IsGlowState(name)) PrepareV9ShallowState(name, map);
            Console.WriteLine("[V9 probe capture] " + name);
        }
        if (v9StateSkipped) return;
        session.Camera.Zoom = V9ScriptedZoom;
        session.Player.TeleportTo(v9StateStand);
        session.Camera.CenterOn(v9StateCenter, width, height);
    }
    private bool v9StateSkipped;

    private void FinishV9ProbeState(bool captured)
    {
        if (v9StateFrames == 0) { v9StateFirst = v9Field.Last; v9StateFirstFieldMs = v9FieldMs; }
        if (captured || v9StateSkipped)
        {
            v9StateIndex++; v9StateFrames = 0; v9StatePrepared = false;
            if (v9StateIndex == v9States.Length)
            {
                File.WriteAllLines(Path.Combine(V9ProbeOptions.Output, "checks.txt"), v9Checks.Append(
                    $"TOTAL: {v9Checks.Count(c => c.StartsWith("PASS"))} passed; {v9Checks.Count(c => c.StartsWith("FAIL"))} failed; {v9Checks.Count(c => c.StartsWith("SKIP"))} skipped."));
                if (v9Checks.Any(c => c.StartsWith("FAIL"))) Environment.ExitCode = 1;
                V9ProbeOptions.Finished = true;
            }
        }
        else v9StateFrames++;
    }

    private void V9Check(string label, bool passed) => v9Checks.Add($"{(passed ? "PASS" : "FAIL")} {V9StateLabel}: {label}");

    private void SaveV9ProbeCapture(string name)
    {
        string prefix = Path.Combine(V9ProbeOptions.Output, name);
        v9Field.ComputeDiagnosticSplit(); // natural / torch-only buffers, diagnostic only (not computed per frame)
        if (V9ProbeOptions.PixelReport.Length > 0) SaveV9PixelReport(prefix);
        SavePng(v9Final, prefix + "_final.png");
        SavePng(v9Albedo, prefix + "_albedo.png");
        SaveBuffer(v9Field.Energy, prefix + "_irradiance-diagnostic.png");
        // Split diagnostics (same encoding): natural part, torch-only sum; and where the sky enters.
        SaveBuffer(v9Field.NaturalEnergy, prefix + "_natural-diagnostic.png");
        SaveBuffer(v9Field.ArtificialEnergy, prefix + "_artificial-diagnostic.png");
        var entryMap = V9SkyEntryMap();
        using (var texture = new Texture2D(graphicsDevice, v9Field.Width, v9Field.Height))
        {
            texture.SetData(entryMap);
            SavePng(texture, prefix + "_sky-entries.png");
        }
        if (V9Exposure || v9Field.IsBackplane)
        {
            // Sky Background Glow: classes (V3 colours) with seeds, and how far the glow went.
            using var classesTexture = new Texture2D(graphicsDevice, v9Field.Width, v9Field.Height);
            classesTexture.SetData(V9SkyClassMap());
            SavePng(classesTexture, prefix + "_sky-classes.png");
            using var glowTexture = new Texture2D(graphicsDevice, v9Field.Width, v9Field.Height);
            glowTexture.SetData(V9SkyGlowMap());
            SavePng(glowTexture, prefix + "_sky-glow.png");
            // Foreground response to visible sky only (tone mapped like the other diagnostics); black elsewhere.
            v9ForegroundSky = v9Field.ForegroundSkyDiagnostic();
            using var fgTexture = new Texture2D(graphicsDevice, v9Field.Width, v9Field.Height);
            fgTexture.SetData(v9ForegroundSky.Delta.Select(d => d > 0 ? new Color(V9LightMath.EncodeSrgb(V9LightMath.ToneMap(V9LabSettings.SkyLinearRgb * d))) : Color.Black).ToArray());
            SavePng(fgTexture, prefix + "_fg-sky-response.png");
            using var visibleTexture = new Texture2D(graphicsDevice, v9Field.Width, v9Field.Height);
            visibleTexture.SetData(V9SkyVisibleMap());
            SavePng(visibleTexture, prefix + "_sky-visible.png");
            if (v9Field.IsBackplane)
            {
                fgTexture.SetData(v9Field.BackplaneForegroundGlow.Select(d => new Color(V9LightMath.EncodeSrgb(V9LightMath.ToneMap(V9LabSettings.SkyLinearRgb * d)))).ToArray());
                SavePng(fgTexture, prefix + "_fg-from-glow.png");
            }
        }
        void SaveBuffer(Vector3[] buffer, string path)
        {
            var preview = buffer.Select(e => new Color(V9LightMath.EncodeSrgb(V9LightMath.ToneMap(e)))).ToArray();
            using var texture = new Texture2D(graphicsDevice, v9Field.Width, v9Field.Height);
            texture.SetData(preview);
            SavePng(texture, path);
        }
        static void SavePng(Texture2D texture, string path)
        {
            using var stream = File.Create(path);
            texture.SaveAsPng(stream, texture.Width, texture.Height);
        }
        object verification = null, energy = null;
        if (V9ProbeOptions.Capture)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            var v = v9Field.Verify(session.WorldMap, sourceStride: 7);
            double verifyMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            verification = new { v.FieldBitsDifferent, v.NaturalBitsDifferent, v.ArtificialBitsDifferent, v.SourceBitsDifferent, v.SourcesChecked,
                v.OutsideBoxNonZero, v.MaskMismatches, v.ExposureMismatches, v.EntryListMatches, v.GlowBitsDifferent, FallbackQueries = v9Field.OccupancyFallbackQueries, VerifyMs = verifyMs };
            if (V9Exposure)
            {
                V9Check($"Energy bit-exact against a from-scratch reference: exposure, openings and open-sky term recomputed with world solidity, then the torches in order ({v9Field.Width}x{v9Field.Height} px; {v.FieldBitsDifferent} float bits differ)", v.FieldBitsDifferent == 0);
                V9Check($"natural and artificial buffers bit-exact (natural {v.NaturalBitsDifferent} bits differ; torch-only sum {v.ArtificialBitsDifferent} bits differ)", v.NaturalBitsDifferent == 0 && v.ArtificialBitsDifferent == 0);
                V9Check($"exposure mask and opening list equal the world rule ({v9Field.ExposureTiles.Width}x{v9Field.ExposureTiles.Height} cells, {v9Field.ExposedCells} exposed; {v.ExposureMismatches} mismatches; {v9Field.NaturalSources.Count} openings, list equal {v.EntryListMatches})", v.ExposureMismatches == 0 && v.EntryListMatches);
            }
            else if (v9Field.IsBackplane)
                V9Check($"Backplane fresh world rebuild: field={v.FieldBitsDifferent}, natural={v.NaturalBitsDifferent}, artificial={v.ArtificialBitsDifferent}, glow={v.GlowBitsDifferent} differing float channels",
                    v.FieldBitsDifferent == 0 && v.NaturalBitsDifferent == 0 && v.ArtificialBitsDifferent == 0 && v.GlowBitsDifferent == 0);
            else V9Check($"Energy bit-exact against whole-list evaluation with world solidity ({v9Field.Width}x{v9Field.Height} px, {v9Field.Sources.Count} sources; {v.FieldBitsDifferent} float bits differ)", v.FieldBitsDifferent == 0);
            V9Check($"radius squares bit-exact per source ({v.SourcesChecked} sources; {v.SourceBitsDifferent} bits differ; {v.OutsideBoxNonZero} non-zero outside)", v.SourceBitsDifferent == 0 && v.OutsideBoxNonZero == 0);
            V9Check($"occupancy mask equals world solidity ({v9Field.OccupancyTiles.Width}x{v9Field.OccupancyTiles.Height} tiles; {v.MaskMismatches} mismatches; {v9Field.OccupancyFallbackQueries} fallback queries)", v.MaskMismatches == 0 && v9Field.OccupancyFallbackQueries == 0);
            if (V9Exposure)
            {
                CheckV9OpenSky();
                V9Check($"sky glow bit-exact against a from-scratch build over world data: classification, seeds, propagation ({v.GlowBitsDifferent} float bits differ)", v.GlowBitsDifferent == 0);
                CheckV9GlowInvariants();
                V9Check($"foreground sky response stays in the terminal section: {v9ForegroundSky.Affected} solid texels lit by visible sky, deepest {v9ForegroundSky.MaxDepth:0.#} px from its face (limit {V9LabSettings.SurfaceDepthPixels} px)",
                    v9ForegroundSky.MaxDepth <= V9LabSettings.SurfaceDepthPixels);
                V9Check($"Space/Surface visible sky equals a from-scratch evaluation of the openings at each tile centre ({v.SurfaceSkyMismatches} mismatches)", v.SurfaceSkyMismatches == 0);
                CheckV9SurfaceSky(name);
            }
            energy = v9Field.IsBackplane ? CheckV9Backplane(name) : V9IsGlowState(name) ? CheckV9ShallowState(name, Sum(V9EnergyAt(session.Player.Position - new Vector2(0, 12)))) : CheckV9State(name);
        }
        var s = v9Field.Last;
        int naturalNonZero = v9Field.NaturalEnergy.Count(e => e != Vector3.Zero), artificialNonZero = v9Field.ArtificialEnergy.Count(e => e != Vector3.Zero);
        File.WriteAllText(prefix + "_metrics.json", JsonSerializer.Serialize(new
        {
            name, capturedUtc = DateTimeOffset.UtcNow,
            resolution = new { width = v9Final.Width, height = v9Final.Height }, zoom = session.Camera.Zoom,
            viewTopLeft = new { x = V9Viewport(v9Final.Width, v9Final.Height).X, y = V9Viewport(v9Final.Width, v9Final.Height).Y },
            player = new { x = session.Player.Position.X, y = session.Player.Position.Y, layer = session.GetPlayerWorldLayer().ToString() },
            naturalModel = v9Field.NaturalModel.ToString(),
            field = new { origin = new { x = v9Field.Origin.X, y = v9Field.Origin.Y }, v9Field.Width, v9Field.Height, sources = v9Field.Sources.Count,
                skySamples = v9Field.SkySampleCount, torches = v9Field.Sources.Count - v9Field.SkySampleCount, cached = v9Field.CachedContributions,
                contributionBytes = v9Field.ContributionBytes, naturalBytes = v9Field.NaturalBytes, peak = v9Field.ComputePeak(), v9Field.Recenters },
            natural = new { exposedCells = v9Field.ExposedCells, openings = v9Field.NaturalSources.Count, nonZeroPixels = naturalNonZero,
                openSky = V9ProbeField.OpenSky, openingPower = V9ProbeField.EntryPower, radius = V9LabSettings.SkyRadiusPixels },
            backplane = new { enabled = v9Field.IsBackplane, v9Field.SurfaceStartRow, cavernStartRow = v9Field.ShallowLayer.EndY + 1,
                glowPeakScale = V9ProbeBackplane.GlowPeak, foregroundGlowResponse = V9ProbeBackplane.ForegroundGlowResponse,
                note = "Only SkyBackplane uses these values; exposure/opening/radius and ShallowFadeTiles metadata describe the previous model." },
            artificial = new { nonZeroPixels = artificialNonZero },
            skyGlow = new
            {
                settings = new { v9Field.GlowSettings.Enabled, v9Field.GlowSettings.ReachPixels, v9Field.GlowSettings.PeakScale, v9Field.GlowSettings.ShallowFadeTiles,
                    v9Field.GlowSettings.Sun, toSun = new { v9Field.GlowSettings.ToSun.X, v9Field.GlowSettings.ToSun.Y },
                    sunRgb = new { v9Field.GlowSettings.SunRgb.X, v9Field.GlowSettings.SunRgb.Y, v9Field.GlowSettings.SunRgb.Z },
                    v9Field.GlowSettings.SunIntensity, v9Field.GlowSettings.SunWarmth },
                seeds = v9Field.SkyGlowSeeds, shallowOpeningCells = v9Field.SkyShallowCells, glowingTexels = v9Field.Last.SkyGlowPixels,
                peak = v9Field.SkyGlowBuffer.Select(g => MathF.Max(g.X, MathF.Max(g.Y, g.Z))).DefaultIfEmpty(0).Max(),
                hash = V9Hash(v9Field.SkyGlowBuffer)
            },
            foregroundSky = new
            {
                enabled = v9Field.ForegroundSky, affectedSolidTexels = v9ForegroundSky.Affected, maxDepthPixels = v9ForegroundSky.MaxDepth,
                depthHistogramWholePixels = v9ForegroundSky.DepthHistogram,
                solidTexelsWithNaturalLight = V9SolidNaturalCount()
            },
            // Where the sky enters: opening cells (exposure model) or sky point positions (legacy model), world pixels.
            skyEntries = V9Exposure
                ? v9Field.NaturalSources.Select(l => new { x = l.Position.X, y = l.Position.Y, kind = "opening" })
                : v9Field.Sources.Take(v9Field.SkySampleCount).Select(l => new { x = l.Position.X, y = l.Position.Y, kind = "legacy-point" }),
            torches = v9Field.Sources.Skip(v9Field.SkySampleCount).Select(l => new { x = l.Position.X, y = l.Position.Y }),
            // SHA-256 of the raw float bits over the window: bit-for-bit comparison across runs and models.
            hashes = new { energy = V9Hash(v9Field.Energy), natural = V9Hash(v9Field.NaturalEnergy), artificial = V9Hash(v9Field.ArtificialEnergy) },
            firstFrameOfState = V9Stats(v9StateFirst, v9StateFirstFieldMs), captureFrame = V9Stats(s, v9FieldMs), v9DrawMs,
            verification, energy,
            note = "CPU milliseconds only (GPU time not measured). Field/window in the camera frame, one texel per world pixel. Diagnostic PNGs = EncodeSrgb(ToneMap(buffer)), diagnostic only. sky-entries map: blue = exposed air (open sky), yellow = opening cells (natural entries), brown = unexposed air with a background wall, black = unexposed air without wall, grey = solid, red = legacy sky points."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string V9Hash(Vector3[] buffer) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(buffer.AsSpan())));

    private (float[] Delta, int Affected, int[] DepthHistogram, float MaxDepth, float[] Depth) v9ForegroundSky = (Array.Empty<float>(), 0, new int[9], 0, Array.Empty<float>());

    /// <summary>The single visible-sky definition per tile: exposed exterior O bright blue; Shallow opening blue by weight;
    /// Space/Surface OpenAtmosphere the natural system reaches cyan, and the one it does not (sealed or unreached) magenta;
    /// foreground dark red; background dark green; anything else black.</summary>
    private Color[] V9SkyVisibleMap()
    {
        int w = v9Field.Width, h = v9Field.Height;
        Point o = v9Field.Origin;
        int surfaceEnd = session.LayerDefinitions.First(l => l.LayerType == World.Generation.WorldLayerType.Surface).EndY;
        var pixels = new Color[w * h];
        for (int y = 0, i = 0; y < h; y++)
        {
            int cy = V9ProbeSky.FloorDiv(o.Y + y, 8);
            for (int x = 0; x < w; x++, i++)
            {
                int cx = V9ProbeSky.FloorDiv(o.X + x, 8);
                float visible = v9Field.SkyVisibleAt(cx, cy);
                pixels[i] = v9Field.SkyClassAt(cx, cy) switch
                {
                    V9SkyClass.Foreground => new Color(70, 20, 20),
                    V9SkyClass.Background => new Color(20, 55, 25),
                    V9SkyClass.SkyExterior => new Color(70, 120, 235),
                    V9SkyClass.SkyShallow or V9SkyClass.SkyBackplane => Color.Lerp(new Color(10, 20, 50), new Color(90, 150, 255), visible),
                    V9SkyClass.Void when cy <= surfaceEnd => visible > 0 ? new Color(90, 210, 235) : new Color(150, 40, 150),
                    _ => Color.Black
                };
            }
        }
        return pixels;
    }

    /// <summary>Open and sealed Surface cases: the air's visible sky and the walls' sky response must agree with the
    /// natural system (open air it reaches: sky and lit faces; sealed air: neither).</summary>
    private void CheckV9SurfaceSky(string name)
    {
        IEnumerable<Point> interior = name switch
        {
            "entrance-sealed" => v9EntrancePath.Where(p => p.Cell.Y > v9EntranceSeal[0].Y).Select(p => p.Cell),
            "shelter-sealed" => V9ShelterInterior(1),
            "shelter-open" => V9ShelterInterior(0),
            "entrance-rim" or "entrance-inside" or "entrance-reopened" => v9EntrancePath.Where(p => p.Distance <= 30).Select(p => p.Cell),
            _ => null
        };
        if (interior == null) return;
        var cells = interior.ToList();
        int visibleCells = cells.Count(c => v9Field.SkyVisibleAt(c.X, c.Y) > 0);
        int litFaces = V9InnerFaceLit(cells);
        bool sealedCase = name.EndsWith("sealed");
        if (sealedCase)
            V9Check($"sealed Surface air: {visibleCells} of {cells.Count} interior tiles show sky, {litFaces} wall texels on its inner faces take sky light (natural light there is zero)",
                visibleCells == 0 && litFaces == 0);
        else
            V9Check($"open Surface air: {visibleCells} of {cells.Count} tiles the natural system reaches show sky, {litFaces} wall texels on the faces toward that air lit",
                visibleCells > 0 && litFaces > 0);
    }

    /// <summary>Texels of the solid tiles around the given air tiles, within 2 px of the face toward that air, lit by the
    /// foreground sky response. Light from beyond a wall cannot reach them (terminal section <= 6 px into 8 px tiles).</summary>
    private int V9InnerFaceLit(IEnumerable<Point> airCells)
    {
        if (v9ForegroundSky.Delta.Length != v9Field.Width * v9Field.Height) return 0;
        var counted = new HashSet<Point>();
        foreach (Point c in airCells)
            foreach (var (dx, dy) in new[] { (0, -1), (-1, 0), (1, 0), (0, 1) })
            {
                int nx = c.X + dx, ny = c.Y + dy;
                if (!V9ProbeOccupancy.WorldSolid(session.WorldMap, nx, ny)) continue;
                for (int k = 0; k < 2; k++)
                    for (int t = 0; t < 8; t++)
                    {
                        // Texel of the solid neighbour at depth k from the shared face, t along it.
                        int px = dx == 0 ? nx * 8 + t : dx > 0 ? nx * 8 + k : nx * 8 + 7 - k;
                        int py = dy == 0 ? ny * 8 + t : dy > 0 ? ny * 8 + k : ny * 8 + 7 - k;
                        int lx = px - v9Field.Origin.X, ly = py - v9Field.Origin.Y;
                        if ((uint)lx >= (uint)v9Field.Width || (uint)ly >= (uint)v9Field.Height) continue;
                        if (v9ForegroundSky.Delta[ly * v9Field.Width + lx] > 0) counted.Add(new Point(px, py));
                    }
            }
        return counted.Count;
    }

    /// <summary>Solid texels of the window with any natural light (existing open-sky faces, openings, and the new response).</summary>
    private int V9SolidNaturalCount()
    {
        int n = 0;
        for (int y = 0, i = 0; y < v9Field.Height; y++)
        {
            int cy = V9ProbeSky.FloorDiv(v9Field.Origin.Y + y, 8);
            for (int x = 0; x < v9Field.Width; x++, i++)
                if (v9Field.NaturalEnergy[i] != Vector3.Zero && v9Field.Solid(V9ProbeSky.FloorDiv(v9Field.Origin.X + x, 8), cy)) n++;
        }
        return n;
    }

    /// <summary>Diagnostic (--v9-probe-pixel-report): what lies under given screen pixels of this capture: world pixel,
    /// tile, foreground/background tiles, probe class, layer, the column's sky floor, whether the black sky mask covers
    /// it, and the final/albedo colours read back from the render targets.</summary>
    private void SaveV9PixelReport(string prefix)
    {
        var map = session.WorldMap;
        Matrix inverse = Matrix.Invert(session.Camera.GetViewMatrix());
        var pixel = new Color[1];
        var report = V9ProbeOptions.PixelReport.Select(s =>
        {
            Vector2 world = Vector2.Transform(new Vector2(s.X + .5f, s.Y + .5f), inverse);
            int wx = (int)MathF.Floor(world.X), wy = (int)MathF.Floor(world.Y);
            int cx = V9ProbeSky.FloorDiv(wx, 8), cy = V9ProbeSky.FloorDiv(wy, 8);
            int skyFloor = v9Field.SkyFloors(map, cx, 1)[0];
            V9SkyClass skyClass = v9Field.SkyClassAt(cx, cy);
            float maskAlpha = V9ProbeOptions.NoSkyMask || (!v9Field.IsBackplane && cy < skyFloor) ? 0 : 1 - v9Field.SkyVisibleAt(cx, cy);
            v9Final.GetData(0, new Rectangle(s.X, s.Y, 1, 1), pixel, 0, 1);
            Color final = pixel[0];
            v9Albedo.GetData(0, new Rectangle(s.X, s.Y, 1, 1), pixel, 0, 1);
            Color albedo = pixel[0];
            return new
            {
                screen = new { s.X, s.Y }, world = new { x = wx, y = wy }, tile = new { x = cx, y = cy },
                foreground = map.GetTile(cx, cy).ToString(), background = map.GetBackgroundTile(cx, cy).ToString(),
                collisionSolid = V9ProbeOccupancy.WorldSolid(map, cx, cy), skyClass = skyClass.ToString(),
                layer = session.LayerDefinitions.Where(l => l.Contains(cy)).Select(l => l.LayerType.ToString()).FirstOrDefault() ?? "outside",
                columnSkyFloorRow = skyFloor, belowSkyFloor = cy >= skyFloor, blackMaskAlpha = maskAlpha,
                final = new { final.R, final.G, final.B, final.A }, albedo = new { albedo.R, albedo.G, albedo.B, albedo.A }
            };
        }).ToList();
        File.WriteAllText(prefix + "_pixel-report.json", JsonSerializer.Serialize(new
        {
            maskDisabled = V9ProbeOptions.NoSkyMask, zoom = session.Camera.Zoom, points = report
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Window-sized map of where natural light enters (colours in the metrics note).</summary>
    private Color[] V9SkyEntryMap()
    {
        var map = session.WorldMap;
        int w = v9Field.Width, h = v9Field.Height;
        Point o = v9Field.Origin;
        int firstColumn = V9ProbeSky.FloorDiv(o.X, 8), lastColumn = V9ProbeSky.FloorDiv(o.X + w - 1, 8);
        int firstRow = V9ProbeSky.FloorDiv(o.Y, 8), lastRow = V9ProbeSky.FloorDiv(o.Y + h - 1, 8);
        int[] envelope = v9Field.Envelope(map, firstColumn, lastColumn - firstColumn + 1);
        var openings = new HashSet<Point>(v9Field.NaturalSources.Select(l => new Point(V9ProbeSky.FloorDiv((int)l.Position.X, 8), V9ProbeSky.FloorDiv((int)l.Position.Y, 8))));
        var pixels = new Color[w * h];
        for (int cy = firstRow; cy <= lastRow; cy++)
            for (int cx = firstColumn; cx <= lastColumn; cx++)
            {
                bool exposed = v9Field.IsBackplane ? v9Field.SkyVisibleAt(cx, cy) > 0 : V9Exposure ? v9Field.ExposedCell(cx, cy) : V9ProbeField.ExposedInWorld(map, cx, cy, envelope[cx - firstColumn]);
                Color c = v9Field.Solid(cx, cy) ? new Color(70, 70, 70)
                    : exposed ? (openings.Contains(new Point(cx, cy)) ? new Color(255, 210, 0) : new Color(40, 90, 160))
                    : map.GetBackgroundTile(cx, cy) != TileType.Empty ? new Color(95, 65, 35) : Color.Black;
                for (int y = Math.Max(cy * 8, o.Y); y < Math.Min(cy * 8 + 8, o.Y + h); y++)
                    for (int x = Math.Max(cx * 8, o.X); x < Math.Min(cx * 8 + 8, o.X + w); x++)
                        pixels[(y - o.Y) * w + x - o.X] = c;
            }
        for (int s = 0; s < v9Field.SkySampleCount; s++)
        {
            Vector2 p = v9Field.Sources[s].Position;
            for (int y = (int)p.Y - 2; y <= (int)p.Y + 2; y++)
                for (int x = (int)p.X - 2; x <= (int)p.X + 2; x++)
                    if (x >= o.X && y >= o.Y && x < o.X + w && y < o.Y + h) pixels[(y - o.Y) * w + x - o.X] = new Color(255, 40, 40);
        }
        return pixels;
    }

    /// <summary>Every exposed-air texel carries exactly the open-sky value; no unexposed texel exceeds it (the cap).</summary>
    private void CheckV9OpenSky()
    {
        Vector3 open = V9LabSettings.SkyLinearRgb * V9ProbeField.OpenSky;
        int exposed = 0, wrongExposed = 0, above = 0;
        for (int y = 0, i = 0; y < v9Field.Height; y++)
        {
            int cy = V9ProbeSky.FloorDiv(v9Field.Origin.Y + y, 8);
            for (int x = 0; x < v9Field.Width; x++, i++)
            {
                Vector3 n = v9Field.NaturalEnergy[i];
                if (v9Field.ExposedCell(V9ProbeSky.FloorDiv(v9Field.Origin.X + x, 8), cy)) { exposed++; if (n != open) wrongExposed++; }
                // The openings' cap: only texels without glow (the glow joins by maximum and may carry the sun's factor).
                else if ((i >= v9Field.SkyGlowBuffer.Length || v9Field.SkyGlowBuffer[i] == Vector3.Zero) && (n.X > open.X || n.Y > open.Y || n.Z > open.Z)) above++;
            }
        }
        V9Check($"open sky: all {exposed} exposed-air texels receive exactly the open-sky value ({wrongExposed} differ); {above} unexposed texels above it", wrongExposed == 0 && above == 0);
    }

    private static object V9Stats(V9ProbeFrameStats s, double fieldMs) => new { s.Recentered, s.WrapShift, s.RegionMs, s.OccupancyRebuilt, s.ChangedCells,
        s.OccupancyMs, s.ExposureRebuilt, s.ExposureChangedCells, s.ExposureMs, s.CollectMs, s.Sources, s.SkySamples, s.Torches, s.Recalculated,
        s.Evicted, s.EvaluateMs, s.EntriesCollected, s.NaturalEntries, s.EntriesMs, s.NaturalRecalculated, s.NaturalEvicted, s.NaturalEvaluateMs,
        s.DirectPixels, s.DirectMs, s.RecomposedPixels, s.RecomposeMs, s.UploadMs, s.TotalMs, FieldUpdateMs = fieldMs, s.FieldWidth, s.FieldHeight };

    private object CheckV9State(string name)
    {
        Vector2 body = session.Player.Position - new Vector2(0, 12);
        float atPlayer = Sum(V9EnergyAt(body));
        bool exposure = V9Exposure;
        switch (name)
        {
            case "surface":
                if (!exposure)
                {
                    V9Check($"surface has sky samples ({v9Field.SkySampleCount}) and lights the player (energy {atPlayer:0.###})", v9Field.SkySampleCount > 0 && atPlayer > 0);
                    return new { atPlayer };
                }
                else
                {
                    // The player's body stands in exposed air: open sky, whatever lies above within any distance.
                    Vector3 open = V9LabSettings.SkyLinearRgb * V9ProbeField.OpenSky;
                    bool inOpenSky = v9Field.ExposedCell(V9ProbeSky.FloorDiv((int)body.X, 8), V9ProbeSky.FloorDiv((int)body.Y, 8));
                    V9Check($"surface: the player stands in exposed air and receives the open-sky irradiance (energy {atPlayer:0.###}, open sky {open.X + open.Y + open.Z:0.###})",
                        inOpenSky && V9EnergyAt(body) == open);
                    return new { atPlayer, openSky = open.X + open.Y + open.Z };
                }
            case "entrance-rim":
            {
                // Mean energy of the tunnel's air cells by path distance: natural light must fall off going in.
                var bands = V9PathBands(v9EntrancePath, new[] { (0, 6), (10, 16), (22, 30) }, V9EnergyAt);
                V9Check($"natural light enters and falls off along the tunnel (path mean energy {bands[0]:0.###} > {bands[1]:0.###} > {bands[2]:0.###})",
                    bands[0] > 0 && bands[0] > bands[1] && bands[1] > bands[2] && bands[2] >= 0);
                return new { atPlayer, tunnelPathMeanEnergy = bands };
            }
            case "entrance-inside":
                v9EntranceInsideEnergy = ((Vector3[])v9Field.Energy.Clone(), v9Field.Bounds);
                v9EntranceInsideNatural = V9TunnelNatural();
                V9Check($"natural light reaches the player inside the tunnel (energy {atPlayer:0.###})", atPlayer > 0);
                return new { atPlayer, tunnelNaturalSum = v9EntranceInsideNatural.Values.Sum() };
            case "entrance-sealed":
            {
                // Lab parity (sky-closed): with the mouth closed, no natural light may reach the tunnel air below it.
                var (lit, inField) = V9LitPixels(v9EntrancePath.Where(p => p.Cell.Y > v9EntranceSeal[0].Y).Select(p => p.Cell));
                if (!exposure)
                {
                    V9Check($"sealed entrance: tunnel air below the seal receives no light ({lit} of {inField} pixels non-zero) with {v9Field.SkySampleCount} sky samples just above; {v9StateFirst.ChangedCells} changed cells, {v9StateFirst.Recalculated} sources recomputed",
                        lit == 0 && inField > 0 && v9Field.SkySampleCount > 0 && v9StateFirst.ChangedCells == v9EntranceSeal.Count);
                    return new { atPlayer, tunnelPixels = inField, litPixels = lit, skySamples = v9Field.SkySampleCount, v9StateFirst.ChangedCells, v9StateFirst.Recalculated };
                }
                V9Check($"sealed entrance: tunnel air below the seal receives no light ({lit} of {inField} pixels non-zero) with open sky right above the seal; {v9StateFirst.ChangedCells} changed cells, {v9StateFirst.ExposureChangedCells} exposure changes, {v9StateFirst.NaturalRecalculated} openings recomputed of {v9StateFirst.NaturalEntries}",
                    lit == 0 && inField > 0 && v9StateFirst.ChangedCells == v9EntranceSeal.Count && v9Field.ExposedCells > 0);
                return new { atPlayer, tunnelPixels = inField, litPixels = lit, v9StateFirst.ChangedCells, v9StateFirst.ExposureChangedCells, v9StateFirst.NaturalRecalculated, openings = v9StateFirst.NaturalEntries };
            }
            case "entrance-reopened":
            {
                var (differ, overlap) = CountDiffering(v9EntranceInsideEnergy);
                V9Check($"reopening restores the entrance field bit for bit ({differ} of {overlap} overlapping world texels differ; {v9StateFirst.Recalculated} sources recomputed, {v9StateFirst.NaturalRecalculated} openings recomputed)", differ == 0 && overlap > 0);
                return new { atPlayer, texelsDifferentFromInside = differ, overlappingTexels = overlap, v9StateFirst.Recalculated, v9StateFirst.NaturalRecalculated };
            }
            case "cave-dark" when exposure && v9Field.GlowSettings.Enabled && v9CavePocket.Any(p => v9Field.SkyClassAt(p.X, p.Y) == V9SkyClass.SkyShallow):
            {
                // Sealed in the foreground but open to the sky through Shallow background openings: the only light allowed
                // is the Sky Background Glow; nothing may reach the pocket through the foreground.
                int holes = v9CavePocket.Count(p => v9Field.SkyClassAt(p.X, p.Y) == V9SkyClass.SkyShallow);
                int lit = 0, litWithoutGlow = 0, inField = 0;
                foreach (Point p in v9CavePocket)
                    for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                    {
                        int wx = p.X * 8 + x, wy = p.Y * 8 + y;
                        if (!v9Field.Bounds.Contains(wx, wy)) continue;
                        int i = (wy - v9Field.Origin.Y) * v9Field.Width + wx - v9Field.Origin.X;
                        inField++;
                        if (v9Field.Energy[i] == Vector3.Zero) continue;
                        lit++;
                        if (v9Field.SkyGlowBuffer[i] == Vector3.Zero || v9Field.Energy[i] != v9Field.SkyGlowBuffer[i]) litWithoutGlow++;
                    }
                V9Check($"cave sealed in the foreground, open to the sky through {holes} of {v9CavePocket.Count} Shallow background cells: {lit} of {inField} pocket pixels lit, all by the background glow ({litWithoutGlow} by anything else)",
                    inField > 0 && litWithoutGlow == 0);
                return new { pocketPixels = inField, litPixels = lit, litByOtherThanGlow = litWithoutGlow, shallowOpeningCells = holes };
            }
            case "cave-dark":
            {
                var (lit, inField) = V9LitPixels(v9CavePocket);
                int naturalInWindow = v9Field.NaturalEnergy.Count(e => e != Vector3.Zero);
                V9Check(exposure
                    ? $"closed cave receives no light: {lit} of {inField} pocket pixels non-zero, no floor (natural light is non-zero on {naturalInWindow} window texels, none in the pocket; {v9Field.NaturalSources.Count} openings in reach)"
                    : $"closed cave receives no light: {lit} of {inField} pocket pixels non-zero, although {v9Field.SkySampleCount} sky samples are in reach of the window", lit == 0 && inField > 0);
                return new { pocketPixels = inField, litPixels = lit, skySamplesInReach = v9Field.SkySampleCount, openingsInReach = v9Field.NaturalSources.Count, naturalTexelsInWindow = naturalInWindow };
            }
            case "cave-torch-1": case "cave-torch-2": case "cave-torch-5":
            {
                if (name == "cave-torch-1") v9TorchOneEnergy = ((Vector3[])v9Field.Energy.Clone(), v9Field.Bounds);
                Point t = v9CaveTorches[0];
                float nearTorch = Sum(V9EnergyAt(new Vector2(t.X * 8 + 4, t.Y * 8 - 6)));
                V9Check($"torches light the player ({atPlayer:0.###}) and the air near the first torch ({nearTorch:0.###})", atPlayer > 0 && nearTorch > 0);
                return new { atPlayer, nearFirstTorch = nearTorch };
            }
            case "cave-block-placed":
            {
                var (differ, overlap) = CountDiffering(v9TorchOneEnergy);
                V9Check($"placed block changes the field ({differ} of {overlap} texels) and invalidated only covering sources ({v9StateFirst.Recalculated} recomputed, {v9StateFirst.ChangedCells} changed cells)",
                    differ > 0 && v9StateFirst.ChangedCells == 1 && v9StateFirst.Recalculated > 0 && v9StateFirst.Recalculated <= v9Field.Sources.Count);
                return new { atPlayer, texelsDifferentFromOneTorch = differ, overlappingTexels = overlap, v9StateFirst.Recalculated, v9StateFirst.ChangedCells };
            }
            case "cave-block-removed":
            {
                var (differ, overlap) = CountDiffering(v9TorchOneEnergy);
                V9Check($"removing the block restores the one-torch field bit for bit ({differ} of {overlap} overlapping world texels differ)", differ == 0 && overlap > 0);
                return new { atPlayer, texelsDifferentFromOneTorch = differ, overlappingTexels = overlap };
            }
            case "wrap-seam":
            {
                v9SeamEnergy = (Vector3[])v9Field.Energy.Clone();
                v9SeamOrigin = v9Field.Origin;
                bool straddles = v9Field.Origin.X < 0 && v9Field.Origin.X + v9Field.Width > 0;
                bool torchImage = v9Field.Sources.Skip(v9Field.SkySampleCount).Any(l => l.Position.X < 0);
                bool skyBothSides;
                if (!exposure)
                    skyBothSides = v9Field.Sources.Take(v9Field.SkySampleCount).Any(l => l.Position.X < 0) &&
                        v9Field.Sources.Take(v9Field.SkySampleCount).Any(l => l.Position.X >= 0);
                else
                {
                    // Open sky on both sides of x = 0 inside the window, from one continuous mask.
                    int row = V9ProbeSky.FloorDiv(v9Field.Origin.Y, 8) + 1;
                    skyBothSides = v9Field.ExposedCell(-1, row) && v9Field.ExposedCell(0, row) &&
                        V9NaturalAt(new Vector2(-4, row * 8 + 4)) == V9NaturalAt(new Vector2(4, row * 8 + 4));
                }
                V9Check($"window straddles the world seam (origin {v9Field.Origin.X}); {(exposure ? "open sky continuous across x=0" : "sky samples on both sides")}; torch at column {v9SeamTorch.X} used through its image at x<0",
                    straddles && torchImage && skyBothSides);
                return new { atPlayer, straddles, torchImage, skyBothSides };
            }
            case "wrap-seam-shifted":
            {
                bool same = v9SeamEnergy != null && v9SeamEnergy.Length == v9Field.Energy.Length;
                int differ = 0;
                if (same)
                    for (int i = 0; i < v9SeamEnergy.Length; i++)
                        if (BitConverter.SingleToInt32Bits(v9SeamEnergy[i].X) != BitConverter.SingleToInt32Bits(v9Field.Energy[i].X) ||
                            BitConverter.SingleToInt32Bits(v9SeamEnergy[i].Y) != BitConverter.SingleToInt32Bits(v9Field.Energy[i].Y) ||
                            BitConverter.SingleToInt32Bits(v9SeamEnergy[i].Z) != BitConverter.SingleToInt32Bits(v9Field.Energy[i].Z)) differ++;
                int shift = v9Field.Origin.X - v9SeamOrigin.X;
                V9Check($"a whole-world camera jump is a relabel: wrap shift {v9StateFirst.WrapShift}, origin moved {shift} px (= world width {session.WorldMap.PixelWidth}), {v9StateFirst.Recalculated} sources and {v9StateFirst.NaturalRecalculated} openings recomputed, exposure rebuilt {v9StateFirst.ExposureRebuilt}, {differ} texels differ",
                    same && v9StateFirst.WrapShift && shift == session.WorldMap.PixelWidth && v9StateFirst.Recalculated == 0 &&
                    v9StateFirst.NaturalRecalculated == 0 && !v9StateFirst.ExposureRebuilt && differ == 0);
                return new { atPlayer, wrapShift = v9StateFirst.WrapShift, originShift = shift, v9StateFirst.Recalculated, v9StateFirst.NaturalRecalculated, texelsDifferent = differ };
            }
            case "entrance-recentre-a": case "entrance-recentre-b": case "entrance-recentre-c":
            {
                // World anchoring: a recentre recomputes the new window; texels shared with the previous window stay bit for bit.
                var (differ, overlap) = name == "entrance-recentre-a" ? (0, 0) : CountDiffering(v9RecentreEnergy);
                v9RecentreEnergy = ((Vector3[])v9Field.Energy.Clone(), v9Field.Bounds);
                V9Check(name == "entrance-recentre-a"
                    ? $"window placed near the mouth (origin {v9Field.Origin}, recentred {v9StateFirst.Recentered})"
                    : $"camera moved 240 px: the window recentred (origin {v9Field.Origin}) and {differ} of {overlap} texels shared with the previous window differ ({v9StateFirst.Recalculated} sources and {v9StateFirst.NaturalRecalculated} openings computed, {v9StateFirst.NaturalEvicted} openings dropped)",
                    v9StateFirst.Recentered && (name == "entrance-recentre-a" || (differ == 0 && overlap > 0)));
                return new { atPlayer, origin = new { v9Field.Origin.X, v9Field.Origin.Y }, texelsDifferentFromPrevious = differ, overlappingTexels = overlap,
                    v9StateFirst.NaturalRecalculated, v9StateFirst.NaturalEvicted, v9StateFirst.Recalculated };
            }
            case "entrance-bg-closed":
            {
                var tunnel = V9TunnelNatural();
                var common = tunnel.Keys.Where(k => v9EntranceInsideNatural != null && v9EntranceInsideNatural.ContainsKey(k)).ToList();
                float before = common.Sum(k => v9EntranceInsideNatural[k]), after = common.Sum(k => tunnel[k]);
                var (differ, overlap) = CountDiffering(v9EntranceInsideEnergy);
                if (!exposure)
                {
                    // Documents the defect: the point samples do not see background walls at all.
                    V9Check($"legacy model ignores background walls: {differ} of {overlap} texels differ from the open porch", differ == 0 && overlap > 0);
                    return new { atPlayer, texelsDifferentFromOpen = differ, overlappingTexels = overlap, tunnelNaturalBefore = before, tunnelNaturalAfter = after };
                }
                int stillExposed = v9EntrancePorch.Count(p => v9Field.ExposedCell(p.X, p.Y));
                var openingCells = new HashSet<Point>(v9Field.NaturalSources.Select(l => new Point(V9ProbeSky.FloorDiv((int)l.Position.X, 8), V9ProbeSky.FloorDiv((int)l.Position.Y, 8))));
                int openingsOnPorch = v9EntrancePorch.Count(openingCells.Contains);
                V9Check($"background walls over the porch ({v9EntrancePorch.Count} cells): none stays exposed ({stillExposed}) or an opening ({openingsOnPorch}); exposure changes {v9StateFirst.ExposureChangedCells}, foreground changes {v9StateFirst.ChangedCells}, torches recomputed {v9StateFirst.Recalculated}",
                    stillExposed == 0 && openingsOnPorch == 0 && v9StateFirst.ExposureChangedCells == v9EntrancePorch.Count && v9StateFirst.ChangedCells == 0 && v9StateFirst.Recalculated == 0);
                V9Check($"the sky entry moves to the porch boundary and the tunnel receives less natural light (sum over {common.Count} tunnel cells: {before:0.###} -> {after:0.###})",
                    common.Count > 0 && after < before);
                return new { atPlayer, porchCells = v9EntrancePorch.Count, v9StateFirst.ExposureChangedCells, openings = v9Field.NaturalSources.Count,
                    tunnelCells = common.Count, tunnelNaturalBefore = before, tunnelNaturalAfter = after, texelsDifferentFromOpen = differ };
            }
            case "entrance-bg-reopened":
            {
                var (differ, overlap) = CountDiffering(v9EntranceInsideEnergy);
                V9Check($"removing the background walls restores the entrance field bit for bit ({differ} of {overlap} overlapping world texels differ; {v9StateFirst.ExposureChangedCells} exposure changes, {v9StateFirst.NaturalRecalculated} openings recomputed)", differ == 0 && overlap > 0);
                return new { atPlayer, texelsDifferentFromInside = differ, overlappingTexels = overlap, v9StateFirst.ExposureChangedCells, v9StateFirst.NaturalRecalculated };
            }
            case "lateral-opening":
            {
                var bands = V9PathBands(v9LateralPath, new[] { (0, 3), (6, 12), (16, 30) }, V9NaturalAt);
                var region = new HashSet<Point>(v9LateralPath.Select(p => p.Cell));
                int openings = v9Field.NaturalSources.Count(l =>
                {
                    var c = new Point(V9ProbeSky.FloorDiv((int)l.Position.X, 8), V9ProbeSky.FloorDiv((int)l.Position.Y, 8));
                    return V9Neighbours(c).Any(region.Contains);
                });
                if (exposure)
                    V9Check($"lateral opening: {openings} opening cells face the unexposed air; natural light enters sideways and falls off inside (mean natural {bands[0]:0.###} > {bands[1]:0.###} > {bands[2]:0.###})",
                        openings > 0 && bands[0] > 0 && bands[0] > bands[1] && bands[1] >= bands[2] && bands[2] >= 0);
                return new { atPlayer, openingCells = openings, meanNaturalByDistance = bands, regionCells = v9LateralPath.Count };
            }
            case "shelter-open":
            {
                // Mean natural energy over the interior by distance from the open side (columns 0-2, 5-7, 10-12).
                float Band(int from, int to) => V9ShelterInterior(from).Where(c => c.X - v9ShelterOrigin.X <= to)
                    .Select(c => Sum(V9NaturalAt(new Vector2(c.X * 8 + 4, c.Y * 8 + 4)))).DefaultIfEmpty(-1).Average();
                var bands = new[] { Band(0, 2), Band(5, 7), Band(10, 12) };
                var interior = new HashSet<Point>(V9ShelterInterior(0));
                int openings = v9Field.NaturalSources.Count(l => V9Neighbours(new Point(V9ProbeSky.FloorDiv((int)l.Position.X, 8), V9ProbeSky.FloorDiv((int)l.Position.Y, 8))).Any(interior.Contains));
                if (exposure)
                    V9Check($"built shelter open on one side: {openings} opening cells face the interior (all lateral); natural light enters sideways and falls off toward the back wall (mean natural {bands[0]:0.###} > {bands[1]:0.###} > {bands[2]:0.###})",
                        openings == 4 && bands[0] > bands[1] && bands[1] > bands[2] && bands[2] >= 0);
                return new { atPlayer, openingCells = openings, meanNaturalByColumnBand = bands };
            }
            case "shelter-sealed":
            {
                var (lit, inField) = V9LitPixels(V9ShelterInterior(1));
                float roof = Sum(V9NaturalAt(new Vector2((v9ShelterOrigin.X + 6) * 8 + 4, (v9ShelterOrigin.Y - 5) * 8 + 1)));
                V9Check($"built shelter sealed under open sky: {lit} of {inField} interior pixels non-zero (no floor), while the roof's top face receives natural light ({roof:0.###})",
                    lit == 0 && inField > 0 && roof > 0);
                return new { atPlayer, interiorPixels = inField, litPixels = lit, roofTopNatural = roof };
            }
            default: return new { atPlayer };
        }
        (int Differ, int Overlap) CountDiffering((Vector3[] Energy, Rectangle Bounds) reference)
        {
            if (reference.Energy == null) return (-1, 0);
            Rectangle overlap = Rectangle.Intersect(reference.Bounds, v9Field.Bounds);
            int n = 0;
            for (int y = overlap.Top; y < overlap.Bottom; y++)
                for (int x = overlap.Left; x < overlap.Right; x++)
                {
                    Vector3 a = reference.Energy[(y - reference.Bounds.Y) * reference.Bounds.Width + x - reference.Bounds.X];
                    Vector3 b = v9Field.Energy[(y - v9Field.Origin.Y) * v9Field.Width + x - v9Field.Origin.X];
                    if (BitConverter.SingleToInt32Bits(a.X) != BitConverter.SingleToInt32Bits(b.X) ||
                        BitConverter.SingleToInt32Bits(a.Y) != BitConverter.SingleToInt32Bits(b.Y) ||
                        BitConverter.SingleToInt32Bits(a.Z) != BitConverter.SingleToInt32Bits(b.Z)) n++;
                }
            return (n, overlap.Width * overlap.Height);
        }
    }

    private float[] V9PathBands(List<(Point Cell, int Distance)> path, (int, int)[] ranges, Func<Vector2, Vector3?> at) =>
        ranges.Select(b =>
        {
            var cells = path.Where(p => p.Distance >= b.Item1 && p.Distance <= b.Item2)
                .Select(p => Sum(at(new Vector2(p.Cell.X * 8 + 4, p.Cell.Y * 8 + 4)))).Where(v => v >= 0).ToList();
            return cells.Count == 0 ? -1f : cells.Average();
        }).ToArray();

    /// <summary>Natural energy (sum of RGB) at the centre of the tunnel cells within 30 steps of the mouth, in the window.</summary>
    private Dictionary<Point, float> V9TunnelNatural()
    {
        var result = new Dictionary<Point, float>();
        foreach (var (cell, distance) in v9EntrancePath)
            if (distance <= 30 && V9NaturalAt(new Vector2(cell.X * 8 + 4, cell.Y * 8 + 4)) is Vector3 v) result[cell] = v.X + v.Y + v.Z;
        return result;
    }

    private (int Lit, int InField) V9LitPixels(IEnumerable<Point> cells)
    {
        int lit = 0, inField = 0;
        foreach (Point p in cells)
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                var e = V9EnergyAt(new Vector2(p.X * 8 + x, p.Y * 8 + y));
                if (e is not Vector3 v) continue;
                inField++;
                if (v != Vector3.Zero) lit++;
            }
        return (lit, inField);
    }

    // ---------------------------------------------------------------- bench (--v9-probe-bench)
    private readonly record struct V9BenchSample(double FrameMs, double DrawMs, double FieldMs, double UpdateMs, bool Focused, V9ProbeFrameStats Stats);
    // The session-3 cases first, in the same order; then the entrance cases (static, camera walking across the mouth,
    // sky opening/closing every frame through the foreground seal).
    private string[] v9BenchCases = { "surface-static", "surface-walk", "cave-static-5", "cave-walk-5", "cave-torch-move", "cave-tile-toggle", "surface-tile-toggle",
        "entrance-static", "entrance-walk", "entrance-sky-toggle",
        // Sky Background Glow at the Shallow fissure: parked, walking (recentres), a background / foreground tile changing
        // every frame, and the sun moving every frame (modulation re-evaluated).
        "shallow-static", "shallow-walk", "shallow-bg-toggle", "shallow-fg-toggle", "shallow-sun-move" };
    private const int V9BenchWarmup = 20, V9BenchMeasured = 240;
    // A surface tile change recomputes every sky sample covering it (legacy): fewer frames keep the run short.
    private static int V9Warmup(string name) => name is "surface-tile-toggle" or "entrance-sky-toggle" ? 5 : V9BenchWarmup;
    private static int V9Measured(string name) => name is "surface-tile-toggle" or "entrance-sky-toggle" ? 30 : name is "shallow-bg-toggle" or "shallow-fg-toggle" ? 120 : V9BenchMeasured;
    private Point v9SurfaceTile;
    private TileType v9SurfaceTileType;
    private int v9BenchIndex, v9BenchFrame;
    private bool v9BenchPrepared;
    private readonly List<V9BenchSample> v9BenchSamples = new();
    private readonly List<object> v9BenchResults = new();

    private void InitializeV9ProbeBench()
    {
        if (V9ProbeOptions.SkyBackplane) v9BenchCases = v9BenchCases.Where(n => n != "shallow-sun-move").ToArray();
        session.Player.SetDebugFly(true);
        File.WriteAllText(Path.Combine(V9ProbeOptions.Output, "run.txt"),
            $"V9 gameplay probe bench. Seed=V8-GAMEPLAY-2026; Small; transient; VSync and fixed step off; {V9BenchWarmup} warm-up + {V9BenchMeasured} measured frames per case; walking = 1.5 px per frame (90 px/s at 60 Hz). Natural model: {v9Field.NaturalModel}.\n" + v9SceneReport + "\n");
    }

    private void PrepareV9ProbeBench(int width, int height)
    {
        string name = v9BenchCases[v9BenchIndex];
        bool cave = name.StartsWith("cave"), entrance = name.StartsWith("entrance");
        if ((cave && !v9CaveFound) || (entrance && (!v9EntranceFound || v9EntranceStands.Count == 0 || (name == "entrance-sky-toggle" && v9EntranceSeal.Count == 0))) ||
            (name.StartsWith("shallow") && !v9FissureFound))
        { v9BenchFrame = V9Warmup(name) + V9Measured(name); return; }
        if (name.StartsWith("shallow"))
        {
            if (!v9BenchPrepared) { v9BenchPrepared = true; v9BenchSamples.Clear(); RestoreV9Torches(Enumerable.Empty<Point>()); }
            PrepareV9ShallowBench(name, v9BenchFrame, width, height);
            return;
        }
        Vector2 mouthCentre = new(v9EntranceMouth.X * 8 + 4, v9EntranceMouth.Y * 8 + 40);
        Vector2 stand = cave ? V9Stand(v9CaveFloor) : name == "entrance-walk" ? mouthCentre + new Vector2(-180, 12)
            : entrance ? V9Stand(v9EntranceStands[0]) : v9Spawn;
        if (!v9BenchPrepared)
        {
            v9BenchPrepared = true; v9BenchSamples.Clear();
            RestoreV9Torches(name switch { "cave-torch-move" => v9CaveTorches.Take(1), _ when cave => v9CaveTorches, _ => Enumerable.Empty<Point>() });
            if (session.WorldMap.GetTile(v9BlockTile.X, v9BlockTile.Y) != TileType.Empty) session.WorldMap.SetTile(v9BlockTile.X, v9BlockTile.Y, TileType.Empty);
            if (name == "surface-tile-toggle")
            {
                // The top solid tile three columns right of the spawn: what mining the ground surface changes.
                int column = (int)MathF.Floor(v9Spawn.X / 8) + 3, row = 0;
                while (V9Air(session.WorldMap, column, row)) row++;
                v9SurfaceTile = new Point(column, row);
                v9SurfaceTileType = session.WorldMap.GetTile(column, row);
            }
        }
        if (name == "surface-tile-toggle")
            session.WorldMap.SetTile(v9SurfaceTile.X, v9SurfaceTile.Y, v9BenchFrame % 2 == 0 ? TileType.Empty : v9SurfaceTileType);
        if (name == "entrance-sky-toggle")
            foreach (Point p in v9EntranceSeal) session.WorldMap.SetTile(p.X, p.Y, v9BenchFrame % 2 == 0 ? TileType.Stone : TileType.Empty);
        float walk = name.EndsWith("walk") || name.EndsWith("walk-5") ? 1.5f * v9BenchFrame : 0f;
        session.Camera.Zoom = V9ScriptedZoom;
        session.Player.TeleportTo(stand + new Vector2(walk, 0));
        session.Camera.CenterOn(stand + new Vector2(walk, -12), width, height);
        if (name == "cave-torch-move") RestoreV9Torches(new[] { v9CaveTorches[v9BenchFrame % 2] });
        if (name == "cave-tile-toggle")
            session.WorldMap.SetTile(v9BlockTile.X, v9BlockTile.Y, v9BenchFrame % 2 == 0 ? TileType.Stone : TileType.Empty);
    }

    private void FinishV9ProbeBenchFrame(float frameSeconds)
    {
        string name = v9BenchCases[v9BenchIndex];
        if (v9BenchFrame >= V9Warmup(name) && v9BenchFrame < V9Warmup(name) + V9Measured(name))
            v9BenchSamples.Add(new V9BenchSample(frameSeconds * 1000, v9DrawMs, v9FieldMs, lastFrameUpdateMs, lastWindowActive, v9Field.Last));
        if (++v9BenchFrame < V9Warmup(name) + V9Measured(name)) return;
        if (name == "surface-tile-toggle") session.WorldMap.SetTile(v9SurfaceTile.X, v9SurfaceTile.Y, v9SurfaceTileType);
        if (name == "entrance-sky-toggle") foreach (Point p in v9EntranceSeal) session.WorldMap.SetTile(p.X, p.Y, TileType.Empty);
        if (name.StartsWith("shallow")) FinishV9ShallowBench(name);
        if (v9BenchSamples.Count > 0)
        {
            var recenters = v9BenchSamples.Where(s => s.Stats.Recentered).ToList();
            v9BenchResults.Add(new
            {
                Case = name, Warmup = V9Warmup(name), Samples = v9BenchSamples.Count, Focused = v9BenchSamples.Count(s => s.Focused),
                Field = new { v9BenchSamples[^1].Stats.FieldWidth, v9BenchSamples[^1].Stats.FieldHeight },
                FrameIntervalMs = V9Summary(v9BenchSamples.Select(s => s.FrameMs)), V9DrawCpuMs = V9Summary(v9BenchSamples.Select(s => s.DrawMs)),
                UpdateCpuMs = V9Summary(v9BenchSamples.Select(s => s.UpdateMs)), FieldUpdateMs = V9Summary(v9BenchSamples.Select(s => s.FieldMs)),
                RegionMs = V9Summary(v9BenchSamples.Select(s => s.Stats.RegionMs)), OccupancyMs = V9Summary(v9BenchSamples.Select(s => s.Stats.OccupancyMs)),
                // Natural path (exposure model): exposure mask, opening detection, opening contributions, open-sky term.
                ExposureMs = V9Summary(v9BenchSamples.Select(s => s.Stats.ExposureMs)), OpeningsMs = V9Summary(v9BenchSamples.Select(s => s.Stats.EntriesMs)),
                NaturalEvaluateMs = V9Summary(v9BenchSamples.Select(s => s.Stats.NaturalEvaluateMs)), DirectMs = V9Summary(v9BenchSamples.Select(s => s.Stats.DirectMs)),
                NaturalTotalMs = V9Summary(v9BenchSamples.Select(s => s.Stats.ExposureMs + s.Stats.EntriesMs + s.Stats.NaturalEvaluateMs + s.Stats.DirectMs)),
                // Torch path (legacy model: sky samples are sources here too).
                CollectMs = V9Summary(v9BenchSamples.Select(s => s.Stats.CollectMs)), EvaluateMs = V9Summary(v9BenchSamples.Select(s => s.Stats.EvaluateMs)),
                RecomposeMs = V9Summary(v9BenchSamples.Select(s => s.Stats.RecomposeMs)), HalfUploadMs = V9Summary(v9BenchSamples.Select(s => s.Stats.UploadMs)),
                Sources = V9Summary(v9BenchSamples.Select(s => (double)s.Stats.Sources)), SkySamples = V9Summary(v9BenchSamples.Select(s => (double)s.Stats.SkySamples)),
                Openings = V9Summary(v9BenchSamples.Select(s => (double)s.Stats.NaturalEntries)), Torches = V9Summary(v9BenchSamples.Select(s => (double)s.Stats.Torches)),
                RecalculatedPerFrame = V9Summary(v9BenchSamples.Select(s => (double)s.Stats.Recalculated)), RecalculatedTotal = v9BenchSamples.Sum(s => s.Stats.Recalculated),
                OpeningsRecalculatedPerFrame = V9Summary(v9BenchSamples.Select(s => (double)s.Stats.NaturalRecalculated)), OpeningsRecalculatedTotal = v9BenchSamples.Sum(s => s.Stats.NaturalRecalculated),
                OccupancyRebuilds = v9BenchSamples.Count(s => s.Stats.OccupancyRebuilt), ChangedCellsTotal = v9BenchSamples.Sum(s => s.Stats.ChangedCells),
                ExposureRebuilds = v9BenchSamples.Count(s => s.Stats.ExposureRebuilt), ExposureChangedCellsTotal = v9BenchSamples.Sum(s => s.Stats.ExposureChangedCells),
                // Sky Background Glow: classification + seeds, propagation, value (incl. sun); rebuilds and sun-only re-evaluations.
                SkyClassifyMs = V9Summary(v9BenchSamples.Select(s => s.Stats.SkyClassifyMs)), SkyPropagateMs = V9Summary(v9BenchSamples.Select(s => s.Stats.SkyPropagateMs)),
                SkyValueMs = V9Summary(v9BenchSamples.Select(s => s.Stats.SkyValueMs)),
                SkyGlowTotalMs = V9Summary(v9BenchSamples.Select(s => s.Stats.SkyClassifyMs + s.Stats.SkyPropagateMs + s.Stats.SkyValueMs)),
                SkyGlowRebuilds = v9BenchSamples.Count(s => s.Stats.SkyGlowRebuilt), SkyGlowReevaluations = v9BenchSamples.Count(s => s.Stats.SkyGlowReevaluated),
                SkyGlowSeeds = V9Summary(v9BenchSamples.Select(s => (double)s.Stats.SkyGlowSeeds)), SkyGlowTexels = V9Summary(v9BenchSamples.Select(s => (double)s.Stats.SkyGlowPixels)),
                RecentreEvents = recenters.Count,
                RecentreFrames = recenters.Select(s => new { s.FieldMs, s.Stats.EvaluateMs, s.Stats.Recalculated, s.Stats.Evicted, s.Stats.ExposureMs, s.Stats.EntriesMs,
                    s.Stats.NaturalEvaluateMs, s.Stats.NaturalRecalculated, s.Stats.NaturalEvicted, s.Stats.DirectMs, s.Stats.RecomposeMs, s.Stats.UploadMs, s.Stats.OccupancyMs,
                    s.Stats.SkyClassifyMs, s.Stats.SkyPropagateMs, s.Stats.SkyValueMs, s.DrawMs }),
                NonRecentreFieldUpdateMs = V9Summary(v9BenchSamples.Where(s => !s.Stats.Recentered).Select(s => s.FieldMs))
            });
        }
        v9BenchIndex++; v9BenchFrame = 0; v9BenchPrepared = false;
        if (v9BenchIndex == v9BenchCases.Length)
        {
            File.WriteAllText(Path.Combine(V9ProbeOptions.Output, "benchmark.json"), JsonSerializer.Serialize(new
            {
                Note = "CPU milliseconds (GPU time not measured). V9DrawCpu = terrain cache preparation + field update + draw submission. FieldUpdate = region + occupancy + exposure + collect + openings + evaluate + natural evaluate + direct + recompose + half/upload.",
                NaturalModel = v9Field.NaturalModel.ToString(),
                Cases = v9BenchResults
            }, new JsonSerializerOptions { WriteIndented = true }));
            V9ProbeOptions.Finished = true;
        }
    }

    private static object V9Summary(IEnumerable<double> values)
    {
        double[] a = values.OrderBy(v => v).ToArray();
        if (a.Length == 0) return null;
        return new { Mean = a.Average(), Median = a[a.Length / 2], P95 = a[(int)((a.Length - 1) * .95)], Min = a[0], Max = a[^1] };
    }
}
