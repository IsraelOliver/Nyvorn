using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Probe;

/// <summary>Per-tile sky classes: the V3 classifier's three classes, with OpenAtmosphere split by where it can be sky.</summary>
public enum V9SkyClass : byte { Void = 0, Background = 1, Foreground = 2, SkyExterior = 3, SkyShallow = 4, SkyBackplane = 5 }

/// <summary>Sky Background Glow parameters. Provisional values, pending the user's visual comparison.</summary>
/// <param name="Enabled">The glow light; off keeps the classification and the sky shown through Shallow openings.</param>
/// <param name="ReachPixels">Glow length along the background, multiple of 8 (40 px = 5 tiles).</param>
/// <param name="PeakScale">Glow at the sky edge, times the open-sky irradiance.</param>
/// <param name="ShallowFadeTiles">Rows at the bottom of the Shallow over which its openings fade to zero.</param>
/// <param name="Sun">Directional sun modulation of the glow (off by default).</param>
/// <param name="ToSun">Unit direction toward the sun, y down (from the game's own sun angle).</param>
/// <param name="SunRgb">Linear sun colour, max channel 1 (from the game's own sun colour).</param>
public readonly record struct V9SkyGlowSettings(bool Enabled = true, int ReachPixels = 40, float PeakScale = 1f, float ShallowFadeTiles = 8,
    bool Sun = false, Vector2 ToSun = default, Vector3 SunRgb = default, float SunIntensity = .25f, float SunWarmth = .3f)
{
    /// <summary>The positional defaults (a bare new() would zero every field).</summary>
    public static V9SkyGlowSettings Default => new(Enabled: true);
    public V9SkyGlowSettings WithoutSun() => this with { Sun = false, ToSun = default, SunRgb = default };
}

/// <summary>
/// The Lighting V3 phase-2 classifier's geometry interface over the probe's data. Foreground = the approved occupancy
/// (collision Solid; platforms do not count, as everywhere in the probe; V3 used WorldMap.IsSolidAt, which counts them);
/// background = any non-empty background tile (V3: IsBackgroundSolidAt). Camera-frame x; the map wraps.
/// </summary>
internal sealed class V9ProbeGeometryAdapter : ILightingWorldGeometryProvider
{
    private readonly WorldMap map;
    private readonly Func<int, int, bool> solid;
    public V9ProbeGeometryAdapter(WorldMap map, Func<int, int, bool> solid) { this.map = map; this.solid = solid; }
    public bool IsForegroundSolidAt(int tileX, int tileY) => solid(tileX, tileY);
    public bool HasBackgroundWallAt(int tileX, int tileY) => map.GetBackgroundTile(tileX, tileY) != TileType.Empty;
    public int WrapTileX(int tileX) => map.WrapTileX(tileX);
    public bool IsInBounds(int tileX, int tileY) => map.InBounds(tileX, tileY);
    public int WorldWidthTiles => map.Width;
    public int WorldHeightTiles => map.Height;
    public bool HasOpenSkyAbove(int tileX, int tileY) => map.HasOpenSkyAbove(tileX, tileY);
}

/// <summary>
/// Sky Background Glow. Classes per tile come from <see cref="SceneWorldClassifier"/> (SolidForeground / VisibleBackground /
/// OpenAtmosphere); OpenAtmosphere is sky only where the world makes it so: the exposed exterior O (weight 1) or a
/// background opening inside the Shallow layer (weight 1, fading to 0 over its last rows); anywhere else it is void.
/// Seeds are background tiles touching sky on a side. The glow runs only through background pixels (foreground and void
/// block), by a small chamfer (5/7) geodesic distance from the sky, up to ReachPixels:
/// glow = weight x (1 - d/R)^2 x PeakScale x open sky x the sky's cold RGB, and the opening itself carries weight x peak.
/// No point lights, no DDA, no global ambient. Optional sun: 1 + SunIntensity x dot(direction to the opening, toward the
/// sun), and up to SunWarmth of the sun colour on the sunny side.
/// </summary>
internal sealed class V9ProbeSkyGlow
{
    private V9SkyClass[] classes = Array.Empty<V9SkyClass>(), previousClasses = Array.Empty<V9SkyClass>();
    private float[] weights = Array.Empty<float>(), previousWeights = Array.Empty<float>();
    private Rectangle previousCells;
    /// <summary>Tiles whose class or weight changed in the last build, inside the overlap with the previous class
    /// rectangle (a new placement is handled by its caller as a whole-window change).</summary>
    public List<Point> ChangedCells { get; } = new();
    private int[] dist = Array.Empty<int>(), source = Array.Empty<int>();
    private float[] sourceWeight = Array.Empty<float>();
    private List<int>[] buckets = Array.Empty<List<int>>();
    private Vector3[] glow = Array.Empty<Vector3>(), previous = Array.Empty<Vector3>();
    private Rectangle previousWindow;
    private int reach, padding, windowWidth, windowHeight;
    private Func<float, float> backplaneDepth;
    private V9SkyGlowSettings evaluatedSettings;
    /// <summary>World pixels whose glow changed in the last evaluation (the whole window when it moved).</summary>
    public Rectangle ChangedBounds { get; private set; }

