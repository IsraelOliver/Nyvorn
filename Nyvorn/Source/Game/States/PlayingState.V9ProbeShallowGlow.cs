using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;
using Nyvorn.Source.Engine.Graphics.LightingV9Probe;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.Game.States;

/// <summary>
/// Sky Background Glow evidence in the Shallow: a natural background fissure seen from a cave and the layer's lower edge,
/// found procedurally (the lower edge is built if the world has none), and a sealed room with a full background built in
/// the transient world (closed, with torches, then a small and a wide background opening). Checks, maps and bench cases.
/// </summary>
public partial class PlayingState
{
    private bool v9FissureFound, v9BottomFound, v9BottomBuilt, v9RoomFound;
    private Point v9FissureCentre, v9BottomCentre, v9RoomOrigin, v9FissureToggleBackground, v9FissureToggleForeground;
    private Vector2 v9FissureStand;
    private int v9FissureSeeds, v9BottomSeeds, v9FissureLitBackground = -1, v9SmallHoleLit = -1, v9SmallHoleSeeds = -1;
    private TileType v9FissureToggleForegroundType;
    // Sky-cover check in the Cavern: a 3x3 block of cave air whose background wall is removed (OpenAtmosphere outside the
    // authorized layers: must stay covered).
    private bool v9CavernHoleFound;
    private Point v9CavernHole;
    private static bool V9IsGlowState(string name) => name.StartsWith("shallow") || name == "cavern-bg-hole";
    private int v9EdgeSmallLit = -1;
    private float v9EdgeSmallDepth = -1;

    /// <summary>Solid texels inside a tile rectangle lit by the foreground sky response, and the deepest of them (px).</summary>
    private (int Count, float Deepest) V9ForegroundSkyIn(Rectangle tiles)
    {
        int count = 0;
        float deepest = 0;
        if (v9ForegroundSky.Delta.Length != v9Field.Width * v9Field.Height) return (0, 0);
        Rectangle pixels = Rectangle.Intersect(new Rectangle(tiles.X * 8, tiles.Y * 8, tiles.Width * 8, tiles.Height * 8), v9Field.Bounds);
        for (int y = pixels.Top; y < pixels.Bottom; y++)
            for (int x = pixels.Left; x < pixels.Right; x++)
            {
                int i = (y - v9Field.Origin.Y) * v9Field.Width + x - v9Field.Origin.X;
                if (v9ForegroundSky.Delta[i] <= 0) continue;
                count++;
                deepest = MathF.Max(deepest, v9ForegroundSky.Depth[i]);
            }
        return (count, deepest);
    }
    private (Vector3[] Glow, Rectangle Bounds) v9NeutralGlow;
    private string v9ShallowReport = string.Empty;

    // Sealed room: a 20x14 box of solid ground in the Shallow; interior 18x12 carved, its background filled with Dirt.
    private IEnumerable<Point> V9RoomInterior()
    {
        for (int y = 1; y <= 12; y++)
            for (int x = 1; x <= 18; x++) yield return new Point(v9RoomOrigin.X + x, v9RoomOrigin.Y + y);
    }
    private IEnumerable<Point> V9RoomTorches() => new[] { 3, 6, 10, 13, 16 }.Select(x => new Point(v9RoomOrigin.X + x, v9RoomOrigin.Y + 12));
    private IEnumerable<Point> V9RoomSmallHole() => new[] { new Point(v9RoomOrigin.X + 9, v9RoomOrigin.Y + 5), new Point(v9RoomOrigin.X + 9, v9RoomOrigin.Y + 6) };
    private IEnumerable<Point> V9RoomWideHole()
    {
        for (int y = 3; y <= 6; y++)
            for (int x = 5; x <= 12; x++) yield return new Point(v9RoomOrigin.X + x, v9RoomOrigin.Y + y);
    }

    /// <summary>The probe's default glow for a state: the shipped glow (V9Gameplay.Glow) with the Sky Backplane, the lab's
    /// default with the diagnostic models; on unless --v9-probe-no-sky-glow; sun only with --v9-probe-glow-sun.</summary>
    private V9SkyGlowSettings V9BaseGlow()
    {
        var settings = (V9ProbeOptions.SkyBackplane ? V9Gameplay.Glow : V9SkyGlowSettings.Default) with { Enabled = !V9ProbeOptions.NoSkyGlow };
        if (V9ProbeOptions.SkyBackplane) return settings;
        return V9ProbeOptions.GlowSun ? V9GlowWithSun(settings, .5f) : settings;
    }

