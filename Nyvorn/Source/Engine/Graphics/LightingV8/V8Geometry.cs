using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nyvorn.Source.World;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.World.Objects;
using Nyvorn.Source.Engine.Physics.Sand;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

public readonly record struct V8Light(Vector2 Position, Vector3 Radiance, float Radius,
    float AmbientIntensity = .24f, float AmbientRadius = 256);
public readonly record struct V8Edge(Vector2 A, Vector2 B, Vector2 Normal);

/// <summary>Reusable world adapter and union contours. Background is deliberately absent from occupancy.
/// Geometry is rebuilt only when the region, tiles or supplied objects change. No GPU readback.
/// Occupancy is one bit per art pixel in 64-bit words per row; contours are extracted with bit-parallel row masks,
/// producing the same merged edges as a per-pixel scan (list order differs; the stencil union does not depend on it).</summary>
public sealed class V8Geometry
{
    private ulong[] bits = Array.Empty<ulong>();
    private int words;
    private ulong[] previousLeft = Array.Empty<ulong>(), previousRight = Array.Empty<ulong>();
    private ulong[] currentLeft = Array.Empty<ulong>(), currentRight = Array.Empty<ulong>();
    private int[] leftStart = Array.Empty<int>(), rightStart = Array.Empty<int>();
    private int revision = -1;
    private int objectHash;
    private WorldMap previousMap;
    private readonly List<Rectangle> sandRuns = new();
    public Rectangle Bounds { get; private set; }
    public List<V8Edge> Edges { get; } = new();
    public int Generation { get; private set; }
    // Timing of the last Update: sand segment collection + cache key, occupancy fill, contour extraction.
    public double LastSandHashMs { get; private set; }
    public double LastFillMs { get; private set; }
    public double LastEdgesMs { get; private set; }
    public bool LastRebuilt { get; private set; }

    /// <summary>Forces a rebuild on the next Update, e.g. when V8 is reactivated after edits made while inactive.</summary>
    public void Invalidate() => previousMap = null;

    public bool IsSolid(int x, int y)
    {
        int lx = x - Bounds.X, ly = y - Bounds.Y;
        if ((uint)lx >= (uint)Bounds.Width || (uint)ly >= (uint)Bounds.Height) return false;
        return ((bits[ly * words + (lx >> 6)] >> (lx & 63)) & 1UL) != 0;
    }

    public void Update(WorldMap map, IReadOnlyList<PlatformInstance> platforms,
        IReadOnlyList<DoorInstance> doors, Rectangle bounds, SandSystem sand = null)
    {
        long stage = System.Diagnostics.Stopwatch.GetTimestamp();
        LastRebuilt = false; LastFillMs = 0; LastEdgesMs = 0;
        var hash = new HashCode();
        sandRuns.Clear();
        if (sand != null)
        {
            int first = (int)MathF.Floor(bounds.Left / (float)map.PixelWidth);
            int last = (int)MathF.Floor((bounds.Right - 1) / (float)map.PixelWidth);
            for (int k = first; k <= last; k++)
                foreach (var run in sand.GetVisibleSegments(Math.Max(0, bounds.Left - k * map.PixelWidth),
                    Math.Min(map.PixelWidth - 1, bounds.Right - 1 - k * map.PixelWidth), bounds.Top, bounds.Bottom - 1))
                {
                    var shifted = new Rectangle(run.X + k * map.PixelWidth, run.Y, run.Width, run.Height);
                    sandRuns.Add(shifted); hash.Add(shifted);
                }
        }
        if (platforms != null) foreach (var p in platforms) hash.Add(p.SurfaceBounds);
        if (doors != null) foreach (var d in doors) { hash.Add(d.Bounds); hash.Add(d.IsOpen); }
        int nextHash = hash.ToHashCode();
        LastSandHashMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        if (previousMap == map && revision == map.TileRevision && objectHash == nextHash && Bounds == bounds) return;
        stage = System.Diagnostics.Stopwatch.GetTimestamp();
        previousMap = map; revision = map.TileRevision; objectHash = nextHash; Bounds = bounds;
        words = (bounds.Width + 63) >> 6;
        int count = words * bounds.Height;
        if (bits.Length < count) bits = new ulong[count];
        Array.Clear(bits, 0, count);
        int size = map.TileSize;
        int left = (int)MathF.Floor(bounds.Left / (float)size);
        int right = (int)MathF.Ceiling(bounds.Right / (float)size);
        int top = Math.Max(0, (int)MathF.Floor(bounds.Top / (float)size));
        int bottom = Math.Min(map.Height, (int)MathF.Ceiling(bounds.Bottom / (float)size));
        for (int y = top; y < bottom; y++)
        {
            // Consecutive full tiles fill as one rectangle: the same union as filling each tile.
            int runStart = int.MinValue;
            for (int x = left; x <= right; x++)
            {
                var tile = x < right ? map.GetTile(map.WrapTileX(x), y) : TileType.Empty;
                bool full = tile != TileType.Empty && tile != TileType.Platform;
                if (tile == TileType.Platform) Fill(new Rectangle(x * size, y * size, size, 3));
                if (full && runStart == int.MinValue) runStart = x;
                if (!full && runStart != int.MinValue)
                {
                    Fill(new Rectangle(runStart * size, y * size, (x - runStart) * size, size));
                    runStart = int.MinValue;
                }
            }
        }
        if (platforms != null) foreach (var p in platforms) FillWrapped(p.SurfaceBounds, map.PixelWidth);
        if (doors != null) foreach (var d in doors) if (!d.IsOpen) FillWrapped(d.Bounds, map.PixelWidth);
        foreach (var run in sandRuns) Fill(run);
        LastFillMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        stage = System.Diagnostics.Stopwatch.GetTimestamp();
        Edges.Clear();
        // Merge adjacent unit edges; internal tile/object boundaries disappear in the union.
        int height = bounds.Height;
        for (int y = 0; y < height; y++)
        {
            int row = y * words;
            HorizontalRuns(y, row, y > 0 ? row - words : -1, -1);
            HorizontalRuns(y, row, y + 1 < height ? row + words : -1, 1);
        }
        VerticalEdges(height);
        LastEdgesMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        LastRebuilt = true;
        Generation++;
    }