    /// <summary>Class rectangle (tiles): the propagation domain in tiles plus one tile of border.</summary>
    public Rectangle Cells { get; private set; }
    /// <summary>Propagation domain (pixels): the window grown by the reach, aligned to tiles.</summary>
    public Rectangle Domain { get; private set; }
    public Rectangle Window { get; private set; }
    public Vector3[] Glow => glow;
    public int Seeds { get; private set; }
    public int SkyShallowCells { get; private set; }
    public int GlowPixels { get; private set; }
    public double ClassifyMilliseconds { get; private set; }
    public double PropagateMilliseconds { get; private set; }
    public double ValueMilliseconds { get; private set; }

    public V9SkyClass ClassAt(int x, int y)
    {
        int lx = x - Cells.X, ly = y - Cells.Y;
        return (uint)lx < (uint)Cells.Width && (uint)ly < (uint)Cells.Height ? classes[ly * Cells.Width + lx] : V9SkyClass.Void;
    }

    public float WeightAt(int x, int y)
    {
        int lx = x - Cells.X, ly = y - Cells.Y;
        return (uint)lx < (uint)Cells.Width && (uint)ly < (uint)Cells.Height ? weights[ly * Cells.Width + lx] : 0f;
    }

    public bool IsSeed(int x, int y) =>
        ClassAt(x, y) == V9SkyClass.Background && (IsSky(x - 1, y) || IsSky(x + 1, y) || IsSky(x, y - 1) || IsSky(x, y + 1));

    private bool IsSky(int x, int y) => WeightAt(x, y) > 0;

    public static float ShallowWeight(WorldLayerDefinition shallow, float fadeTiles, int y) =>
        y < shallow.StartY || y > shallow.EndY ? 0f : Math.Clamp((shallow.EndY + 1 - y) / fadeTiles, 0f, 1f);

    public void ShiftX(int pixels)
    {
        previousCells = new Rectangle(previousCells.X + pixels / 8, previousCells.Y, previousCells.Width, previousCells.Height);
        Cells = new Rectangle(Cells.X + pixels / 8, Cells.Y, Cells.Width, Cells.Height);
        Domain = new Rectangle(Domain.X + pixels, Domain.Y, Domain.Width, Domain.Height);
        Window = new Rectangle(Window.X + pixels, Window.Y, Window.Width, Window.Height);
        previousWindow.Offset(pixels, 0);
    }

