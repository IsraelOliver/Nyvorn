using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.World.Objects;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

/// <summary>Tile-grid scalar sky exposure and independent RGB local fill. Best-path propagation
/// (not neighbour sums) with finite range. The grid is uploaded as two small cell textures and reconstructed per art
/// pixel in the V8 shaders (V8Ambient.fxh), world anchored and obstacle aware: the frame path never walks art pixels
/// on the CPU. Per-pixel SkyTexture/LocalTexture are resolved on the GPU only when a capture or validation reads them.</summary>
public sealed class V8AmbientField : IDisposable
{
    /// <summary>Local fill is stored halved: overlapping sources keep headroom above 1 before interpolation,
    /// like the former float reconstruction that clamped only the final pixel.</summary>
    public const float LocalScale = 2f;
    // Uploading into a texture the GPU may still be reading stalls the driver (measured in V7); rotate through a ring.
    private const int TextureRingSize = 3;
    private const int TextureGrowStep = 64;

    private readonly GraphicsDevice device;
    private readonly Effect resolveEffect;
    private readonly PriorityQueue<int, float> queue = new();
    private float[] cost = Array.Empty<float>(), sky = Array.Empty<float>(), path = Array.Empty<float>(), cap = Array.Empty<float>();
    private Vector3[] local = Array.Empty<Vector3>();
    private Color[] fieldTexels = Array.Empty<Color>(), localTexels = Array.Empty<Color>();
    private readonly Texture2D[] fieldRing = new Texture2D[TextureRingSize], localRing = new Texture2D[TextureRingSize];
    private readonly VertexPositionColorTexture[] resolveQuad = new VertexPositionColorTexture[6];
    private RenderTarget2D resolvedSky, resolvedLocal;
    private bool resolved;
    private int ringIndex;
    private Rectangle grid, pixels;
    private int cells, tileSize = 8;
    private Vector3 skyColor, receiverSkyColor;
    private float backgroundCutoff;
    private V8AmbientContext lastContext;

    public Texture2D FieldTexture { get; private set; }
    public Texture2D LocalCellTexture { get; private set; }
    public Rectangle Grid => grid;
    /// <summary>Frame-path GPU resources (cell texture ring). Capture-only resolve targets are counted separately.</summary>
    public int ResourceCreations { get; private set; }
    public int DiagnosticResourceCreations { get; private set; }
    // CPU split of the last Update: grid classification + propagation, per-art-pixel reconstruction, texture upload.
    public double LastFieldMs { get; private set; }
    /// <summary>Always 0 in the frame path: per-art-pixel reconstruction runs in the shaders.</summary>
    public double LastReconstructMs { get; private set; }
    public double LastUploadMs { get; private set; }

    /// <summary>Per-art-pixel sky (alpha = layer weight) over the last Update's bounds, resolved on the GPU on demand.
    /// Captures and validation only; switches render targets and restores the previous ones.</summary>
    public Texture2D SkyTexture { get { Resolve(); return resolvedSky; } }
    /// <summary>Per-art-pixel local fill over the last Update's bounds, resolved on the GPU on demand.</summary>
    public Texture2D LocalTexture { get { Resolve(); return resolvedLocal; } }

    public V8AmbientField(GraphicsDevice device, Effect resolveEffect = null)
    {
        this.device = device;
        this.resolveEffect = resolveEffect;
    }

