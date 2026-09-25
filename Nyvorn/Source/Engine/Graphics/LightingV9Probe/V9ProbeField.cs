using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Probe;

/// <summary>Exposure: natural light enters where the world is exposed to the sky and through its openings (default).
/// PointSamples: the previous diagnostic representation, sky point lights along a line above the terrain envelope.</summary>
public enum V9NaturalModel { Exposure, PointSamples, SkyBackplane }

/// <summary>Per-frame CPU stages (milliseconds, Stopwatch; GPU time is not measured). Torch path: Collect + Evaluate
/// (legacy mode: sky samples are sources of the same path). Natural path (exposure model): Exposure (mask + diff),
/// Entries (opening detection), NaturalEvaluate (opening contributions), Direct (open-sky term). Shared: Recompose, Upload.</summary>
public readonly record struct V9ProbeFrameStats(bool Recentered, bool WrapShift, double RegionMs, bool OccupancyRebuilt, int ChangedCells,
    double OccupancyMs, bool ExposureRebuilt, int ExposureChangedCells, double ExposureMs, double CollectMs,
    int Sources, int SkySamples, int Torches, int Recalculated, int Evicted, double EvaluateMs,
    bool EntriesCollected, int NaturalEntries, double EntriesMs, int NaturalRecalculated, int NaturalEvicted, double NaturalEvaluateMs,
    int DirectPixels, double DirectMs, int RecomposedPixels, double RecomposeMs,
    double UploadMs, double TotalMs, int FieldWidth, int FieldHeight, Point Origin,
    bool SkyGlowRebuilt, bool SkyGlowReevaluated, int SkyGlowSeeds, int SkyGlowPixels, double SkyClassifyMs, double SkyPropagateMs, double SkyValueMs);

public readonly record struct V9ProbeVerification(long FieldBitsDifferent, long NaturalBitsDifferent, long ArtificialBitsDifferent,
    long SourceBitsDifferent, int SourcesChecked, long OutsideBoxNonZero, int MaskMismatches, int ExposureMismatches, bool EntryListMatches,
    long GlowBitsDifferent, int SurfaceSkyMismatches);

/// <summary>
/// Local V9 window for gameplay, one texel per world pixel, in the camera frame (continuous x; the world wraps).
/// The camera moves freely while the viewport stays inside; leaving it recentres the window. Contributions are cached
/// per source and anchored to the world over the square of the source's radius, so a recentre only recomposes the new
/// window, computes sources that entered and drops those that left. Math, DDA, parameters and shader are the lab's.
/// Natural light (Exposure model) and torches stay separate until the final per-pixel sum: natural first, then the
/// torches in list order, so wherever natural light is zero the torch field is bit-identical to a torch-only sum.
/// </summary>
public sealed class V9ProbeField : IDisposable
{
    public const int MarginX = 128, MarginY = 96;
    // Any ray of a source whose square touches the field stays inside field +/- 2R (+ corner side cells).
    private const int Reach = 2 * 196 + 16;

    /// <summary>Open-sky irradiance (scalar, times the lab's sky RGB): the lab's own natural irradiance on its aperture
    /// plane, right below its middle sky sample (five samples at (86+12i, 28), power 1.4, radius 196; point (110, 48)).
    /// The only new constant of the exposure model, anchored to the approved lab.</summary>
    public static readonly float OpenSky = LabApertureIrradiance();
    /// <summary>One opening entry per 8-px boundary cell carries the lab's linear sky power: 1.4 per 12 px of aperture.</summary>
    public static readonly float EntryPower = V9LabSettings.SkySamplePower * 8f / 12f;

    private sealed class Contribution
    {
        public V9Light Light;
        public Rectangle Box;
        public Vector3[] Values;
        public readonly V9Light[] Single = new V9Light[1];
        public bool Valid, Seen;
    }

    /// <summary>An opening entry's scalar contribution over the square of its radius, only for receivers outside O
    /// (inside O the open-sky term already applies); exact zero elsewhere.</summary>
    private sealed class NaturalEntry
    {
        public V9Light Light;
        public Rectangle Box;
        public float[] Values;
        public readonly V9Light[] Single = new V9Light[1];
        public bool Valid, Seen;
    }

    private readonly GraphicsDevice device;
    private readonly V9ProbeOccupancy occupancy = new();
    private readonly V9ProbeExposure exposure = new();
    private readonly V9ProbeSky sky = new();
    private readonly Dictionary<V9Light, Contribution> entries = new();
    private readonly Dictionary<V9Light, NaturalEntry> naturalEntries = new();
    private readonly List<V9Light> torchScratch = new(), sources = new(), naturalSources = new();
    private readonly List<Point> changed = new(), changedExposure = new();
    private HalfVector4[] upload = Array.Empty<HalfVector4>();
    private float[] direct = Array.Empty<float>(), entrySum = Array.Empty<float>();
    private readonly Func<int, int, bool> exposed;
    private readonly V9ProbeSkyGlow skyGlow = new();
    private V9SkyGlowSettings builtGlow;
    private bool glowBuilt;
    private Rectangle lastViewport;
    private bool hasWindow;

