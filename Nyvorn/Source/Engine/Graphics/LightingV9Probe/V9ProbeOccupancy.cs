using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Probe;

/// <summary>
/// Local flat copy of the gameplay's full-tile solidity (collision type Solid: dirt, grass, stone, sand, wood, iron ore)
/// over a tile rectangle in the camera frame. x wraps like the world; y outside the map is empty, like WorldMap.GetTile.
/// One-way platforms, doors, trees, dynamic sand and liquids never occlude in this first probe (documented limitation).
/// Outside the rectangle a query falls back to the world itself, exactly, and is counted: it should never happen.
/// </summary>
internal sealed class V9ProbeOccupancy
{
    private bool[] cells = Array.Empty<bool>(), previous = Array.Empty<bool>();
    private Rectangle previousTiles;
    private WorldMap map;
    private int revision = -1;
    public Rectangle Tiles { get; private set; }
    public long FallbackQueries { get; private set; }
    public int Builds { get; private set; }
    public double LastBuildMilliseconds { get; private set; }

    public static bool Blocks(TileType tile) => TileCollisionTypeExtensions.GetCollisionType(tile) == TileCollisionType.Solid;
    public static bool WorldSolid(WorldMap map, int x, int y) => Blocks(map.GetTile(x, y));

    /// <summary>Rebuilds when the rectangle or the tile revision changes. On a revision change, every cell whose
    /// solidity differs inside the overlap with the previous rectangle is appended to <paramref name="changed"/>.</summary>
    public bool Update(WorldMap map, Rectangle tiles, List<Point> changed)
    {
        bool geometryChanged = this.map != map || revision != map.TileRevision;
        if (!geometryChanged && tiles == Tiles) return false;
        long start = Stopwatch.GetTimestamp();
        (previous, cells) = (cells, previous);
        previousTiles = Tiles;
        if (cells.Length < tiles.Width * tiles.Height) cells = new bool[tiles.Width * tiles.Height];
        for (int y = 0, i = 0; y < tiles.Height; y++)
            for (int x = 0; x < tiles.Width; x++, i++)
                cells[i] = WorldSolid(map, tiles.X + x, tiles.Y + y);
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
        Builds++;
        LastBuildMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return true;
    }

    /// <summary>World wrap moved the camera frame by whole worlds: same cells, relabelled.</summary>
    public void ShiftX(int tiles) => Tiles = new Rectangle(Tiles.X + tiles, Tiles.Y, Tiles.Width, Tiles.Height);

    public bool Solid(int x, int y)
    {
        int lx = x - Tiles.X, ly = y - Tiles.Y;
        if ((uint)lx < (uint)Tiles.Width && (uint)ly < (uint)Tiles.Height) return cells[ly * Tiles.Width + lx];
        FallbackQueries++;
        return WorldSolid(map, x, y);
    }

    /// <summary>Diagnostic: every cell of the rectangle against the world query.</summary>
    public int CountMismatches()
    {
        int mismatches = 0;
        for (int y = 0; y < Tiles.Height; y++)
            for (int x = 0; x < Tiles.Width; x++)
                if (cells[y * Tiles.Width + x] != WorldSolid(map, Tiles.X + x, Tiles.Y + y)) mismatches++;
        return mismatches;
    }
}
