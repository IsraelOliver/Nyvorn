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
/// Geometry is rebuilt only when the region, tiles or supplied objects change. No GPU readback.</summary>
public sealed class V8Geometry
{
    private bool[] occupied = Array.Empty<bool>();
    private int revision = -1;
    private int objectHash;
    private WorldMap previousMap;
    private readonly List<Rectangle> sandRuns = new();
    public Rectangle Bounds { get; private set; }
    public List<V8Edge> Edges { get; } = new();
    public int Generation { get; private set; }

    public bool IsSolid(int x, int y) => Bounds.Contains(x, y) && occupied[(y - Bounds.Y) * Bounds.Width + x - Bounds.X];

    public void Update(WorldMap map, IReadOnlyList<PlatformInstance> platforms,
        IReadOnlyList<DoorInstance> doors, Rectangle bounds, SandSystem sand = null)
    {
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
        if (previousMap == map && revision == map.TileRevision && objectHash == nextHash && Bounds == bounds) return;
        previousMap = map; revision = map.TileRevision; objectHash = nextHash; Bounds = bounds;
        int count = bounds.Width * bounds.Height;
        if (occupied.Length < count) occupied = new bool[count];
        Array.Clear(occupied, 0, count);
        int size = map.TileSize;
        int left = (int)MathF.Floor(bounds.Left / (float)size);
        int right = (int)MathF.Ceiling(bounds.Right / (float)size);
        int top = Math.Max(0, (int)MathF.Floor(bounds.Top / (float)size));
        int bottom = Math.Min(map.Height, (int)MathF.Ceiling(bounds.Bottom / (float)size));
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
            {
                var tile = map.GetTile(map.WrapTileX(x), y);
                if (tile != TileType.Empty)
                    Fill(new Rectangle(x * size, y * size, size, tile == TileType.Platform ? 3 : size));
            }
        if (platforms != null) foreach (var p in platforms) FillWrapped(p.SurfaceBounds, map.PixelWidth);
        if (doors != null) foreach (var d in doors) if (!d.IsOpen) FillWrapped(d.Bounds, map.PixelWidth);
        foreach (var run in sandRuns) Fill(run);
        Edges.Clear();
        // Merge adjacent unit edges; internal tile/object boundaries disappear in the union.
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            Horizontal(y, -1); Horizontal(y, 1);
        }
        for (int x = bounds.Left; x < bounds.Right; x++)
        {
            Vertical(x, -1); Vertical(x, 1);
        }
        Generation++;
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
        for (int y = rect.Top; y < rect.Bottom; y++)
            for (int x = rect.Left; x < rect.Right; x++) occupied[(y - Bounds.Y) * Bounds.Width + x - Bounds.X] = true;
    }
    private void Horizontal(int y, int normal)
    {
        int start = int.MinValue;
        for (int x = Bounds.Left; x <= Bounds.Right; x++)
        {
            bool exposed = x < Bounds.Right && IsSolid(x, y) && !IsSolid(x, y + normal);
            if (exposed && start == int.MinValue) start = x;
            if (!exposed && start != int.MinValue)
            {
                float edgeY = y + (normal > 0 ? 1 : 0);
                Edges.Add(new V8Edge(new(start, edgeY), new(x, edgeY), new(0, normal)));
                start = int.MinValue;
            }
        }
    }
    private void Vertical(int x, int normal)
    {
        int start = int.MinValue;
        for (int y = Bounds.Top; y <= Bounds.Bottom; y++)
        {
            bool exposed = y < Bounds.Bottom && IsSolid(x, y) && !IsSolid(x + normal, y);
            if (exposed && start == int.MinValue) start = y;
            if (!exposed && start != int.MinValue)
            {
                float edgeX = x + (normal > 0 ? 1 : 0);
                Edges.Add(new V8Edge(new(edgeX, start), new(edgeX, y), new(normal, 0)));
                start = int.MinValue;
            }
        }
    }

    // A pixel belongs to exactly one exposed face. Closest axial face, stable L/R/U/D tie break.
    // Return the center of the first air pixel, never a sample inside the blocker.
    public bool TryFace(int x, int y, int width, out Vector2 sample, out float weight)
    {
        sample = default; weight = 0;
        if (!IsSolid(x, y)) return false;
        for (int depth = 1; depth <= width; depth++)
        {
            if (!IsSolid(x - depth, y)) sample = new(x - depth + .5f, y + .5f);
            else if (!IsSolid(x + depth, y)) sample = new(x + depth + .5f, y + .5f);
            else if (!IsSolid(x, y - depth)) sample = new(x + .5f, y - depth + .5f);
            else if (!IsSolid(x, y + depth)) sample = new(x + .5f, y + depth + .5f);
            else continue;
            weight = 1f - (depth - .5f) / width;
            weight *= weight;
            return true;
        }
        return false;
    }
}

