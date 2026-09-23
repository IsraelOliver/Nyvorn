using System;
using System.Globalization;
using System.IO;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

/// <summary>Process-scoped, explicit opt-in. Never written into a save or global configuration.</summary>
public static class V8GameplayOptions
{
    public static bool Enabled { get; private set; }
    public static bool CaptureWorld { get; private set; }
    public static bool V7Smoke { get; private set; }
    public static bool BackgroundProbe { get; private set; }
    public static bool ToggleBench { get; private set; }
    // Measurement only: disables VSync and the fixed 60 Hz step for this process.
    public static bool UncappedFrames { get; private set; }
    public static bool PerfBench { get; private set; }
    public static bool SkyCompare { get; private set; }
    public static bool TransientWorld => CaptureWorld || V7Smoke || BackgroundProbe || ToggleBench || PerfBench || SkyCompare;
    public static bool CaptureFinished { get; set; }
    public static string Output { get; private set; }
    /// <summary>Diagnostic only (--v8-bg-range &lt;tiles&gt;): visual sky range of the background wall. Null keeps
    /// SkyRangeTiles, which is the shipped behaviour. It never changes the sky transport.</summary>
    public static float? BackgroundSkyRange { get; private set; }
    /// <summary>Diagnostic only (--v8-sky-4-neighbours): falls back to the former 4-neighbour sky transport.
    /// The shipped behaviour is 8 neighbours with diagonal cost sqrt(2).</summary>
    public static bool SkyFourNeighbours { get; private set; }
    /// <summary>Diagnostic only (--v8-torch-radius &lt;tiles&gt;): direct radius of the torch, in tiles. Null keeps the
    /// shipped 14 tiles (112 px). It changes only the direct term; the local fill keeps its own radius.</summary>
    public static float? TorchDirectRadiusTiles { get; private set; }
    /// <summary>Diagnostic only (--v8-torch-local-radius &lt;tiles&gt;): radius of the torch's local fill, in tiles. Null
    /// keeps the shipped 18 tiles (144 px). It never touches the local intensity nor the direct term.</summary>
    public static float? TorchLocalRadiusTiles { get; private set; }
    /// <summary>Diagnostic only (--v8-torch-local-intensity &lt;value&gt;): AmbientIntensity of the torch's local fill.
    /// Null keeps the V8Light default. It never touches the local radius nor the direct term.</summary>
    public static float? TorchLocalIntensity { get; private set; }
    public static void Configure(string[] args)
    {
        Enabled = Array.Exists(args, a => a == "--lighting-v8");
        CaptureWorld = Array.Exists(args, a => a == "--v8-gameplay-capture");
        V7Smoke = Array.Exists(args, a => a == "--v7-gameplay-smoke");
        if (V7Smoke && Enabled) throw new ArgumentException("V7 smoke must run without --lighting-v8");
        if (CaptureWorld && !Enabled) throw new ArgumentException("--v8-gameplay-capture requires --lighting-v8");
        BackgroundProbe = Array.Exists(args, a => a == "--v8-background-probe");
        if (BackgroundProbe && (!Enabled || CaptureWorld)) throw new ArgumentException("--v8-background-probe requires --lighting-v8 and excludes --v8-gameplay-capture");
        ToggleBench = Array.Exists(args, a => a == "--lighting-toggle-bench");
        if (ToggleBench && (Enabled || CaptureWorld || BackgroundProbe || V7Smoke))
            throw new ArgumentException("--lighting-toggle-bench starts from the V7 default and drives the camera itself; run it without other lighting flags");
        UncappedFrames = Array.Exists(args, a => a == "--uncapped-frames");
        PerfBench = Array.Exists(args, a => a == "--v8-perf-bench");
        if (PerfBench && (!Enabled || CaptureWorld || BackgroundProbe || ToggleBench || V7Smoke))
            throw new ArgumentException("--v8-perf-bench requires --lighting-v8 and drives the camera itself; run it without other capture flags");
        SkyCompare = Array.Exists(args, a => a == "--lighting-sky-compare");
        if (SkyCompare && (Enabled || CaptureWorld || BackgroundProbe || ToggleBench || PerfBench || V7Smoke))
            throw new ArgumentException("--lighting-sky-compare starts from the V7 default and drives camera, hour and pipeline itself; run it alone");
        Output = Path.GetFullPath("screenshots/v8/gameplay");
        SkyFourNeighbours = Array.Exists(args, a => a == "--v8-sky-4-neighbours");
        BackgroundSkyRange = null;
        TorchDirectRadiusTiles = null;
        TorchLocalRadiusTiles = null;
        TorchLocalIntensity = null;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--v8-output") Output = Path.GetFullPath(args[i + 1]);
            if (args[i] == "--v8-torch-radius")
            {
                if (!float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float tiles) || tiles <= 0)
                    throw new ArgumentException("--v8-torch-radius needs a positive number of tiles");
                TorchDirectRadiusTiles = tiles;
            }
            if (args[i] == "--v8-torch-local-radius")
            {
                if (!float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float local) || local <= 0)
                    throw new ArgumentException("--v8-torch-local-radius needs a positive number of tiles");
                TorchLocalRadiusTiles = local;
            }
            if (args[i] == "--v8-torch-local-intensity")
            {
                if (!float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float intensity) || intensity < 0)
                    throw new ArgumentException("--v8-torch-local-intensity needs a non-negative number");
                TorchLocalIntensity = intensity;
            }
            if (args[i] == "--v8-bg-range")
            {
                if (!float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float range) || range <= 0)
                    throw new ArgumentException("--v8-bg-range needs a positive number of tiles");
                BackgroundSkyRange = range;
            }
        }
    }
}