    /// <summary>Copies the occupancy of <paramref name="rect"/> as ceil(width/64) words per row (bit 0 = rect.Left).
    /// False when the rectangle is not fully inside the current bounds.</summary>
    public bool TryCopyOccupancy(Rectangle rect, ulong[] destination)
    {
        if (!Bounds.Contains(rect)) return false;
        int k = 0;
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            int row = (y - Bounds.Y) * words;
            for (int start = rect.Left - Bounds.X, remaining = rect.Width; remaining > 0; start += 64, remaining -= 64)
                destination[k++] = ReadBits(row, start, Math.Min(64, remaining));
        }
        return true;
    }

    private ulong ReadBits(int row, int start, int count)
    {
        int word = start >> 6, shift = start & 63;
        ulong value = bits[row + word] >> shift;
        if (shift != 0 && shift + count > 64) value |= bits[row + word + 1] << (64 - shift);
        return count == 64 ? value : value & ((1UL << count) - 1);
    }

    private void FillWrapped(Rectangle rect, int period)
    {
        int first = (int)MathF.Floor((Bounds.Left - rect.Right) / (float)period) + 1;
        int last = (int)MathF.Ceiling((Bounds.Right - rect.Left) / (float)period) - 1;
        for (int k = first; k <= last; k++) Fill(new Rectangle(rect.X + k * period, rect.Y, rect.Width, rect.Height));
    }

    private void Fill(Rectangle rect)
    {
        rect = Rectangle.Intersect(rect, Bounds);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        int start = rect.Left - Bounds.X, end = rect.Right - Bounds.X; // local, end exclusive
        int w0 = start >> 6, w1 = (end - 1) >> 6;
        ulong first = ~0UL << (start & 63);
        ulong last = ((end - 1) & 63) == 63 ? ~0UL : (1UL << (((end - 1) & 63) + 1)) - 1;
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            int row = (y - Bounds.Y) * words;
            if (w0 == w1) { bits[row + w0] |= first & last; continue; }
            bits[row + w0] |= first;
            for (int w = w0 + 1; w < w1; w++) bits[row + w] = ~0UL;
            bits[row + w1] |= last;
        }
    }

    // Runs of solid pixels whose vertical neighbour (row y + normal) is air; air outside the bounds.
    private void HorizontalRuns(int y, int row, int neighbourRow, int normal)
    {
        float edgeY = Bounds.Y + y + (normal > 0 ? 1 : 0);
        int runStart = -1;
        for (int i = 0; i < words; i++)
        {
            ulong mask = bits[row + i] & ~(neighbourRow >= 0 ? bits[neighbourRow + i] : 0UL);
            int pos = 0;
            while (pos < 64)
            {
                if (runStart < 0)
                {
                    ulong candidates = mask & (~0UL << pos);
                    if (candidates == 0) break;
                    pos = System.Numerics.BitOperations.TrailingZeroCount(candidates);
                    runStart = (i << 6) + pos;
                }
                ulong gaps = ~mask & (~0UL << pos);
                if (gaps == 0) break; // the run continues into the next word
                pos = System.Numerics.BitOperations.TrailingZeroCount(gaps);
                Edges.Add(new V8Edge(new(Bounds.X + runStart, edgeY), new(Bounds.X + (i << 6) + pos, edgeY), new(0, normal)));
                runStart = -1;
            }
        }
        // Bits beyond the width are never set, so an open run here reaches exactly the right bound.
        if (runStart >= 0)
            Edges.Add(new V8Edge(new(Bounds.X + runStart, edgeY), new(Bounds.X + Bounds.Width, edgeY), new(0, normal)));
    }

    // Per row, pixels whose horizontal neighbour is air; runs are merged down each column.
    private void VerticalEdges(int height)
    {
        int width = Bounds.Width;
        if (previousLeft.Length < words)
        {
            previousLeft = new ulong[words]; previousRight = new ulong[words];
            currentLeft = new ulong[words]; currentRight = new ulong[words];
        }
        if (leftStart.Length < width) { leftStart = new int[width]; rightStart = new int[width]; }
        Array.Clear(previousLeft, 0, words); Array.Clear(previousRight, 0, words);
        for (int y = 0; y <= height; y++)
        {
            if (y < height)
            {
                int row = y * words;
                ulong carry = 0;
                for (int i = 0; i < words; i++)
                {
                    ulong r = bits[row + i];
                    ulong next = i + 1 < words ? bits[row + i + 1] : 0UL;
                    currentLeft[i] = r & ~((r << 1) | carry);   // neighbour x - 1 is air
                    currentRight[i] = r & ~((r >> 1) | (next << 63)); // neighbour x + 1 is air
                    carry = r >> 63;
                }
            }
            else
            {
                Array.Clear(currentLeft, 0, words); Array.Clear(currentRight, 0, words);
            }
            ColumnRuns(previousLeft, currentLeft, leftStart, y, -1);
            ColumnRuns(previousRight, currentRight, rightStart, y, 1);
            (previousLeft, currentLeft) = (currentLeft, previousLeft);
            (previousRight, currentRight) = (currentRight, previousRight);
        }
    }

    private void ColumnRuns(ulong[] previous, ulong[] current, int[] start, int y, int normal)
    {
        for (int i = 0; i < words; i++)
        {
            ulong closed = previous[i] & ~current[i], opened = current[i] & ~previous[i];
            while (closed != 0)
            {
                int x = (i << 6) + System.Numerics.BitOperations.TrailingZeroCount(closed);
                float edgeX = Bounds.X + x + (normal > 0 ? 1 : 0);
                Edges.Add(new V8Edge(new(edgeX, Bounds.Y + start[x]), new(edgeX, Bounds.Y + y), new(normal, 0)));
                closed &= closed - 1;
            }
            while (opened != 0)
            {
                start[(i << 6) + System.Numerics.BitOperations.TrailingZeroCount(opened)] = y;
                opened &= opened - 1;
            }
        }
    }

    // A pixel belongs to exactly one exposed face. Closest axial face, stable L/R/U/D tie break.
    // Return the center of the first air pixel, never a sample inside the blocker, and the depth (1 = on the face).
    // Weights per light term are applied by V8FaceChunks.
    public bool TryFace(int x, int y, int reach, out Vector2 sample, out int depth)
    {
        sample = default; depth = 0;
        if (!IsSolid(x, y)) return false;
        for (int d = 1; d <= reach; d++)
        {
            if (!IsSolid(x - d, y)) sample = new(x - d + .5f, y + .5f);
            else if (!IsSolid(x + d, y)) sample = new(x + d + .5f, y + .5f);
            else if (!IsSolid(x, y - d)) sample = new(x + .5f, y - d + .5f);
            else if (!IsSolid(x, y + d)) sample = new(x + .5f, y + d + .5f);
            else continue;
            depth = d;
            return true;
        }
        return false;
    }
}
