using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Lab;

/// <summary>On-demand runtime readback only. Crops copy exact pixels; no presentation retouch.</summary>
public static class V9LabEvidence
{
    public static void Capture(string output, string name, RenderTarget2D final, RenderTarget2D albedo,
        RenderTarget2D entityLit, RenderTarget2D entityAlbedo, V9LightField field, Matrix view,
        V9LabScene scene, IReadOnlyList<V9Light> lights, float zoom, Vector2 camera)
    {
        Directory.CreateDirectory(output);
        string Prefix(string suffix) => Path.Combine(output, name + "_" + suffix);
        Save(final, Prefix("final.png"));
        Save(albedo, Prefix("albedo.png"));
        Save(entityLit, Prefix("entity-lit.png"));
        Save(entityAlbedo, Prefix("entity-albedo.png"));
        Color[] pixels = Read(final), raw = Read(albedo), entity = Read(entityLit), entityRaw = Read(entityAlbedo);
        Rectangle screen = new(0, 0, final.Width, final.Height);
        Rectangle player = ScreenPatch(scene.PlayerFeet + new Vector2(0, -16), new Vector2(24, 26), view, screen);
        Rectangle wall = ScreenPatch(new Vector2(244, 162), new Vector2(24), view, screen);
        Rectangle wallMaterial = ScreenPatch(new Vector2(244, 142), new Vector2(14, 4), view, screen);
        Rectangle shadow = ScreenPatch(new Vector2(328, 210), new Vector2(24), view, screen);
        Crop(final.GraphicsDevice, pixels, final.Width, player, Prefix("player-crop.png"));
        Crop(final.GraphicsDevice, pixels, final.Width, wall, Prefix("wall-crop.png"));
        Crop(final.GraphicsDevice, pixels, final.Width, shadow, Prefix("shadow-crop.png"));

        // This preview is explicitly a visualization of irradiance, not the rendered image.
        Color[] energyPreview = field.Energy.Select(e => new Color(V9LightMath.EncodeSrgb(V9LightMath.ToneMap(e)))).ToArray();
        using (Texture2D preview = new(final.GraphicsDevice, V9LabScene.Width, V9LabScene.Height))
        {
            preview.SetData(energyPreview);
            Save(preview, Prefix("irradiance-diagnostic.png"));
        }

        object Point(string label, Vector2 world)
        {
            Vector2 at = Vector2.Transform(world, view);
            int x = (int)MathF.Floor(at.X), y = (int)MathF.Floor(at.Y);
            bool visible = screen.Contains(x, y);
            return new { label, world = Xy(world), screen = Xy(at), linearIrradiance = Rgb(Energy(field, world)),
                renderedRgb8 = visible ? Rgb8(pixels[y * final.Width + x]) : null };
        }
        object Patch(Rectangle rect) => new { screenBounds = Bounds(rect),
            final = Measure(pixels, final.Width, rect), albedo = Measure(raw, albedo.Width, rect) };
        object report = new
        {
            name, capturedUtc = DateTimeOffset.UtcNow, resolution = new { width = final.Width, height = final.Height },
            zoom, camera = Xy(camera), playerFeet = Xy(scene.PlayerFeet), scene.OpeningOpen, scene.BlockerPresent,
            view = new[] { view.M11, view.M12, view.M21, view.M22, view.M41, view.M42 },
            sources = lights.Select(l => new { position = Xy(l.Position), linearRgb = Rgb(l.LinearRgb), power = l.Power, radiusPixels = l.Radius }),
            field = new { field.Generation, field.PeakEnergy, field.LastBuildMilliseconds,
                width = V9LabScene.Width, height = V9LabScene.Height, format = "HalfVector4; linear, source sum without clamp" },
            fixedSamples = new[] { Point("wall", new Vector2(244, 142)), Point("near-source", new Vector2(256, 164)),
                Point("player-region-fixed", new Vector2(214, 206)), Point("shadow", new Vector2(328, 210)),
                Point("player-current", scene.PlayerFeet + new Vector2(0, -16)) },
            patches = new { player = Patch(player), wallAndEmitters = Patch(wall), wallMaterial = Patch(wallMaterial), shadow = Patch(shadow) },
            entity = new { mask = "entity albedo alpha == 255; transparent padding excluded", 
                lit = Measure(entity, entityLit.Width, screen, entityRaw),
                albedo = Measure(entityRaw, entityAlbedo.Width, screen, entityRaw),
                shaderAgreement = VerifyEntity(entity, entityRaw, entityLit.Width, field, Matrix.Invert(view)) },
            interpretation = new
            {
                channelLimits = "RGB8 >= 254 counted independently per channel; not a beauty criterion or proof of upstream clipping.",
                rgbRatio = "Measured as linear RGB / channel sum. Tone mapping applies one common RGB scale. Colored irradiance legitimately changes albedo ratios.",
                contrast = "Luminance standard deviation and range describe texture/contrast separately from channel limits or RGB ratios; compare the same patch across cases.",
                preview = "irradiance-diagnostic.png = EncodeSrgb(ToneMap(CPU irradiance)); diagnostic only. Other PNGs are runtime pixels and exact crops.",
                units = "RGB means/ranges are encoded 0..255; luminance is decoded linear relative Y; irradiance is linear relative energy."
            }
        };
        File.WriteAllText(Prefix("metrics.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object VerifyEntity(Color[] lit, Color[] raw, int width, V9LightField field, Matrix inverseView)
    {
        int count = 0, litChromaticityCount = 0;
        double sum = 0, maximum = 0, chromaticityError = 0, maximumChromaticityError = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i].A != 255) continue;
            Vector2 world = Vector2.Transform(new Vector2(i % width + .5f, i / width + .5f), inverseView);
            Vector3 radiance = V9LightMath.DecodeSrgb(raw[i].ToVector3()) * Energy(field, world);
            Vector3 mapped = V9LightMath.ToneMap(radiance);
            Vector3 expected = V9LightMath.EncodeSrgb(mapped);
            Vector3 error = Abs(lit[i].ToVector3() - expected);
            sum += (error.X + error.Y + error.Z) / 3;
            maximum = Math.Max(maximum, Math.Max(error.X, Math.Max(error.Y, error.Z)));
            count++;
            Vector3 actualLinear = V9LightMath.DecodeSrgb(lit[i].ToVector3());
            float expectedSum = mapped.X + mapped.Y + mapped.Z, actualSum = actualLinear.X + actualLinear.Y + actualLinear.Z;
            if (expectedSum < .0001f || actualSum < .0001f) continue;
            Vector3 ratioError = Abs(mapped / expectedSum - actualLinear / actualSum);
            double ratioMax = Math.Max(ratioError.X, Math.Max(ratioError.Y, ratioError.Z));
            chromaticityError += (ratioError.X + ratioError.Y + ratioError.Z) / 3;
            maximumChromaticityError = Math.Max(maximumChromaticityError, ratioMax);
            litChromaticityCount++;
        }
        return new { opaquePixelCount = count, meanNormalizedRgbError = count == 0 ? 0 : sum / count,
            maxNormalizedRgbError = maximum, litChromaticityCount,
            meanLinearChromaticityError = litChromaticityCount == 0 ? 0 : chromaticityError / litChromaticityCount,
            maxLinearChromaticityError = maximumChromaticityError,
            reference = "CPU original albedo -> decode sRGB -> multiply CPU irradiance at floor(inverseView(screen pixel center)) -> common-scale ToneMap -> encode sRGB. Response=1.",
            limitations = "Includes RGBA8/half precision, GPU sampling and quantization differences. Overlapping translucent sprite layers can differ from one evaluation of composited albedo." };
    }

