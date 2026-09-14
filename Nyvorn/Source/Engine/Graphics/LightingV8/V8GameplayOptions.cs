using System;
using System.IO;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

/// <summary>Process-scoped, explicit opt-in. Never written into a save or global configuration.</summary>
public static class V8GameplayOptions
{
    public static bool Enabled { get; private set; }
    public static bool CaptureWorld { get; private set; }
    public static bool V7Smoke { get; private set; }
    public static bool TransientWorld => CaptureWorld || V7Smoke;
    public static bool CaptureFinished { get; set; }
    public static string Output { get; private set; }
    public static void Configure(string[] args)
    {
        Enabled = Array.Exists(args, a => a == "--lighting-v8");
        CaptureWorld = Array.Exists(args, a => a == "--v8-gameplay-capture");
        V7Smoke = Array.Exists(args, a => a == "--v7-gameplay-smoke");
        if (V7Smoke && Enabled) throw new ArgumentException("V7 smoke must run without --lighting-v8");
        if (CaptureWorld && !Enabled) throw new ArgumentException("--v8-gameplay-capture requires --lighting-v8");
        Output = Path.GetFullPath("screenshots/v8/gameplay");
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--v8-output") Output = Path.GetFullPath(args[i + 1]);
    }
}
