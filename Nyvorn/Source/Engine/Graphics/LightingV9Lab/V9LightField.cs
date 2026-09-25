using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Lab;

/// <param name="Position">World pixels, at the flame or marked exterior aperture.</param>
/// <param name="LinearRgb">Linear RGB chromaticity, dimensionless; not encoded sprite RGB.</param>
/// <param name="Power">Relative irradiance at the source. No photometric-unit claim.</param>
/// <param name="Radius">Finite support in world pixels.</param>
public readonly record struct V9Light(Vector2 Position, Vector3 LinearRgb, float Power, float Radius);

public static class V9LabSettings
{
    public const float TorchRadiusPixels = 144;
    public const float TorchPower = 4;
    public const float SkyRadiusPixels = 196;
    public const float SkySamplePower = 1.4f;
    public const float BackgroundResponse = .14f;
    public const float SurfaceDepthPixels = 6;
    public const float AbsorptionLengthPixels = 2.8f;
    public static readonly Vector3 FireLinearRgb = new(1, .49f, .19f);
    public static readonly Vector3 SkyLinearRgb = new(.46f, .69f, 1);
}

/// <summary>Independent direct light. No ambient floor, transport, V7/V8 state, camera or GPU dependency.</summary>
public static class V9LightMath
{
    public static Vector3 Evaluate(Vector2 receiver, IReadOnlyList<V9Light> lights, Func<int, int, bool> solid)
    {
        Vector3 energy = Vector3.Zero;
        foreach (var light in lights)
        {
            float distance = Vector2.Distance(receiver, light.Position);
            if (distance >= light.Radius || light.Radius <= 0 || light.Power <= 0) continue;
            float falloff = 1 - distance / light.Radius;
            energy += light.LinearRgb * (light.Power * falloff * falloff * Visibility(light.Position, receiver, solid));
        }
        return energy;
    }

    // Exact grid traversal: no fixed-length ray steps, so a thin/corner blocker cannot be skipped.
    // Solid cross-section pixels receive only the terminal segment inside the first opaque mass.
    public static float Visibility(Vector2 from, Vector2 to, Func<int, int, bool> solid)
    {
        const float tile = 8;
        int x = (int)MathF.Floor(from.X / tile), y = (int)MathF.Floor(from.Y / tile);
        int endX = (int)MathF.Floor(to.X / tile), endY = (int)MathF.Floor(to.Y / tile);
        if (solid(x, y)) return 0;
        Vector2 delta = to - from;
        float length = delta.Length();
        if (length < .00001f) return 1;
        int sx = Math.Sign(delta.X), sy = Math.Sign(delta.Y);
        float dx = sx == 0 ? float.PositiveInfinity : tile / MathF.Abs(delta.X);
        float dy = sy == 0 ? float.PositiveInfinity : tile / MathF.Abs(delta.Y);
        float tx = sx == 0 ? float.PositiveInfinity : ((sx > 0 ? (x + 1) * tile : x * tile) - from.X) / delta.X;
        float ty = sy == 0 ? float.PositiveInfinity : ((sy > 0 ? (y + 1) * tile : y * tile) - from.Y) / delta.Y;
        float entered = -1, incidence = 1;
        bool targetSolid = solid(endX, endY);
        while (x != endX || y != endY)
        {
            bool corner = MathF.Abs(tx - ty) < .000001f;
            float t = MathF.Min(tx, ty);
            if (t > 1) break;
            if (corner && (solid(x + sx, y) || solid(x, y + sy))) return 0;
            bool crossX = tx < ty;
            if (corner) { x += sx; y += sy; tx += dx; ty += dy; }
            else if (crossX) { x += sx; tx += dx; }
            else { y += sy; ty += dy; }
            if (solid(x, y))
            {
                if (!targetSolid) return 0;
                if (entered < 0)
                {
                    entered = t;
                    if ((1 - t) * length > V9LabSettings.SurfaceDepthPixels) return 0;
                    incidence = .25f + .75f * MathF.Abs(crossX ? delta.X : delta.Y) / length;
                }
            }
            else if (entered >= 0) return 0; // Never transmit through a solid into air again.
        }
        return entered < 0 ? 1 : incidence * MathF.Exp(-(1 - entered) * length / V9LabSettings.AbsorptionLengthPixels);
    }

    // A common RGB scale after albedo * irradiance; no independent channel clipping or source-count normalization.
    public static Vector3 ToneMap(Vector3 radiance) => radiance / (1 + MathF.Max(radiance.X, MathF.Max(radiance.Y, radiance.Z)));
    public static Vector3 DecodeSrgb(Vector3 v) => new(Decode(v.X), Decode(v.Y), Decode(v.Z));
    public static Vector3 EncodeSrgb(Vector3 v) => new(Encode(v.X), Encode(v.Y), Encode(v.Z));
    private static float Decode(float x) => x <= .04045f ? x / 12.92f : MathF.Pow((x + .055f) / 1.055f, 2.4f);
    private static float Encode(float x) => x <= .0031308f ? x * 12.92f : 1.055f * MathF.Pow(x, 1 / 2.4f) - .055f;
}

