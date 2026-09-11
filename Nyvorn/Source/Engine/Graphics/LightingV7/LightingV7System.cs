using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Physics.Liquids;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.Engine.Graphics.LightingV7
{
    public enum LightingV7DebugView
    {
        Off = 0,        // normal composed frame
        FinalLight = 1, // light texture as sent to the compositor
        SkyOnly = 2,    // sky channel, grayscale
        BlockOnly = 3,  // block (RGB): direct + indirect
        DirectOnly = 4, // direct term alone, where the hard shadows live
        AmbientOcclusion = 5,
        Medium = 6      // cell classification / decay
    }

    /// <summary>
    /// V7 lighting: per-tile light map over an active region (visible tiles + margin).
    ///
    /// Channels: Sky (scalar, 0..1) and Block (RGB, 0..~1.5).
    /// Propagation: Terraria-style row/column sweeps with multiplicative decay per receiving cell.
    /// Output: one Color texel per tile = clamp((Block + Sky * SkyColor + LayerAmbient) / OverbrightScale).
    ///
    /// No per-frame allocations: arrays grow only when the region grows.
    /// </summary>
    public sealed class LightingV7System
    {
        private enum CellMedium : byte
        {
            AirOpen = 0,   // air, no wall (sky seed candidate)
            AirWalled = 1, // air with background wall
            Solid = 2,
            Water = 3
        }

        private readonly GraphicsDevice graphicsDevice;
        private readonly WorldMap worldMap;
        private readonly Stopwatch stopwatch = new();

        private int surfaceEndY;
        private int shallowEndY;
        private int cavernEndY;

        private int originX;
        private int originY;
        private int width;
        private int height;
        private int cellCount;

        /// <summary>A light source kept for the direct pass, in region-local tile coordinates.</summary>
        private struct PointSource
        {
            public int LocalX;
            public int LocalY;
            public Vector3 Color;
        }

        private float[] sky = Array.Empty<float>();
        private float[] blockR = Array.Empty<float>();
        private float[] blockG = Array.Empty<float>();
        private float[] blockB = Array.Empty<float>();
        private float[] directR = Array.Empty<float>();
        private float[] directG = Array.Empty<float>();
        private float[] directB = Array.Empty<float>();
        private float[] ambientOcclusion = Array.Empty<float>();
        private float[] decay = Array.Empty<float>();
        private float[] skyDecay = Array.Empty<float>();
        private CellMedium[] medium = Array.Empty<CellMedium>();

        // Solid foreground tiles and platforms. Background walls are not occluders: the reference
        // art shows shadows falling ON the back wall, so the wall must stay lit-but-shadowed.
        private bool[] occluder = Array.Empty<bool>();

        private PointSource[] sources = new PointSource[64];

        // A channel with no seed anywhere stays zero however many times it is swept, so the whole
        // pass can be skipped. Underground that removes the sky channel; a lightless area removes RGB.
        private bool hasSkySeed;
        private bool hasBlockSeed;
        private float[] rowCap = Array.Empty<float>();
        private float[] rowAmbient = Array.Empty<float>();
        private Color[] texels = Array.Empty<Color>();

        // The texture is uploaded every frame and drawn in the same frame. Writing to the same
        // object the GPU may still be reading stalls the upload badly (measured ~12 ms in-game).
        // Rotating through a small ring gives the driver a texture that is no longer in flight.
        private const int TextureRingSize = 3;
        private readonly Texture2D[] textureRing = new Texture2D[TextureRingSize];
        private int textureRingIndex;
        private Texture2D texture;

        /// <param name="graphicsDevice">
        /// May be null for headless validation: everything is computed, only the texture upload is skipped.
        /// </param>
        public LightingV7System(GraphicsDevice graphicsDevice, WorldMap worldMap, IReadOnlyList<WorldLayerDefinition> layers)
        {
            this.graphicsDevice = graphicsDevice;
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
            SetLayers(layers);
        }

        /// <summary>Sky channel at a world tile, for diagnostics. Returns 0 outside the active region.</summary>
        public float GetSkyAt(int worldTileX, int worldTileY) =>
            TryGetIndex(worldTileX, worldTileY, out int i) ? sky[i] : 0f;

        /// <summary>Indirect (flood) block channel at a world tile, for diagnostics.</summary>
        public Vector3 GetBlockAt(int worldTileX, int worldTileY) =>
            TryGetIndex(worldTileX, worldTileY, out int i) ? new Vector3(blockR[i], blockG[i], blockB[i]) : Vector3.Zero;

        /// <summary>Direct (hard-shadowed) block channel at a world tile, for diagnostics.</summary>
        public Vector3 GetDirectAt(int worldTileX, int worldTileY) =>
            TryGetIndex(worldTileX, worldTileY, out int i) ? new Vector3(directR[i], directG[i], directB[i]) : Vector3.Zero;

        /// <summary>Ambient occlusion factor at a world tile, for diagnostics. 1 = unoccluded.</summary>
        public float GetAmbientOcclusionAt(int worldTileX, int worldTileY) =>
            TryGetIndex(worldTileX, worldTileY, out int i) ? ambientOcclusion[i] : 1f;

        private bool TryGetIndex(int worldTileX, int worldTileY, out int index)
        {
            int localX = worldTileX - originX;
            int localY = worldTileY - originY;
            if (localX < 0 || localX >= width || localY < 0 || localY >= height)
            {
                index = -1;
                return false;
            }

            index = localY * width + localX;
            return true;
        }

        /// <summary>Optional. When set, water cells use WaterDecay (lerped by fill amount).</summary>
        public LiquidSystem LiquidSystem { get; set; }

        /// <summary>Global sky colour multiplied into the Sky channel when filling the texture.</summary>
        public Vector3 SkyColor { get; set; } = LightingV7Config.SkyColorDay;

        public LightingV7DebugView DebugView { get; set; } = LightingV7DebugView.Off;

        public Texture2D Texture => texture;
        public int OriginTileX => originX;
        public int OriginTileY => originY;
        public int RegionWidth => width;
        public int RegionHeight => height;
        public double LastCpuMs { get; private set; }
        public double LastClassifyMs { get; private set; }
        public double LastPropagateMs { get; private set; }
        public double LastFillMs { get; private set; }
        public double LastUploadMs { get; private set; }
        public double LastDirectMs { get; private set; }
        public double LastAoMs { get; private set; }
        public int SourceCount { get; private set; }

        public void SetLayers(IReadOnlyList<WorldLayerDefinition> layers)
        {
            // Fallbacks keep the system usable without layer data: everything is "surface".
            surfaceEndY = worldMap.Height;
            shallowEndY = worldMap.Height;
            cavernEndY = worldMap.Height;

            if (layers == null)
                return;

            for (int i = 0; i < layers.Count; i++)
            {
                switch (layers[i].LayerType)
                {
                    case WorldLayerType.Surface: surfaceEndY = layers[i].EndY; break;
                    case WorldLayerType.ShallowUnderground: shallowEndY = layers[i].EndY; break;
                    case WorldLayerType.Cavern: cavernEndY = layers[i].EndY; break;
                }
            }
        }

        public void CycleDebugView()
        {
            DebugView = (LightingV7DebugView)(((int)DebugView + 1) % 7);
        }

        /// <summary>
        /// Resizes the region around the camera, classifies cells and seeds the sky channel.
        /// Call AddPointLight for each source, then EndFrame.
        /// </summary>
        public void BeginFrame(float cameraX, float cameraY, int screenWidth, int screenHeight, float zoom, int tileSize)
        {
            stopwatch.Restart();

            float viewWidthPx = screenWidth / MathF.Max(zoom, 0.01f);
            float viewHeightPx = screenHeight / MathF.Max(zoom, 0.01f);

            int firstTileX = (int)MathF.Floor(cameraX / tileSize);
            int firstTileY = (int)MathF.Floor(cameraY / tileSize);
            int lastTileX = (int)MathF.Ceiling((cameraX + viewWidthPx) / tileSize);
            int lastTileY = (int)MathF.Ceiling((cameraY + viewHeightPx) / tileSize);

            int margin = LightingV7Config.MarginTiles;
            originX = firstTileX - margin;
            originY = firstTileY - margin;
            width = (lastTileX - firstTileX + 1) + margin * 2;
            height = (lastTileY - firstTileY + 1) + margin * 2;
            cellCount = width * height;

            EnsureCapacity();

            double t0 = stopwatch.Elapsed.TotalMilliseconds;
            ClassifyAndSeed();
            LastClassifyMs = stopwatch.Elapsed.TotalMilliseconds - t0;

            double t1 = stopwatch.Elapsed.TotalMilliseconds;
            ComputeAmbientOcclusion();
            LastAoMs = stopwatch.Elapsed.TotalMilliseconds - t1;

            SourceCount = 0;
        }

        /// <summary>Adds a point source. Position in world pixels (unwrapped camera space or wrapped: both work).</summary>
        public void AddPointLight(Vector2 positionPixels, Vector3 color, float intensity, int tileSize)
        {
            int tileX = (int)MathF.Floor(positionPixels.X / tileSize);
            int tileY = (int)MathF.Floor(positionPixels.Y / tileSize);

            int localX = tileX - originX;
            int localY = tileY - originY;

            // Sources live in wrapped world space; the region is in the camera's unwrapped space.
            // Bring the source into the region by shifting whole world widths.
            int worldWidth = worldMap.Width;
            if (localX < 0 || localX >= width)
            {
                int shifted = tileX + worldWidth - originX;
                if (shifted >= 0 && shifted < width) localX = shifted;
                else
                {
                    shifted = tileX - worldWidth - originX;
                    if (shifted >= 0 && shifted < width) localX = shifted;
                }
            }

            if (localX < 0 || localX >= width || localY < 0 || localY >= height)
                return;

            Vector3 emitted = color * intensity;

            // With direct shadows on, the source is split: the direct pass carries the shadowed
            // term and the flood only carries the bounce, so shadowed areas stay dim but readable.
            float seedScale = LightingV7Config.DirectShadowsEnabled
                ? MathHelper.Clamp(LightingV7Config.BounceStrength, 0f, 1f)
                : 1f;

            int index = localY * width + localX;
            float r = emitted.X * seedScale;
            float g = emitted.Y * seedScale;
            float b = emitted.Z * seedScale;
            if (r > blockR[index]) blockR[index] = r;
            if (g > blockG[index]) blockG[index] = g;
            if (b > blockB[index]) blockB[index] = b;
            hasBlockSeed = true;

            if (SourceCount >= sources.Length)
                Array.Resize(ref sources, sources.Length * 2);

            sources[SourceCount] = new PointSource { LocalX = localX, LocalY = localY, Color = emitted };
            SourceCount++;
        }

        /// <summary>
        /// Contact darkening: how enclosed each air cell is, from its 3x3 neighbourhood.
        /// Orthogonal neighbours count 1, diagonals 0.5, so a fully buried cell sums to 6.
        /// Solid cells keep 1 (they are a lit face, not a crevice).
        /// </summary>
        private void ComputeAmbientOcclusion()
        {
            float strength = MathHelper.Clamp(LightingV7Config.AOStrength, 0f, 1f);
            Span<float> ao = ambientOcclusion.AsSpan(0, cellCount);

            if (strength <= 0f)
            {
                ao.Fill(1f);
                return;
            }

            int w = width;
            int h = height;
            float scale = strength / 6f;

            // The one-cell border sits deep inside the 48-tile margin and is never on screen, so it
            // is left neutral rather than paying a bounds test in the inner loop.
            ao.Fill(1f);

            for (int y = 1; y < h - 1; y++)
            {
                int row = y * w;
                int above = row - w;
                int below = row + w;

                for (int x = 1; x < w - 1; x++)
                {
                    int i = row + x;
                    if (occluder[i])
                        continue;

                    float sum = 0f;
                    if (occluder[i - 1]) sum += 1f;
                    if (occluder[i + 1]) sum += 1f;
                    if (occluder[above + x]) sum += 1f;
                    if (occluder[below + x]) sum += 1f;
                    if (occluder[above + x - 1]) sum += 0.5f;
                    if (occluder[above + x + 1]) sum += 0.5f;
                    if (occluder[below + x - 1]) sum += 0.5f;
                    if (occluder[below + x + 1]) sum += 0.5f;

                    ao[i] = 1f - sum * scale;
                }
            }
        }

        /// <summary>
        /// Direct term: for every cell within DirectRadiusTiles of a source, walk a DDA ray from the
        /// source centre to the cell centre. Any occluder strictly between them kills the light, so
        /// a plank on a wall throws a hard diagonal shadow. The destination cell is never treated as
        /// its own blocker - a solid tile facing the source is the lit face.
        /// Falloff is (1 - d/R)^2. Contributions from several sources add.
        /// </summary>
        private void ComputeDirect()
        {
            Array.Clear(directR, 0, cellCount);
            Array.Clear(directG, 0, cellCount);
            Array.Clear(directB, 0, cellCount);

            if (!LightingV7Config.DirectShadowsEnabled || SourceCount == 0)
                return;

            int radius = Math.Max(1, LightingV7Config.DirectRadiusTiles);
            float invRadius = 1f / radius;
            int radiusSquared = radius * radius;

            for (int s = 0; s < SourceCount; s++)
            {
                PointSource source = sources[s];
                int sx = source.LocalX;
                int sy = source.LocalY;

                int minX = Math.Max(0, sx - radius);
                int maxX = Math.Min(width - 1, sx + radius);
                int minY = Math.Max(0, sy - radius);
                int maxY = Math.Min(height - 1, sy + radius);

                for (int y = minY; y <= maxY; y++)
                {
                    int dy = y - sy;
                    int dySquared = dy * dy;
                    int row = y * width;

                    for (int x = minX; x <= maxX; x++)
                    {
                        int dx = x - sx;
                        int distanceSquared = dx * dx + dySquared;
                        if (distanceSquared > radiusSquared)
                            continue;

                        float distance = MathF.Sqrt(distanceSquared);
                        float falloff = 1f - distance * invRadius;
                        falloff *= falloff;
                        if (falloff <= 0f)
                            continue;

                        if (!IsDirectlyVisible(sx, sy, x, y))
                            continue;

                        int i = row + x;
                        directR[i] += source.Color.X * falloff;
                        directG[i] += source.Color.Y * falloff;
                        directB[i] += source.Color.Z * falloff;
                    }
                }
            }
        }

        /// <summary>
        /// Amanatides-Woo tile traversal from the source centre to the target centre.
        /// Returns false when an occluder lies strictly between the two cells.
        /// </summary>
        private bool IsDirectlyVisible(int sx, int sy, int tx, int ty)
        {
            int dx = tx - sx;
            int dy = ty - sy;
            if (dx == 0 && dy == 0)
                return true;

            int stepX = dx >= 0 ? 1 : -1;
            int stepY = dy >= 0 ? 1 : -1;
            float absDx = MathF.Abs(dx);
            float absDy = MathF.Abs(dy);

            // Parameter (0..1 along the ray) to cross one whole tile, and to reach the first border.
            float tDeltaX = absDx > 0f ? 1f / absDx : float.MaxValue;
            float tDeltaY = absDy > 0f ? 1f / absDy : float.MaxValue;
            float tMaxX = absDx > 0f ? 0.5f / absDx : float.MaxValue;
            float tMaxY = absDy > 0f ? 0.5f / absDy : float.MaxValue;

            int x = sx;
            int y = sy;
            int guard = (int)(absDx + absDy) + 2;   // every step advances one axis by one tile

            while (guard-- > 0)
            {
                if (tMaxX < tMaxY)
                {
                    tMaxX += tDeltaX;
                    x += stepX;
                }
                else
                {
                    tMaxY += tDeltaY;
                    y += stepY;
                }

                if (x == tx && y == ty)
                    return true;

                if (occluder[y * width + x])
                    return false;
            }

            return true;
        }

        /// <summary>Propagates all channels and uploads the texture.</summary>
        public void EndFrame()
        {
            double tDirect = stopwatch.Elapsed.TotalMilliseconds;
            ComputeDirect();
            LastDirectMs = stopwatch.Elapsed.TotalMilliseconds - tDirect;

            double t0 = stopwatch.Elapsed.TotalMilliseconds;
            int rounds = Math.Max(1, LightingV7Config.PropagationRounds);
            for (int round = 0; round < rounds; round++)
            {
                if (hasSkySeed)
                    Sweep(sky, skyDecay, rowCap);
                if (hasBlockSeed)
                    SweepRgb();
            }
            double t1 = stopwatch.Elapsed.TotalMilliseconds;
            LastPropagateMs = t1 - t0;

            FillTexture();
            stopwatch.Stop();
            LastFillMs = stopwatch.Elapsed.TotalMilliseconds - t1;
            LastCpuMs = stopwatch.Elapsed.TotalMilliseconds;
        }

        private void EnsureCapacity()
        {
            if (sky.Length < cellCount)
            {
                sky = new float[cellCount];
                blockR = new float[cellCount];
                blockG = new float[cellCount];
                blockB = new float[cellCount];
                directR = new float[cellCount];
                directG = new float[cellCount];
                directB = new float[cellCount];
                ambientOcclusion = new float[cellCount];
                decay = new float[cellCount];
                skyDecay = new float[cellCount];
                medium = new CellMedium[cellCount];
                occluder = new bool[cellCount];
                texels = new Color[cellCount];
            }

            if (rowCap.Length < height)
            {
                rowCap = new float[height];
                rowAmbient = new float[height];
            }

            if (graphicsDevice == null)
                return;

            textureRingIndex = (textureRingIndex + 1) % TextureRingSize;
            Texture2D candidate = textureRing[textureRingIndex];
            if (candidate == null || candidate.Width != width || candidate.Height != height)
            {
                candidate?.Dispose();
                candidate = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
                textureRing[textureRingIndex] = candidate;
            }

            texture = candidate;
        }

        private void ClassifyAndSeed()
        {
            float airDecay = LightingV7Config.AirDecay;
            float solidDecay = LightingV7Config.SolidDecay;
            float waterDecay = LightingV7Config.WaterDecay;
            float skyAirDecay = LightingV7Config.SkyAirDecay;
            float surfaceSeed = LightingV7Config.SurfaceSkySeed;
            float shallowSeed = LightingV7Config.ShallowSkySeed;
            int fadeTiles = Math.Max(1, LightingV7Config.SkyFadeTiles);

            LiquidSystem liquid = LiquidSystem;
            bool hasLiquid = liquid != null && liquid.CellCount > 0;
            float maxLiquid = hasLiquid ? Math.Max(1, liquid.Rules.MaxLiquidAmount) : 1f;

            int worldHeight = worldMap.Height;
            hasSkySeed = false;
            hasBlockSeed = false;

            for (int localY = 0; localY < height; localY++)
            {
                int worldY = originY + localY;

                // Per-row sky cap and ambient floor
                float cap;
                if (worldY <= shallowEndY) cap = 1f;
                else cap = MathF.Max(0f, 1f - (worldY - shallowEndY) / (float)fadeTiles);
                rowCap[localY] = cap;

                if (worldY <= surfaceEndY) rowAmbient[localY] = LightingV7Config.AmbientSurface;
                else if (worldY <= shallowEndY) rowAmbient[localY] = LightingV7Config.AmbientShallow;
                else if (worldY <= cavernEndY) rowAmbient[localY] = LightingV7Config.AmbientCavern;
                else rowAmbient[localY] = LightingV7Config.AmbientDeep;

                float rowSeed = worldY <= surfaceEndY ? surfaceSeed : (worldY <= shallowEndY ? shallowSeed : 0f);
                if (rowSeed > 0f)
                    hasSkySeed = true;

                int row = localY * width;

                if (worldY < 0)
                {
                    // Above the world: open sky.
                    for (int localX = 0; localX < width; localX++)
                    {
                        int i = row + localX;
                        medium[i] = CellMedium.AirOpen;
                        decay[i] = airDecay;
                        skyDecay[i] = skyAirDecay;
                        sky[i] = surfaceSeed;
                        occluder[i] = false;
                        blockR[i] = 0f; blockG[i] = 0f; blockB[i] = 0f;
                    }
                    continue;
                }

                if (worldY >= worldHeight)
                {
                    for (int localX = 0; localX < width; localX++)
                    {
                        int i = row + localX;
                        medium[i] = CellMedium.Solid;
                        decay[i] = solidDecay;
                        skyDecay[i] = solidDecay;
                        sky[i] = 0f;
                        occluder[i] = true;
                        blockR[i] = 0f; blockG[i] = 0f; blockB[i] = 0f;
                    }
                    continue;
                }

                // One dictionary hit per row instead of one per air cell.
                bool rowHasLiquid = hasLiquid && liquid.HasLiquidInRow(worldY);

                // Walk the wrapped x incrementally so the modulo runs once per row, not per cell.
                int worldWidth = worldMap.Width;
                int wrappedX = originX + 0;
                wrappedX %= worldWidth;
                if (wrappedX < 0) wrappedX += worldWidth;

                for (int localX = 0; localX < width; localX++)
                {
                    int i = row + localX;

                    blockR[i] = 0f; blockG[i] = 0f; blockB[i] = 0f;

                    worldMap.GetTilePairWrapped(wrappedX, worldY, out TileType tile, out TileType wall);

                    int nextWrappedX = wrappedX + 1;
                    if (nextWrappedX >= worldWidth) nextWrappedX = 0;

                    bool isPlatform = tile == TileType.Platform;
                    bool solid = tile != TileType.Empty && !isPlatform;

                    // Platforms block the direct ray (they are what casts the plank shadow in the
                    // reference) but do not slow the flood, so light still wraps around them.
                    occluder[i] = solid || isPlatform;

                    if (solid)
                    {
                        medium[i] = CellMedium.Solid;
                        decay[i] = solidDecay;
                        skyDecay[i] = solidDecay;
                        sky[i] = 0f;
                        wrappedX = nextWrappedX;
                        continue;
                    }

                    float d = airDecay;
                    float sd = skyAirDecay;
                    bool hasWall = wall != TileType.Empty;
                    CellMedium m = hasWall ? CellMedium.AirWalled : CellMedium.AirOpen;

                    if (rowHasLiquid)
                    {
                        int amount = liquid.GetLiquidAmountAtTile(wrappedX, worldY);
                        if (amount > 0)
                        {
                            float fill = MathF.Min(1f, amount / maxLiquid);
                            d = airDecay + (waterDecay - airDecay) * fill;
                            sd = skyAirDecay + (waterDecay - skyAirDecay) * fill;
                            if (fill >= 0.5f) m = CellMedium.Water;
                        }
                    }

                    decay[i] = d;
                    skyDecay[i] = sd;
                    medium[i] = m;
                    sky[i] = hasWall ? 0f : rowSeed;
                    wrappedX = nextWrappedX;
                }
            }
        }

        /// <summary>
        /// One propagation round of a single channel: left→right, right→left, top→bottom, bottom→top.
        /// Decay belongs to the cell that RECEIVES the light.
        /// cap (optional, per row) clamps every write, so the sky channel can never exceed
        /// SkyCap(y) no matter which direction the light arrived from.
        ///
        /// The vertical sweeps walk row by row (not column by column): each row only depends on the
        /// row before it, so this stays correct while reading memory sequentially instead of
        /// jumping one row-stride per step.
        /// </summary>
        private void Sweep(float[] light, float[] cellDecay, float[] cap)
        {
            int w = width;
            int h = height;
            Span<float> l = light.AsSpan(0, cellCount);
            Span<float> d = cellDecay.AsSpan(0, cellCount);

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float rowLimit = cap != null ? cap[y] : float.MaxValue;

                for (int x = 1; x < w; x++)
                {
                    int i = row + x;
                    float v = l[i - 1] * d[i];
                    if (v > rowLimit) v = rowLimit;
                    if (v > l[i]) l[i] = v;
                }
                for (int x = w - 2; x >= 0; x--)
                {
                    int i = row + x;
                    float v = l[i + 1] * d[i];
                    if (v > rowLimit) v = rowLimit;
                    if (v > l[i]) l[i] = v;
                }
            }

            for (int y = 1; y < h; y++)
            {
                int row = y * w;
                float rowLimit = cap != null ? cap[y] : float.MaxValue;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    float v = l[i - w] * d[i];
                    if (v > rowLimit) v = rowLimit;
                    if (v > l[i]) l[i] = v;
                }
            }

            for (int y = h - 2; y >= 0; y--)
            {
                int row = y * w;
                float rowLimit = cap != null ? cap[y] : float.MaxValue;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    float v = l[i + w] * d[i];
                    if (v > rowLimit) v = rowLimit;
                    if (v > l[i]) l[i] = v;
                }
            }
        }

        /// <summary>
        /// Same round for the three block channels at once, so the decay array is read once per
        /// cell instead of three times. Block light has no cap.
        /// </summary>
        private void SweepRgb()
        {
            int w = width;
            int h = height;
            Span<float> r = blockR.AsSpan(0, cellCount);
            Span<float> g = blockG.AsSpan(0, cellCount);
            Span<float> b = blockB.AsSpan(0, cellCount);
            Span<float> d = decay.AsSpan(0, cellCount);

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 1; x < w; x++)
                {
                    int i = row + x;
                    float k = d[i];
                    float vr = r[i - 1] * k; if (vr > r[i]) r[i] = vr;
                    float vg = g[i - 1] * k; if (vg > g[i]) g[i] = vg;
                    float vb = b[i - 1] * k; if (vb > b[i]) b[i] = vb;
                }
                for (int x = w - 2; x >= 0; x--)
                {
                    int i = row + x;
                    float k = d[i];
                    float vr = r[i + 1] * k; if (vr > r[i]) r[i] = vr;
                    float vg = g[i + 1] * k; if (vg > g[i]) g[i] = vg;
                    float vb = b[i + 1] * k; if (vb > b[i]) b[i] = vb;
                }
            }

            for (int y = 1; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    float k = d[i];
                    float vr = r[i - w] * k; if (vr > r[i]) r[i] = vr;
                    float vg = g[i - w] * k; if (vg > g[i]) g[i] = vg;
                    float vb = b[i - w] * k; if (vb > b[i]) b[i] = vb;
                }
            }

            for (int y = h - 2; y >= 0; y--)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;
                    float k = d[i];
                    float vr = r[i + w] * k; if (vr > r[i]) r[i] = vr;
                    float vg = g[i + w] * k; if (vg > g[i]) g[i] = vg;
                    float vb = b[i + w] * k; if (vb > b[i]) b[i] = vb;
                }
            }
        }

        private void FillTexture()
        {
            float cutoff = LightingV7Config.LightCutoff;
            float invOverbright = 1f / MathF.Max(0.01f, LightingV7Config.OverbrightScale);
            Vector3 skyColor = SkyColor;

            switch (DebugView)
            {
                case LightingV7DebugView.SkyOnly:
                    for (int i = 0; i < cellCount; i++)
                    {
                        float s = sky[i] < cutoff ? 0f : sky[i];
                        texels[i] = new Color(s, s, s, 1f);
                    }
                    break;

                case LightingV7DebugView.BlockOnly:
                    for (int i = 0; i < cellCount; i++)
                    {
                        float r = blockR[i] + directR[i];
                        float g = blockG[i] + directG[i];
                        float b = blockB[i] + directB[i];
                        texels[i] = new Color(
                            MathHelper.Clamp(r * invOverbright, 0f, 1f),
                            MathHelper.Clamp(g * invOverbright, 0f, 1f),
                            MathHelper.Clamp(b * invOverbright, 0f, 1f),
                            1f);
                    }
                    break;

                case LightingV7DebugView.DirectOnly:
                    for (int i = 0; i < cellCount; i++)
                    {
                        texels[i] = new Color(
                            MathHelper.Clamp(directR[i] * invOverbright, 0f, 1f),
                            MathHelper.Clamp(directG[i] * invOverbright, 0f, 1f),
                            MathHelper.Clamp(directB[i] * invOverbright, 0f, 1f),
                            1f);
                    }
                    break;

                case LightingV7DebugView.AmbientOcclusion:
                    for (int i = 0; i < cellCount; i++)
                    {
                        float v = MathHelper.Clamp(ambientOcclusion[i], 0f, 1f);
                        texels[i] = new Color(v, v, v, 1f);
                    }
                    break;

                case LightingV7DebugView.Medium:
                    for (int i = 0; i < cellCount; i++)
                    {
                        texels[i] = medium[i] switch
                        {
                            CellMedium.AirOpen => new Color(20, 90, 200),
                            CellMedium.AirWalled => new Color(70, 70, 70),
                            CellMedium.Water => new Color(40, 160, 220),
                            _ => new Color(170, 60, 50)
                        };
                    }
                    break;

                default:
                    for (int y = 0; y < height; y++)
                    {
                        float ambient = rowAmbient[y];
                        int row = y * width;
                        for (int x = 0; x < width; x++)
                        {
                            int i = row + x;
                            float s = sky[i] < cutoff ? 0f : sky[i];

                            // Block = direct (hard-shadowed) + indirect (flood bounce).
                            float r = blockR[i] + directR[i];
                            float g = blockG[i] + directG[i];
                            float b = blockB[i] + directB[i];
                            if (r < cutoff) r = 0f;
                            if (g < cutoff) g = 0f;
                            if (b < cutoff) b = 0f;

                            // Per channel MAX against the sky, not a sum: adding them made ground
                            // near a torch read about twice as bright as ground in open daylight.
                            // Whichever light dominates a cell wins; they no longer stack.
                            float occlusion = ambientOcclusion[i];
                            r = MathF.Max(r, s * skyColor.X) * occlusion + ambient;
                            g = MathF.Max(g, s * skyColor.Y) * occlusion + ambient;
                            b = MathF.Max(b, s * skyColor.Z) * occlusion + ambient;

                            texels[i] = new Color(
                                MathHelper.Clamp(r * invOverbright, 0f, 1f),
                                MathHelper.Clamp(g * invOverbright, 0f, 1f),
                                MathHelper.Clamp(b * invOverbright, 0f, 1f),
                                1f);
                        }
                    }
                    break;
            }

            double beforeUpload = stopwatch.Elapsed.TotalMilliseconds;
            texture?.SetData(texels, 0, cellCount);
            LastUploadMs = stopwatch.Elapsed.TotalMilliseconds - beforeUpload;
        }

        public void Dispose()
        {
            for (int i = 0; i < textureRing.Length; i++)
            {
                textureRing[i]?.Dispose();
                textureRing[i] = null;
            }

            texture = null;
        }
    }
}
