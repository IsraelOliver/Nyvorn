using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Probe;

/// <summary>
/// Sky exposure O per tile cell, over a tile rectangle in the camera frame: air (not collision-solid), no background
/// wall, and above the exterior envelope of its column (V9ProbeSky: sky floor with openings narrower than 17 columns
/// closed at their rim). O is where natural light enters the world; everything else only receives it.
/// Data facts behind this rule: Space/Surface layers never have background walls (above or below the ground), so
/// "no wall" there says nothing; Shallow has a full Dirt wall with carved fissures whose backdrop is not described by
/// the world; Cavern/Deep have a full Stone wall. Hence exposure is decided by the open sky above, and a wall on an
/// exposed cell removes it from O; underground background holes are receivers, not sky (missing world information).
/// </summary>
internal sealed class V9ProbeExposure
{
    private bool[] cells = Array.Empty<bool>(), previous = Array.Empty<bool>();
    private Rectangle previousTiles;
    private WorldMap map;
    private int revision = -1;
    private int[] envelope = Array.Empty<int>();
    public Rectangle Tiles { get; private set; }
    public int ExposedCells { get; private set; }
    public double LastBuildMilliseconds { get; private set; }

    /// <summary>Rebuilds when the rectangle or the tile revision changes; on a revision change, every cell whose
    /// exposure differs inside the overlap with the previous rectangle is appended to <paramref name="changed"/>.</summary>
    public bool Update(WorldMap map, Rectangle tiles, V9ProbeSky sky, Func<int, int, bool> solid, List<Point> changed)
    {
        bool geometryChanged = this.map != map || revision != map.TileRevision;
        if (!geometryChanged && tiles == Tiles) return false;
        long start = Stopwatch.GetTimestamp();
        (previous, cells) = (cells, previous);
        previousTiles = Tiles;
        if (cells.Length < tiles.Width * tiles.Height) cells = new bool[tiles.Width * tiles.Height];
        envelope = sky.Envelope(map, tiles.X, tiles.Width);
        int exposedCells = 0;
        for (int y = 0, i = 0; y < tiles.Height; y++)
            for (int x = 0; x < tiles.Width; x++, i++)
                if (cells[i] = Compute(map, solid, tiles.X + x, tiles.Y + y, envelope[x])) exposedCells++;
        if (geometryChanged && this.map == map)
        {
            Rectangle overlap = Rectangle.Intersect(previousTiles, tiles);
            for (int y = overlap.Top; y < overlap.Bottom; y++)
                for (int x = overlap.Left; x < overlap.Right; x++)
                    if (cells[(y - tiles.Y) * tiles.Width + x - tiles.X] != previous[(y - previousTiles.Y) * previousTiles.Width + x - previousTiles.X])
                        changed.Add(new Point(x, y));
        }
        this.map = map;
        revision = map.TileRevision;
        Tiles = tiles;
        ExposedCells = exposedCells;
        LastBuildMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return true;
    }

    public static bool Compute(WorldMap map, Func<int, int, bool> solid, int x, int y, int envelopeRow) =>
        y < envelopeRow && !solid(x, y) && map.GetBackgroundTile(x, y) == TileType.Empty;

    public void ShiftX(int tiles) => Tiles = new Rectangle(Tiles.X + tiles, Tiles.Y, Tiles.Width, Tiles.Height);

    public bool Exposed(int x, int y)
    {
        int lx = x - Tiles.X, ly = y - Tiles.Y;
        if ((uint)lx < (uint)Tiles.Width && (uint)ly < (uint)Tiles.Height) return cells[ly * Tiles.Width + lx];
        throw new InvalidOperationException($"V9 exposure queried outside its rectangle ({x},{y}); the reach margin is wrong.");
    }

    public int EnvelopeRow(int x) => envelope[x - Tiles.X];
}
