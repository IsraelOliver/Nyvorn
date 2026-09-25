using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Lab;

public sealed partial class V9LabGame
{
    // Border and fixture points in world pixels: exactly on, just inside and just outside the finite grid,
    // the opening (tiles 10..17, row 6 = exterior band boundary y=48), the pillar, and the lab's fixed sources.
    private static readonly Vector2[] OccupancyBorderPoints = BuildOccupancyBorderPoints();

    private static Vector2[] BuildOccupancyBorderPoints()
    {
        const float e = .001f;
        var points = new List<Vector2>();
        foreach (float x in new[] { -8, -e, 0, e, .5f, 240, 479.5f, 480 - e, 480, 480 + e, 488 })
            foreach (float y in new[] { -8, -e, 0, e, .5f, 136, 271.5f, 272 - e, 272, 272 + e, 280 })
                points.Add(new Vector2(x, y));
        foreach (float x in new[] { 80 - e, 80, 80 + e, 112, 144 - e, 144, 144 + e })
            foreach (float y in new[] { 48 - e, 48, 48 + e, 52, 56 - e, 56, 56 + e })
                points.Add(new Vector2(x, y));
        foreach (float x in new[] { 296 - e, 296, 296 + e, 304, 312 - e, 312, 312 + e })
            foreach (float y in new[] { 144 - e, 144, 144 + e, 184, 224 - e, 224, 224 + e })
                points.Add(new Vector2(x, y));
        for (int i = 0; i < 5; i++) points.Add(new Vector2(86 + i * 12, 28));
        points.AddRange(new[] { FirstTorch, new Vector2(416, 136), new Vector2(118, 103), new Vector2(342, 150) });
        return points.ToArray();
    }

    /// <summary>Cross-checks every Solid query at the moment it is issued and answers with the mask.</summary>
    private sealed class SolidProbe
    {
        private readonly V9LabScene scene;
        private readonly V9OccupancyMask mask;
        public long Queries, Mismatches, OutsideGrid;
        public int MinX = int.MaxValue, MaxX = int.MinValue, MinY = int.MaxValue, MaxY = int.MinValue;
        public SolidProbe(V9LabScene scene, V9OccupancyMask mask) { this.scene = scene; this.mask = mask; }
        public bool Query(int x, int y)
        {
            bool answer = mask.Solid(x, y);
            if (answer != scene.Solid(x, y)) Mismatches++;
            Queries++;
            MinX = Math.Min(MinX, x); MaxX = Math.Max(MaxX, x); MinY = Math.Min(MinY, y); MaxY = Math.Max(MaxY, y);
            if ((uint)x >= V9OccupancyMask.Columns || (uint)y >= V9OccupancyMask.Rows) OutsideGrid++;
            return answer;
        }
        public void Add(SolidProbe other)
        {
            Queries += other.Queries; Mismatches += other.Mismatches; OutsideGrid += other.OutsideGrid;
            MinX = Math.Min(MinX, other.MinX); MaxX = Math.Max(MaxX, other.MaxX);
            MinY = Math.Min(MinY, other.MinY); MaxY = Math.Max(MaxY, other.MaxY);
        }
        public object Domain => Queries == 0 ? null : new { MinX, MaxX, MinY, MaxY };
    }

    private sealed class OccupancyTotals
    {
        public long States, GridCells, GridMismatches, ExtremeMismatches, Rays, RayBitMismatches, EnergyBitsDifferent;
        public SolidProbe Border, Dda;
    }

    /// <summary>
    /// 1) every int cell in a 64-cell margin (beyond WorldMap's 60-column wrap period) plus extreme ints;
    /// 2) Visibility bits for every ordered pair of border points with either query;
    /// 3) every query the unchanged DDA issues for the current full field, whose Energy must equal the reference.
    /// </summary>
    private object CheckOccupancyMask(V9OccupancyMask mask, V9LightField reference, OccupancyTotals totals, out bool pass)
    {
        const int margin = 64;
        const int columns = V9OccupancyMask.Columns, rows = V9OccupancyMask.Rows;
        long gridCells = 0, gridMismatches = 0, outsideCells = 0;
        int borderCells = 0, interiorCells = 0, solidInMap = 0, openingSolid = 0, pillarSolid = 0, exteriorSolid = 0;
        for (int y = -margin; y < rows + margin; y++)
            for (int x = -margin; x < columns + margin; x++)
            {
                bool actual = mask.Solid(x, y);
                gridCells++;
                if (actual != scene.Solid(x, y)) gridMismatches++;
                if (x < 0 || y < 0 || x >= columns || y >= rows) { outsideCells++; continue; }
                if (x == 0 || y == 0 || x == columns - 1 || y == rows - 1) borderCells++; else interiorCells++;
                if (!actual) continue;
                solidInMap++;
                if (y == 6 && x >= 10 && x <= 17) openingSolid++;
                if (x >= 37 && x <= 38 && y >= 18 && y <= 27) pillarSolid++;
                if (y < 6) exteriorSolid++;
            }
        int[] extremes = { int.MinValue, int.MinValue + 1, -1_000_000, -121, -120, -61, -60, -1, 0, 1, 33, 34, 59, 60, 61, 119, 120, 1_000_000, int.MaxValue - 1, int.MaxValue };
        int extremeMismatches = 0;
        foreach (int x in extremes)
            foreach (int y in extremes)
                if (scene.Solid(x, y) != mask.Solid(x, y)) extremeMismatches++;

