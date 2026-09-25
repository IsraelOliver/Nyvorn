using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Lab;

/// <summary>Small, independent scene witnesses for the light transport and colour contracts; no GPU or saves.</summary>
public static class V9LabChecks
{
    public static string Run()
    {
        var report = new StringBuilder("V9-Lab CPU scene checks\n");
        int passed = 0, failed = 0;
        void Check(string name, bool condition, string detail = "")
        {
            report.Append(condition ? "PASS " : "FAIL ").Append(name);
            if (detail.Length != 0) report.Append(" | ").Append(detail);
            report.AppendLine();
            if (condition) passed++; else failed++;
        }

        static bool Air(int x, int y) => false;
        var room = new HashSet<Point>();
        for (int n = 3; n <= 11; n++)
        {
            room.Add(new Point(3, n)); room.Add(new Point(11, n));
            room.Add(new Point(n, 3)); room.Add(new Point(n, 11));
        }
        bool Room(int x, int y) => room.Contains(new Point(x, y));
        var centre = new Vector2(60, 60);
        var outside = new V9Light(new Vector2(108, 60), new Vector3(.32f, .58f, 1f), 3f, 160f);
        var exteriorSources = new[] { outside };
        Check("unlit sealed room is exactly dark", IsZero(V9LightMath.Evaluate(centre, Array.Empty<V9Light>(), Room)));
        Check("unlit open air has no global light floor", IsZero(V9LightMath.Evaluate(centre, Array.Empty<V9Light>(), Air)));
        Check("exterior source cannot cross sealed room wall", IsZero(V9LightMath.Evaluate(centre, exteriorSources, Room)));
        room.Remove(new Point(11, 7));
        Vector3 admitted = V9LightMath.Evaluate(centre, exteriorSources, Room);
        Check("one tile aperture admits the exterior source", Peak(admitted) > .01f, Rgb(admitted));
        room.Add(new Point(11, 7));
        Check("reclosing aperture restores darkness immediately", IsZero(V9LightMath.Evaluate(centre, exteriorSources, Room)));

        var wall = new HashSet<Point>();
        for (int y = 0; y < 12; y++) wall.Add(new Point(5, y));
        bool Wall(int x, int y) => wall.Contains(new Point(x, y));
        var receiver = new Vector2(68, 36);
        var first = new V9Light(new Vector2(20, 36), new Vector3(1f, .42f, .13f), 2f, 128f);
        var firstOnly = new[] { first };
        Check("torch behind opaque blocker leaves air dark", IsZero(V9LightMath.Evaluate(receiver, firstOnly, Wall)));
        var moved = new V9Light(new Vector2(60, 36), first.LinearRgb, first.Power, first.Radius);
        Check("moving torch to receiver side reveals receiver", Peak(V9LightMath.Evaluate(receiver, new[] { moved }, Wall)) > .01f);
        Check("moving torch back leaves no residual illumination", IsZero(V9LightMath.Evaluate(receiver, firstOnly, Wall)));
        wall.Remove(new Point(5, 4));
        Check("removing blocker opens torch path immediately", Peak(V9LightMath.Evaluate(receiver, firstOnly, Wall)) > .01f);
        wall.Add(new Point(5, 4));
        Check("replacing blocker leaves no residual illumination", IsZero(V9LightMath.Evaluate(receiver, firstOnly, Wall)));

        var second = new V9Light(new Vector2(100, 36), new Vector3(.25f, .55f, 1f), 2f, 128f);
        Vector3 secondAlone = V9LightMath.Evaluate(receiver, new[] { second }, Wall);
        Vector3 both = V9LightMath.Evaluate(receiver, new[] { first, second }, Wall);
        Check("second visible source reveals the first source's shadow", Peak(both) > .01f && Near(both, secondAlone), Rgb(both));
        for (int y = 0; y < 12; y++) wall.Add(new Point(10, y));
        Check("two blocked sources cannot reveal their common shadow", IsZero(V9LightMath.Evaluate(receiver, new[] { first, second }, Wall)));

        var near = new V9Light(new Vector2(44, 36), new Vector3(1f, .6f, .2f), 2f, 64f);
        var remote = new V9Light(new Vector2(1000, 1000), new Vector3(.1f, .4f, 1f), 50f, 16f);
        Vector3 localOnly = V9LightMath.Evaluate(receiver, new[] { near }, Air);
        Check("adding out of radius source leaves local illumination unchanged",
            Near(localOnly, V9LightMath.Evaluate(receiver, new[] { near, remote }, Air)), Rgb(localOnly));
        Check("finite radius has no light at or beyond its boundary",
            IsZero(V9LightMath.Evaluate(new Vector2(108, 36), new[] { near }, Air)) &&
            IsZero(V9LightMath.Evaluate(new Vector2(112, 36), new[] { near }, Air)));

        var sources = new[]
        {
            near, second,
            new V9Light(new Vector2(48, 20), new Vector3(.2f, 1f, .3f), .6f, 96f),
            new V9Light(new Vector2(60, 64), new Vector3(.8f, .3f, .7f), 1.1f, 96f),
            new V9Light(new Vector2(84, 52), new Vector3(.4f, .7f, .9f), .9f, 96f)
        };
        Vector3 forward = V9LightMath.Evaluate(receiver, sources, Air);
        Array.Reverse(sources);
        Vector3 reverse = V9LightMath.Evaluate(receiver, sources, Air);
        Check("source order does not affect radiance", Near(forward, reverse), "max delta=" + F(Peak(Abs(forward - reverse))));
        Vector3 one = V9LightMath.Evaluate(receiver, new[] { near }, Air);
        Vector3 two = V9LightMath.Evaluate(receiver, new[] { near, near }, Air);
        Vector3 five = V9LightMath.Evaluate(receiver, new[] { near, near, near, near, near }, Air);
        Check("one, two and five colocated sources retain their individual energy",
            Peak(one) > .01f && Near(two, one * 2) && Near(five, one * 5),
            "peak 1/2/5=" + F(Peak(one)) + "/" + F(Peak(two)) + "/" + F(Peak(five)));

        // Witnesses distinguish shallow receiver penetration from transport through a whole wall.
        static bool Slab(int x, int y) => x == 5 && y >= 0 && y <= 11;
        float face = V9LightMath.Visibility(new Vector2(20, 36), new Vector2(42, 36), Slab);
        float inset = V9LightMath.Visibility(new Vector2(20, 36), new Vector2(45, 36), Slab);
        Check("exposed solid surface receives light and attenuates with depth", face > inset && inset > 0 && face < 1,
            "2px=" + F(face) + "; 5px=" + F(inset));
        Check("deep solid receiver stays dark", V9LightMath.Visibility(new Vector2(20, 36), new Vector2(47, 36), Slab) == 0);
        Check("surface penetration never exits into air on the far side", V9LightMath.Visibility(new Vector2(20, 36), new Vector2(52, 36), Slab) == 0);
        static bool Corner(int x, int y) => x == 1 && y == 0;
        Check("exact tile corner contact cannot leak light diagonally",
            V9LightMath.Visibility(new Vector2(4, 4), new Vector2(20, 20), Corner) == 0 &&
            V9LightMath.Visibility(new Vector2(4, 4), new Vector2(20, 20), Air) > 0);

        var colour = new Vector3(1f, .42f, .13f);
        Vector3 previous = Vector3.Zero;
        bool bounded = true, monotonic = true, huePreserved = true;
        foreach (float exposure in new[] { 0f, .05f, .5f, 2f, 8f, 32f, 1000f, 1e20f })
        {
            Vector3 mapped = V9LightMath.ToneMap(colour * exposure);
            bounded &= Finite(mapped) && Min(mapped) >= 0 && Peak(mapped) <= 1;
            monotonic &= mapped.X >= previous.X && mapped.Y >= previous.Y && mapped.Z >= previous.Z;
            if (exposure > 0) huePreserved &= MathF.Abs(mapped.Y / mapped.X - colour.Y) < .00002f &&
                MathF.Abs(mapped.Z / mapped.X - colour.Z) < .00002f;
            previous = mapped;
        }
        Check("tone response stays finite and bounded over wide exposures", bounded);
        Check("tone response is monotonic for all colour channels", monotonic);
        Check("tone response preserves warm linear RGB ratios", huePreserved);
        Check("tone response preserves zero without a hidden light floor", IsZero(V9LightMath.ToneMap(Vector3.Zero)));
        Vector3 toneOne = V9LightMath.ToneMap(one), toneTwo = V9LightMath.ToneMap(two), toneFive = V9LightMath.ToneMap(five);
        Check("overlapping warm sources increase brightness without whitening",
            Peak(toneOne) < Peak(toneTwo) && Peak(toneTwo) < Peak(toneFive) &&
            MathF.Abs(toneOne.Y / toneOne.X - toneFive.Y / toneFive.X) < .00002f &&
            MathF.Abs(toneOne.Z / toneOne.X - toneFive.Z / toneFive.X) < .00002f,
            "mapped peak 1/2/5=" + F(Peak(toneOne)) + "/" + F(Peak(toneTwo)) + "/" + F(Peak(toneFive)));

        // Match the receiver contract: albedo * illumination, then tone response, then display encoding.
        Vector3 illumination = colour * 8;
        Vector3 darkPatch = V9LightMath.ToneMap(illumination * .025f);
        Vector3 midPatch = V9LightMath.ToneMap(illumination * .18f);
        Vector3 brightPatch = V9LightMath.ToneMap(illumination * .6f);
        float darkLinear = Luma(darkPatch), midLinear = Luma(midPatch), brightLinear = Luma(brightPatch);
        float darkDisplay = Luma(V9LightMath.EncodeSrgb(darkPatch));
        float midDisplay = Luma(V9LightMath.EncodeSrgb(midPatch));
        float brightDisplay = Luma(V9LightMath.EncodeSrgb(brightPatch));
        float linearContrast = (brightLinear - darkLinear) / (brightLinear + darkLinear);
        float displayContrast = (brightDisplay - darkDisplay) / (brightDisplay + darkDisplay);
        Check("material patches retain ordered texture contrast under strong warm illumination",
            darkLinear < midLinear && midLinear < brightLinear && darkDisplay < midDisplay && midDisplay < brightDisplay &&
            linearContrast > .5f && displayContrast > .25f && brightDisplay < 1,
            "Michelson linear=" + F(linearContrast) + "; display=" + F(displayContrast) +
            "; displayed dark/mid/bright=" + F(darkDisplay) + "/" + F(midDisplay) + "/" + F(brightDisplay));

        float roundTripError = 0;
        foreach (float value in new[] { 0f, .001f, .02f, .04045f, .18f, .5f, .9f, 1f })
        {
            var srgb = new Vector3(value, value * .5f, value * .1f);
            Vector3 recovered = V9LightMath.EncodeSrgb(V9LightMath.DecodeSrgb(srgb));
            roundTripError = MathF.Max(roundTripError, Peak(Abs(recovered - srgb)));
        }
        Check("sRGB colour patches survive decode and encode", roundTripError < .00001f, "max error=" + F(roundTripError));
        Vector3 middleGrey = V9LightMath.DecodeSrgb(new Vector3(.5f));
        Check("sRGB mid grey is decoded to linear light", MathF.Abs(middleGrey.X - .21404114f) < .00001f,
            "linear 0.5 sRGB=" + F(middleGrey.X));

        report.Append("TOTAL: ").Append(passed).Append(" passed; ").Append(failed).AppendLine(" failed. GPU and visual checks are separate.");
        string result = report.ToString();
        if (failed != 0) throw new InvalidOperationException(result);
        return result;
    }

    private static bool IsZero(Vector3 v) => v.X == 0 && v.Y == 0 && v.Z == 0;
    private static float Peak(Vector3 v) => MathF.Max(v.X, MathF.Max(v.Y, v.Z));
    private static float Min(Vector3 v) => MathF.Min(v.X, MathF.Min(v.Y, v.Z));
    private static Vector3 Abs(Vector3 v) => new(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));
    private static bool Near(Vector3 a, Vector3 b) => Peak(Abs(a - b)) <= .000002f * MathF.Max(1f, MathF.Max(Peak(Abs(a)), Peak(Abs(b))));
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static float Luma(Vector3 v) => .2126f * v.X + .7152f * v.Y + .0722f * v.Z;
    private static string F(float value) => value.ToString("G6", CultureInfo.InvariantCulture);
    private static string Rgb(Vector3 value) => "linear RGB=" + F(value.X) + "," + F(value.Y) + "," + F(value.Z);
}
