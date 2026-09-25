using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Probe;

/// <summary>
/// First explicit representation of natural light for the probe; not a general sky system.
/// Sky floor s(c): first solid row of column c scanning down from the world top. Exterior envelope e = opening of s over
/// 17 columns (max of min): openings narrower than that keep their rim level, as the lab's samples sit above its
/// aperture (on the lab's geometry this yields exactly its y=28 above the y=48 roof), while slopes and wide valleys keep
/// their own height. One lab sample (1.4 / 196 px / linear (0.46, 0.69, 1)) every 12 px (the lab's spacing), 20 px above
/// e (the lab's height above its roof), on a canonical world grid so samples do not move when the view wraps.
/// No layer rule and no fill: whether a sample lights a cell is decided by the unchanged DDA.
/// </summary>
internal sealed class V9ProbeSky
{
    public const int Spacing = 12, Phase = 6, HeightAbove = 20, HalfWindow = 8;
    private readonly Dictionary<int, int> floors = new();
    private WorldMap map;
    private int revision = -1;
    public int ColumnsScanned { get; private set; }

    private int Floor(int column)
    {
        int canonical = map.WrapTileX(column);
        if (floors.TryGetValue(canonical, out int row)) return row;
        row = 0;
        while (row < map.Height && !V9ProbeOccupancy.WorldSolid(map, canonical, row)) row++;
        ColumnsScanned++;
        return floors[canonical] = row;
    }

    private void Sync(WorldMap map)
    {
        if (this.map != map || revision != map.TileRevision) { floors.Clear(); this.map = map; revision = map.TileRevision; }
    }

    /// <summary>Sky floor row (first solid row from the world top) for camera-frame columns [first, first + count).</summary>
    public int[] Floors(WorldMap map, int first, int count)
    {
        Sync(map);
        var result = new int[count];
        for (int i = 0; i < count; i++) result[i] = Floor(first + i);
        return result;
    }

    /// <summary>Exterior envelope row for camera-frame columns [first, first + count).</summary>
    public int[] Envelope(WorldMap map, int first, int count)
    {
        Sync(map);
        int w = HalfWindow;
        var s = new int[count + 4 * w];
        for (int i = 0; i < s.Length; i++) s[i] = Floor(first - 2 * w + i);
        var eroded = new int[count + 2 * w];
        for (int i = 0; i < eroded.Length; i++)
        {
            int m = int.MaxValue;
            for (int k = 0; k <= 2 * w; k++) m = Math.Min(m, s[i + k]);
            eroded[i] = m;
        }
        var e = new int[count];
        for (int i = 0; i < count; i++)
        {
            int m = int.MinValue;
            for (int k = 0; k <= 2 * w; k++) m = Math.Max(m, eroded[i + k]);
            e[i] = m;
        }
        return e;
    }

    /// <summary>Appends, in increasing x, the samples whose support square can touch [left,right)x[top,bottom).</summary>
    public void Collect(WorldMap map, int left, int right, int top, int bottom, List<V9Light> output)
    {
        float radius = V9LabSettings.SkyRadiusPixels;
        int reachLeft = left - (int)radius - 2, reachRight = right + (int)radius + 2;
        int firstColumn = FloorDiv(reachLeft, 8), lastColumn = FloorDiv(reachRight, 8);
        int[] envelope = Envelope(map, firstColumn, lastColumn - firstColumn + 1);
        int width = map.PixelWidth, lastK = (width - 1 - Phase) / Spacing;
        for (int image = FloorDiv(reachLeft, width); image <= FloorDiv(reachRight, width); image++)
            for (int k = 0; k <= lastK; k++)
            {
                int x = image * width + k * Spacing + Phase;
                if (x < reachLeft || x > reachRight) continue;
                float y = envelope[FloorDiv(x, 8) - firstColumn] * 8 - HeightAbove;
                if (y + radius + 2 < top || y - radius - 2 > bottom) continue;
                output.Add(new V9Light(new Vector2(x, y), V9LabSettings.SkyLinearRgb, V9LabSettings.SkySamplePower, radius));
            }
    }

    public static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}
