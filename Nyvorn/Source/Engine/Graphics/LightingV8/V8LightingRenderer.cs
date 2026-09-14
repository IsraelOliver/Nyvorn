using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.World.Objects;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

/// <summary>V8 renderer shared by the diagnostic and future gameplay integration.
/// Targets use one texel per world-art pixel, independent of viewport zoom. Caller draws receivers
/// in world coordinates with the same effective camera matrix. No scene tint or ambient is implicit.</summary>
public sealed class V8LightingRenderer : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly Effect directEffect, faceEffect, receiverEffect;
    private readonly BasicEffect maskEffect;
    private readonly BlendState noColor = new() { ColorWriteChannels = ColorWriteChannels.None };
    private readonly BlendState add = new() {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One };
    private readonly DepthStencilState write = new() {
        DepthBufferEnable = false, StencilEnable = true, StencilFunction = CompareFunction.Always,
        StencilPass = StencilOperation.Replace, ReferenceStencil = 1 };
    private readonly DepthStencilState visible = new() {
        DepthBufferEnable = false, StencilEnable = true, StencilFunction = CompareFunction.Equal,
        StencilPass = StencilOperation.Keep, ReferenceStencil = 0 };
    private VertexPositionColorTexture[] shadows = new VertexPositionColorTexture[256];
    private readonly VertexPositionColorTexture[] lightQuad = new VertexPositionColorTexture[6];
    private VertexPositionColorTexture[] faceVertices = Array.Empty<VertexPositionColorTexture>();
    private DynamicVertexBuffer faceBuffer;
    private int faceCount, faceGeneration = -1;
    private Rectangle previousFaceBounds;
    private int previousFaceWidth;
    public V8Geometry Geometry { get; } = new();
    public V8AmbientField Ambient { get; }
    public V8AmbientContext AmbientContext { get; set; }
    public RenderTarget2D Direct { get; private set; }
    public RenderTarget2D Foreground { get; private set; }
    public Rectangle LightBounds { get; private set; }
    private int resourceCreations;
    public int ResourceCreations => resourceCreations + Ambient.ResourceCreations;
    public int FaceWidth { get; set; } = 8;
    public int GeometryRegionStep { get; set; } = 1;
    public Nyvorn.Source.Engine.Physics.Sand.SandSystem Sand { get; set; }
    public double LastGeometryMs { get; private set; }
    public double LastDirectMs { get; private set; }
    public double LastAmbientMs { get; private set; }
    public double LastFacesMs { get; private set; }

    public V8LightingRenderer(GraphicsDevice device, ContentManager content)
    {
        this.device = device;
        Ambient = new V8AmbientField(device);
        directEffect = content.Load<Effect>("effects/V8Direct").Clone();
        faceEffect = content.Load<Effect>("effects/V8Faces").Clone();
        receiverEffect = content.Load<Effect>("effects/V8Receiver").Clone();
        maskEffect = new BasicEffect(device) { VertexColorEnabled = true };
    }

    public static Rectangle VisibleBounds(Matrix effectiveView, int screenWidth, int screenHeight)
    {
        Matrix inverse = Matrix.Invert(effectiveView);
        var a = Vector2.Transform(Vector2.Zero, inverse);
        var b = Vector2.Transform(new Vector2(screenWidth, 0), inverse);
        var c = Vector2.Transform(new Vector2(0, screenHeight), inverse);
        var d = Vector2.Transform(new Vector2(screenWidth, screenHeight), inverse);
        var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        int x = (int)MathF.Floor(min.X), y = (int)MathF.Floor(min.Y);
        return new Rectangle(x, y, (int)MathF.Ceiling(max.X) - x, (int)MathF.Ceiling(max.Y) - y);
    }

    public void Render(WorldMap map, IReadOnlyList<PlatformInstance> platforms, IReadOnlyList<DoorInstance> doors,
        IReadOnlyList<V8Light> sources, Rectangle viewBounds)
    {
        if (FaceWidth < 1) throw new InvalidOperationException("Face width must be positive");
        var bounds = viewBounds;
        bounds.Inflate(FaceWidth + 2, FaceWidth + 2); // external face samples remain in the light field
        EnsureTargets(bounds);
        float maxRadius = 0;
        foreach (var source in sources)
            if (source.Radius > 0) maxRadius = MathF.Max(maxRadius, source.Radius);
        var geometryBounds = bounds;
        geometryBounds.Inflate((int)MathF.Ceiling(maxRadius) + FaceWidth + 2, (int)MathF.Ceiling(maxRadius) + FaceWidth + 2);
        if (GeometryRegionStep > 1)
        {
            // A containing world-aligned cache avoids scanning all contours on every camera pixel.
            // Keep the FULL required reach; tile/door/platform/sand edits still invalidate immediately.
            int step = GeometryRegionStep;
            int left = (int)MathF.Floor(geometryBounds.Left / (float)step) * step;
            int top = (int)MathF.Floor(geometryBounds.Top / (float)step) * step;
            int right = (int)MathF.Ceiling(geometryBounds.Right / (float)step) * step;
            int bottom = (int)MathF.Ceiling(geometryBounds.Bottom / (float)step) * step;
            geometryBounds = new Rectangle(left, top, right - left, bottom - top);
        }
        long stage = System.Diagnostics.Stopwatch.GetTimestamp();
        Geometry.Update(map, platforms, doors, geometryBounds, Sand);
        LastGeometryMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        stage = System.Diagnostics.Stopwatch.GetTimestamp();
        Matrix projection = Matrix.CreateTranslation(-bounds.X, -bounds.Y, 0) *
            Matrix.CreateOrthographicOffCenter(0, bounds.Width, bounds.Height, 0, 0, 1);
        maskEffect.World = Matrix.Identity; maskEffect.View = Matrix.Identity; maskEffect.Projection = projection;
        directEffect.Parameters["MatrixTransform"].SetValue(projection);
        device.SetRenderTarget(Direct);
        device.Clear(ClearOptions.Target | ClearOptions.Stencil, Color.Black, 1, 0);
        device.RasterizerState = RasterizerState.CullNone;
        foreach (var source in sources)
        {
            if (source.Radius <= 0 || source.Radiance == Vector3.Zero) continue;
            int first = (int)MathF.Ceiling((bounds.Left - source.Radius - source.Position.X) / map.PixelWidth);
            int last = (int)MathF.Floor((bounds.Right + source.Radius - source.Position.X) / map.PixelWidth);
            for (int k = first; k <= last; k++)
            {
                Vector2 position = source.Position + new Vector2(k * map.PixelWidth, 0);
                if (position.Y + source.Radius < bounds.Top || position.Y - source.Radius > bounds.Bottom) continue;
                if (Geometry.IsSolid((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y))) continue;
                DrawSource(source with { Position = position });
            }
        }
        device.SetRenderTarget(null);
        LastDirectMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        stage = System.Diagnostics.Stopwatch.GetTimestamp();
        Ambient.Update(map, platforms, doors, sources, bounds, AmbientContext, Sand);
        LastAmbientMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        stage = System.Diagnostics.Stopwatch.GetTimestamp();
        BuildFaces();
        device.SetRenderTarget(Foreground);
        device.Clear(Color.Transparent);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        faceEffect.Parameters["MatrixTransform"].SetValue(projection);
        faceEffect.Parameters["DirectTexture"].SetValue(Direct);
        faceEffect.Parameters["SkyTexture"].SetValue(Ambient.SkyTexture);
        faceEffect.Parameters["LocalTexture"].SetValue(Ambient.LocalTexture);
        faceEffect.Parameters["LightOrigin"].SetValue(bounds.Location.ToVector2());
        faceEffect.Parameters["LightSize"].SetValue(bounds.Size.ToVector2());
        if (faceCount > 0)
        {
            device.SetVertexBuffer(faceBuffer);
            foreach (var pass in faceEffect.CurrentTechnique.Passes)
            { pass.Apply(); device.DrawPrimitives(PrimitiveType.TriangleList, 0, faceCount / 3); }
            device.SetVertexBuffer(null);
        }
        device.SetRenderTarget(null);
        device.DepthStencilState = DepthStencilState.None;
        LastFacesMs = System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        device.BlendState = BlendState.AlphaBlend;
    }

    private void DrawSource(V8Light source)
    {
        // Union of all shadow quads for this source. Replace/Keep avoids winding overflow.
        device.Clear(ClearOptions.Stencil, Color.Black, 1, 0);
        int count = 0;
        foreach (var edge in Geometry.Edges)
        {
            if (Vector2.Dot(source.Position - edge.A, edge.Normal) <= 0) continue;
            Vector2 a = edge.A, b = edge.B;
            if (MathF.Max(a.X, b.X) < source.Position.X - source.Radius ||
                MathF.Min(a.X, b.X) > source.Position.X + source.Radius ||
                MathF.Max(a.Y, b.Y) < source.Position.Y - source.Radius ||
                MathF.Min(a.Y, b.Y) > source.Position.Y + source.Radius) continue;
            Vector2 farA = a + Vector2.Normalize(a - source.Position) * (source.Radius * 2 + 2);
            Vector2 farB = b + Vector2.Normalize(b - source.Position) * (source.Radius * 2 + 2);
            if (shadows.Length < count + 6) Array.Resize(ref shadows, Math.Max(count + 6, shadows.Length * 2));
            Quad(shadows, count, a, b, farB, farA, Vector2.Zero, Color.White); count += 6;
        }
        device.BlendState = noColor; device.DepthStencilState = write;
        foreach (var pass in maskEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (count > 0) device.DrawUserPrimitives(PrimitiveType.TriangleList, shadows, 0, count / 3);
        }
        device.BlendState = add; device.DepthStencilState = visible;
        directEffect.Parameters["SourcePosition"].SetValue(source.Position);
        directEffect.Parameters["Radiance"].SetValue(source.Radiance);
        directEffect.Parameters["Radius"].SetValue(source.Radius);
        Vector2 min = source.Position - new Vector2(source.Radius), max = source.Position + new Vector2(source.Radius);
        Quad(lightQuad, 0, min, new(max.X, min.Y), max, new(min.X, max.Y), Vector2.Zero, Color.White);
        foreach (var pass in directEffect.CurrentTechnique.Passes)
        { pass.Apply(); device.DrawUserPrimitives(PrimitiveType.TriangleList, lightQuad, 0, 2); }
    }

    private void BuildFaces()
    {
        if (faceGeneration == Geometry.Generation && previousFaceBounds == LightBounds && previousFaceWidth == FaceWidth) return;
        int required = LightBounds.Width * LightBounds.Height * 6;
        if (faceVertices.Length < required) faceVertices = new VertexPositionColorTexture[required];
        faceCount = 0;
        for (int y = LightBounds.Top; y < LightBounds.Bottom; y++)
            for (int x = LightBounds.Left; x < LightBounds.Right; x++)
            {
                if (!Geometry.IsSolid(x, y)) continue;
                Geometry.TryFace(x, y, FaceWidth, out Vector2 sample, out float weight);
                Vector2 uv = (sample - LightBounds.Location.ToVector2()) / LightBounds.Size.ToVector2();
                Quad(faceVertices, faceCount, new(x, y), new(x + 1, y), new(x + 1, y + 1), new(x, y + 1), uv, new Color(weight, weight, weight, 1f));
                faceCount += 6;
            }
        if (faceCount > 0)
        {
            if (faceBuffer == null || faceBuffer.VertexCount < faceCount)
            {
                faceBuffer?.Dispose();
                // Reserve for the entire receiver region so moving into denser terrain never
                // allocates a new GPU buffer. Capacity changes are limited to viewport growth.
                faceBuffer = new DynamicVertexBuffer(device, VertexPositionColorTexture.VertexDeclaration, Math.Max(required, 4096), BufferUsage.WriteOnly);
                resourceCreations++;
            }
            faceBuffer.SetData(faceVertices, 0, faceCount, SetDataOptions.Discard);
        }
        faceGeneration = Geometry.Generation; previousFaceBounds = LightBounds; previousFaceWidth = FaceWidth;
    }

    public void BeginReceivers(SpriteBatch batch, Matrix effectiveView, int screenWidth, int screenHeight, bool foreground, float worldOffsetX = 0)
    {
        receiverEffect.Parameters["MatrixTransform"].SetValue(effectiveView * Matrix.CreateOrthographicOffCenter(0, screenWidth, screenHeight, 0, 0, 1));
        receiverEffect.Parameters["LightOrigin"].SetValue(LightBounds.Location.ToVector2() - new Vector2(worldOffsetX, 0));
        receiverEffect.Parameters["LightSize"].SetValue(LightBounds.Size.ToVector2());
        receiverEffect.Parameters["LightTexture"].SetValue(foreground ? Foreground : Direct);
        receiverEffect.Parameters["SkyTexture"].SetValue(Ambient.SkyTexture);
        receiverEffect.Parameters["LocalTexture"].SetValue(Ambient.LocalTexture);
        receiverEffect.Parameters["AmbientScale"].SetValue(foreground ? 0f : 1f);
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone, receiverEffect);
    }

    private void EnsureTargets(Rectangle bounds)
    {
        LightBounds = bounds;
        if (Direct != null && Direct.Width == bounds.Width && Direct.Height == bounds.Height) return;
        Direct?.Dispose(); Foreground?.Dispose();
        Direct = new RenderTarget2D(device, bounds.Width, bounds.Height, false, SurfaceFormat.Color,
            DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
        Foreground = new RenderTarget2D(device, bounds.Width, bounds.Height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        resourceCreations += 2;
    }

    private static void Quad(VertexPositionColorTexture[] output, int start, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector2 uv, Color color)
    {
        output[start] = new(new Vector3(a, 0), color, uv); output[start + 1] = new(new Vector3(b, 0), color, uv);
        output[start + 2] = new(new Vector3(c, 0), color, uv); output[start + 3] = output[start];
        output[start + 4] = output[start + 2]; output[start + 5] = new(new Vector3(d, 0), color, uv);
    }
    public void Dispose()
    {
        Direct?.Dispose(); Foreground?.Dispose(); faceBuffer?.Dispose();
        Ambient.Dispose();
        directEffect.Dispose(); faceEffect.Dispose(); receiverEffect.Dispose(); maskEffect.Dispose();
        noColor.Dispose(); add.Dispose(); write.Dispose(); visible.Dispose();
    }
}
