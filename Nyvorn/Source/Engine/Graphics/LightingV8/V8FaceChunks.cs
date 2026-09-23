using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

/// <summary>World-anchored face vertices in 64 px chunks, kept in one GPU arena and reused while the occupancy that
/// defines them (the chunk plus the face reach on every side, compared bit for bit) is unchanged. Camera movement, zoom,
/// wrap copies of the light bounds, sources and hour never rebuild a chunk: only chunks entering the light bounds and
/// chunks whose occupancy changed (tiles, doors, platforms, sand) are built and uploaded. Dark core pixels (no exposed
/// face within the reach) are merged into one quad per horizontal run: the same pixels receive the same black.
/// Vertex colour R = direct/local weight (quadratic over FaceWidth), G = sky band weight (smooth over SkyFaceDepth).</summary>
public sealed class V8FaceChunks : IDisposable
{
    public const int ChunkSize = 64;
    private const int KeepChunks = 2;         // chunks kept around the light bounds before eviction
    private const int AllocationStep = 96;    // vertices; whole quads, small headroom for in-place rebuilds
    private static readonly int Stride = VertexPositionColorTexture.VertexDeclaration.VertexStride;

    private sealed class Chunk
    {
        public ulong[] Occupancy;
        public VertexPositionColorTexture[] Vertices = Array.Empty<VertexPositionColorTexture>();
        public int VertexCount, WeightedPixels;
        public int Start = -1, Capacity;
        public int CheckedGeneration = -1;
    }

    private readonly GraphicsDevice device;
    private readonly Dictionary<long, Chunk> chunks = new();
    private readonly List<Chunk> visible = new();
    private readonly List<long> evicted = new();
    private readonly List<(int Start, int Count)> free = new();
    private DynamicVertexBuffer arena;
    private int arenaEnd, faceWidth = -1, skyDepth = -1;
    private int Reach => Math.Max(faceWidth, skyDepth);
    private bool deviceReset;
    private VertexPositionColorTexture[] scratch = new VertexPositionColorTexture[ChunkSize * ChunkSize * 6];
    private ulong[] occupancy = Array.Empty<ulong>();

    public int ResourceCreations { get; private set; }
    public int ArenaRewrites { get; private set; }
    public int ChunksBuilt { get; private set; }
    public int ChunksValidated { get; private set; }
    public int ChunksVisible => visible.Count;
    public int UncoveredChunks { get; private set; }
    public int VisibleVertices { get; private set; }
    public int VisibleWeightedPixels { get; private set; }
    public double LastBuildMs { get; private set; }
    public double LastUploadMs { get; private set; }

    public V8FaceChunks(GraphicsDevice device)
    {
        this.device = device;
        // DynamicVertexBuffer.IsContentLost is obsolete in MonoGame 3.8.5.1 (always false); the device event is not.
        device.DeviceReset += OnDeviceReset;
    }

    private void OnDeviceReset(object sender, EventArgs e) => deviceReset = true;

    public static int FloorDiv(int value, int divisor) => (int)Math.Floor(value / (double)divisor);

    public void Update(V8Geometry geometry, Rectangle lightBounds, int width, int skyBandDepth)
    {
        ChunksBuilt = 0; ChunksValidated = 0; UncoveredChunks = 0; LastBuildMs = 0; LastUploadMs = 0;
        if (width != faceWidth || skyBandDepth != skyDepth) { ReleaseAll(); faceWidth = width; skyDepth = skyBandDepth; }
        // After a device reset every live chunk is re-uploaded from its CPU copy.
        if (deviceReset) { deviceReset = false; if (arena != null) Rewrite(0); }
        int reach = Reach, side = ChunkSize + 2 * reach, perRow = (side + 63) >> 6, words = side * perRow;
        if (occupancy.Length < words) occupancy = new ulong[words];

        visible.Clear();
        VisibleVertices = 0; VisibleWeightedPixels = 0;
        int cx0 = FloorDiv(lightBounds.Left, ChunkSize), cx1 = FloorDiv(lightBounds.Right - 1, ChunkSize);
        int cy0 = FloorDiv(lightBounds.Top, ChunkSize), cy1 = FloorDiv(lightBounds.Bottom - 1, ChunkSize);
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                long key = Key(cx, cy);
                if (!chunks.TryGetValue(key, out Chunk chunk)) chunks[key] = chunk = new Chunk();
                if (chunk.CheckedGeneration != geometry.Generation)
                {
                    var area = new Rectangle(cx * ChunkSize - reach, cy * ChunkSize - reach, side, side);
                    if (!geometry.TryCopyOccupancy(area, occupancy))
                    {
                        // Not expected (the renderer's geometry region covers visible chunks plus their margin).
                        // Visible pixels are still exact; the chunk just cannot be trusted for reuse.
                        UncoveredChunks++;
                        chunk.Occupancy = null;
                        Build(chunk, geometry, cx, cy);
                        chunk.CheckedGeneration = -1;
                    }
                    else if (chunk.Occupancy != null && occupancy.AsSpan(0, words).SequenceEqual(chunk.Occupancy))
                    {
                        ChunksValidated++;
                        chunk.CheckedGeneration = geometry.Generation;
                    }
                    else
                    {
                        chunk.Occupancy ??= new ulong[words];
                        occupancy.AsSpan(0, words).CopyTo(chunk.Occupancy);
                        Build(chunk, geometry, cx, cy);
                        chunk.CheckedGeneration = geometry.Generation;
                    }
                }
                visible.Add(chunk);
                VisibleVertices += chunk.VertexCount;
                VisibleWeightedPixels += chunk.WeightedPixels;
            }

