using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics.LightingV8;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;

namespace Nyvorn.Source.Game.States;

/// <summary>Reproduction of "generated Shallow background is not lit by the torch": an unedited procedural
/// cave, one generated background tile traced through albedo/direct/sky/local/final, then the same cell
/// re-placed by the player API (same material). Transient session only; nothing is saved.</summary>
public partial class PlayingState
{
    private readonly List<string> v8BgCases = new() { "natural-lit", "natural-off", "placed-lit", "placed-off", "placed-blocked", "fissure-lit" };
    private int v8BgCase, v8BgFrames;
    private bool v8BgPrepared;
    private Point v8BgTorch, v8BgWall, v8BgFissureTorch, v8BgFissure, v8BgBlocker;
    private readonly List<string> v8BgLines = new();
    private readonly Dictionary<string, Vector3> v8BgFinal = new(), v8BgDirect = new();
    private string V8BackgroundProbeLabel => "bgprobe-" + v8BgCases[Math.Min(v8BgCase, v8BgCases.Count - 1)];
    private bool ShouldCaptureV8BackgroundProbe => V8GameplayOptions.BackgroundProbe && v8BgFrames == 29;

    private void InitializeV8BackgroundProbe()
    {
        var map = session.WorldMap;
        var layer = session.LayerDefinitions.First(l => l.LayerType == WorldLayerType.ShallowUnderground);
        session.Player.SetDebugFly(true);
        session.Camera.UseBounds = false;
        long air = 0, bare = 0;
        for (int y = layer.StartY; y <= layer.EndY; y++) for (int x = 0; x < map.Width; x++)
            if (map.GetTile(x, y) == TileType.Empty) { air++; if (map.GetBackgroundTile(x, y) == TileType.Empty) bare++; }
        V8BgLog($"Seed=V8-GAMEPLAY-2026 Small {map.Width}x{map.Height}; Shallow rows {layer.StartY}..{layer.EndY}");
        V8BgLog($"Shallow foreground-empty cells={air}; background-empty among them (distant scenery visible)={bare}");
        if (!FindV8BackgroundSite(layer, TileType.Dirt, true, out v8BgTorch, out v8BgWall, out v8BgBlocker) &&
            !FindV8BackgroundSite(layer, TileType.Dirt, false, out v8BgTorch, out v8BgWall, out v8BgBlocker))
            throw new InvalidOperationException("No generated Shallow cave site with natural background");
        V8BgLog($"Natural site: torch tile {v8BgTorch}, traced wall tile {v8BgWall}, blocker column x={v8BgBlocker.X} rows {v8BgBlocker.Y - 3}..{v8BgBlocker.Y}");
        if (FindV8BackgroundSite(layer, TileType.Empty, false, out v8BgFissureTorch, out v8BgFissure, out _))
            V8BgLog($"Fissure site: torch tile {v8BgFissureTorch}, traced cell {v8BgFissure}");
        else { v8BgCases.Remove("fissure-lit"); V8BgLog("Fissure site: none found"); }
    }

