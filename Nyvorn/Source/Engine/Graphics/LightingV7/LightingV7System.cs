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
        BlockOnly = 3,  // block (RGB) channel
        Medium = 4      // cell classification / decay
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

        private float[] sky = Array.Empty<float>();
        private float[] blockR = Array.Empty<float>();
        private float[] blockG = Array.Empty<float>();
        private float[] blockB = Array.Empty<float>();
        private float[] decay = Array.Empty<float>();
        private float[] skyDecay = Array.Empty<float>();
        private CellMedium[] medium = Array.Empty<CellMedium>();
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

        /// <summary>Block channel at a world tile, for diagnostics. Returns zero outside the active region.</summary>
        public Vector3 GetBlockAt(int worldTileX, int worldTileY) =>
            TryGetIndex(worldTileX, worldTileY, out int i) ? new Vector3(blockR[i], blockG[i], blockB[i]) : Vector3.Zero;

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
            DebugView = (LightingV7DebugView)(((int)DebugView + 1) % 5);
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

            int index = localY * width + localX;
            float r = color.X * intensity;
            float g = color.Y * intensity;
            float b = color.Z * intensity;
            if (r > blockR[index]) blockR[index] = r;
            if (g > blockG[index]) blockG[index] = g;
            if (b > blockB[index]) blockB[index] = b;
            SourceCount++;
        }

        /// <summary>Propagates all channels and uploads the texture.</summary>
        public void EndFrame()
        {
            double t0 = stopwatch.Elapsed.TotalMilliseconds;
            int rounds = Math.Max(1, LightingV7Config.PropagationRounds);
            for (int round = 0; round < rounds; round++)
            {
                Sweep(sky, skyDecay, rowCap);
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
                decay = new float[cellCount];
                skyDecay = new float[cellCount];
                medium = new CellMedium[cellCount];
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
                        blockR[i] = 0f; blockG[i] = 0f; blockB[i] = 0f;
                    }
                    continue;
                }

                for (int localX = 0; localX < width; localX++)
                {
                    int i = row + localX;
                    int worldX = worldMap.WrapTileX(originX + localX);

                    blockR[i] = 0f; blockG[i] = 0f; blockB[i] = 0f;

                    TileType tile = worldMap.GetTile(worldX, worldY);
                    bool solid = tile != TileType.Empty && tile != TileType.Platform;

                    if (solid)
                    {
                        medium[i] = CellMedium.Solid;
                        decay[i] = solidDecay;
                        skyDecay[i] = solidDecay;
                        sky[i] = 0f;
                        continue;
                    }

                    float d = airDecay;
                    float sd = skyAirDecay;
                    bool hasWall = worldMap.GetBackgroundTile(worldX, worldY) != TileType.Empty;
                    CellMedium m = hasWall ? CellMedium.AirWalled : CellMedium.AirOpen;

                    if (hasLiquid)
                    {
                        int amount = liquid.GetLiquidAmountAtTile(worldX, worldY);
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
                        float r = blockR[i] < cutoff ? 0f : blockR[i];
                        float g = blockG[i] < cutoff ? 0f : blockG[i];
                        float b = blockB[i] < cutoff ? 0f : blockB[i];
                        texels[i] = new Color(
                            MathHelper.Clamp(r * invOverbright, 0f, 1f),
                            MathHelper.Clamp(g * invOverbright, 0f, 1f),
                            MathHelper.Clamp(b * invOverbright, 0f, 1f),
                            1f);
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
                            float r = blockR[i] < cutoff ? 0f : blockR[i];
                            float g = blockG[i] < cutoff ? 0f : blockG[i];
                            float b = blockB[i] < cutoff ? 0f : blockB[i];

                            r = (r + s * skyColor.X + ambient) * invOverbright;
                            g = (g + s * skyColor.Y + ambient) * invOverbright;
                            b = (b + s * skyColor.Z + ambient) * invOverbright;

                            texels[i] = new Color(
                                MathHelper.Clamp(r, 0f, 1f),
                                MathHelper.Clamp(g, 0f, 1f),
                                MathHelper.Clamp(b, 0f, 1f),
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