    /// <summary>Classifies, finds seeds and propagates over the window grown by the reach (window aligned to 8 px).</summary>
    public void Build(WorldMap map, Func<int, int, bool> solid, Func<int, int, bool> exposed, WorldLayerDefinition shallow,
        Rectangle window, V9SkyGlowSettings settings, Func<float, float> backplaneDepth = null)
    {
        this.backplaneDepth = backplaneDepth;
        long start = Stopwatch.GetTimestamp();
        reach = (settings.ReachPixels + 7) / 8 * 8;
        padding = reach + (backplaneDepth != null ? 8 : 0);
        Window = window;
        Domain = new Rectangle(window.X - padding, window.Y - padding, window.Width + 2 * padding, window.Height + 2 * padding);
        previousCells = Cells;
        (previousClasses, classes) = (classes, previousClasses);
        (previousWeights, weights) = (weights, previousWeights);
        Cells = new Rectangle(Domain.X / 8 - 1, Domain.Y / 8 - 1, Domain.Width / 8 + 2, Domain.Height / 8 + 2);
        if (classes.Length < Cells.Width * Cells.Height) { classes = new V9SkyClass[Cells.Width * Cells.Height]; weights = new float[classes.Length]; }
        var classifier = new SceneWorldClassifier(new V9ProbeGeometryAdapter(map, solid));
        int shallowCells = 0;
        for (int y = 0, i = 0; y < Cells.Height; y++)
            for (int x = 0; x < Cells.Width; x++, i++)
            {
                int cx = Cells.X + x, cy = Cells.Y + y;
                float w = 0;
                V9SkyClass c = classifier.ClassifyTile(cx, cy) switch
                {
                    LightingCellClassification.SolidForeground => V9SkyClass.Foreground,
                    LightingCellClassification.VisibleBackground => V9SkyClass.Background,
                    _ when backplaneDepth != null => (w = backplaneDepth(cy + .5f)) > 0 ? V9SkyClass.SkyBackplane : V9SkyClass.Void,
                    _ when exposed(cx, cy) => V9SkyClass.SkyExterior,
                    _ when (w = ShallowWeight(shallow, settings.ShallowFadeTiles, cy)) > 0 => V9SkyClass.SkyShallow,
                    _ => V9SkyClass.Void
                };
                if (c == V9SkyClass.SkyExterior) w = 1;
                if (c == V9SkyClass.SkyShallow) shallowCells++;
                classes[i] = c;
                weights[i] = w;
            }
        ChangedCells.Clear();
        if (previousClasses.Length >= previousCells.Width * previousCells.Height && previousCells.Width > 0)
        {
            Rectangle overlap = Rectangle.Intersect(previousCells, Cells);
            for (int y = overlap.Top; y < overlap.Bottom; y++)
                for (int x = overlap.Left; x < overlap.Right; x++)
                {
                    int now = (y - Cells.Y) * Cells.Width + x - Cells.X, before = (y - previousCells.Y) * previousCells.Width + x - previousCells.X;
                    if (classes[now] != previousClasses[before] || weights[now] != previousWeights[before]) ChangedCells.Add(new Point(x, y));
                }
        }
        int seeds = 0;
        for (int y = 1; y < Cells.Height - 1; y++)
            for (int x = 1; x < Cells.Width - 1; x++)
                if (IsSeed(Cells.X + x, Cells.Y + y)) seeds++;
        Seeds = seeds;
        SkyShallowCells = shallowCells;
        ClassifyMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        start = Stopwatch.GetTimestamp();
        if (settings.Enabled) Propagate();
        PropagateMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Evaluate(settings);
    }

    private V9SkyClass PixelClass(int px, int py) => classes[((py >> 3) + 1) * Cells.Width + (px >> 3) + 1];
    private float PixelWeight(int px, int py) => weights[((py >> 3) + 1) * Cells.Width + (px >> 3) + 1];
    private bool ReceivesTransport(V9SkyClass c) => c == V9SkyClass.Background || (backplaneDepth != null && c == V9SkyClass.Void);
    private bool Passable(int px, int py) => ReceivesTransport(PixelClass(px, py)) || PixelWeight(px, py) > 0;