    private void FindV9ShallowGlowScenes(int spawnColumn)
    {
        var map = session.WorldMap;
        var shallow = v9Field.ShallowLayer;
        bool Wall(int x, int y) => V9Air(map, x, y) && map.GetBackgroundTile(x, y) != TileType.Empty;
        bool Hole(int x, int y) => y >= shallow.StartY && y <= shallow.EndY && V9Air(map, x, y) && map.GetBackgroundTile(x, y) == TileType.Empty;
        bool Seed(int x, int y) => Wall(x, y) && (Hole(x - 1, y) || Hole(x + 1, y) || Hole(x, y - 1) || Hole(x, y + 1));

        // Prefix sums of seeds and background walls over columns spawn +/- 600 and the Shallow +/- margins.
        int left = spawnColumn - 600, cols = 1201, top = shallow.StartY - 4, rows = shallow.EndY + 16 - top;
        var seeds = new int[(cols + 1) * (rows + 1)];
        var walls = new int[(cols + 1) * (rows + 1)];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                int i = (y + 1) * (cols + 1) + x + 1;
                seeds[i] = (Seed(left + x, top + y) ? 1 : 0) + seeds[i - 1] + seeds[i - cols - 1] - seeds[i - cols - 2];
                walls[i] = (Wall(left + x, top + y) ? 1 : 0) + walls[i - 1] + walls[i - cols - 1] - walls[i - cols - 2];
            }
        int Count(int[] sums, int x0, int y0, int x1, int y1)
        {
            x0 = Math.Max(x0 - left, 0); y0 = Math.Max(y0 - top, 0); x1 = Math.Min(x1 - left, cols - 1); y1 = Math.Min(y1 - top, rows - 1);
            if (x1 < x0 || y1 < y0) return 0;
            return sums[(y1 + 1) * (cols + 1) + x1 + 1] - sums[y0 * (cols + 1) + x1 + 1] - sums[(y1 + 1) * (cols + 1) + x0] + sums[y0 * (cols + 1) + x0];
        }

        // Fissure: the cave view (61x33 tiles, inside the 80x45-tile viewport) with the most background tiles touching a
        // Shallow opening, in rows where the openings keep full weight; nearest to the spawn on ties.
        int best = 0;
        int fullBottom = shallow.EndY - (int)MathF.Ceiling(V9SkyGlowSettings.Default.ShallowFadeTiles);
        for (int cy = shallow.StartY + 16; cy <= fullBottom - 16; cy += 2)
            for (int cx = left + 30; cx <= left + cols - 31; cx += 4)
            {
                int n = Count(seeds, cx - 30, cy - 16, cx + 30, cy + 16);
                if (n > best || (n == best && n > 0 && Math.Abs(cx - spawnColumn) < Math.Abs(v9FissureCentre.X - spawnColumn)))
                { best = n; v9FissureCentre = new Point(cx, cy); }
            }
        v9FissureFound = best >= 12;
        v9FissureSeeds = best;
        if (v9FissureFound)
        {
            // The player on the nearest standable cell of the view (the scripted pose flies, so floating is fine too).
            var stand = Enumerable.Range(-20, 41).SelectMany(dx => Enumerable.Range(-12, 25).Select(dy => new Point(v9FissureCentre.X + dx, v9FissureCentre.Y + dy)))
                .Where(p => V9CanStand(map, p.X, p.Y)).OrderBy(p => Math.Abs(p.X - v9FissureCentre.X) + Math.Abs(p.Y - v9FissureCentre.Y)).Cast<Point?>().FirstOrDefault();
            v9FissureStand = stand is Point s ? V9Stand(new Point(s.X, s.Y + 1)) : new Vector2(v9FissureCentre.X * 8 + 4, v9FissureCentre.Y * 8 + 16);
            // Bench toggles: the seed nearest to the centre (its background) and a solid tile beside a wall near it.
            var seedCells = Enumerable.Range(-30, 61).SelectMany(dx => Enumerable.Range(-16, 33).Select(dy => new Point(v9FissureCentre.X + dx, v9FissureCentre.Y + dy)))
                .Where(p => Seed(p.X, p.Y)).OrderBy(p => Math.Abs(p.X - v9FissureCentre.X) + Math.Abs(p.Y - v9FissureCentre.Y)).ToList();
            v9FissureToggleBackground = seedCells[0];
            v9FissureToggleForeground = seedCells.SelectMany(p => V9Neighbours(p)).Where(p => V9ProbeOccupancy.WorldSolid(map, p.X, p.Y))
                .DefaultIfEmpty(new Point(v9FissureCentre.X, v9FissureCentre.Y + 20)).First();
            v9FissureToggleForegroundType = map.GetTile(v9FissureToggleForeground.X, v9FissureToggleForeground.Y);
        }