    public void Update(WorldMap map, IReadOnlyList<PlatformInstance> platforms, IReadOnlyList<DoorInstance> doors,
        IReadOnlyList<V8Light> lights, Rectangle pixels, V8AmbientContext context,
        Nyvorn.Source.Engine.Physics.Sand.SandSystem sand = null)
    {
        long stage = System.Diagnostics.Stopwatch.GetTimestamp();
        this.pixels = pixels;
        lastContext = context;
        resolved = false;
        tileSize = map.TileSize;
        if (context == null)
        {
            grid = Rectangle.Empty; cells = 0; skyColor = Vector3.Zero; receiverSkyColor = Vector3.Zero;
            LastFieldMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
            LastReconstructMs = 0;
            stage = System.Diagnostics.Stopwatch.GetTimestamp();
            Upload();
            LastUploadMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
            return;
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
            Propagate(sky, 1f / Math.Max(1, context.SkyRangeTiles), true, context.SkyDiagonalTransport);
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
        skyColor = context.SkyColor;
        receiverSkyColor = context.ReceiverSkyColor;
        backgroundCutoff = context.BackgroundSkyCutoff;
        LastFieldMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        LastReconstructMs = 0;
        stage = System.Diagnostics.Stopwatch.GetTimestamp();
        Upload();
        LastUploadMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    }

    /// <summary>Binds the cell textures and grid mapping used by V8Ambient.fxh.</summary>
    /// <param name="worldOffsetX">Wrap copy offset of the receivers being drawn (0 for light-space passes).</param>
    /// <param name="backgroundSky">Background wall receivers and capture resolves use the background sky colour; every
    /// other receiver and the foreground faces use the receiver sky colour.</param>
    /// <param name="backgroundRange">Background wall receivers only: remap the exposure to the background's own visual
    /// range. The transport, the cell textures and the capture resolves keep the raw exposure.</param>
    public void Apply(Effect effect, float worldOffsetX, bool backgroundSky = false, bool backgroundRange = false)
    {
        effect.Parameters["AmbientFieldTexture"]?.SetValue(FieldTexture);
        effect.Parameters["AmbientLocalTexture"]?.SetValue(LocalCellTexture);
        effect.Parameters["AmbientOrigin"]?.SetValue(new Vector2(grid.X * tileSize - worldOffsetX, grid.Y * tileSize));
        effect.Parameters["AmbientCells"]?.SetValue(new Vector2(grid.Width, grid.Height));
        effect.Parameters["AmbientTextureSize"]?.SetValue(new Vector2(FieldTexture.Width, FieldTexture.Height));
        effect.Parameters["AmbientCellSize"]?.SetValue((float)tileSize);
        effect.Parameters["AmbientLocalScale"]?.SetValue(LocalScale);
        effect.Parameters["AmbientSkyColor"]?.SetValue(backgroundSky ? skyColor : receiverSkyColor);
        effect.Parameters["AmbientSkyCutoff"]?.SetValue(backgroundRange ? backgroundCutoff : 0f);
    }

    /// <summary>Validation only, never called by the frame path: the former per-art-pixel CPU reconstruction over the
    /// last Update's bounds, kept as the reference the shader reconstruction is compared against.</summary>
    public void BuildReferencePixels(Color[] skyPixels, Color[] localPixels)
    {
        Array.Clear(skyPixels); Array.Clear(localPixels);
        if (lastContext == null) return;
        for (int y = 0; y < pixels.Height; y++)
        {
            float rowWeight = lastContext.SkyWeight((int)MathF.Floor((pixels.Y + y + .5f) / tileSize));
            for (int x = 0; x < pixels.Width; x++)
            {
                Vector2 point = new(pixels.X + x + .5f, pixels.Y + y + .5f);
                Reconstruct(point, tileSize, out float exposure, out Vector3 fill);
                exposure = MathF.Min(exposure, rowWeight);
                int i = y * pixels.Width + x;
                skyPixels[i] = new Color(new Vector4(skyColor * exposure, rowWeight));
                localPixels[i] = new Color(fill);
            }
        }
    }

    private void Upload()
    {
        int width = Math.Max(1, grid.Width), height = Math.Max(1, grid.Height);
        EnsureRing(width, height);
        ringIndex = (ringIndex + 1) % TextureRingSize;
        FieldTexture = fieldRing[ringIndex];
        LocalCellTexture = localRing[ringIndex];
        if (cells == 0) return; // AmbientCells = 0: the shader treats every cell as outside
        if (fieldTexels.Length < cells)
        {
            fieldTexels = new Color[cells];
            localTexels = new Color[cells];
        }
        for (int i = 0; i < cells; i++)
        {
            fieldTexels[i] = new Color(ToByte(sky[i]), ToByte(cap[i]), cost[i] > 0 ? (byte)255 : (byte)0, (byte)255);
            Vector3 fill = local[i] / LocalScale;
            localTexels[i] = new Color(ToByte(fill.X), ToByte(fill.Y), ToByte(fill.Z), (byte)255);
        }
        var region = new Rectangle(0, 0, grid.Width, grid.Height);
        FieldTexture.SetData(0, region, fieldTexels, 0, cells);
        LocalCellTexture.SetData(0, region, localTexels, 0, cells);
    }

    private static byte ToByte(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);

    // Grow-only and rounded up, and the WHOLE ring at once: a repeated unchanged frame never allocates, and a new
    // maximum grid size (light margin or view) costs one allocation event instead of one per ring slot per frame.
    private void EnsureRing(int width, int height)
    {
        bool fits = true;
        for (int i = 0; i < TextureRingSize; i++)
            fits &= fieldRing[i] != null && fieldRing[i].Width >= width && fieldRing[i].Height >= height;
        if (fits) return;
        int w = RoundUp(Math.Max(width, fieldRing[0]?.Width ?? 0)), h = RoundUp(Math.Max(height, fieldRing[0]?.Height ?? 0));
        for (int i = 0; i < TextureRingSize; i++)
        {
            fieldRing[i]?.Dispose(); localRing[i]?.Dispose();
            fieldRing[i] = new Texture2D(device, w, h, false, SurfaceFormat.Color);
            localRing[i] = new Texture2D(device, w, h, false, SurfaceFormat.Color);
            ResourceCreations += 2;
        }
        static int RoundUp(int value) => (value + TextureGrowStep - 1) / TextureGrowStep * TextureGrowStep;
    }

    private void Resolve()
    {
        if (resolved && resolvedSky != null) return;
        if (resolveEffect == null) throw new InvalidOperationException("Resolving ambient pixels needs the V8AmbientResolve effect");
        int width = Math.Max(1, pixels.Width), height = Math.Max(1, pixels.Height);
        if (resolvedSky == null || resolvedSky.Width != width || resolvedSky.Height != height)
        {
            resolvedSky?.Dispose(); resolvedLocal?.Dispose();
            resolvedSky = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            resolvedLocal = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            DiagnosticResourceCreations += 2;
        }
        var previousTargets = device.GetRenderTargets();
        var previousBlend = device.BlendState;
        var previousDepth = device.DepthStencilState;
        var previousRasterizer = device.RasterizerState;
        resolveEffect.Parameters["MatrixTransform"].SetValue(Matrix.CreateTranslation(-pixels.X, -pixels.Y, 0) *
            Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1));
        if (FieldTexture == null) Upload();
        Apply(resolveEffect, 0, backgroundSky: true); // SkyTexture stays the background sky, matching BuildReferencePixels
        Vector2 min = pixels.Location.ToVector2(), max = min + new Vector2(width, height);
        resolveQuad[0] = new(new Vector3(min, 0), Color.White, Vector2.Zero);
        resolveQuad[1] = new(new Vector3(max.X, min.Y, 0), Color.White, Vector2.Zero);
        resolveQuad[2] = new(new Vector3(max, 0), Color.White, Vector2.Zero);
        resolveQuad[3] = resolveQuad[0];
        resolveQuad[4] = resolveQuad[2];
        resolveQuad[5] = new(new Vector3(min.X, max.Y, 0), Color.White, Vector2.Zero);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        Draw(resolvedSky, "Sky");
        Draw(resolvedLocal, "Local");
        device.SetRenderTargets(previousTargets);
        device.BlendState = previousBlend;
        device.DepthStencilState = previousDepth;
        device.RasterizerState = previousRasterizer;
        resolved = true;

        void Draw(RenderTarget2D target, string technique)
        {
            device.SetRenderTarget(target);
            device.Clear(Color.Transparent);
            resolveEffect.CurrentTechnique = resolveEffect.Techniques[technique];
            foreach (var pass in resolveEffect.CurrentTechnique.Passes)
            { pass.Apply(); device.DrawUserPrimitives(PrimitiveType.TriangleList, resolveQuad, 0, 2); }
        }
    }