    private static object Measure(Color[] data, int width, Rectangle rect, Color[] opaqueMask = null)
    {
        int count = 0, anyLimit = 0;
        long[] sum = new long[3], limits = new long[3];
        int[] min = { 255, 255, 255 }, max = new int[3];
        double[] square = new double[3];
        double luminanceSum = 0, luminanceSquare = 0, luminanceMin = 1, luminanceMax = 0;
        Vector3 linearSum = Vector3.Zero;
        for (int y = rect.Top; y < rect.Bottom; y++)
        for (int x = rect.Left; x < rect.Right; x++)
        {
            int index = y * width + x;
            if (opaqueMask != null && opaqueMask[index].A != 255) continue;
            Color c = data[index];
            for (int channel = 0; channel < 3; channel++)
            {
                int value = channel == 0 ? c.R : channel == 1 ? c.G : c.B;
                sum[channel] += value; square[channel] += value * value;
                min[channel] = Math.Min(min[channel], value); max[channel] = Math.Max(max[channel], value);
                if (value >= 254) limits[channel]++;
            }
            if (c.R >= 254 || c.G >= 254 || c.B >= 254) anyLimit++;
            Vector3 linear = V9LightMath.DecodeSrgb(c.ToVector3());
            double yLinear = Vector3.Dot(linear, new Vector3(.2126f, .7152f, .0722f));
            linearSum += linear; luminanceSum += yLinear; luminanceSquare += yLinear * yLinear;
            luminanceMin = Math.Min(luminanceMin, yLinear); luminanceMax = Math.Max(luminanceMax, yLinear);
            count++;
        }
        double divisor = Math.Max(1, count), meanY = luminanceSum / divisor;
        double[] means = sum.Select(v => v / divisor).ToArray();
        float total = linearSum.X + linearSum.Y + linearSum.Z;
        return new { pixelCount = count, meanRgb8 = means,
            stddevRgb8 = Enumerable.Range(0, 3).Select(i => Math.Sqrt(Math.Max(0, square[i] / divisor - means[i] * means[i]))),
            minRgb8 = count == 0 ? new int[3] : min, maxRgb8 = max,
            rangeRgb8 = Enumerable.Range(0, 3).Select(i => count == 0 ? 0 : max[i] - min[i]),
            channelCounts254Plus = limits, pixelsAnyChannel254Plus = anyLimit,
            meanLinearLuminance = meanY, stddevLinearLuminance = Math.Sqrt(Math.Max(0, luminanceSquare / divisor - meanY * meanY)),
            minLinearLuminance = count == 0 ? 0 : luminanceMin, maxLinearLuminance = luminanceMax,
            rangeLinearLuminance = count == 0 ? 0 : luminanceMax - luminanceMin,
            aggregateLinearRgbRatio = Rgb(total > 0 ? linearSum / total : Vector3.Zero) };
    }

