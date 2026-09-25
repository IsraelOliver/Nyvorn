using System;
using System.Diagnostics;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Lab;

/// <summary>
/// Diagnostic flat copy of <see cref="V9LabScene.Solid"/> over the lab's finite 60x34 tile grid.
/// Cells outside the grid answer solid by the same explicit bounds rule, so WorldMap's horizontal wrap
/// and its out-of-range Empty answer stay unreachable, exactly as through V9LabScene.Solid.
/// Rebuilt in full, and only when the scene instance or its tile revision changes.
/// </summary>
internal sealed class V9OccupancyMask
{
    public const int Columns = V9LabScene.Width / V9LabScene.TileSize;
    public const int Rows = V9LabScene.Height / V9LabScene.TileSize;
    private readonly bool[] cells = new bool[Columns * Rows];
    private V9LabScene lastScene;
    private int revision = -1;
    public int Builds { get; private set; }
    public double FirstBuildMilliseconds { get; private set; }
    public double LastBuildMilliseconds { get; private set; }
    public long Bytes => cells.Length * sizeof(bool);

    /// <summary>Rebuilds only for a new scene or tile revision; returns whether it rebuilt.</summary>
    public bool Update(V9LabScene scene)
    {
        if (ReferenceEquals(lastScene, scene) && revision == scene.Map.TileRevision) return false;
        Rebuild(scene);
        return true;
    }

    public void Rebuild(V9LabScene scene)
    {
        long start = Stopwatch.GetTimestamp();
        if (scene.Map.Width != Columns || scene.Map.Height != Rows)
            throw new InvalidOperationException("V9 occupancy mask expects the lab's finite 60x34 tile grid.");
        // Copies the answers of the unchanged query itself: no second interpretation of tile types.
        for (int y = 0, i = 0; y < Rows; y++)
            for (int x = 0; x < Columns; x++, i++)
                cells[i] = scene.Solid(x, y);
        lastScene = scene;
        revision = scene.Map.TileRevision;
        LastBuildMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (++Builds == 1) FirstBuildMilliseconds = LastBuildMilliseconds;
    }

    // The unsigned compare folds "x < 0 || x >= Columns" into one test with the same answer for every int.
    public bool Solid(int tileX, int tileY) => (uint)tileX >= Columns || (uint)tileY >= Rows || cells[tileY * Columns + tileX];
}