        var border = new SolidProbe(scene, mask);
        Func<int, int, bool> original = scene.Solid, probedBorder = border.Query;
        long rays = 0, rayBitMismatches = 0, nonZero = 0;
        foreach (Vector2 a in OccupancyBorderPoints)
            foreach (Vector2 b in OccupancyBorderPoints)
            {
                float expected = V9LightMath.Visibility(a, b, original), actual = V9LightMath.Visibility(a, b, probedBorder);
                rays++;
                if (BitConverter.SingleToInt32Bits(expected) != BitConverter.SingleToInt32Bits(actual)) rayBitMismatches++;
                if (actual > 0) nonZero++;
            }

        var dda = new SolidProbe(scene, mask);
        Func<int, int, bool> probedField = dda.Query;
        int energyBitsDifferent = 0;
        for (int y = 0, i = 0; y < V9LabScene.Height; y++)
            for (int x = 0; x < V9LabScene.Width; x++, i++)
            {
                Vector3 e = V9LightMath.Evaluate(new Vector2(x + .5f, y + .5f), lights, probedField), r = reference.Energy[i];
                if (BitConverter.SingleToInt32Bits(e.X) != BitConverter.SingleToInt32Bits(r.X)) energyBitsDifferent++;
                if (BitConverter.SingleToInt32Bits(e.Y) != BitConverter.SingleToInt32Bits(r.Y)) energyBitsDifferent++;
                if (BitConverter.SingleToInt32Bits(e.Z) != BitConverter.SingleToInt32Bits(r.Z)) energyBitsDifferent++;
            }

        pass = gridMismatches == 0 && extremeMismatches == 0 && rayBitMismatches == 0 && border.Mismatches == 0
            && dda.Mismatches == 0 && energyBitsDifferent == 0;
        totals.States++; totals.GridCells += gridCells; totals.GridMismatches += gridMismatches; totals.ExtremeMismatches += extremeMismatches;
        totals.Rays += rays; totals.RayBitMismatches += rayBitMismatches; totals.EnergyBitsDifferent += energyBitsDifferent;
        (totals.Border ??= new SolidProbe(scene, mask)).Add(border);
        (totals.Dda ??= new SolidProbe(scene, mask)).Add(dda);
        return new
        {
            Pass = pass,
            Grid = new { Cells = gridCells, Mismatches = gridMismatches, OutsideGridCells = outsideCells, MapBorderCells = borderCells,
                MapInteriorCells = interiorCells, SolidCellsInMap = solidInMap, OpeningSolidCells = openingSolid,
                PillarSolidCells = pillarSolid, ExteriorBandSolidCells = exteriorSolid,
                ExtremePairs = extremes.Length * extremes.Length, ExtremeMismatches = extremeMismatches },
            BorderRays = new { Points = OccupancyBorderPoints.Length, Rays = rays, VisibilityBitMismatches = rayBitMismatches,
                NonZeroVisibility = nonZero, border.Queries, QueryMismatches = border.Mismatches, OutsideGridQueries = border.OutsideGrid, border.Domain },
            FieldDda = new { dda.Queries, QueryMismatches = dda.Mismatches, OutsideGridQueries = dda.OutsideGrid, dda.Domain,
                EnergyBitsDifferentFromReference = energyBitsDifferent }
        };
    }

    /// <summary>Warm full-rebuild cost, measured outside any frame; the first build also pays JIT and is reported apart.</summary>
    private object MeasureOccupancyMaskRebuild()
    {
        var mask = new V9OccupancyMask();
        mask.Rebuild(scene);
        var samples = new double[2000];
        for (int i = 0; i < samples.Length; i++) { mask.Rebuild(scene); samples[i] = mask.LastBuildMilliseconds; }
        Array.Sort(samples);
        return new { Rebuilds = samples.Length, FirstBuildMs = mask.FirstBuildMilliseconds, Mean = samples.Average(),
            Median = samples[samples.Length / 2], P95 = samples[(int)((samples.Length - 1) * .95)], Min = samples[0], Max = samples[^1],
            Cells = V9OccupancyMask.Columns * V9OccupancyMask.Rows, mask.Bytes };
    }
}