    public V9NaturalModel NaturalModel { get; }
    public bool IsBackplane => NaturalModel == V9NaturalModel.SkyBackplane;
    public int SurfaceStartRow { get; set; }
    public float BackplaneDepth(float row) => V9ProbeBackplane.DepthWeight(row, SurfaceStartRow, ShallowLayer.EndY + 1);
    private int backplaneRevision = -1;
    private Vector3[] backplane = Array.Empty<Vector3>();
    public float[] BackplaneForegroundGlow { get; private set; } = Array.Empty<float>();
    private float[] backplaneDepths = Array.Empty<float>();
    /// <summary>Sky Background Glow (exposure model only). Changing it rebuilds the glow on the next update; a change of the
    /// sun fields alone only re-evaluates it.</summary>
    public V9SkyGlowSettings GlowSettings { get; set; } = V9SkyGlowSettings.Default;
    /// <summary>The world's Shallow layer: where background openings are sky (set once from the session).</summary>
    public WorldLayerDefinition ShallowLayer { get; set; }
    /// <summary>Last row of the Surface layer: OpenAtmosphere at or above it is visible sky (the presentation's rule).</summary>
    public int SkyLayersEndRow { get; set; } = int.MinValue;
    /// <summary>Foreground response to visible sky (set once): solid faces next to a visible-sky tile (a Shallow
    /// background opening, or OpenAtmosphere in Space/Surface) take the open-sky term through the unchanged terminal
    /// section, exactly as faces next to the exposed region O already do. Off: --v9-probe-no-foreground-sky.</summary>
    public bool ForegroundSky { get; set; } = true;
    /// <summary>Glow per window texel (same indexing as Energy); diagnostic reads only.</summary>
    public Vector3[] SkyGlowBuffer => skyGlow.Glow;
    public int SkyGlowSeeds => skyGlow.Seeds;
    public int SkyShallowCells => skyGlow.SkyShallowCells;
    public V9SkyClass SkyClassAt(int x, int y) => NaturalModel != V9NaturalModel.PointSamples ? skyGlow.ClassAt(x, y) : V9SkyClass.Void;
    public float SkyWeightAt(int x, int y) => NaturalModel != V9NaturalModel.PointSamples ? skyGlow.WeightAt(x, y) : 0f;
    public bool IsSkySeed(int x, int y) => NaturalModel != V9NaturalModel.PointSamples && skyGlow.IsSeed(x, y);
    public float SkyGlowDistanceAt(int worldX, int worldY) => skyGlow.DistanceAt(worldX, worldY);
    public Point Origin { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public Vector3[] Energy { get; private set; } = Array.Empty<Vector3>();
    /// <summary>Diagnostic split of Energy, filled on demand by <see cref="ComputeDiagnosticSplit"/> (never per frame):
    /// natural part and torch-only sum from zero (not the operands of Energy's own sum).</summary>
    public Vector3[] NaturalEnergy { get; private set; } = Array.Empty<Vector3>();
    public Vector3[] ArtificialEnergy { get; private set; } = Array.Empty<Vector3>();
    public Texture2D Texture { get; private set; }
    public IReadOnlyList<V9Light> Sources => sources;
    public IReadOnlyList<V9Light> NaturalSources => naturalSources;
    public int SkySampleCount { get; private set; }
    public int ExposedCells => exposure.ExposedCells;
    public V9ProbeFrameStats Last { get; private set; }
    public int Recenters { get; private set; }
    public int WrapShifts { get; private set; }
    public long TotalRecalculated { get; private set; }
    public long OccupancyFallbackQueries => occupancy.FallbackQueries;
    public Rectangle OccupancyTiles => occupancy.Tiles;
    public long ContributionBytes { get; private set; }
    public long NaturalBytes { get; private set; }
    public int CachedContributions => entries.Count + naturalEntries.Count;
    public Rectangle Bounds => new(Origin.X, Origin.Y, Width, Height);

    public V9ProbeField(GraphicsDevice device, V9NaturalModel model = V9NaturalModel.Exposure)
    {
        this.device = device;
        NaturalModel = model;
        exposed = exposure.Exposed;
        skyOpening = (x, y) => SkyOpeningWeight(skyGlow, x, y);
    }

    private readonly Func<int, int, float> skyOpening;

    /// <summary>Visible-sky weight of a tile that is not in O, from the glow's classification (the same that shows the sky
    /// and seeds the glow): a Shallow opening's weight, 1 for OpenAtmosphere in Space/Surface, else 0.</summary>
    private float SkyOpeningWeight(V9ProbeSkyGlow classes, int x, int y) =>
        ForegroundSky ? SkyVisibleWeight(classes, SurfaceSkyCell, x, y) : 0;

    /// <summary>The one definition of visible sky outside O, shared by the presentation and the foreground response:
    /// a Shallow background opening at its weight; OpenAtmosphere in Space/Surface only where the natural system itself
    /// reaches it (see <see cref="UpdateSurfaceSky"/>); nothing in Cavern/Deep.</summary>
    private float SkyVisibleWeight(V9ProbeSkyGlow classes, Func<int, int, bool> surfaceSky, int x, int y)
    {
        if (!classes.Cells.Contains(x, y)) return 0;
        return classes.ClassAt(x, y) switch
        {
            V9SkyClass.SkyShallow => classes.WeightAt(x, y),
            V9SkyClass.Void when y <= SkyLayersEndRow && surfaceSky(x, y) => 1,
            _ => 0
        };
    }

    /// <summary>Visible sky of a tile, 0..1: 1 in O, else <see cref="SkyVisibleWeight"/>. The presentation's cover uses
    /// 1 - this below the sky floor; the foreground response uses the same weights.</summary>
    public float SkyVisibleAt(int x, int y) =>
        IsBackplane ? skyGlow.WeightAt(x, y) : NaturalModel != V9NaturalModel.Exposure || !exposure.Tiles.Contains(x, y) ? 0
            : exposure.Exposed(x, y) ? 1 : SkyVisibleWeight(skyGlow, SurfaceSkyCell, x, y);

    // Space/Surface OpenAtmosphere is sky only where an opening of the exposed region lights its tile centre (the
    // openings' own sum > 0). Sealed air has exactly zero there; open air no opening sees (beyond 196 px, or in shadow)
    // too. Derived from the opening caches already held; no flood fill.
    private bool[] surfaceSky = Array.Empty<bool>(), previousSurfaceSky = Array.Empty<bool>();
    private Rectangle surfaceSkyCells, previousSurfaceSkyCells;
    private readonly List<Point> changedSurfaceSky = new();

    private bool SurfaceSkyCell(int x, int y)
    {
        int lx = x - surfaceSkyCells.X, ly = y - surfaceSkyCells.Y;
        return (uint)lx < (uint)surfaceSkyCells.Width && (uint)ly < (uint)surfaceSkyCells.Height && surfaceSky[ly * surfaceSkyCells.Width + lx];
    }

    private float EntrySumAt(int wx, int wy)
    {
        float sum = 0;
        foreach (V9Light light in naturalSources)
        {
            var entry = naturalEntries[light];
            if (entry.Box.Contains(wx, wy)) sum += entry.Values[(wy - entry.Box.Y) * entry.Box.Width + wx - entry.Box.X];
        }
        return sum;
    }

    private void UpdateSurfaceSky()
    {
        (previousSurfaceSky, surfaceSky) = (surfaceSky, previousSurfaceSky);
        previousSurfaceSkyCells = surfaceSkyCells;
        surfaceSkyCells = new Rectangle(FloorDiv(Origin.X, 8) - 1, FloorDiv(Origin.Y, 8) - 1, Width / 8 + 2, Height / 8 + 2);
        if (surfaceSky.Length < surfaceSkyCells.Width * surfaceSkyCells.Height) surfaceSky = new bool[surfaceSkyCells.Width * surfaceSkyCells.Height];
        for (int y = 0, i = 0; y < surfaceSkyCells.Height; y++)
            for (int x = 0; x < surfaceSkyCells.Width; x++, i++)
            {
                int cx = surfaceSkyCells.X + x, cy = surfaceSkyCells.Y + y;
                surfaceSky[i] = cy <= SkyLayersEndRow && skyGlow.ClassAt(cx, cy) == V9SkyClass.Void && EntrySumAt(cx * 8 + 4, cy * 8 + 4) > 0;
            }
        changedSurfaceSky.Clear();
        Rectangle overlap = Rectangle.Intersect(previousSurfaceSkyCells, surfaceSkyCells);
        if (previousSurfaceSky.Length >= previousSurfaceSkyCells.Width * previousSurfaceSkyCells.Height)
            for (int y = overlap.Top; y < overlap.Bottom; y++)
                for (int x = overlap.Left; x < overlap.Right; x++)
                    if (surfaceSky[(y - surfaceSkyCells.Y) * surfaceSkyCells.Width + x - surfaceSkyCells.X] !=
                        previousSurfaceSky[(y - previousSurfaceSkyCells.Y) * previousSurfaceSkyCells.Width + x - previousSurfaceSkyCells.X])
                        changedSurfaceSky.Add(new Point(x, y));
    }

    public bool Solid(int x, int y) => occupancy.Solid(x, y);
    public bool ExposedCell(int x, int y) => NaturalModel == V9NaturalModel.Exposure && exposure.Exposed(x, y);
    public Rectangle ExposureTiles => exposure.Tiles;
    /// <summary>Diagnostic maps of the legacy mode: the exposure rule from world data, no cache.</summary>
    public static bool ExposedInWorld(WorldMap map, int x, int y, int envelopeRow) =>
        V9ProbeExposure.Compute(map, (a, b) => V9ProbeOccupancy.WorldSolid(map, a, b), x, y, envelopeRow);
    public int[] Envelope(WorldMap map, int firstColumn, int count) => sky.Envelope(map, firstColumn, count);
    public int[] SkyFloors(WorldMap map, int firstColumn, int count) => sky.Floors(map, firstColumn, count);

    /// <summary>Forces a full rebuild on the next update (teleports between scripted states).</summary>
    public void Reset() { entries.Clear(); naturalEntries.Clear(); hasWindow = false; ContributionBytes = 0; NaturalBytes = 0; }

    public void Update(WorldMap map, IReadOnlyList<TorchInstance> torches, Rectangle viewport)
    {
        long start = Stopwatch.GetTimestamp(), stage = start;
        int worldWidth = map.PixelWidth;
        bool exposureModel = NaturalModel == V9NaturalModel.Exposure;
        bool wrapShift = false, recentered = false, placed = false;
        if (hasWindow && Math.Abs(viewport.X - lastViewport.X) > worldWidth / 2)
        {
            // World wrap moved player and camera by whole worlds: same window, relabelled, nothing recomputed.
            int shift = (int)MathF.Round((viewport.X - lastViewport.X) / (float)worldWidth) * worldWidth;
            Origin = new Point(Origin.X + shift, Origin.Y);
            occupancy.ShiftX(shift / map.TileSize);
            if (exposureModel) { exposure.ShiftX(shift / map.TileSize); skyGlow.ShiftX(shift); }
            if (IsBackplane) skyGlow.ShiftX(shift);
            var shifted = new List<Contribution>(entries.Values);
            entries.Clear();
            foreach (var entry in shifted)
            {
                entry.Light = entry.Light with { Position = entry.Light.Position + new Vector2(shift, 0) };
                entry.Box.Offset(shift, 0);
                entry.Single[0] = entry.Light;
                entries[entry.Light] = entry;
            }
            var shiftedNatural = new List<NaturalEntry>(naturalEntries.Values);
            naturalEntries.Clear();
            foreach (var entry in shiftedNatural)
            {
                entry.Light = entry.Light with { Position = entry.Light.Position + new Vector2(shift, 0) };
                entry.Box.Offset(shift, 0);
                entry.Single[0] = entry.Light;
                naturalEntries[entry.Light] = entry;
            }
            wrapShift = true;
            WrapShifts++;
        }
        lastViewport = viewport;
        // Grow-only: a zoom-in (interior focus) fits in the current window without reallocating it every frame.
        int width = Math.Max(Width, Align(viewport.Width + 2 * MarginX)), height = Math.Max(Height, Align(viewport.Height + 2 * MarginY));
        if (!hasWindow || width != Width || height != Height || !Bounds.Contains(viewport))
        {
            if (width != Width || height != Height) Allocate(width, height);
            Origin = new Point(FloorAlign(viewport.Center.X - width / 2), FloorAlign(viewport.Center.Y - height / 2));
            recentered = hasWindow;
            if (recentered) Recenters++;
            hasWindow = true;
            placed = true;
        }
        double regionMs = Lap(ref stage);

        changed.Clear();
        int tile = map.TileSize;
        var tiles = new Rectangle(FloorDiv(Origin.X - Reach, tile), FloorDiv(Origin.Y - Reach, tile),
            (Width + 2 * Reach) / tile + 2, (Height + 2 * Reach) / tile + 2);
        bool occupancyRebuilt = occupancy.Update(map, tiles, changed);
        foreach (Point cell in changed)
        {
            var cellBox = new Rectangle(cell.X * tile, cell.Y * tile, tile, tile);
            foreach (var entry in entries.Values)
                if (entry.Box.Intersects(cellBox)) entry.Valid = false;
        }
        double occupancyMs = Lap(ref stage);

        changedExposure.Clear();
        bool exposureRebuilt = false;
        if (exposureModel)
        {
            exposureRebuilt = exposure.Update(map, tiles, sky, occupancy.Solid, changedExposure);
            // Opening entries trace through solids and are masked by exposure: either kind of change invalidates them.
            foreach (Point cell in changed) InvalidateNatural(cell, tile);
            foreach (Point cell in changedExposure) InvalidateNatural(cell, tile);
        }
        double exposureMs = Lap(ref stage);

        // Order: (legacy sky samples by x), torches by (y, x); opening entries row-major. Deterministic, independent of
        // placement history.
        sources.Clear();
        if (NaturalModel == V9NaturalModel.PointSamples) sky.Collect(map, Origin.X, Origin.X + Width, Origin.Y, Origin.Y + Height, sources);
        SkySampleCount = sources.Count;
        torchScratch.Clear();
        float centre = Origin.X + Width / 2f;
        for (int i = 0; i < torches.Count; i++)
        {
            Vector2 p = torches[i].LightOrigin;
            p.X += MathF.Round((centre - p.X) / worldWidth) * worldWidth;
            var light = new V9Light(p, V9LabSettings.FireLinearRgb, V9LabSettings.TorchPower, V9LabSettings.TorchRadiusPixels);
            if (SupportBox(light).Intersects(Bounds)) torchScratch.Add(light);
        }
        torchScratch.Sort((a, b) => a.Position.Y != b.Position.Y ? a.Position.Y.CompareTo(b.Position.Y) : a.Position.X.CompareTo(b.Position.X));
        sources.AddRange(torchScratch);
        double collectMs = Lap(ref stage);
        // The opening set only moves with the exposure rectangle/geometry, a new placement or a wrap relabel.
        bool entriesCollected = exposureModel && (exposureRebuilt || placed || wrapShift);
        if (entriesCollected) CollectNaturalEntries(naturalSources);
        double entriesMs = Lap(ref stage);

        // A new placement (first frame, recentre, reset, resize) recomposes the whole window; otherwise only the squares
        // of sources that were computed again, entered or left, and cells whose direct sky term changed.
        Rectangle dirty = placed ? Bounds : Rectangle.Empty;
        foreach (var entry in entries.Values) entry.Seen = false;
        int recalculated = 0, evicted = 0;
        Func<int, int, bool> solid = occupancy.Solid;
        foreach (V9Light light in sources)
        {
            if (!entries.TryGetValue(light, out var entry))
            {
                entry = new Contribution { Light = light, Box = SupportBox(light) };
                entry.Single[0] = light;
                entry.Values = new Vector3[entry.Box.Width * entry.Box.Height];
                ContributionBytes += entry.Values.Length * 12L;
                entries[light] = entry;
            }
            entry.Seen = true;
            if (entry.Valid) continue;
            Compute(entry, solid);
            recalculated++;
            dirty = Union(dirty, entry.Box);
        }
        if (entries.Count > sources.Count)
        {
            var dropped = new List<V9Light>();
            foreach (var pair in entries) if (!pair.Value.Seen) dropped.Add(pair.Key);
            foreach (var key in dropped)
            {
                dirty = Union(dirty, entries[key].Box);
                ContributionBytes -= entries[key].Values.Length * 12L;
                entries.Remove(key);
                evicted++;
            }
        }
        TotalRecalculated += recalculated;
        double evaluateMs = Lap(ref stage);

        int naturalRecalculated = 0, naturalEvicted = 0;
        if (exposureModel)
        {
            foreach (var entry in naturalEntries.Values) entry.Seen = false;
            foreach (V9Light light in naturalSources)
            {
                if (!naturalEntries.TryGetValue(light, out var entry))
                {
                    entry = new NaturalEntry { Light = light, Box = SupportBox(light) };
                    entry.Single[0] = light;
                    entry.Values = new float[entry.Box.Width * entry.Box.Height];
                    NaturalBytes += entry.Values.Length * 4L;
                    naturalEntries[light] = entry;
                }
                entry.Seen = true;
                if (entry.Valid) continue;
                ComputeNatural(entry, solid);
                naturalRecalculated++;
                dirty = Union(dirty, entry.Box);
            }
            if (naturalEntries.Count > naturalSources.Count)
            {
                var dropped = new List<V9Light>();
                foreach (var pair in naturalEntries) if (!pair.Value.Seen) dropped.Add(pair.Key);
                foreach (var key in dropped)
                {
                    dirty = Union(dirty, naturalEntries[key].Box);
                    NaturalBytes -= naturalEntries[key].Values.Length * 4L;
                    naturalEntries.Remove(key);
                    naturalEvicted++;
                }
            }
        }
        double naturalMs = Lap(ref stage);

        // Sky Background Glow: rebuilt with the exposure (geometry or rectangle), a new placement or new parameters; a
        // sun-only change re-evaluates the stored distances; a wrap relabel keeps it. Only texels whose glow changed recompose.
        // Before the direct term: its classification also says which tiles are visible sky for the foreground.
        bool glowRebuilt = false, glowReevaluated = false;
        if (IsBackplane && (placed || !glowBuilt || backplaneRevision != map.TileRevision || builtGlow != GlowSettings))
        {
            if (GlowSettings.Sun) throw new InvalidOperationException("Sky Backplane has no sun modulation.");
            skyGlow.Build(map, solid, (_, _) => false, ShallowLayer, Bounds, GlowSettings, BackplaneDepth);
            backplaneRevision = map.TileRevision;
            builtGlow = GlowSettings;
            glowBuilt = glowRebuilt = true;
            dirty = Bounds;
        }
        if (exposureModel)
        {
            if (!glowBuilt || exposureRebuilt || placed || GlowSettings.WithoutSun() != builtGlow.WithoutSun())
            {
                skyGlow.Build(map, solid, exposed, ShallowLayer, Bounds, GlowSettings);
                glowRebuilt = true;
            }
            else if (GlowSettings != builtGlow)
            {
                skyGlow.Evaluate(GlowSettings);
                glowReevaluated = true;
            }
            if (glowRebuilt || glowReevaluated)
            {
                builtGlow = GlowSettings;
                glowBuilt = true;
                dirty = Union(dirty, skyGlow.ChangedBounds);
            }
        }
        // Space/Surface visible sky from the natural system itself (needs the classification and the opening caches).
        bool surfaceSkyUpdated = false;
        if (exposureModel && (placed || wrapShift || glowRebuilt || entriesCollected || naturalRecalculated > 0 || naturalEvicted > 0))
        {
            UpdateSurfaceSky();
            surfaceSkyUpdated = true;
        }
        Lap(ref stage);

        int directPixels = 0;
        if (IsBackplane && glowRebuilt)
        {
            if (backplane.Length != Width * Height)
            {
                backplane = new Vector3[Width * Height];
                BackplaneForegroundGlow = new float[backplane.Length];
                backplaneDepths = new float[backplane.Length];
            }
            for (int y = 0, i = 0; y < Height; y++)
                for (int x = 0; x < Width; x++, i++)
                    backplane[i] = V9ProbeBackplane.Evaluate(new Vector2(Origin.X + x + .5f, Origin.Y + y + .5f),
                        skyGlow, solid, BackplaneDepth, ForegroundSky, out backplaneDepths[i], out BackplaneForegroundGlow[i]);
            directPixels = backplane.Length;
        }
        if (exposureModel)
        {
            Rectangle directRect = placed ? Bounds : Rectangle.Empty;
            // A solidity, exposure or visible-sky change moves the direct term of its own cell and of the faces around it.
            foreach (Point cell in changed) directRect = Union(directRect, new Rectangle((cell.X - 1) * tile, (cell.Y - 1) * tile, 3 * tile, 3 * tile));
            foreach (Point cell in changedExposure) directRect = Union(directRect, new Rectangle((cell.X - 1) * tile, (cell.Y - 1) * tile, 3 * tile, 3 * tile));
            if (glowRebuilt && ForegroundSky)
                foreach (Point cell in skyGlow.ChangedCells) directRect = Union(directRect, new Rectangle((cell.X - 1) * tile, (cell.Y - 1) * tile, 3 * tile, 3 * tile));
            if (surfaceSkyUpdated && ForegroundSky)
                foreach (Point cell in changedSurfaceSky) directRect = Union(directRect, new Rectangle((cell.X - 1) * tile, (cell.Y - 1) * tile, 3 * tile, 3 * tile));
            directRect = Rectangle.Intersect(directRect, Bounds);
            directPixels = directRect.Width * directRect.Height;
            if (directPixels > 0) ComputeDirect(directRect, solid);
            dirty = Union(dirty, directRect);
        }
        double directMs = Lap(ref stage);

        dirty = Rectangle.Intersect(dirty, Bounds);
        int recomposed = dirty.Width * dirty.Height;
        if (recomposed > 0) Recompose(dirty);
        double recomposeMs = Lap(ref stage);
        if (recomposed > 0) Upload(dirty);
        double uploadMs = Lap(ref stage);

        Last = new V9ProbeFrameStats(recentered, wrapShift, regionMs, occupancyRebuilt, changed.Count, occupancyMs,
            exposureRebuilt, changedExposure.Count, exposureMs, collectMs, sources.Count, SkySampleCount, torchScratch.Count,
            recalculated, evicted, evaluateMs, entriesCollected, naturalSources.Count, entriesMs, naturalRecalculated, naturalEvicted,
            naturalMs, directPixels, directMs, recomposed, recomposeMs, uploadMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            Width, Height, Origin, glowRebuilt, glowReevaluated, skyGlow.Seeds, skyGlow.GlowPixels,
            glowRebuilt ? skyGlow.ClassifyMilliseconds : 0, glowRebuilt ? skyGlow.PropagateMilliseconds : 0,
            glowRebuilt || glowReevaluated ? skyGlow.ValueMilliseconds : 0);
    }

    private void InvalidateNatural(Point cell, int tile)
    {
        var cellBox = new Rectangle(cell.X * tile, cell.Y * tile, tile, tile);
        foreach (var entry in naturalEntries.Values)
            if (entry.Box.Intersects(cellBox)) entry.Valid = false;
    }

    /// <summary>Openings: every exposed cell with a 4-neighbour that is air but not exposed. Row-major order.</summary>
    private void CollectNaturalEntries(List<V9Light> output)
    {
        output.Clear();
        Rectangle t = exposure.Tiles;
        for (int y = t.Top + 1; y < t.Bottom - 1; y++)
            for (int x = t.Left + 1; x < t.Right - 1; x++)
            {
                if (!exposure.Exposed(x, y)) continue;
                if (!Interior(x - 1, y) && !Interior(x + 1, y) && !Interior(x, y - 1) && !Interior(x, y + 1)) continue;
                var light = EntryLight(x, y);
                if (SupportBox(light).Intersects(Bounds)) output.Add(light);
            }
        bool Interior(int x, int y) => !exposure.Exposed(x, y) && !occupancy.Solid(x, y);
    }

    public static V9Light EntryLight(int cellX, int cellY) =>
        new(new Vector2(cellX * 8 + 4, cellY * 8 + 4), Vector3.One, EntryPower, V9LabSettings.SkyRadiusPixels);

    // Exactly the lab's single-source evaluation, restricted to the square of the radius. Every receiver centre outside
    // it is at least R + 0.5 px from the source, where Evaluate already returns exact zero.
    private static void Compute(Contribution entry, Func<int, int, bool> solid)
    {
        Rectangle box = entry.Box;
        Vector3[] values = entry.Values;
        for (int y = 0, i = 0; y < box.Height; y++)
            for (int x = 0; x < box.Width; x++, i++)
                values[i] = V9LightMath.Evaluate(new Vector2(box.X + x + .5f, box.Y + y + .5f), entry.Single, solid);
        entry.Valid = true;
    }

    // The same unchanged evaluator with a white source: X is the scalar sky contribution. Receivers inside O are skipped
    // (the open-sky term dominates there); a change of exposure inside the square invalidates the entry.
    private void ComputeNatural(NaturalEntry entry, Func<int, int, bool> solid)
    {
        Rectangle box = entry.Box;
        float[] values = entry.Values;
        for (int y = 0, i = 0; y < box.Height; y++)
        {
            int wy = box.Y + y, cy = FloorDiv(wy, 8);
            for (int x = 0; x < box.Width; x++, i++)
            {
                int wx = box.X + x;
                values[i] = exposure.Exposed(FloorDiv(wx, 8), cy) ? 0f :
                    V9LightMath.Evaluate(new Vector2(wx + .5f, wy + .5f), entry.Single, solid).X;
            }
        }
        entry.Valid = true;
    }

    // Open-sky term: OpenSky inside O; for a solid pixel, the best exposed 4-neighbour seen through the unchanged
    // terminal-section rule (Visibility from that cell's centre: depth <= 6 px, absorption 2.8 px, incidence); else 0.
    // Decided per cell; only solid cells facing O go per pixel. Same values as DirectAt per pixel (what Verify uses).
    private void ComputeDirect(Rectangle rect, Func<int, int, bool> solid)
    {
        int firstRow = FloorDiv(rect.Top, 8), lastRow = FloorDiv(rect.Bottom - 1, 8);
        int firstColumn = FloorDiv(rect.Left, 8), lastColumn = FloorDiv(rect.Right - 1, 8);
        for (int cy = firstRow; cy <= lastRow; cy++)
        {
            int top = Math.Max(rect.Top, cy * 8), bottom = Math.Min(rect.Bottom, cy * 8 + 8);
            for (int cx = firstColumn; cx <= lastColumn; cx++)
            {
                int left = Math.Max(rect.Left, cx * 8), right = Math.Min(rect.Right, cx * 8 + 8);
                bool open = exposed(cx, cy);
                bool face = !open && solid(cx, cy) && (Lit(cx, cy - 1) || Lit(cx - 1, cy) || Lit(cx + 1, cy) || Lit(cx, cy + 1));
                for (int y = top; y < bottom; y++)
                {
                    int i = (y - Origin.Y) * Width + left - Origin.X;
                    if (!face) { Array.Fill(direct, open ? OpenSky : 0f, i, right - left); continue; }
                    for (int x = left; x < right; x++, i++) direct[i] = DirectAt(cx, cy, new Vector2(x + .5f, y + .5f), exposed, skyOpening, solid);
                }
            }
        }
        bool Lit(int x, int y) => exposed(x, y) || skyOpening(x, y) > 0;
    }

    // A solid pixel takes the best 4-neighbour that is sky: O at the open-sky value (as before), or a visible-sky tile at
    // open sky x its weight, always through the unchanged terminal section from that tile's centre (<= 6 px, 2.8 px
    // absorption, incidence). Joined by maximum: never stacked over the same light.
    private static float DirectAt(int cx, int cy, Vector2 p, Func<int, int, bool> exposed, Func<int, int, float> skyOpening, Func<int, int, bool> solid)
    {
        if (exposed(cx, cy)) return OpenSky;
        if (!solid(cx, cy)) return 0;
        float best = 0;
        best = Face(best, cx, cy - 1, new Vector2(cx * 8 + 4, cy * 8 - 4));
        best = Face(best, cx - 1, cy, new Vector2(cx * 8 - 4, cy * 8 + 4));
        best = Face(best, cx + 1, cy, new Vector2(cx * 8 + 12, cy * 8 + 4));
        best = Face(best, cx, cy + 1, new Vector2(cx * 8 + 4, cy * 8 + 12));
        return best;
        float Face(float current, int nx, int ny, Vector2 from)
        {
            if (exposed(nx, ny)) return MathF.Max(current, OpenSky * V9LightMath.Visibility(from, p, solid));
            float weight = skyOpening(nx, ny);
            return weight > 0 ? MathF.Max(current, OpenSky * weight * V9LightMath.Visibility(from, p, solid)) : current;
        }
    }

    private static Vector3 Natural(float direct, float entries) => V9LabSettings.SkyLinearRgb * MathF.Max(direct, MathF.Min(entries, OpenSky));

    // The Sky Background Glow joins the natural term by maximum, as the natural term already joins open sky and openings:
    // it never stacks over light that already reaches a texel. Where the glow is zero the natural term is untouched.
    private static Vector3 WithGlow(Vector3 natural, Vector3[] glow, int i) =>
        i >= glow.Length || glow[i] == Vector3.Zero ? natural : Vector3.Max(natural, glow[i]);

    // From zero, per pixel: natural first, then the torches in list order. Where natural light is zero this is exactly
    // the torch-only sequence of additions (legacy mode: the previous single sum, sky samples first).
    private void Recompose(Rectangle dirty)
    {
        if (IsBackplane)
        {
            for (int y = dirty.Top; y < dirty.Bottom; y++)
            {
                int i = (y - Origin.Y) * Width + dirty.Left - Origin.X;
                Array.Copy(backplane, i, Energy, i, dirty.Width);
            }
            foreach (V9Light light in sources) AddSource(entries[light], dirty, Energy);
            return;
        }
        if (NaturalModel == V9NaturalModel.PointSamples)
        {
            for (int y = dirty.Top; y < dirty.Bottom; y++)
                Array.Fill(Energy, Vector3.Zero, (y - Origin.Y) * Width + dirty.Left - Origin.X, dirty.Width);
            foreach (V9Light light in sources) AddSource(entries[light], dirty, Energy);
            return;
        }
        for (int y = dirty.Top; y < dirty.Bottom; y++)
            Array.Fill(entrySum, 0f, (y - Origin.Y) * Width + dirty.Left - Origin.X, dirty.Width);
        foreach (V9Light light in naturalSources)
        {
            var entry = naturalEntries[light];
            Rectangle overlap = Rectangle.Intersect(entry.Box, dirty);
            for (int y = overlap.Top; y < overlap.Bottom; y++)
            {
                int f = (y - Origin.Y) * Width + overlap.Left - Origin.X;
                int b = (y - entry.Box.Y) * entry.Box.Width + overlap.Left - entry.Box.X;
                for (int x = 0; x < overlap.Width; x++) entrySum[f + x] += entry.Values[b + x];
            }
        }
        for (int y = dirty.Top; y < dirty.Bottom; y++)
            for (int i = (y - Origin.Y) * Width + dirty.Left - Origin.X, end = i + dirty.Width; i < end; i++)
                Energy[i] = WithGlow(Natural(direct[i], entrySum[i]), skyGlow.Glow, i);
        foreach (V9Light light in sources) AddSource(entries[light], dirty, Energy);
    }

    private void AddSource(Contribution entry, Rectangle dirty, Vector3[] target)
    {
        Rectangle overlap = Rectangle.Intersect(entry.Box, dirty);
        for (int y = overlap.Top; y < overlap.Bottom; y++)
        {
            int f = (y - Origin.Y) * Width + overlap.Left - Origin.X;
            int b = (y - entry.Box.Y) * entry.Box.Width + overlap.Left - entry.Box.X;
            for (int x = 0; x < overlap.Width; x++) target[f + x] += entry.Values[b + x];
        }
    }

    /// <summary>Diagnostic only, on demand (captures): fills NaturalEnergy and ArtificialEnergy over the whole window
    /// from the same caches and per-pixel terms the frame used. Natural = the natural part of Energy (legacy: the sky
    /// samples' sum from zero); Artificial = the torches' sum from zero, in list order.</summary>
    public void ComputeDiagnosticSplit()
    {
        Array.Fill(ArtificialEnergy, Vector3.Zero);
        if (IsBackplane)
        {
            Array.Copy(backplane, NaturalEnergy, backplane.Length);
            foreach (V9Light light in sources) AddSource(entries[light], Bounds, ArtificialEnergy);
            return;
        }
        if (NaturalModel == V9NaturalModel.PointSamples)
        {
            Array.Fill(NaturalEnergy, Vector3.Zero);
            for (int s = 0; s < sources.Count; s++)
                AddSource(entries[sources[s]], Bounds, s < SkySampleCount ? NaturalEnergy : ArtificialEnergy);
            return;
        }
        for (int i = 0; i < NaturalEnergy.Length; i++) NaturalEnergy[i] = WithGlow(Natural(direct[i], entrySum[i]), skyGlow.Glow, i);
        foreach (V9Light light in sources) AddSource(entries[light], Bounds, ArtificialEnergy);
    }

    private void Upload(Rectangle dirty)
    {
        for (int y = dirty.Top; y < dirty.Bottom; y++)
            for (int x = dirty.Left, i = (y - Origin.Y) * Width + dirty.Left - Origin.X; x < dirty.Right; x++, i++)
            {
                var e = Energy[i];
                // Presentation only: soften direct sky's terminal edge and corners inside its existing support.
                // Keep the cached field, transported glow and additive torch contribution unchanged.
                if (IsBackplane && backplaneDepths[i] > 0 && BackplaneForegroundGlow[i] == 0 && backplane[i].Z > 0)
                {
                    float shown = SmoothBackplaneSurface(x, y, i);
                    e -= backplane[i] * (1f - shown / backplane[i].Z);
                }
                float peak = MathF.Max(e.X, MathF.Max(e.Y, e.Z));
                if (!float.IsFinite(peak) || peak >= 65000) throw new InvalidOperationException("V9 irradiance exceeds half texture range; do not silently clamp.");
                upload[i] = new HalfVector4(e.X, e.Y, e.Z, 1);
            }
        Texture.SetData(upload);
    }

    private float BackplaneSurfacePresentation(int i)
    {
        const float featherPixels = 2f;
        float t = Math.Clamp((backplaneDepths[i] - (V9LabSettings.SurfaceDepthPixels - featherPixels)) / featherPixels, 0f, 1f);
        return backplane[i].Z * (1f - t * t * (3f - 2f * t));
    }

    private float SmoothBackplaneSurface(int worldX, int worldY, int i)
    {
        float center = BackplaneSurfacePresentation(i);
        float sum = center * 4f, weights = 4f;
        // One world-pixel binomial kernel, sampled from the unfiltered field (never recursive).
        // Only rock participates: sky/air and transported glow must not bleed across its silhouette.
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int x = worldX + dx, y = worldY + dy;
                if (!Bounds.Contains(x, y) || !Rock(x, y)) continue;
                if (dx != 0 && dy != 0 && (!Rock(x, worldY) || !Rock(worldX, y))) continue;
                int neighbor = i + dy * Width + dx;
                if (BackplaneForegroundGlow[neighbor] > 0) continue;
                float weight = dx == 0 || dy == 0 ? 2f : 1f;
                sum += BackplaneSurfacePresentation(neighbor) * weight;
                weights += weight;
            }
        // Round toward the lit side only; retain exact darkness and the original light reach.
        return MathF.Min(center, sum / weights);

        bool Rock(int x, int y) => skyGlow.ClassAt(FloorDiv(x, 8), FloorDiv(y, 8)) == V9SkyClass.Foreground;
    }