    private const float DiagonalStep = 1.41421356f; // sqrt(2)

    /// <param name="diagonals">Diagnostic only: also step on the four diagonals, at sqrt(2) times the cell's own transport
    /// cost. A diagonal is taken only when both orthogonal cells of that corner are transitable, so light never crosses
    /// the diagonal contact of two solids. Transitability and per-cell costs are the existing ones.</param>
    private void Propagate(float[] field, float loss, bool skyField, bool diagonals = false)
    {
        while (queue.TryDequeue(out int i, out float negative))
        {
            float value = -negative;
            if (value < field[i]) continue;
            int x = i % grid.Width, y = i / grid.Width;
            bool left = x > 0, right = x + 1 < grid.Width, up = y > 0, down = y + 1 < grid.Height;
            if (left) Visit(i - 1, 1f);
            if (right) Visit(i + 1, 1f);
            if (up) Visit(i - grid.Width, 1f);
            if (down) Visit(i + grid.Width, 1f);
            if (diagonals)
            {
                if (left && up && Open(i - 1) && Open(i - grid.Width)) Visit(i - grid.Width - 1, DiagonalStep);
                if (right && up && Open(i + 1) && Open(i - grid.Width)) Visit(i - grid.Width + 1, DiagonalStep);
                if (left && down && Open(i - 1) && Open(i + grid.Width)) Visit(i + grid.Width - 1, DiagonalStep);
                if (right && down && Open(i + 1) && Open(i + grid.Width)) Visit(i + grid.Width + 1, DiagonalStep);
            }
            void Visit(int j, float step)
            {
                if (cost[j] == 0) return;
                float next = value - loss * cost[j] * step;
                if (skyField) next = MathF.Min(next, cap[j]);
                if (next <= field[j] + .000001f) return;
                field[j] = next; queue.Enqueue(j, -next);
            }
        }
        bool Open(int j) => cost[j] > 0;
    }

    // Reference only (BuildReferencePixels); V8Ambient.fxh is the frame-path implementation of the same rule.
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
    public void Dispose()
    {
        foreach (var texture in fieldRing) texture?.Dispose();
        foreach (var texture in localRing) texture?.Dispose();
        resolvedSky?.Dispose(); resolvedLocal?.Dispose();
        resolveEffect?.Dispose();
    }
}