/// <summary>One world-anchored texel per world pixel. Rebuilt only when sources or geometry change.</summary>
public sealed class V9LightField : IDisposable
{
    public Texture2D Texture { get; }
    public Vector3[] Energy { get; } = new Vector3[V9LabScene.Width * V9LabScene.Height];
    private readonly HalfVector4[] upload = new HalfVector4[V9LabScene.Width * V9LabScene.Height];
    private readonly V9SourceCache sourceCache;
    private readonly V9OccupancyMask occupancy;
    public bool UsesSourceCache => sourceCache != null;
    public bool UsesOccupancyMask => occupancy != null;
    public ReadOnlySpan<HalfVector4> UploadValues => upload;
    public long SourceCachePayloadBytes => sourceCache?.PayloadBytes ?? 0;
    public int SourceCacheInvalidations { get; private set; }
    internal V9OccupancyMask OccupancyMask => occupancy;
    public long OccupancyMaskBytes => occupancy?.Bytes ?? 0;
    public bool LastMaskRebuilt { get; private set; }
    public double LastMaskUpdateMilliseconds { get; private set; }
    public int LastRecalculatedSources { get; private set; }
    public double LastEvaluateMilliseconds { get; private set; }
    public double LastRecomposeMilliseconds { get; private set; }
    public double LastConvertUploadMilliseconds { get; private set; }
    public int Generation { get; private set; }
    public double LastBuildMilliseconds { get; private set; }
    public float PeakEnergy { get; private set; }

    public V9LightField(GraphicsDevice device, bool useSourceCache = false, bool useOccupancyMask = false)
    {
        // The mask is verified only as a variant of the cache; the reference path keeps scene.Solid.
        if (useOccupancyMask && !useSourceCache) throw new ArgumentException("The V9 occupancy mask is a variant of the source-cache path.");
        // Sample-only half texture, not a floating-point render target or blend requirement.
        Texture = new Texture2D(device, V9LabScene.Width, V9LabScene.Height, false, SurfaceFormat.HalfVector4);
        if (useSourceCache) sourceCache = new V9SourceCache();
        if (useOccupancyMask) occupancy = new V9OccupancyMask();
    }

    public void InvalidateSourceCache()
    {
        if (sourceCache == null) return;
        sourceCache.Invalidate();
        SourceCacheInvalidations++;
    }

    public void Rebuild(V9LabScene scene, IReadOnlyList<V9Light> lights)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (sourceCache != null)
        {
            Func<int, int, bool> solid = null;
            if (occupancy != null)
            {
                // Geometry-only state: camera, zoom, player and source edits leave the tile revision alone.
                LastMaskRebuilt = occupancy.Update(scene);
                LastMaskUpdateMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                solid = occupancy.Solid;
            }
            sourceCache.Rebuild(scene, lights, Energy, solid);
            LastEvaluateMilliseconds = sourceCache.EvaluateMilliseconds;
            LastRecomposeMilliseconds = sourceCache.RecomposeMilliseconds;
            LastRecalculatedSources = sourceCache.Recalculated;
        }
        else
        {
            Func<int, int, bool> solid = scene.Solid;
            for (int y = 0, i = 0; y < V9LabScene.Height; y++)
                for (int x = 0; x < V9LabScene.Width; x++, i++)
                    Energy[i] = V9LightMath.Evaluate(new Vector2(x + .5f, y + .5f), lights, solid);
            LastEvaluateMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            LastRecomposeMilliseconds = 0; // Original evaluator already sums the sources.
            LastRecalculatedSources = lights.Count;
        }
        // Separate instrumentation pass only: identical conversion and full upload in both paths.
        long conversionStart = System.Diagnostics.Stopwatch.GetTimestamp();
        PeakEnergy = 0;
        for (int i = 0; i < Energy.Length; i++)
        {
            var e = Energy[i];
            PeakEnergy = MathF.Max(PeakEnergy, MathF.Max(e.X, MathF.Max(e.Y, e.Z)));
            if (!float.IsFinite(PeakEnergy) || PeakEnergy >= 65000) throw new InvalidOperationException("V9 irradiance exceeds half texture range; do not silently clamp.");
            upload[i] = new HalfVector4(e.X, e.Y, e.Z, 1);
        }
        Texture.SetData(upload);
        LastConvertUploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(conversionStart).TotalMilliseconds;
        Generation++;
        LastBuildMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    public void Dispose() => Texture.Dispose();
}
