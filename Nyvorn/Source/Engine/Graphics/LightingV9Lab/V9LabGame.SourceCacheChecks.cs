using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Lab;

public sealed partial class V9LabGame
{
    // Explicit, readback-heavy diagnostic. Never runs in the benchmark or ordinary interactive path.
    private void RunSourceCacheVerification()
    {
        var rows = new List<object>();
        var original = field;
        using var reference = new V9LightField(GraphicsDevice);
        using var cached = new V9LightField(GraphicsDevice, true);
        // Cache x cache+mask under the same exactness criteria; skipped with --v9-no-occupancy-mask (two-way only).
        using var masked = occupancyMaskEnabled ? new V9LightField(GraphicsDevice, true, true) : null;
        string referenceOutput = Path.Combine(output, "reference"), cacheOutput = Path.Combine(output, "cache"), maskOutput = Path.Combine(output, "mask");
        Directory.CreateDirectory(referenceOutput); Directory.CreateDirectory(cacheOutput);
        if (masked != null) Directory.CreateDirectory(maskOutput);
        var occupancyTotals = new OccupancyTotals();
        int lastMaskRevision = -1;
        try
        {
            foreach (string name in Cases)
                Compare(name, () => ConfigureCase(name), -1, true);

            Compare("sequence-01-base", () => ResetScene(), 6);
            // Explicit mask expectations: source moves, camera, zoom and player never rebuild it; geometry does.
            Compare("sequence-02-move-first", () => MoveFirst(new(248, 166)), 1, expectedMaskRebuild: 0);
            Compare("sequence-03-move-first-again", () => MoveFirst(new(254, 169)), 1, expectedMaskRebuild: 0);
            Compare("sequence-04-add-source", () => SetTorchCount(2), 7);
            Compare("sequence-05-move-first", () => MoveFirst(new(245, 171)), 1, expectedMaskRebuild: 0);
            Compare("sequence-06-close", () => { scene.SetOpening(false); dirty = true; }, 7, expectedMaskRebuild: 1);
            Compare("sequence-07-open", () => { scene.SetOpening(true); dirty = true; }, 7, expectedMaskRebuild: 1);
            Compare("sequence-08-remove-pillar", () => { scene.SetBlocker(false); dirty = true; }, 7, expectedMaskRebuild: 1);
            Compare("sequence-09-restore-pillar", () => { scene.SetBlocker(true); dirty = true; }, 7, expectedMaskRebuild: 1);
            Compare("sequence-10-camera", () => camera += new Vector2(17.2f, -8.4f), 0, expectedMaskRebuild: 0);
            Compare("sequence-11-zoom", () => zoom = 3, 0, expectedMaskRebuild: 0);
            Compare("sequence-12-player", () => scene.PlayerFeet += new Vector2(12, 0), 0, expectedMaskRebuild: 0);
            Compare("sequence-13-reverse", () => { torches.Reverse(); dirty = true; }, 2);
            Compare("sequence-14-remove-source", () => { torches.RemoveAt(0); dirty = true; }, 6);
            Compare("sequence-15-five", () => SetTorchCount(5), 10);
            Compare("sequence-16-move-of-five", () => MoveFirst(new(260, 163)), 1, expectedMaskRebuild: 0);
            Compare("sequence-17-remove-all-torches", () => SetTorchCount(0), 5);
            Compare("sequence-18-create-from-zero", () => { torches.Add(FirstTorch); dirty = true; }, 6);
            Compare("sequence-19-reset-base", () => ResetScene(), 6);
            Compare("sequence-20-identical-reset", () => ResetScene(), 6);
            // Adversarial storage cases: duplicate source slots and an entirely empty source list.
            Compare("sequence-21-duplicate", () => { torches.Add(FirstTorch); dirty = true; }, 7);
            Compare("sequence-22-reverse-identical", () => { torches.Reverse(); dirty = true; }, 0);
            Compare("sequence-23-zero-sources", () => { torches.Clear(); sky = false; dirty = true; }, 0);
            Compare("sequence-24-back-to-base", () => ResetScene(), 6);
            File.WriteAllText(Path.Combine(output, "equivalence-summary.txt"),
                $"PASS {rows.Count} states: Energy float32 bits, HalfVector4 upload bits and runtime RGBA pixels identical.\n" +
                "All expected invalidation counts passed. No tolerance used.\n" +
                $"Vector3 payload/source={V9LabScene.Width * V9LabScene.Height * Marshal.SizeOf<Vector3>()} bytes; GPU cache resources=0.\n");
            if (masked != null)
            {
                var t = occupancyTotals;
                File.WriteAllText(Path.Combine(output, "occupancy-mask.json"), JsonSerializer.Serialize(new
                {
                    States = t.States, t.GridCells, t.GridMismatches, t.ExtremeMismatches, BorderRays = t.Rays, BorderRayBitMismatches = t.RayBitMismatches,
                    BorderQueries = t.Border.Queries, BorderQueryMismatches = t.Border.Mismatches, BorderOutsideGridQueries = t.Border.OutsideGrid, BorderDomain = t.Border.Domain,
                    DdaQueries = t.Dda.Queries, DdaQueryMismatches = t.Dda.Mismatches, DdaOutsideGridQueries = t.Dda.OutsideGrid, DdaDomain = t.Dda.Domain,
                    ProbeEnergyBitsDifferentFromReference = t.EnergyBitsDifferent, MaskBuildsInVerification = masked.OccupancyMask.Builds,
                    WarmRebuildCost = MeasureOccupancyMaskRebuild(),
                    Domain = "V9LabScene.Solid: any cell outside [0,60)x[0,34) is solid before WorldMap is read, so its x wrap and out-of-range Empty never apply; inside, solid = tile != Empty."
                }, new JsonSerializerOptions { WriteIndented = true }));
                File.AppendAllText(Path.Combine(output, "equivalence-summary.txt"),
                    $"PASS cache x cache+occupancy-mask in all {rows.Count} states: Energy float32 bits, HalfVector4 bits and runtime RGBA pixels identical; expected mask rebuilds passed.\n" +
                    $"scene.Solid == mask for {t.GridCells} grid cells (+{t.States} x 400 extreme pairs), {t.Rays} border rays (Visibility bits) and {t.Dda.Queries} DDA queries; mismatches 0.\n" +
                    $"Occupancy mask bytes={masked.OccupancyMaskBytes}; GPU mask resources=0.\n");
            }
        }
        finally { field = original; }

        void MoveFirst(Vector2 position) { torches[0] = position; dirty = true; }
        void Compare(string name, Action change, int expectedRecalculated, bool fullCapture = false, int expectedMaskRebuild = -1)
        {
            field = cached;
            int invalidations = cached.SourceCacheInvalidations;
            change();
            // ResetScene invalidates only the active field; replay the same event on the masked cache.
            if (masked != null && cached.SourceCacheInvalidations != invalidations) masked.InvalidateSourceCache();
            RebuildLights();
            reference.Rebuild(scene, lights);
            bool rebuilt = dirty;
            if (rebuilt) { cached.Rebuild(scene, lights); masked?.Rebuild(scene, lights); }
            dirty = false;
            int recalculated = rebuilt ? cached.LastRecalculatedSources : 0;
            if (expectedRecalculated < 0) expectedRecalculated = lights.Count;

            int scalarBitsDifferent = 0, halfTexelsDifferent = 0;
            double maxAbsolute = 0, maxRelative = 0;
            for (int i = 0; i < reference.Energy.Length; i++)
            {
                Vector3 a = reference.Energy[i], b = cached.Energy[i];
                Scalar(a.X, b.X); Scalar(a.Y, b.Y); Scalar(a.Z, b.Z);
                if (reference.UploadValues[i].PackedValue != cached.UploadValues[i].PackedValue) halfTexelsDifferent++;
            }
            Color[] referencePixels = RenderAndCapture(reference, referenceOutput);
            Color[] cachePixels = RenderAndCapture(cached, cacheOutput);
            Color[] maskPixels = masked != null ? RenderAndCapture(masked, maskOutput) : null;
            int pixelsDifferent = 0, maxChannelError = 0;
            for (int i = 0; i < referencePixels.Length; i++)
            {
                Color a = referencePixels[i], b = cachePixels[i];
                if (a.PackedValue != b.PackedValue) pixelsDifferent++;
                maxChannelError = Math.Max(maxChannelError, Math.Max(Math.Abs(a.R - b.R),
                    Math.Max(Math.Abs(a.G - b.G), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)))));
            }
            object maskRow = null;
            bool maskPass = true;
            if (masked != null)
            {
                int maskScalarBitsDifferent = 0, maskHalfTexelsDifferent = 0, maskPixelsDifferent = 0;
                for (int i = 0; i < cached.Energy.Length; i++)
                {
                    Vector3 a = cached.Energy[i], b = masked.Energy[i];
                    if (BitConverter.SingleToInt32Bits(a.X) != BitConverter.SingleToInt32Bits(b.X)) maskScalarBitsDifferent++;
                    if (BitConverter.SingleToInt32Bits(a.Y) != BitConverter.SingleToInt32Bits(b.Y)) maskScalarBitsDifferent++;
                    if (BitConverter.SingleToInt32Bits(a.Z) != BitConverter.SingleToInt32Bits(b.Z)) maskScalarBitsDifferent++;
                    if (cached.UploadValues[i].PackedValue != masked.UploadValues[i].PackedValue) maskHalfTexelsDifferent++;
                }
                for (int i = 0; i < cachePixels.Length; i++)
                    if (cachePixels[i].PackedValue != maskPixels[i].PackedValue) maskPixelsDifferent++;
                int maskRecalculated = rebuilt ? masked.LastRecalculatedSources : 0;
                bool maskRebuilt = rebuilt && masked.LastMaskRebuilt;
                // Independent expectation: rebuild exactly when a tile changed since the last mask build.
                bool revisionMoved = rebuilt && scene.Map.TileRevision != lastMaskRevision;
                if (rebuilt) lastMaskRevision = scene.Map.TileRevision;
                bool explicitHolds = expectedMaskRebuild < 0 || (expectedMaskRebuild == 1) == revisionMoved;
                object occupancy = CheckOccupancyMask(masked.OccupancyMask, reference, occupancyTotals, out bool occupancyPass);
                maskPass = maskScalarBitsDifferent == 0 && maskHalfTexelsDifferent == 0 && maskPixelsDifferent == 0
                    && maskRecalculated == expectedRecalculated && maskRebuilt == revisionMoved && explicitHolds && occupancyPass;
                maskRow = new { Pass = maskPass, MaskRebuilt = maskRebuilt, ExpectedMaskRebuilt = revisionMoved, ExplicitExpectation = expectedMaskRebuild,
                    MaskUpdateMs = rebuilt ? masked.LastMaskUpdateMilliseconds : 0, MaskBuildMs = maskRebuilt ? masked.OccupancyMask.LastBuildMilliseconds : 0,
                    RecalculatedSources = maskRecalculated, EnergyScalarBitsDifferentFromCache = maskScalarBitsDifferent,
                    HalfTexelsDifferentFromCache = maskHalfTexelsDifferent, FinalPixelsDifferentFromCache = maskPixelsDifferent,
                    MaskEnergySha256 = Hash(masked),
                    // Single samples interleaved with readback: indicative only, not a benchmark.
                    CacheFieldBuildMs = rebuilt ? cached.LastBuildMilliseconds : 0, MaskFieldBuildMs = rebuilt ? masked.LastBuildMilliseconds : 0,
                    Occupancy = occupancy };
            }
            bool pass = scalarBitsDifferent == 0 && halfTexelsDifferent == 0 && pixelsDifferent == 0 && recalculated == expectedRecalculated && maskPass;
            rows.Add(new { Name = name, Pass = pass, SourceCount = lights.Count, CachedFieldRebuilt = rebuilt,
                RecalculatedSources = recalculated, ExpectedRecalculatedSources = expectedRecalculated,
                GeometryRevision = scene.Map.TileRevision, CachePayloadBytes = cached.SourceCachePayloadBytes,
                EnergyScalarBitsDifferent = scalarBitsDifferent, EnergyMaxAbsoluteError = maxAbsolute, EnergyMaxRelativeError = maxRelative,
                ReferenceEnergySha256 = Hash(reference), CacheEnergySha256 = Hash(cached),
                HalfTexelsDifferent = halfTexelsDifferent, FinalPixelsDifferent = pixelsDifferent, FinalMaxChannelError = maxChannelError,
                Mask = maskRow });
            File.WriteAllText(Path.Combine(output, "equivalence.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            if (!pass) throw new InvalidOperationException($"Source-cache equivalence failed: {name}; float bits={scalarBitsDifferent}, half={halfTexelsDifferent}, pixels={pixelsDifferent}, recalculated={recalculated}/{expectedRecalculated}, mask pass={maskPass}. No tolerance accepted.");

            void Scalar(float a, float b)
            {
                if (BitConverter.SingleToInt32Bits(a) != BitConverter.SingleToInt32Bits(b)) scalarBitsDifferent++;
                double error = Math.Abs((double)a - b);
                maxAbsolute = Math.Max(maxAbsolute, error);
                maxRelative = Math.Max(maxRelative, error / Math.Max(Math.Abs((double)a), 1e-30));
            }
            Color[] RenderAndCapture(V9LightField selected, string folder)
            {
                field = selected;
                Render(final, true, false);
                GraphicsDevice.SetRenderTarget(null);
                var pixels = new Color[final.Width * final.Height]; final.GetData(pixels);
                if (fullCapture)
                {
                    Render(albedo, false, false); Render(entityLit, true, true); Render(entityAlbedo, false, true);
                    GraphicsDevice.SetRenderTarget(null);
                    V9LabEvidence.Capture(folder, name, final, albedo, entityLit, entityAlbedo, field, View, scene, lights, zoom, camera);
                }
                else
                {
                    using var stream = File.Create(Path.Combine(folder, name + "_final.png"));
                    final.SaveAsPng(stream, final.Width, final.Height);
                }
                return pixels;
            }
        }
        static string Hash(V9LightField f) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(f.Energy.AsSpan())));
    }
}