        // Lower edge: openings fading in the Shallow's last rows with background walls just below the layer.
        int bestBottom = 0;
        for (int cy = shallow.EndY - 4; cy <= shallow.EndY + 2; cy++)
            for (int cx = left + 30; cx <= left + cols - 31; cx += 4)
            {
                if (Count(walls, cx - 30, shallow.EndY + 1, cx + 30, shallow.EndY + 6) < 20) continue;
                int n = Count(seeds, cx - 30, shallow.EndY - 10, cx + 30, shallow.EndY);
                if (n > bestBottom || (n == bestBottom && n > 0 && Math.Abs(cx - spawnColumn) < Math.Abs(v9BottomCentre.X - spawnColumn)))
                { bestBottom = n; v9BottomCentre = new Point(cx, cy); }
            }
        v9BottomFound = bestBottom >= 4;
        v9BottomSeeds = bestBottom;
        if (!v9BottomFound) v9BottomCentre = new Point(spawnColumn + 80, shallow.EndY);

        // Sealed room: the nearest 20x14 box of solid ground in the Shallow's full-weight rows.
        for (int radius = 0; radius <= 300 && !v9RoomFound; radius++)
            foreach (int x0 in radius == 0 ? new[] { spawnColumn } : new[] { spawnColumn + radius, spawnColumn - radius })
                for (int y0 = shallow.StartY + 4; y0 + 13 <= fullBottom - 2 && !v9RoomFound; y0++)
                {
                    bool solid = true;
                    for (int y = 0; y < 14 && solid; y++)
                        for (int x = 0; x < 20 && solid; x++) solid = V9ProbeOccupancy.WorldSolid(map, x0 + x, y0 + y);
                    if (solid) { v9RoomOrigin = new Point(x0, y0); v9RoomFound = true; }
                }

        var cavern = session.LayerDefinitions.First(l => l.LayerType == WorldLayerType.Cavern);
        for (int radius = 0; radius <= 400 && !v9CavernHoleFound; radius++)
            foreach (int cx in radius == 0 ? new[] { spawnColumn } : new[] { spawnColumn + radius, spawnColumn - radius })
                for (int cy = cavern.StartY + 8; cy <= cavern.StartY + 80 && !v9CavernHoleFound; cy++)
                {
                    bool block = true;
                    for (int dy = -1; dy <= 1 && block; dy++)
                        for (int dx = -1; dx <= 1 && block; dx++) block = Wall(cx + dx, cy + dy);
                    if (block) { v9CavernHole = new Point(cx, cy); v9CavernHoleFound = true; }
                }