        // Evict chunks well outside the light bounds; their arena ranges are reused by entering chunks.
        evicted.Clear();
        foreach (var pair in chunks)
        {
            int cx = (int)(pair.Key >> 32), cy = (int)(uint)pair.Key;
            if (cx < cx0 - KeepChunks || cx > cx1 + KeepChunks || cy < cy0 - KeepChunks || cy > cy1 + KeepChunks)
                evicted.Add(pair.Key);
        }
        foreach (long key in evicted) { Free(chunks[key]); chunks.Remove(key); }
    }

    public void Draw(Effect effect)
    {
        if (arena == null) return;
        device.SetVertexBuffer(arena);
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            foreach (var chunk in visible)
                if (chunk.VertexCount > 0 && chunk.Start >= 0)
                    device.DrawPrimitives(PrimitiveType.TriangleList, chunk.Start, chunk.VertexCount / 3);
        }
        device.SetVertexBuffer(null);
    }

    private void Build(Chunk chunk, V8Geometry geometry, int cx, int cy)
    {
        long stage = System.Diagnostics.Stopwatch.GetTimestamp();
        int count = 0, weighted = 0, reach = Reach;
        int left = cx * ChunkSize, top = cy * ChunkSize, right = left + ChunkSize;
        for (int y = top; y < top + ChunkSize; y++)
        {
            int coreStart = int.MinValue;
            for (int x = left; x <= right; x++)
            {
                if (x < right && geometry.IsSolid(x, y))
                {
                    if (!geometry.TryFace(x, y, reach, out Vector2 sample, out int depth))
                    {
                        if (coreStart == int.MinValue) coreStart = x;
                        continue;
                    }
                    FlushCore(ref coreStart, x, y, ref count);
                    Quad(count, x, x + 1, y, sample, new Color(FaceWeight(depth, faceWidth), SkyBandWeight(depth, skyDepth), 0f, 1f));
                    count += 6;
                    weighted++;
                    continue;
                }
                FlushCore(ref coreStart, x, y, ref count);
            }
        }
        if (chunk.Vertices.Length < count) chunk.Vertices = new VertexPositionColorTexture[count];
        Array.Copy(scratch, chunk.Vertices, count);
        chunk.VertexCount = count;
        chunk.WeightedPixels = weighted;
        ChunksBuilt++;
        LastBuildMs += System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        Upload(chunk);
    }

    /// <summary>Direct and local fill weight at a depth inside the solid: quadratic decay over the face width.</summary>
    public static float FaceWeight(int depth, int width)
    {
        if (depth > width) return 0f;
        float w = 1f - (depth - .5f) / width;
        return w * w;
    }

    /// <summary>Sky ambient weight at a depth inside the solid (depth 1 = on the face): (1 - x/band)^1.15 with x the
    /// pixel centre's distance to the face. With a 24 px band (V6 visual reference): ~0.6 one tile in, ~0.26-0.28 two
    /// tiles in, ~0 at three tiles, and exactly 0 past the band.</summary>
    public const float SkyBandExponent = 1.15f;
    public static float SkyBandWeight(int depth, int band)
    {
        if (depth > band) return 0f;
        float t = (depth - .5f) / band;
        return MathF.Pow(1f - t, SkyBandExponent);
    }

    // No face within the reach: output is black whatever the sample, so the run shares one quad.
    private void FlushCore(ref int coreStart, int x, int y, ref int count)
    {
        if (coreStart == int.MinValue) return;
        Quad(count, coreStart, x, y, new Vector2(coreStart + .5f, y + .5f), Color.Black);
        count += 6;
        coreStart = int.MinValue;
    }

    private void Quad(int start, int x0, int x1, int y, Vector2 sample, Color color)
    {
        // Same vertex order as the former per-pixel quads; TextureCoordinate carries the world sample position.
        scratch[start] = new(new Vector3(x0, y, 0), color, sample);
        scratch[start + 1] = new(new Vector3(x1, y, 0), color, sample);
        scratch[start + 2] = new(new Vector3(x1, y + 1, 0), color, sample);
        scratch[start + 3] = scratch[start];
        scratch[start + 4] = scratch[start + 2];
        scratch[start + 5] = new(new Vector3(x0, y + 1, 0), color, sample);
    }

    private void Upload(Chunk chunk)
    {
        if (chunk.VertexCount == 0) { Free(chunk); return; }
        if (chunk.Start < 0 || chunk.Capacity < chunk.VertexCount)
        {
            Free(chunk);
            int capacity = (chunk.VertexCount + AllocationStep - 1) / AllocationStep * AllocationStep;
            chunk.Start = Allocate(capacity);
            chunk.Capacity = capacity;
        }
        long stage = System.Diagnostics.Stopwatch.GetTimestamp();
        arena.SetData(chunk.Start * Stride, chunk.Vertices, 0, chunk.VertexCount, Stride, SetDataOptions.None);
        LastUploadMs += System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    }

    private int Allocate(int count)
    {
        for (int i = 0; i < free.Count; i++)
        {
            var range = free[i];
            if (range.Count < count) continue;
            if (range.Count == count) free.RemoveAt(i);
            else free[i] = (range.Start + count, range.Count - count);
            return range.Start;
        }
        if (arena != null && arenaEnd + count <= arena.VertexCount)
        {
            int start = arenaEnd;
            arenaEnd += count;
            return start;
        }
        Rewrite(count);
        int compacted = arenaEnd;
        arenaEnd += count;
        return compacted;
    }

    private void Free(Chunk chunk)
    {
        if (chunk.Start < 0) return;
        int start = chunk.Start, count = chunk.Capacity;
        chunk.Start = -1; chunk.Capacity = 0;
        int index = 0;
        while (index < free.Count && free[index].Start < start) index++;
        free.Insert(index, (start, count));
        // Merge with neighbours.
        if (index + 1 < free.Count && free[index].Start + free[index].Count == free[index + 1].Start)
        {
            free[index] = (free[index].Start, free[index].Count + free[index + 1].Count);
            free.RemoveAt(index + 1);
        }
        if (index > 0 && free[index - 1].Start + free[index - 1].Count == free[index].Start)
        {
            free[index - 1] = (free[index - 1].Start, free[index - 1].Count + free[index].Count);
            free.RemoveAt(index);
            index--;
        }
        // A free range at the high-water mark returns to the tail.
        if (free.Count > 0 && free[^1].Start + free[^1].Count == arenaEnd)
        {
            arenaEnd = free[^1].Start;
            free.RemoveAt(free.Count - 1);
        }
    }

    // Compacts every live chunk to the front of the arena, growing it (1.5x headroom) only when required.
    private void Rewrite(int extra)
    {
        int live = 0;
        foreach (var chunk in chunks.Values) if (chunk.Start >= 0) live += chunk.Capacity;
        int required = live + extra;
        if (arena == null || required > arena.VertexCount)
        {
            arena?.Dispose();
            int capacity = Math.Max(required * 3 / 2, 64 * 1024);
            capacity = (capacity + 5) / 6 * 6;
            arena = new DynamicVertexBuffer(device, VertexPositionColorTexture.VertexDeclaration, capacity, BufferUsage.WriteOnly);
            ResourceCreations++;
        }
        ArenaRewrites++;
        free.Clear();
        arenaEnd = 0;
        long stage = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var chunk in chunks.Values)
        {
            if (chunk.Start < 0) continue;
            chunk.Start = arenaEnd;
            arena.SetData(chunk.Start * Stride, chunk.Vertices, 0, chunk.VertexCount, Stride, SetDataOptions.None);
            arenaEnd += chunk.Capacity;
        }
        LastUploadMs += System.Diagnostics.Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    }

    private void ReleaseAll()
    {
        chunks.Clear();
        visible.Clear();
        free.Clear();
        arenaEnd = 0;
    }

    private static long Key(int cx, int cy) => ((long)cx << 32) | (uint)cy;

    public void Dispose()
    {
        device.DeviceReset -= OnDeviceReset;
        arena?.Dispose();
    }
}