    // Multi-source Dijkstra (bucket queue, chamfer 5/7) from sky tiles touching background, into background pixels only.
    private void Propagate()
    {
        int w = Domain.Width, h = Domain.Height, n = w * h, max = 5 * reach;
        if (dist.Length < n) { dist = new int[n]; source = new int[n]; sourceWeight = new float[n]; }
        Array.Fill(dist, int.MaxValue, 0, n);
        if (buckets.Length < max + 1)
        {
            buckets = new List<int>[max + 1];
            for (int b = 0; b <= max; b++) buckets[b] = new List<int>();
        }
        for (int cy = 1; cy < Cells.Height - 1; cy++)
            for (int cx = 1; cx < Cells.Width - 1; cx++)
            {
                float weight = weights[cy * Cells.Width + cx];
                if (weight <= 0) continue;
                bool touches = false;
                for (int dy = -1; dy <= 1 && !touches; dy++)
                    for (int dx = -1; dx <= 1 && !touches; dx++)
                        touches = ReceivesTransport(classes[(cy + dy) * Cells.Width + cx + dx]);
                if (!touches) continue;
                for (int py = (cy - 1) * 8; py < cy * 8; py++)
                    for (int px = (cx - 1) * 8; px < cx * 8; px++)
                    {
                        int j = py * w + px;
                        dist[j] = 0; sourceWeight[j] = weight; source[j] = j;
                        buckets[0].Add(j);
                    }
            }
        for (int d = 0; d <= max; d++)
        {
            var bucket = buckets[d];
            for (int b = 0; b < bucket.Count; b++)
            {
                int j = bucket[b];
                if (dist[j] != d) continue;
                int x = j % w, y = j / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                        int nd = d + (dx != 0 && dy != 0 ? 7 : 5);
                        if (nd > max || !ReceivesTransport(PixelClass(nx, ny))) continue;
                        if (dx != 0 && dy != 0 && (!Passable(x + dx, y) || !Passable(x, y + dy))) continue;
                        int k = ny * w + nx;
                        if (nd >= dist[k]) continue;
                        dist[k] = nd; sourceWeight[k] = sourceWeight[j]; source[k] = source[j];
                        buckets[nd].Add(k);
                    }
            }
            bucket.Clear();
        }
    }

    /// <summary>Glow per window pixel from the stored distances (a sun-only change re-runs just this).</summary>
    public void Evaluate(V9SkyGlowSettings settings)
    {
        evaluatedSettings = settings;
        long start = Stopwatch.GetTimestamp();
        windowWidth = Window.Width; windowHeight = Window.Height;
        (glow, previous) = (previous, glow);
        bool sameWindow = previousWindow == Window && previous.Length == windowWidth * windowHeight;
        previousWindow = Window;
        if (glow.Length != windowWidth * windowHeight) glow = new Vector3[windowWidth * windowHeight];
        Array.Fill(glow, Vector3.Zero);
        int lit = 0;
        if (settings.Enabled)
        {
            int w = Domain.Width, max = 5 * reach;
            float peak = settings.PeakScale * V9ProbeField.OpenSky;
            Vector3 cold = V9LabSettings.SkyLinearRgb;
            for (int y = 0, i = 0; y < windowHeight; y++)
                for (int x = 0; x < windowWidth; x++, i++)
                {
                    int px = x + padding, py = y + padding;
                    V9SkyClass c = PixelClass(px, py);
                    float g = 0;
                    int j = py * w + px;
                    if (c == V9SkyClass.SkyShallow) g = PixelWeight(px, py);
                    else if (ReceivesTransport(c) && dist[j] <= max)
                    {
                        float t = 1 - dist[j] / (5f * reach);
                        g = sourceWeight[j] * t * t;
                    }
                    if (g <= 0) continue;
                    // Below the source band, only transported light exists; do not erase it with the source cutoff.
                    Vector3 colour = cold;
                    if (settings.Sun && c == V9SkyClass.Background)
                    {
                        int s = source[j];
                        var toOpening = new Vector2(s % w - px, s / w - py);
                        float facing = toOpening == Vector2.Zero ? 0 : Vector2.Dot(Vector2.Normalize(toOpening), settings.ToSun);
                        g *= 1 + settings.SunIntensity * facing;
                        colour = Vector3.Lerp(cold, settings.SunRgb, settings.SunWarmth * MathF.Max(facing, 0));
                    }
                    glow[i] = colour * (g * (backplaneDepth != null && c == V9SkyClass.Void ? V9ProbeField.OpenSky : peak));
                    lit++;
                }
        }
        GlowPixels = lit;
        if (!sameWindow) ChangedBounds = Window;
        else
        {
            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            for (int y = 0, i = 0; y < windowHeight; y++)
                for (int x = 0; x < windowWidth; x++, i++)
                    if (glow[i] != previous[i])
                    {
                        left = Math.Min(left, x); right = Math.Max(right, x);
                        top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                    }
            ChangedBounds = right < 0 ? Rectangle.Empty : new Rectangle(Window.X + left, Window.Y + top, right - left + 1, bottom - top + 1);
        }
        ValueMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>Geodesic distance (pixels) of a window pixel from the sky, or -1 when the glow does not reach it.</summary>
    public float DistanceAt(int worldX, int worldY)
    {
        int px = worldX - Domain.X, py = worldY - Domain.Y;
        if ((uint)px >= (uint)Domain.Width || (uint)py >= (uint)Domain.Height || dist.Length == 0) return -1;
        int d = dist[py * Domain.Width + px];
        return d == int.MaxValue || d > 5 * reach ? -1 : d / 5f;
    }

    // Backplane-only receiver query, including the one-tile halo beyond Window for camera-independent solid faces.
    public Vector3 BackplaneBackgroundAt(int worldX, int worldY)
    {
        if (!evaluatedSettings.Enabled || backplaneDepth == null || !Domain.Contains(worldX, worldY)) return Vector3.Zero;
        int px = worldX - Domain.X, py = worldY - Domain.Y, j = py * Domain.Width + px;
        V9SkyClass c = PixelClass(px, py);
        if (!ReceivesTransport(c) || dist[j] > 5 * reach) return Vector3.Zero;
        float t = 1 - dist[j] / (5f * reach);
        float g = sourceWeight[j] * t * t;
        float peak = c == V9SkyClass.Void ? V9ProbeField.OpenSky : evaluatedSettings.PeakScale * V9ProbeField.OpenSky;
        return V9LabSettings.SkyLinearRgb * (g * peak);
    }
}