        int roomHoles = v9RoomFound ? V9RoomInterior().Count(p => map.GetBackgroundTile(p.X, p.Y) == TileType.Empty) : 0;
        int pocketHoles = v9CavePocket.Count(p => map.GetBackgroundTile(p.X, p.Y) == TileType.Empty);
        v9ShallowReport = $"Shallow rows {shallow.StartY}..{shallow.EndY}; " +
            (v9FissureFound ? $"background fissure view centred on {v9FissureCentre} ({best} seeds in 61x33 tiles), player {v9FissureStand}; " : $"no Shallow fissure view (best {best} seeds); ") +
            (v9BottomFound ? $"lower edge view centred on {v9BottomCentre} ({bestBottom} seeds in the last 11 rows); " : $"no natural lower-edge view (best {bestBottom} seeds): built at column {v9BottomCentre.X}; ") +
            (v9RoomFound ? $"sealed room box at {v9RoomOrigin} (20x14 solid; {roomHoles} background openings behind its interior, filled when built); " : "no solid box for the sealed room; ") +
            $"closed cave pocket: {pocketHoles} of {v9CavePocket.Count} air cells have no background wall (Shallow openings); " +
            (v9CavernHoleFound ? $"Cavern cave 3x3 background-wall block at {v9CavernHole}" : "no Cavern cave block found");
    }

    private bool V9ShallowSceneAvailable(string name) => name switch
    {
        _ when name.StartsWith("shallow-fissure") => v9FissureFound,
        "shallow-bottom" => true,
        "cavern-bg-hole" => v9CavernHoleFound,
        _ => v9RoomFound
    };

    /// <summary>Stand, camera, glow settings, construction and torches of a Shallow state (after the generic preparation).</summary>
    private void PrepareV9ShallowState(string name, WorldMap map)
    {
        var glow = V9BaseGlow();
        var shallow = v9Field.ShallowLayer;
        if (name.StartsWith("shallow-fissure"))
        {
            v9StateStand = v9FissureStand;
            v9StateCenter = new Vector2(v9FissureCentre.X * 8 + 4, v9FissureCentre.Y * 8 + 4);
            glow = name switch
            {
                "shallow-fissure-noglow" => glow with { Enabled = false },
                "shallow-fissure-reach-24" => glow with { ReachPixels = 24 },
                "shallow-fissure-reach-64" => glow with { ReachPixels = 64 },
                "shallow-fissure-peak-2" => glow with { PeakScale = 2 },
                "shallow-fissure-peak-4" => glow with { PeakScale = 4 },
                "shallow-fissure-sun-morning" => V9GlowWithSun(glow, .35f),
                "shallow-fissure-sun-afternoon" => V9GlowWithSun(glow, .65f),
                _ => glow
            };
        }
        else if (name == "cavern-bg-hole")
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++) map.SetBackgroundTile(v9CavernHole.X + dx, v9CavernHole.Y + dy, TileType.Empty);
            v9StateCenter = new Vector2(v9CavernHole.X * 8 + 4, v9CavernHole.Y * 8 + 4);
            v9StateStand = v9StateCenter + new Vector2(64, 12); // floating to the side, clear of the hole
        }
        else if (name == "shallow-bottom")
        {
            if (!v9BottomFound && !v9BottomBuilt)
            {
                // Built lower edge: a 20x22-tile cave across the Shallow's last row; Dirt wall above with a 2-column
                // background fissure, Stone wall below (the Cavern's own wall).
                int cx = v9BottomCentre.X;
                for (int y = shallow.EndY - 15; y <= shallow.EndY + 6; y++)
                    for (int x = cx - 10; x <= cx + 9; x++)
                    {
                        map.SetTile(x, y, TileType.Empty);
                        map.SetBackgroundTile(x, y, y > shallow.EndY ? TileType.Stone : x == cx - 1 || x == cx ? TileType.Empty : TileType.Dirt);
                    }
                v9BottomBuilt = true;
            }
            v9StateCenter = new Vector2(v9BottomCentre.X * 8 + 4, shallow.EndY * 8 + 4);
            v9StateStand = v9StateCenter + new Vector2(0, 12);
        }
        else
        {
            v9StateCenter = new Vector2((v9RoomOrigin.X + 10) * 8, (v9RoomOrigin.Y + 7) * 8);
            v9StateStand = V9Stand(new Point(v9RoomOrigin.X + 9, v9RoomOrigin.Y + 13));
            if (name == "shallow-room-closed")
                foreach (Point p in V9RoomInterior()) { map.SetTile(p.X, p.Y, TileType.Empty); map.SetBackgroundTile(p.X, p.Y, TileType.Dirt); }
            if (name == "shallow-room-small-hole") foreach (Point p in V9RoomSmallHole()) map.SetBackgroundTile(p.X, p.Y, TileType.Empty);
            if (name == "shallow-room-wide-hole") foreach (Point p in V9RoomWideHole()) map.SetBackgroundTile(p.X, p.Y, TileType.Empty);
            if (name.StartsWith("shallow-room-edge"))
            {
                // Start from the closed room again, then open the background against its left wall (and ceiling).
                foreach (Point p in V9RoomInterior()) map.SetBackgroundTile(p.X, p.Y, TileType.Dirt);
                int x0 = v9RoomOrigin.X, y0 = v9RoomOrigin.Y;
                var holes = name.EndsWith("small")
                    ? new[] { new Point(x0 + 1, y0 + 5), new Point(x0 + 1, y0 + 6) }
                    : Enumerable.Range(1, 6).SelectMany(x => Enumerable.Range(1, 3).Select(y => new Point(x0 + x, y0 + y))).ToArray();
                foreach (Point p in holes) map.SetBackgroundTile(p.X, p.Y, TileType.Empty);
            }
            if (name == "shallow-room-torch-5") RestoreV9Torches(V9RoomTorches());
            if (name.StartsWith("shallow-room-offset"))
            {
                foreach (Point p in V9RoomInterior()) { map.SetTile(p.X, p.Y, TileType.Empty); map.SetBackgroundTile(p.X, p.Y, TileType.Dirt); }
                if (name != "shallow-room-offset-closed")
                    for (int dy = 5; dy <= 6; dy++) map.SetBackgroundTile(v9RoomOrigin.X + 3, v9RoomOrigin.Y + dy, TileType.Empty);
                // Pillar between the opening and a strip of background. Extends to both room walls, blocking all routes.
                if (name == "shallow-room-offset-blocked")
                    for (int dy = 1; dy <= 12; dy++) map.SetTile(v9RoomOrigin.X + 5, v9RoomOrigin.Y + dy, TileType.Stone);
            }
        }
        v9Field.GlowSettings = glow;
    }

    // ---------------------------------------------------------------- measurements over the viewport
    private readonly record struct V9GlowTexel(int X, int Y, V9SkyClass Class, float Glow, float Distance, Vector3 Rgb);

    private IEnumerable<V9GlowTexel> V9GlowTexels(Rectangle area)
    {
        Rectangle r = Rectangle.Intersect(area, v9Field.Bounds);
        var glow = v9Field.SkyGlowBuffer;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                int i = (y - v9Field.Origin.Y) * v9Field.Width + x - v9Field.Origin.X;
                Vector3 g = i < glow.Length ? glow[i] : Vector3.Zero;
                yield return new V9GlowTexel(x, y, v9Field.SkyClassAt(V9ProbeSky.FloorDiv(x, 8), V9ProbeSky.FloorDiv(y, 8)),
                    g.X + g.Y + g.Z, v9Field.SkyGlowDistanceAt(x, y), g);
            }
    }

    private Rectangle V9CaptureViewport => V9Viewport(v9Final.Width, v9Final.Height);

    /// <summary>Generic glow checks of every exposure-model state: the foreground never carries glow; glow only where the
    /// reach allows.</summary>
    private void CheckV9GlowInvariants()
    {
        int foreground = 0, beyond = 0, reach = v9Field.GlowSettings.ReachPixels;
        foreach (var t in V9GlowTexels(v9Field.Bounds))
        {
            if (t.Glow == 0) continue;
            if (t.Class == V9SkyClass.Foreground) foreground++;
            if (t.Class == V9SkyClass.Background && (t.Distance < 0 || t.Distance > reach)) beyond++;
        }
        V9Check($"sky glow: no foreground texel carries glow ({foreground}); no background texel beyond the {reach} px reach does ({beyond}); {v9Field.SkyGlowSeeds} seeds, {v9Field.Last.SkyGlowPixels} glowing texels",
            foreground == 0 && beyond == 0);
    }

    private object CheckV9ShallowState(string name, float atPlayer)
    {
        var shallow = v9Field.ShallowLayer;
        var view = V9CaptureViewport;
        var texels = V9GlowTexels(view).ToList();
        var background = texels.Where(t => t.Class == V9SkyClass.Background).ToList();
        int lit = background.Count(t => t.Glow > 0);
        int openingsShown = texels.Count(t => t.Class == V9SkyClass.SkyShallow);
        float Band(float from, float to) => background.Where(t => t.Distance >= from && t.Distance < to).Select(t => t.Glow).DefaultIfEmpty(-1).Average();
        // Glow checks only where the glow runs (exposure model, glow on); the no-glow baseline records the same metrics.
        bool exposure = V9Exposure && (v9Field.GlowSettings.Enabled || name == "shallow-fissure-noglow");
        switch (name)
        {
            case "shallow-fissure-noglow":
                if (exposure)
                    V9Check($"Shallow fissure without glow: the openings are shown as sky ({openingsShown} texels in view), no glow anywhere ({texels.Count(t => t.Glow > 0)} texels)",
                        openingsShown > 0 && texels.All(t => t.Glow == 0));
                return new { atPlayer, openingTexelsInView = openingsShown };
            case "shallow-fissure":
            {
                var bands = new[] { Band(0, 8), Band(16, 24), Band(32, 40) };
                v9FissureLitBackground = lit;
                v9NeutralGlow = ((Vector3[])v9Field.SkyGlowBuffer.Clone(), v9Field.Bounds);
                if (exposure)
                    V9Check($"Shallow fissure: background touching the sky glows and fades going in (mean glow {bands[0]:0.###} > {bands[1]:0.###} > {bands[2]:0.###} at 0-8, 16-24, 32-40 px); {lit} of {background.Count} background texels in view lit ({100.0 * lit / Math.Max(1, background.Count):0.#}%)",
                        v9Field.SkyGlowSeeds > 0 && bands[0] > bands[1] && bands[1] > bands[2] && bands[2] >= 0 && lit < background.Count);
                return new { atPlayer, seeds = v9Field.SkyGlowSeeds, backgroundTexels = background.Count, litBackground = lit, meanGlowByDistance = bands, openingTexelsInView = openingsShown };
            }
            case "shallow-fissure-reach-24": case "shallow-fissure-reach-64":
            {
                bool shorter = name.EndsWith("24");
                if (exposure)
                    V9Check($"reach {v9Field.GlowSettings.ReachPixels} px: {lit} background texels lit (40 px: {v9FissureLitBackground})",
                        v9FissureLitBackground > 0 && (shorter ? lit < v9FissureLitBackground : lit > v9FissureLitBackground));
                return new { atPlayer, reach = v9Field.GlowSettings.ReachPixels, litBackground = lit, litAtDefaultReach = v9FissureLitBackground };
            }
            case "shallow-fissure-peak-2": case "shallow-fissure-peak-4":
            {
                // Same seeds and reach; only the level scales (same lit extent as the default).
                float peak = background.Where(t => t.Glow > 0).Select(t => t.Glow).DefaultIfEmpty(0).Max();
                if (exposure)
                    V9Check($"peak x{v9Field.GlowSettings.PeakScale:0.#}: same lit extent as the default ({lit} vs {v9FissureLitBackground} background texels), brightest background texel {peak:0.###}",
                        lit == v9FissureLitBackground);
                return new { atPlayer, peakScale = v9Field.GlowSettings.PeakScale, litBackground = lit, brightestBackground = peak };
            }
            case "shallow-fissure-sun-morning": case "shallow-fissure-sun-afternoon":
            {
                // Against the neutral glow of the same view: sunny side up to +25% and warmer, shady side down to -25%.
                int sunny = 0, shady = 0;
                double warmSunny = 0, warmNeutral = 0, minRatio = double.MaxValue, maxRatio = 0;
                var (neutral, bounds) = v9NeutralGlow;
                foreach (var t in background)
                {
                    if (neutral == null || !bounds.Contains(t.X, t.Y) || t.Glow <= 0) continue;
                    Vector3 n = neutral[(t.Y - bounds.Y) * bounds.Width + t.X - bounds.X];
                    float ns = n.X + n.Y + n.Z;
                    if (ns <= 0) continue;
                    double ratio = t.Glow / ns;
                    minRatio = Math.Min(minRatio, ratio); maxRatio = Math.Max(maxRatio, ratio);
                    if (ratio > 1.01) { sunny++; warmSunny += t.Rgb.X / t.Rgb.Z; warmNeutral += n.X / n.Z; }
                    else if (ratio < .99) shady++;
                }
                var s = v9Field.GlowSettings;
                if (exposure)
                    V9Check($"sun {(name.EndsWith("morning") ? "09:36" : "14:24")} (toward the sun {s.ToSun.X:0.##},{s.ToSun.Y:0.##}): {sunny} background texels brighter and warmer (R/B x{(sunny > 0 ? warmSunny / warmNeutral : 0):0.###}), {shady} dimmer; glow ratio {minRatio:0.###}..{maxRatio:0.###} of neutral",
                        sunny > 0 && shady > 0 && minRatio >= 1 - s.SunIntensity - 1e-3 && maxRatio <= 1 + s.SunIntensity + 1e-3 && warmSunny > warmNeutral);
                return new { atPlayer, toSun = new { s.ToSun.X, s.ToSun.Y }, sunRgb = new { s.SunRgb.X, s.SunRgb.Y, s.SunRgb.Z }, sunnyTexels = sunny, shadyTexels = shady,
                    minRatio = minRatio == double.MaxValue ? 0 : minRatio, maxRatio, warmthGainSunny = sunny > 0 ? warmSunny / warmNeutral : 0 };
            }
            case "shallow-bottom":
            {
                // Mean glow of background texels per row around the layer's last row (-1: no background texel in the row).
                var profile = Enumerable.Range(shallow.EndY - 16, 23).Select(row => new
                {
                    row,
                    weight = V9ProbeSkyGlow.ShallowWeight(shallow, v9Field.GlowSettings.ShallowFadeTiles, row),
                    meanGlow = background.Where(t => V9ProbeSky.FloorDiv(t.Y, 8) == row).Select(t => t.Glow).DefaultIfEmpty(-1).Average()
                }).ToList();
                float Mean(int from, int to) => profile.Where(p => p.row >= from && p.row <= to && p.meanGlow >= 0).Select(p => p.meanGlow).DefaultIfEmpty(-1).Average();
                float upper = Mean(shallow.EndY - 14, shallow.EndY - 10), edge = Mean(shallow.EndY - 2, shallow.EndY), below = Mean(shallow.EndY + 1, shallow.EndY + 3);
                int deep = background.Count(t => t.Glow > 0 && V9ProbeSky.FloorDiv(t.Y, 8) > shallow.EndY + v9Field.GlowSettings.ReachPixels / 8 + 1);
                if (exposure)
                    V9Check($"Shallow lower edge ({(v9BottomBuilt ? "built" : "natural")}): glow fades toward the last row (mean {upper:0.###} at 10-14 rows above, {edge:0.###} in the last 3 rows), leaks a little below ({below:0.###} in the 3 rows under it) and nothing deeper than the reach ({deep} texels)",
                        edge >= 0 && (upper < 0 || edge < upper) && below >= 0 && deep == 0);
                return new { atPlayer, built = v9BottomBuilt, meanAbove = upper, meanLastRows = edge, meanBelow = below, glowDeeperThanReach = deep, profile };
            }
            case "shallow-room-closed": case "shallow-room-torch-5":
            {
                int glowing = 0, lightInRoom = 0, naturalInRoom = 0, differsFromTorches = 0, inField = 0;
                foreach (Point cell in V9RoomInterior())
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                        {
                            int wx = cell.X * 8 + x, wy = cell.Y * 8 + y;
                            int i = (wy - v9Field.Origin.Y) * v9Field.Width + wx - v9Field.Origin.X;
                            if (!v9Field.Bounds.Contains(wx, wy)) continue;
                            inField++;
                            if (i < v9Field.SkyGlowBuffer.Length && v9Field.SkyGlowBuffer[i] != Vector3.Zero) glowing++;
                            if (v9Field.Energy[i] != Vector3.Zero) lightInRoom++;
                            if (v9Field.NaturalEnergy[i] != Vector3.Zero) naturalInRoom++;
                            if (v9Field.Energy[i] != v9Field.ArtificialEnergy[i]) differsFromTorches++;
                        }
                var (roomFg, _) = V9ForegroundSkyIn(new Rectangle(v9RoomOrigin.X, v9RoomOrigin.Y, 20, 14));
                if (name == "shallow-room-closed")
                    V9Check($"sealed room with a full background (in the Shallow): {glowing} of {inField} texels glow, {lightInRoom} receive any light, {roomFg} wall texels take sky light", inField > 0 && glowing == 0 && lightInRoom == 0 && roomFg == 0);
                else
                    V9Check($"five torches in the sealed room: natural light 0 ({naturalInRoom} texels), Energy equals the torch-only sum on {inField - differsFromTorches} of {inField} texels, player {atPlayer:0.###}",
                        inField > 0 && naturalInRoom == 0 && differsFromTorches == 0 && atPlayer > 0);
                return new { atPlayer, roomTexels = inField, glowingTexels = glowing, litTexels = lightInRoom, naturalTexels = naturalInRoom, texelsDifferentFromTorchOnly = differsFromTorches };
            }
            case "shallow-room-small-hole": case "shallow-room-wide-hole":
            {
                var room = new HashSet<Point>(V9RoomInterior());
                var roomTexels = texels.Where(t => room.Contains(new Point(V9ProbeSky.FloorDiv(t.X, 8), V9ProbeSky.FloorDiv(t.Y, 8)))).ToList();
                int litRoom = roomTexels.Count(t => t.Class == V9SkyClass.Background && t.Glow > 0);
                int seeds = room.Count(p => v9Field.IsSkySeed(p.X, p.Y));
                float maxDistance = roomTexels.Where(t => t.Glow > 0 && t.Class == V9SkyClass.Background).Select(t => t.Distance).DefaultIfEmpty(-1).Max();
                if (name.Contains("small"))
                {
                    v9SmallHoleLit = litRoom; v9SmallHoleSeeds = seeds;
                    if (exposure)
                        V9Check($"small background opening (1x2 tiles): {seeds} seeds, a localized halo of {litRoom} background texels, farthest {maxDistance:0.#} px from the sky", seeds > 0 && litRoom > 0 && maxDistance <= v9Field.GlowSettings.ReachPixels);
                }
                else if (exposure)
                    V9Check($"wide background opening (8x4 tiles): {seeds} seeds and {litRoom} background texels lit, against {v9SmallHoleSeeds} and {v9SmallHoleLit} for the small one", seeds > v9SmallHoleSeeds && litRoom > v9SmallHoleLit);
                return new { atPlayer, seeds, litBackgroundInRoom = litRoom, farthestLitPixels = maxDistance };
            }
            case "cavern-bg-hole":
            {
                // The hole's tiles are OpenAtmosphere in the Cavern: the cover must stay (black albedo, no painted sky).
                Matrix cameraView = session.Camera.GetViewMatrix();
                Vector2 topLeft = Vector2.Transform(Vector2.Zero, Matrix.Invert(cameraView));
                float zoom = session.Camera.Zoom;
                var screen = new Rectangle((int)MathF.Round(((v9CavernHole.X - 1) * 8 - topLeft.X) * zoom), (int)MathF.Round(((v9CavernHole.Y - 1) * 8 - topLeft.Y) * zoom),
                    (int)(24 * zoom), (int)(24 * zoom));
                screen = Rectangle.Intersect(screen, new Rectangle(0, 0, v9Albedo.Width, v9Albedo.Height));
                var pixels = new Color[screen.Width * screen.Height];
                v9Albedo.GetData(0, screen, pixels, 0, pixels.Length);
                int visible = pixels.Count(c => c.R != 0 || c.G != 0 || c.B != 0);
                var classes = Enumerable.Range(-1, 3).SelectMany(dy => Enumerable.Range(-1, 3).Select(dx => v9Field.SkyClassAt(v9CavernHole.X + dx, v9CavernHole.Y + dy))).Distinct().ToList();
                var (holeFg, _) = V9ForegroundSkyIn(new Rectangle(v9CavernHole.X - 3, v9CavernHole.Y - 3, 7, 7));
                if (!V9ProbeOptions.NoSkyMask)
                    V9Check($"background hole in a Cavern cave ({string.Join("/", classes)}): stays covered, {visible} of {pixels.Length} albedo pixels not black; {holeFg} solid texels around it take sky light",
                        pixels.Length > 0 && visible == 0 && holeFg == 0);
                return new { atPlayer, holeTile = new { v9CavernHole.X, v9CavernHole.Y }, classes = classes.Select(c => c.ToString()), coveredPixels = pixels.Length, visiblePixels = visible, foregroundSkyTexels = holeFg };
            }
            case "shallow-room-edge-small": case "shallow-room-edge-wide":
            {
                // Openings touching the room's solid wall (and ceiling): those faces take the terminal sky response.
                var (edgeLit, deepest) = V9ForegroundSkyIn(new Rectangle(v9RoomOrigin.X, v9RoomOrigin.Y, 20, 14));
                if (name.EndsWith("small"))
                {
                    v9EdgeSmallLit = edgeLit; v9EdgeSmallDepth = deepest;
                    if (V9Exposure && v9Field.ForegroundSky)
                        V9Check($"narrow opening (1x2 tiles) against the wall: {edgeLit} wall texels lit, deepest {deepest:0.#} px into the rock", edgeLit > 0 && deepest <= V9LabSettings.SurfaceDepthPixels);
                }
                else if (V9Exposure && v9Field.ForegroundSky)
                    V9Check($"wide opening (6x3 tiles) against wall and ceiling: {edgeLit} solid texels lit (narrow: {v9EdgeSmallLit}), deepest {deepest:0.#} px (narrow: {v9EdgeSmallDepth:0.#} px): more border, same depth",
                        edgeLit > v9EdgeSmallLit && deepest <= v9EdgeSmallDepth + 1e-3f);
                return new { atPlayer, litSolidTexels = edgeLit, deepestPixels = deepest };
            }
            default: return new { atPlayer };
        }
    }

    // ---------------------------------------------------------------- diagnostic maps
    /// <summary>The V3 debug colours: sky blue (exterior bright, Shallow openings by weight), background green, foreground
    /// red, void black; seeds (background touching sky) yellow.</summary>
    private Color[] V9SkyClassMap()
    {
        int w = v9Field.Width, h = v9Field.Height;
        Point o = v9Field.Origin;
        var pixels = new Color[w * h];
        for (int y = 0, i = 0; y < h; y++)
        {
            int cy = V9ProbeSky.FloorDiv(o.Y + y, 8);
            for (int x = 0; x < w; x++, i++)
            {
                int cx = V9ProbeSky.FloorDiv(o.X + x, 8);
                V9SkyClass c = v9Field.SkyClassAt(cx, cy);
                pixels[i] = c switch
                {
                    V9SkyClass.Foreground => new Color(170, 40, 40),
                    V9SkyClass.Background => v9Field.IsSkySeed(cx, cy) ? new Color(235, 205, 40) : new Color(40, 140, 50),
                    V9SkyClass.SkyExterior => new Color(70, 120, 235),
                    V9SkyClass.SkyShallow or V9SkyClass.SkyBackplane => Color.Lerp(new Color(10, 20, 50), new Color(90, 150, 255), v9Field.SkyWeightAt(cx, cy)),
                    _ => Color.Black
                };
            }
        }
        return pixels;
    }

    /// <summary>Where the glow went: the class map dimmed to a quarter, the glow (tone mapped like the other diagnostics)
    /// on top.</summary>
    private Color[] V9SkyGlowMap()
    {
        var classes = V9SkyClassMap();
        var glow = v9Field.SkyGlowBuffer;
        for (int i = 0; i < classes.Length; i++)
        {
            Vector3 dim = classes[i].ToVector3() * .25f;
            Vector3 g = i < glow.Length && glow[i] != Vector3.Zero ? V9LightMath.EncodeSrgb(V9LightMath.ToneMap(glow[i])) : Vector3.Zero;
            classes[i] = new Color(Vector3.Min(Vector3.One, dim + g));
        }
        return classes;
    }

    // ---------------------------------------------------------------- bench
    private void PrepareV9ShallowBench(string name, int frame, int width, int height)
    {
        var map = session.WorldMap;
        var glow = V9BaseGlow();
        Vector2 centre = new(v9FissureCentre.X * 8 + 4, v9FissureCentre.Y * 8 + 4);
        float walk = name == "shallow-walk" ? 1.5f * frame - 180 : 0;
        if (name == "shallow-bg-toggle")
            map.SetBackgroundTile(v9FissureToggleBackground.X, v9FissureToggleBackground.Y, frame % 2 == 0 ? TileType.Empty : TileType.Dirt);
        if (name == "shallow-fg-toggle")
            map.SetTile(v9FissureToggleForeground.X, v9FissureToggleForeground.Y, frame % 2 == 0 ? TileType.Empty : v9FissureToggleForegroundType);
        // The sun sweeps from 07:12 to 16:48 across the measured frames; only the glow modulation reads it.
        if (name == "shallow-sun-move") glow = V9GlowWithSun(glow, .3f + .4f * frame / (V9Warmup(name) + V9Measured(name)));
        v9Field.GlowSettings = glow;
        session.Camera.Zoom = V9ScriptedZoom;
        session.Player.TeleportTo(v9FissureStand + new Vector2(walk, 0));
        session.Camera.CenterOn(centre + new Vector2(walk, 0), width, height);
    }

    private void FinishV9ShallowBench(string name)
    {
        var map = session.WorldMap;
        if (name == "shallow-bg-toggle") map.SetBackgroundTile(v9FissureToggleBackground.X, v9FissureToggleBackground.Y, TileType.Dirt);
        if (name == "shallow-fg-toggle") map.SetTile(v9FissureToggleForeground.X, v9FissureToggleForeground.Y, v9FissureToggleForegroundType);
        v9Field.GlowSettings = V9BaseGlow();
    }
}