    private bool FindV8BackgroundSite(WorldLayerDefinition layer, TileType background, bool isolatedFromSky,
        out Point torch, out Point wall, out Point blocker)
    {
        var map = session.WorldMap;
        int size = map.TileSize, range = new V8AmbientContext().SkyRangeTiles + 2; // renderer may not exist yet
        torch = wall = blocker = default;
        bool Solid(int x, int y) => map.GetTile(x, y) is not (TileType.Empty or TileType.Platform);
        for (int y = layer.StartY + 8; y <= layer.EndY - 10; y++)
            for (int x = 64; x < map.Width - 64; x++)
            {
                if (map.GetTile(x, y) != TileType.Empty || !Solid(x, y + 1) || map.IsObjectOccupiedAt(x, y)) continue;
                for (int d = 5; d <= 7; d++) foreach (int sign in new[] { 1, -1 })
                {
                    int wx = x + sign * d, wy = y - 1, bx = x + sign * (d / 2 + 1);
                    if (map.GetBackgroundTile(wx, wy) != background) continue;
                    bool ok = Solid(bx, y + 1);
                    for (int cx = Math.Min(x, wx) - 1; cx <= Math.Max(x, wx) + 1 && ok; cx++)
                        for (int cy = y - 3; cy <= y && ok; cy++) ok = map.GetTile(cx, cy) == TileType.Empty;
                    // Natural tile surrounded by the same generated material, so re-placing keeps its autotile shape.
                    if (background != TileType.Empty)
                        for (int oy = -1; oy <= 1 && ok; oy++) for (int ox = -1; ox <= 1 && ok; ox++)
                            ok = map.GetBackgroundTile(wx + ox, wy + oy) == background;
                    if (!ok || session.SandSystem.HasSandInRectangle((x - 12) * size, (y - 12) * size, 24 * size, 24 * size)) continue;
                    if (isolatedFromSky)
                        for (int cy = wy - range; cy <= wy + range && ok; cy++) for (int cx = wx - range; cx <= wx + range && ok; cx++)
                            ok = !(map.GetTile(cx, cy) == TileType.Empty && map.GetBackgroundTile(cx, cy) == TileType.Empty);
                    if (!ok) continue;
                    torch = new Point(x, y); wall = new Point(wx, wy); blocker = new Point(bx, y);
                    return true;
                }
            }
        return false;
    }

    private void PrepareV8BackgroundProbe(int width, int height)
    {
        string name = v8BgCases[v8BgCase];
        var map = session.WorldMap;
        int size = map.TileSize;
        bool fissure = name == "fissure-lit";
        Point torch = fissure ? v8BgFissureTorch : v8BgTorch, wall = fissure ? v8BgFissure : v8BgWall;
        if (!v8BgPrepared)
        {
            session.SetWorldTimeOfDay(.5f);
            if (name == "placed-lit")
            {
                bool broke = map.TryBreakBackgroundTile(wall.X, wall.Y, out TileType removed);
                bool placed = broke && map.TryPlaceBackgroundTile(wall.X, wall.Y, removed);
                V8BgLog($"[{name}] player API: TryBreakBackgroundTile={broke} ({removed}), TryPlaceBackgroundTile={placed}");
            }
            if (name == "placed-blocked")
            {
                bool placed = true;
                for (int y = v8BgBlocker.Y; y >= v8BgBlocker.Y - 3; y--) placed &= map.TryPlaceTile(v8BgBlocker.X, y, TileType.Stone);
                V8BgLog($"[{name}] foreground column TryPlaceTile={placed}");
            }
            if (fissure)
                for (int y = v8BgBlocker.Y - 3; y <= v8BgBlocker.Y; y++) map.TryBreakTile(v8BgBlocker.X, y, out _);
            var instance = new TorchInstance(torch, 0, size);
            session.TorchRuntimeSystem.Restore(name.EndsWith("off") ? Array.Empty<TorchSaveData>() :
                new[] { new TorchSaveData { PositionX = instance.Position.X, PositionY = instance.Position.Y, PoleFrameIndex = 0 } });
            v8BgPrepared = true;
            Console.WriteLine("[V8 background probe] " + name);
        }
        session.Camera.Zoom = 3;
        int away = Math.Sign(wall.X - torch.X);
        session.Player.TeleportTo(new Vector2((torch.X - away * 10) * size, (torch.Y - 2) * size));
        session.Camera.CenterOn(new Vector2((torch.X + wall.X + 1) * size / 2f, (wall.Y + .5f) * size), width, height);
    }

    private void FinishV8BackgroundProbe(bool captured)
    {
        if (!captured) { v8BgFrames++; return; }
        ObserveV8BackgroundProbe();
        v8BgCase++; v8BgFrames = 0; v8BgPrepared = false;
        if (v8BgCase < v8BgCases.Count) return;
        File.WriteAllLines(Path.Combine(V8GameplayOptions.Output, "probe.txt"), v8BgLines);
        V8GameplayOptions.CaptureFinished = true;
    }

