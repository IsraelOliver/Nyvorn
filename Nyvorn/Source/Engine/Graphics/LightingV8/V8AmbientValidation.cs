using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

/// <summary>Transport witnesses plus actual GPU receiver checks; separate from direct-only acceptance.</summary>
public sealed class V8AmbientValidation
{
    private readonly string output;
    private readonly V8DiagnosticScene scene;
    private readonly List<string> results = new();
    private Color[] localReference, directReference, skyReference, closedDoor;
    private Rectangle localBounds, skyBounds;
    private int failures;
    public V8AmbientValidation(string output, V8DiagnosticScene scene) { this.output = output; this.scene = scene; }
    public void Check(string name, bool passed, string detail = "")
    {
        results.Add($"{(passed ? "PASS" : "FAIL")} {name} {detail}");
        if (!passed) failures++;
    }
    public void Observe(string name, V8LightingRenderer renderer, Texture2D albedo, Texture2D final,
        Texture2D entityAlbedo, Texture2D entityLit, Matrix view)
    {
        var sky = V8Capture.Read(renderer.Ambient.SkyTexture);
        var local = V8Capture.Read(renderer.Ambient.LocalTexture);
        var direct = V8Capture.Read(renderer.Direct);
        var faces = V8Capture.Read(renderer.Foreground);
        var a = V8Capture.Read(albedo); var f = V8Capture.Read(final);
        var ea = V8Capture.Read(entityAlbedo); var el = V8Capture.Read(entityLit);
        var bounds = renderer.LightBounds;
        var context = renderer.AmbientContext;
        Color At(Color[] pixels, int x, int y) => Sample(pixels, bounds, scene.Origin + new Vector2(x, y));
        Check(name + "/alpha", a.Select(p => p.A).SequenceEqual(f.Select(p => p.A)) && ea.Select(p => p.A).SequenceEqual(el.Select(p => p.A)));
        bool deepZero = true, deepFacesZero = true;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            if (context.SkyWeight((int)MathF.Floor(y / 8f)) == 0)
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    int i = (y - bounds.Top) * bounds.Width + x - bounds.Left;
                    deepZero &= Peak(sky[i]) == 0;
                    if (!context.LocalEnabled && scene.Lights.Count == 0) deepFacesZero &= Peak(faces[i]) == 0;
                }
        Check(name + "/sky-veto-after-reconstruction-and-faces", deepZero && deepFacesZero);
        if (name == "local")
        {
            localReference = local; directReference = direct; localBounds = bounds;
            Check("platform shadow receives weak local fill", Peak(At(direct, 160, 144)) == 0 && Peak(At(local, 160, 144)) >= 8,
                $"direct={At(direct, 160, 144)} local={At(local, 160, 144)}");
            Check("direct remains dominant", Peak(At(direct, 96, 48)) > 3 * Peak(At(local, 96, 48)));
            Check("ambient walks around solid mass", Peak(At(direct, 16, 200)) == 0 && Peak(At(local, 16, 200)) > 0);
            Check("ambient does not cross thin wall", Peak(At(local, 224, 104)) == 0);
            Check("mass core stays black", Peak(At(faces, 40, 200)) == 0);
        }
        if (name is "local-moved" or "local-wrap")
        {
            int shift = name == "local-wrap" ? scene.Map.PixelWidth : 0;
            Check(name + "/world-anchored-local", CompareOverlap(local, bounds, localReference, localBounds, shift) == 0);
            Check(name + "/world-anchored-direct", CompareOverlap(direct, bounds, directReference, localBounds, shift) <= 1);
        }
        if (name is "local-off" or "local-deep-off")
            Check(name + "/no-residual", local.All(p => Peak(p) == 0) && sky.All(p => Peak(p) == 0) && direct.All(p => Peak(p) == 0) && f.All(p => Peak(p) == 0));
        // Face band fixtures (V8DiagnosticScene.FaceBandSolid). Curve-agnostic: along a line into the solid from its sky
        // face, pixels nearer the sky face must follow SkyBandWeight (relative to depth 1, the same air sample for the whole
        // line), pixels nearer the dark face must stay 0. The value just before the midline is the step, recorded only.
        if (name == "sky-band")
        {
            // 6-tile ceiling (block rows 11..15 plus the roof), centre column x 188, 28 px from the block sides.
            FaceBand("6-tile ceiling (block)", Line(188, 88, 0, 1, 48), 48);
        }
        if (name == "sky-band-ceilings")
            foreach (var c in V8DiagnosticScene.CeilingFixtures)
                FaceBand($"{c.Thickness}-tile ceiling", Line((c.Left + c.Right + 1) * 4, (17 - c.Thickness) * 8, 0, 1, c.Thickness * 8), c.Thickness * 8);
        if (name == "sky-band-walls")
            foreach (var w in V8DiagnosticScene.WallFixtures)
            {
                // Row y 100: 36 px from the cap top and the roof bottom, so the horizontal faces are the nearest.
                FaceBand($"{w.Thickness}-tile wall", Line(w.Left * 8, 100, 1, 0, w.Thickness * 8), w.Thickness * 8);
                int pocketX = (w.Left + w.Thickness + 1) * 8;
                Check($"sky band fixture: {w.Thickness}-tile wall pocket receives no sky or local", Peak(At(sky, pocketX + 64, 100 - 128)) == 0 &&
                    Peak(At(local, pocketX + 64, 100 - 128)) == 0);
            }
        if (name == "sky-aperture")
        {
            // 2x2 background hole inside the room: exposure leaving it along the axis and along the 45 degree diagonal.
            var hole = V8DiagnosticScene.Aperture;
            int[] axis = Enumerable.Range(0, 12).Select(k => Peak(At(sky, (hole.X + 2 + k) * 8 + 4 + 64, (hole.Y + 1) * 8 + 4 - 128))).ToArray();
            int[] diagonal = Enumerable.Range(0, 12).Select(k => Peak(At(sky, (hole.X + 2 + k) * 8 + 4 + 64, (hole.Y + 2 + k) * 8 + 4 - 128))).ToArray();
            int Reach(int[] line)
            {
                int last = -1;
                for (int k = 0; k < line.Length; k++) if (line[k] > 0) last = k;
                return last + 1;
            }
            Check("sky aperture fixture: the 2x2 hole lights the room", axis[0] > 0 && diagonal[0] > 0);
            results.Add($"INFO sky aperture fixture: sky peak along the axis, tiles 1..12 = {string.Join(",", axis)}; reach {Reach(axis)} tiles");
            results.Add($"INFO sky aperture fixture: sky peak along the 45 degree diagonal, steps 1..12 = {string.Join(",", diagonal)}; reach {Reach(diagonal)} steps");
        }
        if (name == "sky-corner")
        {
            var lit = V8DiagnosticScene.CornerLit;
            var pocket = V8DiagnosticScene.CornerPocket;
            int litSky = Peak(At(sky, lit.X * 8 + 4 + 64, lit.Y * 8 + 4 - 128));
            int pocketSky = Peak(At(sky, pocket.X * 8 + 4 + 64, pocket.Y * 8 + 4 - 128));
            Check("sky corner fixture: the air beside the closed corner is lit", litSky > 0, $"sky {litSky}");
            Check("sky corner fixture: no sky crosses the closed diagonal corner", pocketSky == 0, $"pocket sky {pocketSky}");
        }
        if (name is "sky-sealed" or "sky-interior-void" or "sky-deep-void" || name.StartsWith("sky-band"))
        {
            bool dark = true;
            for (int y = 16; y < 232; y++) for (int x = 16; x < 200; x++) dark &= Peak(At(sky, x, y)) == 0 && Peak(At(local, x, y)) == 0;
            Check(name + "/interior-dark", dark);
        }
        if (name == "sky-window")
        {
            skyReference = sky; skyBounds = bounds;
            Check("window works under opaque roof", scene.Map.GetTile(0, 16) != Nyvorn.Source.World.TileType.Empty &&
                context.IsExteriorAperture(scene.Map, 0, 21) && Peak(At(sky, 68, 44)) > 40);
            Check("window fades progressively laterally", At(sky, 92, 60).B > At(sky, 132, 60).B && At(sky, 132, 60).B > At(sky, 188, 60).B,
                $"blue={At(sky, 92, 60).B},{At(sky, 132, 60).B},{At(sky, 188, 60).B}");
            Check("fade ends within Shallow", context.SkyWeight(38) > 0 && context.SkyWeight(39) == 0 && Peak(At(sky, 100, 188)) == 0);
            Check("Space and Surface exterior eligibility", context.IsExteriorAperture(scene.Map, 80, 4) && context.IsExteriorAperture(scene.Map, 80, 12));
            Check("deep and unknown emptiness not sky", !context.IsExteriorAperture(scene.Map, 80, 44) && !context.IsExteriorAperture(scene.Map, 80, 56) && !context.IsExteriorAperture(scene.Map, 80, -1));
        }
        if (name == "sky-moved") Check("world-anchored-sky", CompareOverlap(sky, bounds, skyReference, skyBounds, 0) <= 1);
        if (name == "sky-fissure") Check("Shallow fissure compatibility", context.IsExteriorAperture(scene.Map, 6, 29) && Peak(At(sky, 116, 108)) > 40 && Peak(At(sky, 140, 108)) > 0);
        if (name is "local-cavern" or "local-deep")
            // Padding above the view can belong to Shallow. The full per-layer veto was checked
            // above; only the actual deep receivers must have zero sky, not the entire padded RT.
            Check(name + "/local-without-sky", Peak(At(sky, 160, 144)) == 0 && Peak(At(sky, 96, 48)) == 0 &&
                Peak(At(local, 160, 144)) >= 8 && Peak(At(direct, 96, 48)) > 100);
        if (name == "corner")
        {
            bool dark = true;
            for (int y = 128; y < 136; y++) for (int x = 160; x < 168; x++) dark &= Peak(At(local, x, y)) == 0;
            Check("no diagonal bridge into enclosed air cell", dark && Peak(At(local, 156, 124)) > 0);
        }
        if (name == "door-closed") { closedDoor = local; Check("closed door blocks tile transport", Peak(At(local, 224, 104)) == 0); }
        if (name == "door-open") Check("open door permits transport", Peak(At(closedDoor, 224, 104)) == 0 && Peak(At(local, 224, 104)) > 0);

