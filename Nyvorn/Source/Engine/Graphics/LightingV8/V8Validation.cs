using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

/// <summary>Checks actual GPU outputs, with independently selected witnesses and camera remapping.
/// Readbacks and retained frames exist only in automatic diagnostics.</summary>
public sealed class V8Validation
{
    private readonly string output;
    private readonly V8DiagnosticScene scene;
    private readonly List<string> results = new();
    private Color[] primary, primaryFaces, primaryFinal, two, twoFinal, rightRoom;
    private Rectangle primaryBounds;
    private int failures;
    public V8Validation(string output, V8DiagnosticScene scene) { this.output = output; this.scene = scene; }
    public void Check(string name, bool pass, string detail = "")
    {
        results.Add($"{(pass ? "PASS" : "FAIL")} {name} {detail}");
        if (!pass) failures++;
    }
    public void Observe(string name, V8LightingRenderer renderer, Texture2D albedo, Texture2D final,
        Texture2D entityAlbedo, Texture2D entityLit, Matrix view)
    {
        var direct = V8Capture.Read(renderer.Direct);
        var faces = V8Capture.Read(renderer.Foreground);
        var a = V8Capture.Read(albedo); var f = V8Capture.Read(final);
        var ea = V8Capture.Read(entityAlbedo); var el = V8Capture.Read(entityLit);
        Rectangle bounds = renderer.LightBounds;
        Color At(Color[] field, int x, int y) => Sample(field, bounds, scene.Origin + new Vector2(x, y));
        Check(name + "/composition-alpha", a.Select(p => p.A).SequenceEqual(f.Select(p => p.A)));
        Check(name + "/entity-alpha", ea.Select(p => p.A).SequenceEqual(el.Select(p => p.A)) && ea.Any(p => p.A == 0) && ea.Any(p => p.A == 255));
        Check(name + "/foreground-alpha", faces.Any(p => p.A == 0) && faces.Any(p => p.A == 255));
        if (name == "primary")
        {
            primary = direct; primaryFaces = faces; primaryBounds = bounds; primaryFinal = f;
            Check("background lateral receiver", Peak(At(direct, 96, 48)) > 30);
            Check("platform shadow", Peak(At(direct, 160, 144)) == 0);
            Check("platform surface is 3px", renderer.Geometry.IsSolid((int)scene.Origin.X + 120, (int)scene.Origin.Y + 98)
                && !renderer.Geometry.IsSolid((int)scene.Origin.X + 120, (int)scene.Origin.Y + 99));
            bool blocked = true;
            for (int y = 16; y < 152; y++) for (int x = 224; x < 368; x++) blocked &= Peak(At(direct, x, y)) == 0;
            Check("opaque wall direct zero", blocked, "19584 pixels behind wall");
            Check("exposed wall face", Peak(At(faces, 208, 48)) > 10);
            Check("opposite wall face", Peak(At(faces, 215, 48)) == 0);
            Check("thick mass surface", Peak(At(faces, 40, 176)) > 10);
            Check("thick mass core", Peak(At(faces, 40, 200)) == 0);
            // Fractional source origin checked against the analytic falloff at an unoccluded pixel.
            Vector2 witness = scene.Origin + new Vector2(96.5f, 48.5f);
            float t = MathF.Max(0, 1 - Vector2.Distance(witness, scene.First.Position) / scene.First.Radius);
            int expected = (int)MathF.Round(t * t * 255);
            Check("world pixel source position", Math.Abs(At(direct, 96, 48).R - expected) <= 1, $"GPU={At(direct, 96, 48).R} expected={expected}");
        }
        if (name == "off")
        {
            Check("source off direct zero", direct.All(p => Peak(p) == 0));
            Check("source off faces zero", faces.All(p => Peak(p) == 0));
            Check("source off composition zero", f.All(p => Peak(p) == 0));
        }
        if (name == "two")
        {
            two = direct; twoFinal = f;
            Check("second source fills platform shadow", Peak(At(primary, 160, 144)) == 0 && Peak(At(direct, 160, 144)) > 100);
        }
        if (name == "reverse")
        {
            Check("source order direct", MaxDifference(two, direct) == 0);
            Check("source order composition", MaxDifference(twoFinal, f) == 0);
        }
        if (name == "second")
        {
            // Select an overlapping, unoccluded region. Compare to independently captured fields.
            int error = 0, overlap = 0;
            for (int i = 0; i < two.Length; i++)
            {
                if (Peak(primary[i]) > 10 && Peak(direct[i]) > 10) overlap++;
                var sum = new Color((byte)Math.Min(255, primary[i].R + direct[i].R),
                    (byte)Math.Min(255, primary[i].G + direct[i].G), (byte)Math.Min(255, primary[i].B + direct[i].B));
                error = Math.Max(error, Difference(sum, two[i]));
            }
            Check("overlapping sources add independently", overlap > 100 && error <= 1, $"overlap={overlap} maxDelta={error}");
            Check("two sources preserve accumulated RGB", two.Where((p, i) => p.R < primary[i].R || p.G < direct[i].G || p.B < direct[i].B).Count() == 0);
        }
        if (name == "right")
        {
            rightRoom = direct;
            Check("right mass exposed face", Peak(At(faces, 272, 176)) > 5);
            Check("right mass core", Peak(At(faces, 296, 200)) == 0);
            Check("closed door geometry", renderer.Geometry.IsSolid((int)scene.Origin.X + 304, (int)scene.Origin.Y + 104));
            Check("closed door shadow", Peak(At(direct, 336, 120)) == 0);
        }
        if (name == "door-open") Check("opening door removes shadow", Peak(At(rightRoom, 336, 120)) == 0 && Peak(At(direct, 336, 120)) > 20);
        if (name == "platform-removed") Check("removing platform removes shadow", Peak(At(primary, 160, 144)) == 0 && Peak(At(direct, 160, 144)) > 20);
        if (name is "moved" or "zoom3" or "wrap" or "offscreen")
        {
            int shift = name == "wrap" ? scene.Map.PixelWidth : 0;
            int max = 0, faceMax = 0, pixels = 0;
            for (int y = bounds.Top; y < bounds.Bottom; y++) for (int x = bounds.Left; x < bounds.Right; x++)
            {
                if (!primaryBounds.Contains(x - shift, y)) continue;
                int i = (y - bounds.Y) * bounds.Width + x - bounds.X;
                int j = (y - primaryBounds.Y) * primaryBounds.Width + x - shift - primaryBounds.X;
                max = Math.Max(max, Difference(direct[i], primary[j]));
                faceMax = Math.Max(faceMax, Difference(faces[i], primaryFaces[j])); pixels++;
            }
            Check(name + "/world-registered-direct", max <= 1 && pixels > 1000, $"pixels={pixels} maxDelta={max}");
            Check(name + "/world-registered-faces", faceMax <= 1, $"maxDelta={faceMax}");
            if (name == "wrap") Check("wrap composed frame", MaxDifference(primaryFinal, f) <= 1);
            if (name == "offscreen") Check("offscreen source still lights view", Peak(At(direct, 128, 56)) > 100);
        }
        if (name == "subpixel") Check("subpixel source changes field", MaxDifference(primary, direct) > 0);
        if (name == "finite-radius")
        {
            bool finite = true;
            for (int y = bounds.Top; y < bounds.Bottom; y++) for (int x = bounds.Left; x < bounds.Right; x++)
                if (Vector2.Distance(new Vector2(x + .5f, y + .5f), scene.First.Position) > 32)
                    finite &= Peak(direct[(y - bounds.Top) * bounds.Width + x - bounds.Left]) == 0;
            Check("finite support", finite);
            Check("smooth falloff witnesses", At(direct, 80, 56).R > At(direct, 88, 56).R && At(direct, 88, 56).R > At(direct, 96, 56).R && At(direct, 96, 56).R > At(direct, 104, 56).R);
        }

        // Verify receiver output against albedo * direct on actual opaque enemy pixels.
        Matrix inverse = Matrix.Invert(view);
        int litCount = 0, shadowCount = 0, maxError = 0, tested = 0;
        for (int y = 0; y < entityAlbedo.Height; y++) for (int x = 0; x < entityAlbedo.Width; x++)
        {
            int i = y * entityAlbedo.Width + x;
            if (ea[i].A < 200) continue;
            Vector2 world = Vector2.Transform(new Vector2(x + .5f, y + .5f), inverse);
            float canonicalX = world.X - scene.EntityPosition.X;
            canonicalX -= MathF.Floor(canonicalX / scene.Map.PixelWidth) * scene.Map.PixelWidth;
            if (canonicalX >= 32 || world.Y < scene.EntityPosition.Y || world.Y >= scene.EntityPosition.Y + 32) continue;
            Color light = Sample(direct, bounds, world);
            var expected = new Color((byte)MathF.Round(ea[i].R * light.R / 255f),
                (byte)MathF.Round(ea[i].G * light.G / 255f), (byte)MathF.Round(ea[i].B * light.B / 255f), ea[i].A);
            maxError = Math.Max(maxError, Difference(expected, el[i])); tested++;
            if (Peak(light) > 10) litCount++;
            if (Peak(light) == 0) shadowCount++;
        }
        Check(name + "/entity-per-pixel", maxError <= 2, $"samples={tested} maxDelta={maxError}");
        if (name == "primary") Check("shadow crosses opaque entity pixels", litCount > 0 && shadowCount > 0, $"lit={litCount} shadow={shadowCount}");
        File.WriteAllLines(Path.Combine(output, "checks.txt"), results);
    }
    private static Color Sample(Color[] data, Rectangle bounds, Vector2 world)
    {
        int x = (int)MathF.Floor(world.X), y = (int)MathF.Floor(world.Y);
        return bounds.Contains(x, y) ? data[(y - bounds.Y) * bounds.Width + x - bounds.X] : Color.Transparent;
    }
    private static int Peak(Color p) => Math.Max(p.R, Math.Max(p.G, p.B));
    private static int Difference(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    private static int MaxDifference(Color[] a, Color[] b) => a.Length != b.Length ? 255 : a.Zip(b, Difference).Max();
    public void Finish()
    {
        results.Add($"TOTAL: {results.Count - failures} passed; {failures} failed. Visual inspection is a separate step.");
        File.WriteAllLines(Path.Combine(output, "checks.txt"), results);
        if (failures != 0) throw new InvalidOperationException($"{failures} V8 GPU checks failed; see {output}/checks.txt");
    }
}