    /// <summary>Diagnostic only (captures): the foreground response to visible sky per window texel, i.e. the open-sky term
    /// with the visible-sky tiles minus the same term with O alone, on solid texels; the count of affected texels and the
    /// histogram of their perpendicular depth (whole pixels) from the face that lights them.</summary>
    public (float[] Delta, int Affected, int[] DepthHistogram, float MaxDepth, float[] Depth) ForegroundSkyDiagnostic()
    {
        var delta = new float[Width * Height];
        var depths = new float[Width * Height];
        var histogram = new int[9];
        int affected = 0;
        float maxDepth = 0;
        if (IsBackplane)
        {
            for (int i = 0; i < backplane.Length; i++)
            {
                if (backplaneDepths[i] <= 0 || backplane[i] == Vector3.Zero) continue;
                delta[i] = backplane[i].Z; depths[i] = backplaneDepths[i]; affected++;
                histogram[Math.Min(8, (int)depths[i])]++;
                maxDepth = MathF.Max(maxDepth, depths[i]);
            }
            return (delta, affected, histogram, maxDepth, depths);
        }
        if (NaturalModel != V9NaturalModel.Exposure) return (delta, 0, histogram, 0, depths);
        Func<int, int, bool> solid = occupancy.Solid;
        Func<int, int, float> none = (x, y) => 0f;
        for (int y = 0, i = 0; y < Height; y++)
        {
            int wy = Origin.Y + y, cy = FloorDiv(wy, 8);
            for (int x = 0; x < Width; x++, i++)
            {
                int wx = Origin.X + x, cx = FloorDiv(wx, 8);
                if (!solid(cx, cy)) continue;
                var p = new Vector2(wx + .5f, wy + .5f);
                float with = DirectAt(cx, cy, p, exposed, skyOpening, solid), without = DirectAt(cx, cy, p, exposed, none, solid);
                if (with <= without) continue;
                delta[i] = with - without;
                affected++;
                float depth = float.MaxValue;
                Check(cx, cy - 1, new Vector2(cx * 8 + 4, cy * 8 - 4), p.Y - cy * 8);
                Check(cx - 1, cy, new Vector2(cx * 8 - 4, cy * 8 + 4), p.X - cx * 8);
                Check(cx + 1, cy, new Vector2(cx * 8 + 12, cy * 8 + 4), cx * 8 + 8 - p.X);
                Check(cx, cy + 1, new Vector2(cx * 8 + 4, cy * 8 + 12), cy * 8 + 8 - p.Y);
                maxDepth = MathF.Max(maxDepth, depth);
                depths[i] = depth;
                histogram[Math.Min(8, (int)depth)]++;
                void Check(int nx, int ny, Vector2 from, float faceDepth)
                {
                    float weight = exposed(nx, ny) ? 0 : skyOpening(nx, ny);
                    if (weight > 0 && OpenSky * weight * V9LightMath.Visibility(from, p, solid) == with) depth = MathF.Min(depth, faceDepth);
                }
            }
        }
        return (delta, affected, histogram, maxDepth, depths);
    }