    private void ObserveV8BackgroundProbe()
    {
        string name = v8BgCases[v8BgCase];
        var map = session.WorldMap;
        int size = map.TileSize;
        Point wall = name == "fissure-lit" ? v8BgFissure : v8BgWall;
        Rectangle bounds = v8Renderer.LightBounds;
        Color[] direct = V8Capture.Read(v8Renderer.Direct), sky = V8Capture.Read(v8Renderer.Ambient.SkyTexture),
            local = V8Capture.Read(v8Renderer.Ambient.LocalTexture), albedo = V8Capture.Read(v8Albedo), final = V8Capture.Read(v8Final);
        Matrix view = session.Camera.GetViewMatrix();
        Vector3 d = default, s = default, l = default, a = default, f = default, predicted = default;
        int samples = 0, receiver = 0, solid = 0;
        for (int py = wall.Y * size; py < (wall.Y + 1) * size; py++)
            for (int px = wall.X * size; px < (wall.X + 1) * size; px++)
            {
                Vector2 screen = Vector2.Transform(new Vector2(px + .5f, py + .5f), view);
                int sx = (int)screen.X, sy = (int)screen.Y;
                if (!bounds.Contains(px, py) || sx < 0 || sy < 0 || sx >= v8Final.Width || sy >= v8Final.Height) continue;
                int li = (py - bounds.Y) * bounds.Width + px - bounds.X, si = sy * v8Final.Width + sx;
                Vector3 light = direct[li].ToVector3() + sky[li].ToVector3() + local[li].ToVector3();
                d += direct[li].ToVector3(); s += sky[li].ToVector3(); l += local[li].ToVector3();
                a += albedo[si].ToVector3(); f += final[si].ToVector3();
                predicted += Vector3.Min(Vector3.One, albedo[si].ToVector3() * light);
                if (albedo[si].A > 0) receiver++;
                if (v8Renderer.Geometry.IsSolid(px, py)) solid++;
                samples++;
            }
        if (samples > 0) { d /= samples; s /= samples; l /= samples; a /= samples; f /= samples; predicted /= samples; }
        v8BgFinal[name] = f; v8BgDirect[name] = d;
        V8BgLog($"[{name}] cell {wall}: foreground={map.GetTile(wall.X, wall.Y)} background={map.GetBackgroundTile(wall.X, wall.Y)} " +
            $"skyWeight={v8Renderer.AmbientContext.SkyWeight(wall.Y):0.###} layer={session.GetPlayerWorldLayer()} sources={v8Sources.Count} torches={session.TorchRuntimeSystem.Torches.Count}");
        V8BgLog($"[{name}]   samples={samples} receiverAlbedoPixels={receiver} geometrySolidPixels={solid}");
        V8BgLog($"[{name}]   mean albedo={Fmt(a)} direct={Fmt(d)} sky={Fmt(s)} local={Fmt(l)}");
        V8BgLog($"[{name}]   mean final={Fmt(f)} predicted albedo*(direct+sky+local)={Fmt(predicted)}");
        SaveV8BackgroundCrop(wall, view, final);
        if (name is "natural-lit" or "fissure-lit") ObserveV8BackgroundFrame(name, bounds, view, direct, sky, local, albedo, final);
        static string Fmt(Vector3 v) => $"({v.X:0.000},{v.Y:0.000},{v.Z:0.000})";
    }

