using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.World.Objects;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

/// <summary>Tile-grid scalar sky exposure and independent RGB local fill. Best-path propagation
/// (not neighbour sums) with finite range. Reconstruction is world anchored and obstacle aware.</summary>
public sealed class V8AmbientField : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly PriorityQueue<int, float> queue = new();
    private float[] cost = Array.Empty<float>(), sky = Array.Empty<float>(), path = Array.Empty<float>(), cap = Array.Empty<float>();
    private Vector3[] local = Array.Empty<Vector3>();
    private Color[] skyPixels = Array.Empty<Color>(), localPixels = Array.Empty<Color>();
    private Rectangle grid;
    private int cells;
    public Texture2D SkyTexture { get; private set; }
    public Texture2D LocalTexture { get; private set; }
    public int ResourceCreations { get; private set; }
    public V8AmbientField(GraphicsDevice device) { this.device = device; }

    public void Update(WorldMap map, IReadOnlyList<PlatformInstance> platforms, IReadOnlyList<DoorInstance> doors,
        IReadOnlyList<V8Light> lights, Rectangle pixels, V8AmbientContext context,
        Nyvorn.Source.Engine.Physics.Sand.SandSystem sand = null)
    {
        EnsureTextures(pixels.Width, pixels.Height);
        if (context == null)
        {
            Array.Clear(skyPixels); Array.Clear(localPixels);
            Upload(); return;
        }
        int size = map.TileSize;
        float range = context.SkyEnabled ? Math.Max(1, context.SkyRangeTiles) : 0;
        if (context.LocalEnabled) foreach (var light in lights)
            if (light.AmbientIntensity > 0) range = MathF.Max(range, light.AmbientRadius / size);
        // Every possible finite path to a receiver fits inside this margin, including around corners.
        int margin = (int)MathF.Ceiling(range) + 2;
        int left = (int)MathF.Floor(pixels.Left / (float)size) - margin;
        int top = Math.Max(0, (int)MathF.Floor(pixels.Top / (float)size) - margin);
        int right = (int)MathF.Ceiling(pixels.Right / (float)size) + margin;
        int bottom = Math.Min(map.Height, (int)MathF.Ceiling(pixels.Bottom / (float)size) + margin);
        grid = new Rectangle(left, top, right - left, Math.Max(0, bottom - top));
        cells = grid.Width * grid.Height;
        if (cost.Length < cells)
        {
            cost = new float[cells]; sky = new float[cells]; path = new float[cells]; cap = new float[cells]; local = new Vector3[cells];
        }
        Array.Clear(sky, 0, cells); Array.Clear(local, 0, cells);
        for (int y = grid.Top; y < grid.Bottom; y++) for (int x = grid.Left; x < grid.Right; x++)
        {
            int i = Index(x, y);
            var tile = map.GetTile(map.WrapTileX(x), y);
            cost[i] = tile == TileType.Empty ? 1 : tile == TileType.Platform ? 1.75f : 0;
            // Conservative tile approximation: any sand in a cell blocks ambient transport.
            // Direct silhouettes and foreground receivers still use actual pixel runs.
            if (cost[i] > 0 && sand != null && sand.HasSandInRectangle(x * size, y * size, size, size)) cost[i] = 0;
            cap[i] = context.SkyWeight(y);
        }
        if (platforms != null) foreach (var p in platforms) MarkObject(p.SurfaceBounds, map, 1.75f);
        if (doors != null) foreach (var d in doors) if (!d.IsOpen) MarkObject(d.Bounds, map, 0);
        queue.Clear();
        if (context.SkyEnabled)
        {
            for (int y = grid.Top; y < grid.Bottom; y++) for (int x = grid.Left; x < grid.Right; x++)
            {
                int i = Index(x, y);
                if (cost[i] > 0 && context.IsExteriorAperture(map, x, y))
                { sky[i] = cap[i]; queue.Enqueue(i, -sky[i]); }
            }
            Propagate(sky, 1f / Math.Max(1, context.SkyRangeTiles), true);
        }
        if (context.LocalEnabled) foreach (var light in lights)
        {
            if (light.AmbientRadius <= 0 || light.AmbientIntensity <= 0 || light.Radiance == Vector3.Zero) continue;
            Array.Clear(path, 0, cells); queue.Clear();
            int y = (int)MathF.Floor(light.Position.Y / size);
            int canonicalX = (int)MathF.Floor(light.Position.X / size);
            int first = (int)MathF.Ceiling((grid.Left - canonicalX) / (float)map.Width);
            int last = (int)MathF.Floor((grid.Right - 1 - canonicalX) / (float)map.Width);
            for (int k = first; k <= last; k++)
            {
                int x = canonicalX + k * map.Width;
                if (!grid.Contains(x, y)) continue;
                int i = Index(x, y);
                if (cost[i] > 0) { path[i] = 1; queue.Enqueue(i, -1); }
            }
            // Replicas of ONE source share a best-path field. Sources sum once each, not per path.
            Propagate(path, size / light.AmbientRadius, false);
            Vector3 color = light.Radiance * light.AmbientIntensity;
            for (int i = 0; i < cells; i++) local[i] += color * path[i];
        }
        // Layer classification is constant across a world row, and sky color across this frame.
        // Do not enumerate layers or rebuild the color for every art pixel.
        Vector3 skyColor = context.SkyColor;
        for (int y = 0; y < pixels.Height; y++)
        {
            float rowWeight = context.SkyWeight((int)MathF.Floor((pixels.Y + y + .5f) / size));
            for (int x = 0; x < pixels.Width; x++)
            {
            Vector2 point = new(pixels.X + x + .5f, pixels.Y + y + .5f);
            Reconstruct(point, size, out float exposure, out Vector3 fill);
            // The final receiver veto happens AFTER interpolation, so no sky texel leaks into Cavern.
            exposure = MathF.Min(exposure, rowWeight);
            int i = y * pixels.Width + x;
            skyPixels[i] = new Color(new Vector4(skyColor * exposure, rowWeight));
            localPixels[i] = new Color(fill);
            }
        }
        Upload();
    }

    private void Propagate(float[] field, float loss, bool skyField)
    {
        while (queue.TryDequeue(out int i, out float negative))
        {
            float value = -negative;
            if (value < field[i]) continue;
            int x = i % grid.Width, y = i / grid.Width;
            if (x > 0) Visit(i - 1);
            if (x + 1 < grid.Width) Visit(i + 1);
            if (y > 0) Visit(i - grid.Width);
            if (y + 1 < grid.Height) Visit(i + grid.Width);
            void Visit(int j)
            {
                if (cost[j] == 0) return;
                float next = value - loss * cost[j];
                if (skyField) next = MathF.Min(next, cap[j]);
                if (next <= field[j] + .000001f) return;
                field[j] = next; queue.Enqueue(j, -next);
            }
        }
    }

    private void Reconstruct(Vector2 point, int size, out float exposure, out Vector3 fill)
    {
        exposure = 0; fill = Vector3.Zero;
        int cx = (int)MathF.Floor(point.X / size), cy = (int)MathF.Floor(point.Y / size);
        if (!Open(cx, cy)) return;
        Vector2 uv = point / size - new Vector2(.5f);
        int x0 = (int)MathF.Floor(uv.X), y0 = (int)MathF.Floor(uv.Y);
        float fx = uv.X - x0, fy = uv.Y - y0, total = 0;
        for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
        {
            int x = x0 + dx, y = y0 + dy;
            if (!Open(x, y)) continue;
            // No bilinear bridge across a diagonal corner closed by two orthogonal solids.
            if (x != cx && y != cy && !Open(x, cy) && !Open(cx, y)) continue;
            float w = (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
            int i = Index(x, y);
            exposure += sky[i] * w; fill += local[i] * w; total += w;
        }
        if (total > 0) { exposure /= total; fill /= total; }
    }
    private bool Open(int x, int y) => grid.Contains(x, y) && cost[Index(x, y)] > 0;
    private int Index(int x, int y) => (y - grid.Y) * grid.Width + x - grid.X;
    private void MarkObject(Rectangle bounds, WorldMap map, float transportCost)
    {
        int left = (int)MathF.Floor(bounds.Left / (float)map.TileSize), right = (int)MathF.Ceiling(bounds.Right / (float)map.TileSize);
        int top = Math.Max(grid.Top, (int)MathF.Floor(bounds.Top / (float)map.TileSize));
        int bottom = Math.Min(grid.Bottom, (int)MathF.Ceiling(bounds.Bottom / (float)map.TileSize));
        for (int x = grid.Left; x < grid.Right; x++)
        {
            int wrapped = map.WrapTileX(x);
            bool covered = false;
            for (int bx = left; bx < right; bx++) if (map.WrapTileX(bx) == wrapped) { covered = true; break; }
            if (!covered) continue;
            for (int y = top; y < bottom; y++)
            {
                int i = Index(x, y);
                if (cost[i] > 0) cost[i] = transportCost; // objects never reopen terrain
            }
        }
    }
    private void EnsureTextures(int width, int height)
    {
        if (SkyTexture != null && SkyTexture.Width == width && SkyTexture.Height == height) return;
        SkyTexture?.Dispose(); LocalTexture?.Dispose();
        SkyTexture = new Texture2D(device, width, height, false, SurfaceFormat.Color);
        LocalTexture = new Texture2D(device, width, height, false, SurfaceFormat.Color);
        skyPixels = new Color[width * height]; localPixels = new Color[width * height];
        ResourceCreations += 2;
    }
    private void Upload() { SkyTexture.SetData(skyPixels); LocalTexture.SetData(localPixels); }
    public void Dispose() { SkyTexture?.Dispose(); LocalTexture?.Dispose(); }
}