    /// <summary>Diagnostic only; not part of the measured frame.</summary>
    public float ComputePeak()
    {
        float peak = 0;
        foreach (var e in Energy) peak = MathF.Max(peak, MathF.Max(e.X, MathF.Max(e.Y, e.Z)));
        return peak;
    }

    private void Allocate(int width, int height)
    {
        Width = width; Height = height;
        Energy = new Vector3[width * height];
        NaturalEnergy = new Vector3[width * height];
        ArtificialEnergy = new Vector3[width * height];
        direct = new float[width * height];
        entrySum = new float[width * height];
        upload = new HalfVector4[width * height];
        Texture?.Dispose();
        Texture = new Texture2D(device, width, height, false, SurfaceFormat.HalfVector4);
    }

    public static Rectangle SupportBox(V9Light light)
    {
        int left = (int)MathF.Floor(light.Position.X - light.Radius) - 1, top = (int)MathF.Floor(light.Position.Y - light.Radius) - 1;
        int right = (int)MathF.Ceiling(light.Position.X + light.Radius) + 1, bottom = (int)MathF.Ceiling(light.Position.Y + light.Radius) + 1;
        return new Rectangle(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Diagnostic, expensive; everything against the world's own solidity, independent of masks, caches and boxes.
    /// Artificial: torch-only sum per pixel. Natural (exposure): exposure, openings and the direct term recomputed from
    /// scratch, entries evaluated over the whole field. Field: natural then torches in order (legacy: the whole list).
    /// Sources: every torch and a stride of openings/sky samples, exact inside their square, exact zero outside.
    /// </summary>
    public V9ProbeVerification Verify(WorldMap map, int sourceStride)
    {
        ComputeDiagnosticSplit();
        Func<int, int, bool> world = (x, y) => V9ProbeOccupancy.WorldSolid(map, x, y);
        var torchList = new List<V9Light>();
        for (int s = SkySampleCount; s < sources.Count; s++) torchList.Add(sources[s]);
        var single = new V9Light[1];
        long fieldBits = 0, naturalBits = 0, artificialBits = 0, sourceBits = 0, outsideNonZero = 0;
        int exposureMismatches = 0;
        bool entryListMatches = true;

        Func<int, int, bool> exposedRef = null;
        var referenceGlow = new V9ProbeSkyGlow();
        // Reference Space/Surface visible sky: the reference openings evaluated at each tile centre over world solidity.
        bool[] surfaceSkyReference = Array.Empty<bool>();
        Rectangle surfaceSkyReferenceCells = surfaceSkyCells;
        Func<int, int, bool> surfaceSkyRef = (x, y) =>
        {
            int lx = x - surfaceSkyReferenceCells.X, ly = y - surfaceSkyReferenceCells.Y;
            return (uint)lx < (uint)surfaceSkyReferenceCells.Width && (uint)ly < (uint)surfaceSkyReferenceCells.Height && surfaceSkyReference[ly * surfaceSkyReferenceCells.Width + lx];
        };
        Func<int, int, float> skyOpeningRef = (x, y) => ForegroundSky ? SkyVisibleWeight(referenceGlow, surfaceSkyRef, x, y) : 0;
        int surfaceSkyMismatches = 0;
        var referenceEntries = new List<V9Light>();
        if (IsBackplane) referenceGlow.Build(map, world, (_, _) => false, ShallowLayer, Bounds, GlowSettings, BackplaneDepth);
        if (NaturalModel == V9NaturalModel.Exposure)
        {
            var freshSky = new V9ProbeSky();
            Rectangle t = exposure.Tiles;
            int[] envelope = freshSky.Envelope(map, t.X, t.Width);
            var reference = new bool[t.Width * t.Height];
            for (int y = 0, i = 0; y < t.Height; y++)
                for (int x = 0; x < t.Width; x++, i++)
                {
                    reference[i] = V9ProbeExposure.Compute(map, world, t.X + x, t.Y + y, envelope[x]);
                    if (reference[i] != exposure.Exposed(t.X + x, t.Y + y)) exposureMismatches++;
                }
            exposedRef = (x, y) => reference[(y - t.Y) * t.Width + x - t.X];
            for (int y = t.Top + 1; y < t.Bottom - 1; y++)
                for (int x = t.Left + 1; x < t.Right - 1; x++)
                {
                    if (!exposedRef(x, y)) continue;
                    bool Interior(int ix, int iy) => !exposedRef(ix, iy) && !world(ix, iy);
                    if (!Interior(x - 1, y) && !Interior(x + 1, y) && !Interior(x, y - 1) && !Interior(x, y + 1)) continue;
                    var light = EntryLight(x, y);
                    if (SupportBox(light).Intersects(Bounds)) referenceEntries.Add(light);
                }
            entryListMatches = referenceEntries.Count == naturalSources.Count;
            for (int k = 0; entryListMatches && k < referenceEntries.Count; k++) entryListMatches = referenceEntries[k] == naturalSources[k];
            // The glow from scratch: classification, seeds and propagation over world solidity and the fresh exposure.
            referenceGlow.Build(map, world, exposedRef, ShallowLayer, Bounds, GlowSettings);
            surfaceSkyReference = new bool[surfaceSkyReferenceCells.Width * surfaceSkyReferenceCells.Height];
            for (int y = 0, i = 0; y < surfaceSkyReferenceCells.Height; y++)
                for (int x = 0; x < surfaceSkyReferenceCells.Width; x++, i++)
                {
                    int cx = surfaceSkyReferenceCells.X + x, cy = surfaceSkyReferenceCells.Y + y;
                    if (cy > SkyLayersEndRow || referenceGlow.ClassAt(cx, cy) != V9SkyClass.Void) continue;
                    var centre = new Vector2(cx * 8 + 4.5f, cy * 8 + 4.5f);
                    float sum = 0;
                    foreach (V9Light light in referenceEntries) { single[0] = light; sum += V9LightMath.Evaluate(centre, single, world).X; }
                    surfaceSkyReference[i] = sum > 0;
                    if (surfaceSkyReference[i] != SurfaceSkyCell(cx, cy)) surfaceSkyMismatches++;
                }
        }

        for (int y = 0, i = 0; y < Height; y++)
            for (int x = 0; x < Width; x++, i++)
            {
                var p = new Vector2(Origin.X + x + .5f, Origin.Y + y + .5f);
                artificialBits += Bits(V9LightMath.Evaluate(p, torchList, world), ArtificialEnergy[i]);
                Vector3 expected;
                if (IsBackplane)
                {
                    Vector3 natural = V9ProbeBackplane.Evaluate(p, referenceGlow, world, BackplaneDepth, ForegroundSky, out _, out _);
                    naturalBits += Bits(natural, NaturalEnergy[i]);
                    expected = natural;
                    foreach (V9Light light in torchList) { single[0] = light; expected += V9LightMath.Evaluate(p, single, world); }
                }
                else if (NaturalModel == V9NaturalModel.Exposure)
                {
                    float entrySumReference = 0;
                    foreach (V9Light light in referenceEntries) { single[0] = light; entrySumReference += V9LightMath.Evaluate(p, single, world).X; }
                    float directReference = DirectAt(FloorDiv(Origin.X + x, 8), FloorDiv(Origin.Y + y, 8), p, exposedRef, skyOpeningRef, world);
                    Vector3 natural = WithGlow(Natural(directReference, entrySumReference), referenceGlow.Glow, i);
                    naturalBits += Bits(natural, NaturalEnergy[i]);
                    expected = natural;
                    foreach (V9Light light in torchList) { single[0] = light; expected += V9LightMath.Evaluate(p, single, world); }
                }
                else expected = V9LightMath.Evaluate(p, sources, world);
                fieldBits += Bits(expected, Energy[i]);
            }

        int checkedSources = 0;
        for (int s = 0; s < sources.Count; s++)
        {
            if (s < SkySampleCount && s % sourceStride != 0) continue;
            var entry = entries[sources[s]];
            checkedSources++;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int wx = Origin.X + x, wy = Origin.Y + y;
                    Vector3 expected = V9LightMath.Evaluate(new Vector2(wx + .5f, wy + .5f), entry.Single, world);
                    if (entry.Box.Contains(wx, wy)) sourceBits += Bits(expected, entry.Values[(wy - entry.Box.Y) * entry.Box.Width + wx - entry.Box.X]);
                    else if (expected != Vector3.Zero) outsideNonZero++;
                }
        }
        for (int s = 0; s < naturalSources.Count; s += sourceStride)
        {
            var entry = naturalEntries[naturalSources[s]];
            checkedSources++;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int wx = Origin.X + x, wy = Origin.Y + y;
                    float expected = V9LightMath.Evaluate(new Vector2(wx + .5f, wy + .5f), entry.Single, world).X;
                    if (!entry.Box.Contains(wx, wy)) { if (expected != 0) outsideNonZero++; continue; }
                    float cached = entry.Values[(wy - entry.Box.Y) * entry.Box.Width + wx - entry.Box.X];
                    float wanted = exposedRef(FloorDiv(wx, 8), FloorDiv(wy, 8)) ? 0f : expected;
                    if (BitConverter.SingleToInt32Bits(cached) != BitConverter.SingleToInt32Bits(wanted)) sourceBits++;
                }
        }
        long glowBits = 0;
        if (NaturalModel == V9NaturalModel.Exposure || IsBackplane)
            for (int i = 0; i < referenceGlow.Glow.Length; i++) glowBits += Bits(referenceGlow.Glow[i], skyGlow.Glow[i]);
        return new V9ProbeVerification(fieldBits, naturalBits, artificialBits, sourceBits, checkedSources, outsideNonZero,
            occupancy.CountMismatches(), exposureMismatches, entryListMatches, glowBits, surfaceSkyMismatches);
        static int Bits(Vector3 a, Vector3 b) =>
            (BitConverter.SingleToInt32Bits(a.X) != BitConverter.SingleToInt32Bits(b.X) ? 1 : 0) +
            (BitConverter.SingleToInt32Bits(a.Y) != BitConverter.SingleToInt32Bits(b.Y) ? 1 : 0) +
            (BitConverter.SingleToInt32Bits(a.Z) != BitConverter.SingleToInt32Bits(b.Z) ? 1 : 0);
    }

    private static float LabApertureIrradiance()
    {
        float e = 0;
        for (int i = 0; i < 5; i++)
        {
            float falloff = 1 - Vector2.Distance(new Vector2(110, 48), new Vector2(86 + 12 * i, 28)) / V9LabSettings.SkyRadiusPixels;
            e += V9LabSettings.SkySamplePower * falloff * falloff;
        }
        return e;
    }

    private static Rectangle Union(Rectangle a, Rectangle b) => a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b);
    private static int Align(int v) => (v + 7) / 8 * 8;
    private static int FloorAlign(int v) => FloorDiv(v, 8) * 8;
    private static int FloorDiv(int a, int b) => V9ProbeSky.FloorDiv(a, b);
    private static double Lap(ref long stage)
    {
        long now = Stopwatch.GetTimestamp();
        double ms = (now - stage) * 1000.0 / Stopwatch.Frequency;
        stage = now;
        return ms;
    }

    public void Dispose() => Texture?.Dispose();
}