        // Check addition BEFORE albedo on actual entity pixels, not a max or a second multiply.
        Matrix inverse = Matrix.Invert(view);
        int error = 0, count = 0, shadowReadable = 0;
        for (int y = 0; y < entityAlbedo.Height; y++) for (int x = 0; x < entityAlbedo.Width; x++)
        {
            int i = y * entityAlbedo.Width + x;
            if (ea[i].A < 200) continue;
            var world = Vector2.Transform(new Vector2(x + .5f, y + .5f), inverse);
            float dx = world.X - scene.EntityPosition.X;
            dx -= MathF.Floor(dx / scene.Map.PixelWidth) * scene.Map.PixelWidth;
            if (dx >= 32 || world.Y < scene.EntityPosition.Y || world.Y >= scene.EntityPosition.Y + 32) continue;
            Color d = Sample(direct, bounds, world), s = Sample(sky, bounds, world), l = Sample(local, bounds, world);
            var expected = new Color((byte)Math.Min(255, MathF.Round(ea[i].R * (d.R + s.R + l.R) / 255f)),
                (byte)Math.Min(255, MathF.Round(ea[i].G * (d.G + s.G + l.G) / 255f)),
                (byte)Math.Min(255, MathF.Round(ea[i].B * (d.B + s.B + l.B) / 255f)), ea[i].A);
            error = Math.Max(error, Difference(expected, el[i])); count++;
            if (Peak(d) == 0 && Peak(el[i]) > 0) shadowReadable++;
        }
        Check(name + "/additive-receiver", count > 0 && error <= 2, $"pixels={count} maxError={error}");
        if (name == "local") Check("entity readable inside direct shadow", shadowReadable > 0, $"pixels={shadowReadable}");
        File.WriteAllLines(Path.Combine(output, "ambient-checks.txt"), results);

