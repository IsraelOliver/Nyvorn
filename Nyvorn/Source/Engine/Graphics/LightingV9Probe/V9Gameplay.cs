using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics.LightingV8;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Probe;

/// <summary>
/// V9 as the gameplay's lighting, the default pipeline since 25/09/2026: its selection and its shipped configuration, in
/// one place. The gameplay probe (--lighting-v9-gameplay-probe) is this same V9 plus instrumentation and starts from these
/// values; only its explicit diagnostic flags change them. This folder keeps its historical "Probe" names: its classes are
/// the V9 runtime. Process-scoped; never written into a save or a global configuration.
/// </summary>
public static class V9Gameplay
{
    /// <summary>V9 draws the gameplay frame. Off only when another pipeline is selected explicitly (see Configure).</summary>
    public static bool Enabled { get; private set; }
    /// <summary>--v9-gameplay-smoke: the default V9 path (no probe instrumentation) on a transient world, scripted
    /// checks, then exit (PlayingState.V9Smoke.cs).</summary>
    public static bool Smoke { get; private set; }
    public static string SmokeOutput { get; private set; }
    /// <summary>Probe and smoke run on a transient world: nothing is read from or written to the user's saves.</summary>
    public static bool TransientSession => V9ProbeOptions.Enabled || Smoke;
    /// <summary>A transient V9 run is over; Game1 exits.</summary>
    public static bool Finished { get; set; }

    /// <summary>Shipped natural light: FG/BG presence over a depth-weighted sky (V9ProbeBackplane).</summary>
    public const V9NaturalModel NaturalModel = V9NaturalModel.SkyBackplane;
    /// <summary>Shipped Sky Background Glow: the default reach, peak V9ProbeBackplane.GlowPeak x open sky.</summary>
    public static V9SkyGlowSettings Glow => V9SkyGlowSettings.Default with { PeakScale = V9ProbeBackplane.GlowPeak };

    /// <summary>After V8GameplayOptions and V9ProbeOptions. V9 stays off only when another pipeline is selected
    /// explicitly: --lighting-v7, --lighting-v8, NYVORN_LIGHTING=legacy (V6), a V7 environment diagnostic
    /// (NYVORN_V7_AUTOSHOT, NYVORN_V7_VIEW) or a V7/V8 diagnostic mode, all of which start from V7/V8 as before.</summary>
    public static void Configure(string[] args)
    {
        bool v7 = Array.Exists(args, a => a == "--lighting-v7");
        Smoke = Array.Exists(args, a => a == "--v9-gameplay-smoke");
        if (v7 && (V8GameplayOptions.Enabled || V9ProbeOptions.Enabled || Smoke))
            throw new ArgumentException("--lighting-v7 selects V7: not with --lighting-v8, the V9 probe or the V9 smoke");
        bool legacy = string.Equals(Environment.GetEnvironmentVariable("NYVORN_LIGHTING"), "legacy", StringComparison.OrdinalIgnoreCase);
        bool v7Diagnostics = Environment.GetEnvironmentVariable("NYVORN_V7_AUTOSHOT") != null || Environment.GetEnvironmentVariable("NYVORN_V7_VIEW") != null;
        bool other = v7 || legacy || v7Diagnostics || V8GameplayOptions.Enabled || V8GameplayOptions.TransientWorld;
        if (Smoke && (V9ProbeOptions.Enabled || other))
            throw new ArgumentException("--v9-gameplay-smoke runs the default V9 alone: no probe, V7/V8 flag or lighting environment override");
        Enabled = V9ProbeOptions.Enabled || !other;
        SmokeOutput = Path.GetFullPath("screenshots/v9-gameplay-smoke");
        for (int i = 0; i + 1 < args.Length; i++)
            if (Smoke && args[i] == "--v9-output") SmokeOutput = Path.GetFullPath(args[i + 1]);
    }

    /// <summary>The V9 field of a world, with the shipped configuration. <paramref name="model"/> differs from
    /// <see cref="NaturalModel"/> only under the probe's diagnostic flags.</summary>
    public static V9ProbeField CreateField(GraphicsDevice device, IReadOnlyList<WorldLayerDefinition> layers, V9NaturalModel model = NaturalModel)
    {
        WorldLayerDefinition surface = layers.First(l => l.LayerType == WorldLayerType.Surface);
        return new V9ProbeField(device, model)
        {
            SurfaceStartRow = surface.StartY,
            ShallowLayer = layers.First(l => l.LayerType == WorldLayerType.ShallowUnderground),
            SkyLayersEndRow = surface.EndY,
            GlowSettings = Glow,
            ForegroundSky = true
        };
    }
}
