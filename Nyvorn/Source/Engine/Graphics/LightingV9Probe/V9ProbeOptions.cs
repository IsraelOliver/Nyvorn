using System;
using System.IO;
using Nyvorn.Source.Engine.Graphics.LightingV8;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Probe;

/// <summary>
/// Process-scoped opt-in for the V9 gameplay probe (--lighting-v9-gameplay-probe): the gameplay's V9 (V9Gameplay) plus
/// instrumentation, on a transient world. Never written into a save or global configuration. The probe runs alone:
/// V7/V8 and their diagnostic modes never compute or compose its frame.
/// </summary>
public static class V9ProbeOptions
{
    public static bool Enabled { get; private set; }
    /// <summary>--v9-probe-capture: scripted evidence states, then exit.</summary>
    public static bool Capture { get; private set; }
    /// <summary>--v9-probe-bench: measured scenarios with VSync and the fixed step off, then exit.</summary>
    public static bool Bench { get; private set; }
    /// <summary>--v9-probe-sky-points: the first natural light (point samples above the envelope), diagnostic only.</summary>
    public static bool SkyPoints { get; private set; }
    /// <summary>--v9-probe-exposure: the natural model before the Sky Backplane (exposure + openings), diagnostic
    /// reference only; it was the probe's default until the promotion.</summary>
    public static bool Exposure { get; private set; }
    /// <summary>The shipped Sky Backplane: the probe's default since the promotion (--v9-probe-sky-backplane is still
    /// accepted, redundant).</summary>
    public static bool SkyBackplane { get; private set; }
    /// <summary>The probe's natural model; the shipped one unless a diagnostic model is requested.</summary>
    public static V9NaturalModel NaturalModel => SkyPoints ? V9NaturalModel.PointSamples : Exposure ? V9NaturalModel.Exposure : V9Gameplay.NaturalModel;
    /// <summary>--v9-probe-no-sky-glow: no Sky Background Glow light (the Shallow sky classification and the sky shown
    /// through its background openings stay). The glow is on by default (Sky Backplane and exposure models).</summary>
    public static bool NoSkyGlow { get; private set; }
    /// <summary>--v9-probe-glow-sun: directional sun modulation of the exposure model's glow (off by default; requires
    /// --v9-probe-exposure).</summary>
    public static bool GlowSun { get; private set; }
    /// <summary>Diagnostic only (black-band investigation): --v9-probe-no-sky-mask skips the black mask drawn below each
    /// column's sky floor; --v9-probe-pixel-report "x,y;x,y" writes, per capture, what lies under those screen pixels.</summary>
    public static bool NoSkyMask { get; private set; }
    /// <summary>--v9-probe-no-foreground-sky: solid faces next to visible sky (Shallow openings, OpenAtmosphere in
    /// Space/Surface) do not take the open-sky term; faces next to the exposed region O still do, as before.</summary>
    public static bool NoForegroundSky { get; private set; }
    public static Microsoft.Xna.Framework.Point[] PixelReport { get; private set; } = Array.Empty<Microsoft.Xna.Framework.Point>();
    public static string Output { get; private set; }
    public static bool Finished { get; set; }

    public static void Configure(string[] args)
    {
        Enabled = Array.Exists(args, a => a == "--lighting-v9-gameplay-probe");
        Capture = Array.Exists(args, a => a == "--v9-probe-capture");
        Bench = Array.Exists(args, a => a == "--v9-probe-bench");
        SkyPoints = Array.Exists(args, a => a == "--v9-probe-sky-points");
        Exposure = Array.Exists(args, a => a == "--v9-probe-exposure");
        bool backplaneFlag = Array.Exists(args, a => a == "--v9-probe-sky-backplane");
        if ((SkyPoints || Exposure || backplaneFlag) && !Enabled)
            throw new ArgumentException("--v9-probe-sky-points/--v9-probe-exposure/--v9-probe-sky-backplane require --lighting-v9-gameplay-probe");
        if ((SkyPoints ? 1 : 0) + (Exposure ? 1 : 0) + (backplaneFlag ? 1 : 0) > 1)
            throw new ArgumentException("Choose one natural model: --v9-probe-sky-points, --v9-probe-exposure or --v9-probe-sky-backplane (the default)");
        SkyBackplane = Enabled && !SkyPoints && !Exposure;
        NoSkyGlow = Array.Exists(args, a => a == "--v9-probe-no-sky-glow");
        GlowSun = Array.Exists(args, a => a == "--v9-probe-glow-sun");
        if (GlowSun && !Exposure)
            throw new ArgumentException("--v9-probe-glow-sun modulates the exposure model's glow: add --v9-probe-exposure (the Sky Backplane has no sun)");
        if ((NoSkyGlow || GlowSun) && (!Enabled || SkyPoints))
            throw new ArgumentException("--v9-probe-no-sky-glow/--v9-probe-glow-sun belong to the probe's glow models (not with --v9-probe-sky-points)");
        if (NoSkyGlow && GlowSun) throw new ArgumentException("--v9-probe-glow-sun modulates the glow; it cannot be combined with --v9-probe-no-sky-glow");
        if ((Capture || Bench) && !Enabled) throw new ArgumentException("--v9-probe-capture/--v9-probe-bench require --lighting-v9-gameplay-probe");
        if (Capture && Bench) throw new ArgumentException("Run --v9-probe-capture and --v9-probe-bench separately; captures read back from the GPU.");
        if (Enabled && (V8GameplayOptions.Enabled || V8GameplayOptions.TransientWorld || V8GameplayOptions.UncappedFrames))
            throw new ArgumentException("--lighting-v9-gameplay-probe runs alone: no V7/V8 lighting flag or diagnostic mode participates.");
        NoSkyMask = Array.Exists(args, a => a == "--v9-probe-no-sky-mask");
        NoForegroundSky = Array.Exists(args, a => a == "--v9-probe-no-foreground-sky");
        if (NoForegroundSky && (!Enabled || SkyPoints)) throw new ArgumentException("--v9-probe-no-foreground-sky belongs to the probe's Sky Backplane or exposure model");
        if (NoSkyMask && !Enabled) throw new ArgumentException("--v9-probe-no-sky-mask requires --lighting-v9-gameplay-probe");
        Output = Path.GetFullPath("screenshots/v9-gameplay-probe");
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--v9-output") Output = Path.GetFullPath(args[i + 1]);
            if (args[i] == "--v9-probe-pixel-report")
                PixelReport = Array.ConvertAll(args[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries), p =>
                {
                    string[] xy = p.Split(',');
                    return new Microsoft.Xna.Framework.Point(int.Parse(xy[0]), int.Parse(xy[1]));
                });
        }
    }
}
