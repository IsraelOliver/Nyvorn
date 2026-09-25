using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Lab;

/// <summary>Diagnostic slot cache. Stores complete linear float32 contributions, never a running total.</summary>
internal sealed class V9SourceCache
{
    private sealed class Entry
    {
        public readonly V9Light[] Source = new V9Light[1];
        public readonly Vector3[] Contribution = new Vector3[V9LabScene.Width * V9LabScene.Height];
        public bool Valid;
    }
    private readonly List<Entry> entries = new();
    private V9LabScene lastScene;
    private int geometryRevision = -1;
    public int Recalculated { get; private set; }
    public double EvaluateMilliseconds { get; private set; }
    public double RecomposeMilliseconds { get; private set; }
    public long PayloadBytes => (long)entries.Count * V9LabScene.Width * V9LabScene.Height * 3 * sizeof(float);

    public void Invalidate()
    {
        foreach (var entry in entries) entry.Valid = false;
    }

    public void Rebuild(V9LabScene scene, IReadOnlyList<V9Light> lights, Vector3[] energy, Func<int, int, bool> solid = null)
    {
        long start = Stopwatch.GetTimestamp();
        // Count changes deliberately invalidate everything. Reorders of the same count change only
        // affected slots. Full source-value equality also covers position, RGB, power and radius.
        if (entries.Count != lights.Count)
        {
            while (entries.Count > lights.Count) entries.RemoveAt(entries.Count - 1);
            while (entries.Count < lights.Count) entries.Add(new Entry());
            Invalidate();
        }
        if (!ReferenceEquals(lastScene, scene) || geometryRevision != scene.Map.TileRevision)
        {
            Invalidate();
            lastScene = scene;
            geometryRevision = scene.Map.TileRevision;
        }
        Recalculated = 0;
        solid ??= scene.Solid; // Diagnostic injection point only; null keeps the original query.
        for (int s = 0; s < lights.Count; s++)
        {
            var entry = entries[s];
            if (entry.Valid && entry.Source[0] == lights[s]) continue;
            entry.Source[0] = lights[s];
            // Call the unchanged evaluator with exactly one source: same contribution formula and DDA.
            for (int y = 0, i = 0; y < V9LabScene.Height; y++)
                for (int x = 0; x < V9LabScene.Width; x++, i++)
                    entry.Contribution[i] = V9LightMath.Evaluate(new Vector2(x + .5f, y + .5f), entry.Source, solid);
            entry.Valid = true;
            Recalculated++;
        }
        EvaluateMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < energy.Length; i++)
        {
            Vector3 sum = Vector3.Zero;
            for (int s = 0; s < entries.Count; s++) sum += entries[s].Contribution[i];
            energy[i] = sum;
        }
        RecomposeMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}