    private static Vector3 Energy(V9LightField field, Vector2 world)
    {
        int x = (int)MathF.Floor(world.X), y = (int)MathF.Floor(world.Y);
        return x < 0 || y < 0 || x >= V9LabScene.Width || y >= V9LabScene.Height ? Vector3.Zero : field.Energy[y * V9LabScene.Width + x];
    }
    private static Rectangle ScreenPatch(Vector2 center, Vector2 halfSize, Matrix view, Rectangle bounds)
    {
        Vector2 a = Vector2.Transform(center - halfSize, view), b = Vector2.Transform(center + halfSize, view);
        return Rectangle.Intersect(bounds, new Rectangle((int)MathF.Floor(a.X), (int)MathF.Floor(a.Y),
            (int)MathF.Ceiling(b.X) - (int)MathF.Floor(a.X), (int)MathF.Ceiling(b.Y) - (int)MathF.Floor(a.Y)));
    }
    private static Color[] Read(Texture2D texture)
    {
        Color[] pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        return pixels;
    }
    private static void Save(Texture2D texture, string path)
    {
        using FileStream stream = File.Create(path);
        texture.SaveAsPng(stream, texture.Width, texture.Height);
    }
    private static void Crop(GraphicsDevice device, Color[] source, int width, Rectangle rect, string path)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        Color[] cropped = new Color[rect.Width * rect.Height];
        for (int y = 0; y < rect.Height; y++)
            Array.Copy(source, (rect.Y + y) * width + rect.X, cropped, y * rect.Width, rect.Width);
        using Texture2D texture = new(device, rect.Width, rect.Height);
        texture.SetData(cropped);
        Save(texture, path);
    }
    private static object Xy(Vector2 v) => new { x = v.X, y = v.Y };
    private static Vector3 Abs(Vector3 v) => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));
    private static object Rgb(Vector3 v) => new { r = v.X, g = v.Y, b = v.Z };
    private static int[] Rgb8(Color c) => new[] { (int)c.R, (int)c.G, (int)c.B };
    private static object Bounds(Rectangle r) => new { x = r.X, y = r.Y, width = r.Width, height = r.Height };
}