        // Face light (blue) along a world-pixel line; At() is relative to scene.Origin (-64, 128).
        int[] Line(int worldX, int worldY, int dx, int dy, int count) =>
            Enumerable.Range(0, count).Select(k => (int)At(faces, worldX + dx * k + 64, worldY + dy * k - 128).B).ToArray();
        void FaceBand(string label, int[] line, int thickness)
        {
            int half = thickness / 2, depth = renderer.SkyFaceDepth;
            float first = V8FaceChunks.SkyBandWeight(1, depth);
            bool follows = line[0] > 40, dark = true;
            int error = 0;
            for (int k = 0; k < thickness; k++)
            {
                if (k >= half) { dark &= line[k] == 0; continue; }
                int expected = (int)MathF.Round(line[0] * V8FaceChunks.SkyBandWeight(k + 1, depth) / first);
                error = Math.Max(error, Math.Abs(line[k] - expected));
            }
            follows &= error <= 2;
            Check($"sky band fixture: {label} follows the band curve (SkyFaceDepth {depth})", follows, $"max error {error}");
            Check($"sky band fixture: {label} dark half stays 0 (no leak)", dark);
            results.Add($"INFO sky band fixture: {label}: step at the midline {line[half - 1]} -> {line[half]} (blue 0..255); line = {string.Join(",", line)}");
        }
    }
    private static Color Sample(Color[] pixels, Rectangle bounds, Vector2 world)
    {
        int x = (int)MathF.Floor(world.X), y = (int)MathF.Floor(world.Y);
        return bounds.Contains(x, y) ? pixels[(y - bounds.Y) * bounds.Width + x - bounds.X] : Color.Transparent;
    }
    private static int CompareOverlap(Color[] current, Rectangle bounds, Color[] reference, Rectangle original, int shift)
    {
        int error = 0, count = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y++) for (int x = bounds.Left; x < bounds.Right; x++)
        {
            if (!original.Contains(x - shift, y)) continue;
            error = Math.Max(error, Difference(current[(y - bounds.Top) * bounds.Width + x - bounds.Left], reference[(y - original.Top) * original.Width + x - shift - original.Left])); count++;
        }
        return count > 1000 ? error : 255;
    }
    private static int Peak(Color p) => Math.Max(p.R, Math.Max(p.G, p.B));
    private static int Difference(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    public void Finish()
    {
        results.Add($"TOTAL: {results.Count - failures} passed; {failures} failed. Visual inspection recorded separately.");
        File.WriteAllLines(Path.Combine(output, "ambient-checks.txt"), results);
        if (failures > 0) throw new InvalidOperationException($"{failures} ambient checks failed; see {output}/ambient-checks.txt");
    }
}