    // Whole visible world (HUD rows excluded): every screen pixel classified by the cell it shows.
    private void ObserveV8BackgroundFrame(string name, Rectangle bounds, Matrix view, Color[] direct, Color[] sky, Color[] local, Color[] albedo, Color[] final)
    {
        var map = session.WorldMap;
        Matrix inverse = Matrix.Invert(view);
        int background = 0, backgroundMissing = 0, backgroundLit = 0, backgroundOff = 0, scenery = 0, sceneryLit = 0, sceneryReceiver = 0, maxError = 0;
        for (int sy = 100; sy < v8Final.Height - 70; sy++)
            for (int sx = 0; sx < v8Final.Width; sx++)
            {
                Vector2 world = Vector2.Transform(new Vector2(sx + .5f, sy + .5f), inverse);
                int px = (int)MathF.Floor(world.X), py = (int)MathF.Floor(world.Y);
                if (!bounds.Contains(px, py) || v8Renderer.Geometry.IsSolid(px, py)) continue;
                int tx = (int)MathF.Floor(px / (float)map.TileSize), ty = (int)MathF.Floor(py / (float)map.TileSize);
                if (map.GetTile(tx, ty) != TileType.Empty) continue;
                int li = (py - bounds.Y) * bounds.Width + px - bounds.X, si = sy * v8Final.Width + sx;
                bool lit = direct[li].R + direct[li].G + direct[li].B > 0;
                if (map.GetBackgroundTile(tx, ty) == TileType.Empty)
                {
                    scenery++; if (lit) sceneryLit++; if (albedo[si].A > 0) sceneryReceiver++;
                    continue;
                }
                background++;
                if (albedo[si].A == 0) { backgroundMissing++; continue; }
                if (!lit) continue;
                backgroundLit++;
                Vector3 expected = Vector3.Min(Vector3.One, albedo[si].ToVector3() * (direct[li].ToVector3() + sky[li].ToVector3() + local[li].ToVector3())) * 255;
                Vector3 error = expected - final[si].ToVector3() * 255;
                int e = (int)MathF.Round(MathF.Max(MathF.Abs(error.X), MathF.Max(MathF.Abs(error.Y), MathF.Abs(error.Z))));
                maxError = Math.Max(maxError, e);
                if (e > 8) backgroundOff++;
            }
        V8BgLog($"[{name}] frame: background-cell pixels={background}, without receiver albedo={backgroundMissing}, directly lit={backgroundLit}, " +
            $"final off albedo*(direct+sky+local) by >8/255={backgroundOff} (max {maxError}/255)");
        V8BgLog($"[{name}] frame: background-empty cell pixels (sky/parallax visible)={scenery}, of which inside torch direct={sceneryLit}, covered by some receiver={sceneryReceiver}");
    }

    // 48x32-tile crop around the traced cell, from the actual final composition; red frame outlines the cell.
    private void SaveV8BackgroundCrop(Point cell, Matrix view, Color[] final)
    {
        int size = session.WorldMap.TileSize;
        Vector2 origin = Vector2.Transform(new Vector2((cell.X - 16) * size, (cell.Y - 10) * size), view);
        Vector2 end = Vector2.Transform(new Vector2((cell.X + 16) * size, (cell.Y + 10) * size), view);
        var region = Rectangle.Intersect(new Rectangle((int)origin.X, (int)origin.Y, (int)(end.X - origin.X), (int)(end.Y - origin.Y)),
            new Rectangle(0, 0, v8Final.Width, v8Final.Height));
        Vector2 c0 = Vector2.Transform(new Vector2(cell.X * size, cell.Y * size), view), c1 = Vector2.Transform(new Vector2((cell.X + 1) * size, (cell.Y + 1) * size), view);
        var pixels = new Color[region.Width * region.Height];
        for (int y = 0; y < region.Height; y++) for (int x = 0; x < region.Width; x++)
        {
            int sx = region.X + x, sy = region.Y + y;
            bool frame = (sx == (int)c0.X - 1 || sx == (int)c1.X) && sy >= c0.Y - 1 && sy <= c1.Y ||
                (sy == (int)c0.Y - 1 || sy == (int)c1.Y) && sx >= c0.X - 1 && sx <= c1.X;
            pixels[y * region.Width + x] = frame ? Color.Red : final[sy * v8Final.Width + sx] with { A = 255 };
        }
        using var texture = new Texture2D(graphicsDevice, region.Width, region.Height);
        texture.SetData(pixels);
        V8Capture.Save(texture, Path.Combine(V8GameplayOptions.Output, V8BackgroundProbeLabel + "_crop.png"));
    }

    private void V8BgLog(string line) { v8BgLines.Add(line); Console.WriteLine("[V8 background probe] " + line); }
}
