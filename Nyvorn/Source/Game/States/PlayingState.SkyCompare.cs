using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics.LightingV7;
using Nyvorn.Source.Engine.Graphics.LightingV8;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;

namespace Nyvorn.Source.Game.States;

/// <summary>--lighting-sky-compare: diagnostic only, transient session, nothing saved. Renders the same scene, camera, zoom
/// and hour as V7 without sun, V8, and V7 with sun (the V7 default, as reference), with no local light source in either
/// pipeline: torches removed, mushroom sources suppressed in both, V7 tissue emission off. Pipelines switch through
/// SetLightingPipeline (the F4 path). Underground scenes are carved relative to the generated world's real layer limits.
/// Normal gameplay never sets the suppression flags.</summary>
public partial class PlayingState
{
    private enum SkyScene { Exterior, OpeningSmall, OpeningLarge, Sealed, ShallowToCavern }
    private sealed record SkyCase(string Name, SkyScene Scene, float Time);
    private sealed record SkyVariant(string Name, string Label, bool V8, bool Sun);

    private static readonly SkyVariant[] SkyVariants =
    {
        new("v7-nosun", "V7 sem sol (F7 off: ceu x1,0)", false, false),
        new("v8", "V8 (ceu AmbientLight x0,5)", true, false),
        new("v7-sun", "V7 padrao com sol (ceu x0,6 + sol)", false, true)
    };

    private const int SkySettleFrames = 180, SkyVariantFrames = 20, SkyZoom = 2;
    private const int SkyShell = 16, SkyTunnelLength = 56, SkyTunnelHeight = 6, SkyShaftWidth = 10, SkyShaftHalf = 14, SkyShallowFade = 6;
    private static readonly Rectangle SkyCrop = new(160, 150, 960, 500); // below the HUD lines, above the hotbar
    private static readonly int[] SkyProfileMarks = { 0, 2, 4, 6, 9, 12, 16, 20, 24, 32, 40, 48 };

    private readonly List<SkyCase> skyCases = new();
    private readonly List<string> skyChecks = new(), skySummary = new();
    private readonly Color[][] skyFinals = new Color[3][];
    private readonly Color[][] skyLights = new Color[3][];
    private readonly Rectangle[] skyLightBounds = new Rectangle[3];
    private readonly Matrix[] skyViews = new Matrix[3];
    private readonly string[] skySkyState = new string[3];
    private int skyCase = -1, skyVariant, skyFrames;
    private bool skyAdvance = true, skyFinished, v7SuppressLocalSources, skyOriginalSun, skyOriginalTissue;
    private Point skySpawnTile, skyOrigin;
    private Vector2 skyCenter, skyPlayerParking;
    private SpriteBatch skyBatch;

    private void InitializeSkyCompare()
    {
        Directory.CreateDirectory(V8GameplayOptions.Output);
        session.Player.SetDebugFly(true);
        session.Camera.UseBounds = false;
        skyOriginalSun = LightingV7Config.SunEnabled;
        skyOriginalTissue = LightingV7Config.TissueEmissiveEnabled;
        LightingV7Config.TissueEmissiveEnabled = false; // tissue seeds V7's block channel: a local source
        v7SuppressLocalSources = true;                   // no torch or mushroom point lights in V7
        v8SuppressDecorationSources = true;              // no mushroom sources in V8
        session.TorchRuntimeSystem.Restore(Array.Empty<TorchSaveData>());
        skySpawnTile = session.WorldMap.WorldToTile(session.Player.Position);
        skyBatch = new SpriteBatch(graphicsDevice);

        skyCases.Add(new("exterior-noon", SkyScene.Exterior, .5f));
        skyCases.Add(new("exterior-night", SkyScene.Exterior, .02f));
        skyCases.Add(new("shallow-opening-small", SkyScene.OpeningSmall, .5f));
        skyCases.Add(new("shallow-opening-large", SkyScene.OpeningLarge, .5f));
        skyCases.Add(new("shallow-sealed", SkyScene.Sealed, .5f));
        skyCases.Add(new("shallow-to-cavern", SkyScene.ShallowToCavern, .5f));
    }

    private void UpdateSkyCompare()
    {
        if (!skyAdvance || skyFinished)
            return;

        skyAdvance = false;
        if (skyCase < 0 || skyVariant + 1 >= SkyVariants.Length)
        {
            if (skyCase >= 0)
                ComposeSkyCase();
            skyCase++;
            skyVariant = 0;
            if (skyCase == skyCases.Count)
            {
                FinishSkyCompareRun();
                return;
            }
            BuildSkyScene(skyCases[skyCase]);
        }
        else skyVariant++;

        var variant = SkyVariants[skyVariant];
        LightingV7Config.SunEnabled = variant.Sun;
        SetLightingPipeline(variant.V8);
        skyFrames = 0;
        Console.WriteLine($"[Sky compare] {skyCases[skyCase].Name} / {variant.Name}");
    }

    private void PrepareSkyCompare(int width, int height)
    {
        if (skyFinished || skyCase < 0 || skyCase >= skyCases.Count)
            return;

        session.SetWorldTimeOfDay(skyCases[skyCase].Time);
        session.Camera.Zoom = SkyZoom;
        session.Player.TeleportTo(skyPlayerParking);
        // The scene centre lands at screen y = 400, the middle of the crop below the HUD.
        session.Camera.CenterOn(skyCenter - new Vector2(0, 40f / SkyZoom), width, height);
    }

    private void FinishSkyCompareFrame()
    {
        if (skyFinished || skyAdvance || skyCase < 0 || skyCase >= skyCases.Count)
            return;

        skyFrames++;
        if (skyFrames < (skyVariant == 0 ? SkySettleFrames : SkyVariantFrames))
            return;

        CaptureSkyVariant();
        skyAdvance = true;
    }

    private void BuildSkyScene(SkyCase sc)
    {
        var map = session.WorldMap;
        int size = map.TileSize;
        var shallow = session.LayerDefinitions.First(l => l.LayerType == WorldLayerType.ShallowUnderground);
        int index = (int)sc.Scene - (int)SkyScene.OpeningSmall;

        if (sc.Scene == SkyScene.Exterior)
        {
            skyCenter = session.Player.Position - new Vector2(0, 40);
            skyCenter = new Vector2(skySpawnTile.X * size + size / 2f, skySpawnTile.Y * size) - new Vector2(0, 40);
            skyPlayerParking = skyCenter + new Vector2(90 * size, 0);
            skyOrigin = skySpawnTile;
            SkyCheck(sc, null, $"exterior at player spawn tile {skySpawnTile} (layer {LayerName(skySpawnTile.Y)})", LayerName(skySpawnTile.Y) is "Surface" or "Space");
            return;
        }

        bool shaft = sc.Scene == SkyScene.ShallowToCavern;
        int innerWidth = shaft ? SkyShaftWidth : SkyTunnelLength;
        int innerHeight = shaft ? SkyShaftHalf * 2 : SkyTunnelHeight;
        int areaWidth = innerWidth + 2 * SkyShell, areaHeight = innerHeight + 2 * SkyShell;
        int left = map.WrapTileX(skySpawnTile.X + 140 + index * (SkyTunnelLength + 2 * SkyShell + 40));
        if (left + areaWidth >= map.Width) left = map.Width - areaWidth - 1;

        // Tunnels sit near the top of the Shallow layer, far from its fade rows; the shaft is centred on its last row.
        int innerTop = shaft ? shallow.EndY - SkyShaftHalf + 1 : shallow.StartY + 2 + SkyShell;
        int top = innerTop - SkyShell;
        bool fits = shaft
            ? innerTop + 6 <= shallow.EndY - SkyShallowFade && top >= shallow.StartY
            : innerTop + innerHeight <= shallow.EndY - SkyShallowFade && top + areaHeight <= shallow.EndY;
        if (!fits) throw new InvalidOperationException($"{sc.Name}: layer {shallow.StartY}..{shallow.EndY} too short for the scene");

        session.SandSystem.RemoveSandInRectangle(left * size, top * size, areaWidth * size, areaHeight * size);
        for (int y = top; y < top + areaHeight; y++)
            for (int x = left; x < left + areaWidth; x++)
            {
                map.SetTile(x, y, TileType.Stone);
                map.SetBackgroundTile(x, y, TileType.Stone);
            }

        int innerLeft = left + SkyShell;
        for (int y = innerTop; y < innerTop + innerHeight; y++)
            for (int x = innerLeft; x < innerLeft + innerWidth; x++)
                map.SetTile(x, y, TileType.Empty); // background stays Stone: the tunnel is under a roof

        // Openings are background holes (air in front and behind): exterior apertures by both pipelines' rules.
        Rectangle opening = sc.Scene switch
        {
            SkyScene.OpeningSmall => new Rectangle(innerLeft, innerTop + 2, 2, 2),
            SkyScene.OpeningLarge => new Rectangle(innerLeft, innerTop, 6, 6),
            SkyScene.ShallowToCavern => new Rectangle(innerLeft, innerTop, SkyShaftWidth, 6),
            _ => Rectangle.Empty
        };
        for (int y = opening.Top; y < opening.Bottom; y++)
            for (int x = opening.Left; x < opening.Right; x++)
                map.SetBackgroundTile(x, y, TileType.Empty);

        skyOrigin = new Point(innerLeft, innerTop);
        skyCenter = new Vector2((innerLeft + innerWidth / 2f) * size, shaft ? (shallow.EndY + 1) * size : (innerTop + innerHeight / 2f) * size);
        skyPlayerParking = skyCenter + new Vector2(90 * size, 0);

        var context = new V8AmbientContext { Layers = session.LayerDefinitions };
        int apertures = 0, liquidTiles = 0;
        for (int y = top; y < top + areaHeight; y++)
            for (int x = left; x < left + areaWidth; x++)
            {
                if (context.IsExteriorAperture(map, x, y)) apertures++;
                if (session.LiquidSystem != null && session.LiquidSystem.GetLiquidAmountAtTile(map.WrapTileX(x), y) > 0) liquidTiles++;
            }
        int expected = opening.Width * opening.Height;
        SkyCheck(sc, null, $"scene tiles {left}..{left + areaWidth - 1} x {top}..{top + areaHeight - 1}; inner origin {skyOrigin}; " +
            $"apertures {apertures} (expected {expected}); layers top {LayerName(innerTop)}, bottom {LayerName(innerTop + innerHeight - 1)}", apertures == expected);
        skyChecks.Add($"INFO {sc.Name}: liquid tiles in scene {liquidTiles} (V7 attenuates water, V8 ignores it)");
    }

    private string LayerName(int tileY)
    {
        foreach (var layer in session.LayerDefinitions)
            if (layer.Contains(tileY))
                return layer.LayerType.ToString();
        return "none";
    }

    private void CaptureSkyVariant()
    {
        var sc = skyCases[skyCase];
        var variant = SkyVariants[skyVariant];
        var pp = graphicsDevice.PresentationParameters;
        var final = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
        graphicsDevice.GetBackBufferData(final);
        for (int i = 0; i < final.Length; i++) final[i].A = 255;
        skyFinals[skyVariant] = final;
        skyViews[skyVariant] = session.Camera.GetViewMatrix();
        var sky = session.EnvironmentSystem.SkyState;
        skySkyState[skyVariant] = $"time {session.TimeOfDay01:0.0000}; AmbientLight {sky.AmbientLight}; rain {sky.RainIntensity:0.00}; eclipse {sky.EclipseIntensity:0.00}";

        if (variant.V8)
        {
            skyLightBounds[skyVariant] = v8Renderer.LightBounds;
            skyLights[skyVariant] = V8Capture.Read(v8Renderer.Ambient.SkyTexture);
            SkyCheck(sc, variant, $"no V8 light source ({v8Sources.Count})", v8Sources.Count == 0);
        }
        else
        {
            var buffer = session.ViewCoordinator.GetPixelLightBuffer();
            skyLightBounds[skyVariant] = new Rectangle(0, 0, buffer.Width, buffer.Height);
            skyLights[skyVariant] = V8Capture.Read(buffer);
            SkyCheck(sc, variant, $"no V7 light source ({v7Lighting.SourceCount}), tissue emission off, sun {(LightingV7Config.SunEnabled ? "on" : "off")}",
                v7Lighting.SourceCount == 0 && !LightingV7Config.TissueEmissiveEnabled && LightingV7Config.SunEnabled == variant.Sun);
        }
        SkyCheck(sc, variant, $"active pipeline {ActiveLightingLabel}", ActiveLightingLabel == (variant.V8 ? "V8" : "V7"));

        using var texture = new Texture2D(graphicsDevice, pp.BackBufferWidth, pp.BackBufferHeight);
        texture.SetData(final);
        V8Capture.Save(texture, Path.Combine(V8GameplayOptions.Output, $"{sc.Name}_{variant.Name}_frame.png"));
    }

    private void ComposeSkyCase()
    {
        var sc = skyCases[skyCase];
        SkyCheck(sc, null, "identical camera view in the three captures", skyViews[1] == skyViews[0] && skyViews[2] == skyViews[0]);
        SkyCheck(sc, null, $"identical hour and sky state in the three captures ({skySkyState[0]})",
            skySkyState[1] == skySkyState[0] && skySkyState[2] == skySkyState[0]);

        const int panelWidth = 480, panelHeight = 250, labelHeight = 30, gap = 8;
        int width = 3 * panelWidth + 2 * gap, height = labelHeight + panelHeight;
        using var target = new RenderTarget2D(graphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var panels = new Texture2D[3];
        var pp = graphicsDevice.PresentationParameters;
        for (int v = 0; v < 3; v++)
        {
            var crop = new Color[SkyCrop.Width * SkyCrop.Height];
            for (int y = 0; y < SkyCrop.Height; y++)
                Array.Copy(skyFinals[v], (SkyCrop.Y + y) * pp.BackBufferWidth + SkyCrop.X, crop, y * SkyCrop.Width, SkyCrop.Width);
            panels[v] = new Texture2D(graphicsDevice, SkyCrop.Width, SkyCrop.Height);
            panels[v].SetData(crop);
            V8Capture.Save(panels[v], Path.Combine(V8GameplayOptions.Output, $"{sc.Name}_{SkyVariants[v].Name}_crop.png"));
        }

        graphicsDevice.SetRenderTarget(target);
        graphicsDevice.Clear(new Color(24, 24, 28));
        skyBatch.Begin(samplerState: SamplerState.PointClamp);
        for (int v = 0; v < 3; v++)
        {
            int x = v * (panelWidth + gap);
            skyBatch.Draw(panels[v], new Rectangle(x, labelHeight, panelWidth, panelHeight), Color.White);
            skyBatch.DrawString(consoleFont, $"{sc.Name} | {SkyVariants[v].Label}", new Vector2(x + 4, 6), Color.White, 0f, Vector2.Zero, .55f, SpriteEffects.None, 0f);
        }
        skyBatch.End();
        graphicsDevice.SetRenderTarget(null);
        V8Capture.Save(target, Path.Combine(V8GameplayOptions.Output, $"compare-{sc.Name}.png"));
        foreach (var panel in panels) panel.Dispose();

        WriteSkyProfile(sc);
    }

    private void WriteSkyProfile(SkyCase sc)
    {
        var map = session.WorldMap;
        int size = map.TileSize;
        var pp = graphicsDevice.PresentationParameters;
        var context = new V8AmbientContext { Layers = session.LayerDefinitions, SkyState = session.EnvironmentSystem.SkyState };
        Vector3 v8SkyColor = context.SkyColor;

        if (sc.Scene == SkyScene.Exterior)
        {
            Vector2 air = skyCenter - new Vector2(0, 60);
            skySummary.Add($"{sc.Name}: open air {air / size} tiles; final luma V7 no sun {Luma(Screen(0, air)):0.0}, V8 {Luma(Screen(1, air)):0.0}, V7 sun {Luma(Screen(2, air)):0.0} (sky backdrop); " +
                $"light V7 no sun {Fmt(V7Light(0, air))}, V8 sky {Fmt(V8Sky(air))}, V7 sun {Fmt(V7Light(2, air))}; {skySkyState[0]}");
            return;
        }

        bool shaft = sc.Scene == SkyScene.ShallowToCavern;
        var shallow = session.LayerDefinitions.First(l => l.LayerType == WorldLayerType.ShallowUnderground);
        int count = shaft ? SkyShaftHalf * 2 : SkyTunnelLength;
        var csv = new StringBuilder(shaft
            ? "row_from_shallow_end,layer,luma_v7_nosun,luma_v8,luma_v7_sun,v7_nosun_light_r,v7_nosun_light_g,v7_nosun_light_b,v8_sky_r,v8_sky_g,v8_sky_b,v8_exposure,v8_layer_weight,v7_sky_scalar\n"
            : "tiles_from_opening,luma_v7_nosun,luma_v8,luma_v7_sun,v7_nosun_light_r,v7_nosun_light_g,v7_nosun_light_b,v8_sky_r,v8_sky_g,v8_sky_b,v8_exposure,v7_sky_scalar\n");
        var lumas = new List<(int Step, double V7, double V8, double V7Sun)>();
        for (int step = 0; step < count; step++)
        {
            // Tunnel: the tile row 3 of the tunnel (the small opening's lower row), walking away from the opening.
            // Shaft: the centre column, walking down from the opening.
            Point tile = shaft ? new Point(skyOrigin.X + SkyShaftWidth / 2, skyOrigin.Y + step) : new Point(skyOrigin.X + step, skyOrigin.Y + 3);
            Vector2 world = new(tile.X * size + size / 2f, tile.Y * size + size / 2f);
            double a = Luma(Screen(0, world)), b = Luma(Screen(1, world)), c = Luma(Screen(2, world));
            lumas.Add((step, a, b, c));
            Vector3 light = V7Light(0, world), skyRgb = V8Sky(world);
            double exposure = v8SkyColor.X > 0 ? skyRgb.X / v8SkyColor.X : 0;
            float v7Sky = v7Lighting.GetSkyAt(tile.X, tile.Y); // last V7 frame drawn: same view, sky channel is independent of the sun
            var inv = CultureInfo.InvariantCulture;
            string common = string.Join(",", new[] { a, b, c, light.X, light.Y, light.Z, skyRgb.X, skyRgb.Y, skyRgb.Z, exposure }.Select(v => v.ToString("0.####", inv)));
            csv.AppendLine(shaft
                ? $"{tile.Y - shallow.EndY},{LayerName(tile.Y)},{common},{context.SkyWeight(tile.Y).ToString("0.###", inv)},{v7Sky.ToString("0.####", inv)}"
                : $"{step},{common},{v7Sky.ToString("0.####", inv)}");
        }
        File.WriteAllText(Path.Combine(V8GameplayOptions.Output, $"{sc.Name}-profile.csv"), csv.ToString());

        int Reach(Func<(int Step, double V7, double V8, double V7Sun), double> pick) =>
            lumas.Where(l => pick(l) > 2.0).Select(l => l.Step + 1).DefaultIfEmpty(0).Max();
        var marks = SkyProfileMarks.Where(m => m < count).Select(m =>
        {
            var l = lumas[m];
            string label = shaft ? $"row {skyOrigin.Y + m - shallow.EndY:+0;-0;0}" : $"{m} t";
            return $"{label}: {l.V7:0.0}/{l.V8:0.0}/{l.V7Sun:0.0}";
        });
        skySummary.Add($"{sc.Name}: final background luma V7 no sun / V8 / V7 sun along {(shaft ? "the shaft (row relative to the last Shallow row)" : "the tunnel")}: " +
            string.Join("; ", marks) + $" | reach (luma > 2) V7 no sun {Reach(l => l.V7)}, V8 {Reach(l => l.V8)}, V7 sun {Reach(l => l.V7Sun)} tiles | {skySkyState[0]}");

        Vector3 Screen(int variant, Vector2 world)
        {
            Vector2 s = Vector2.Transform(world, skyViews[variant]);
            int cx = (int)s.X, cy = (int)s.Y;
            Vector3 sum = Vector3.Zero; int n = 0;
            for (int y = cy - 2; y <= cy + 2; y++)
                for (int x = cx - 2; x <= cx + 2; x++)
                {
                    if (x < 0 || y < 0 || x >= pp.BackBufferWidth || y >= pp.BackBufferHeight) continue;
                    var p = skyFinals[variant][y * pp.BackBufferWidth + x];
                    sum += new Vector3(p.R, p.G, p.B); n++;
                }
            return n > 0 ? sum / n : Vector3.Zero;
        }
        Vector3 V7Light(int variant, Vector2 world)
        {
            Vector2 s = Vector2.Transform(world, skyViews[variant]);
            var bounds = skyLightBounds[variant];
            int x = (int)s.X, y = (int)s.Y;
            if (!bounds.Contains(x, y)) return Vector3.Zero;
            // Stored as light / OverbrightScale.
            return skyLights[variant][y * bounds.Width + x].ToVector3() * LightingV7Config.OverbrightScale;
        }
        Vector3 V8Sky(Vector2 world)
        {
            var bounds = skyLightBounds[1];
            int x = (int)world.X, y = (int)world.Y;
            if (!bounds.Contains(x, y)) return Vector3.Zero;
            return skyLights[1][(y - bounds.Y) * bounds.Width + x - bounds.X].ToVector3();
        }
        static double Luma(Vector3 c) => .2126 * c.X + .7152 * c.Y + .0722 * c.Z;
        static string Fmt(Vector3 v) => $"{v.X:0.000}/{v.Y:0.000}/{v.Z:0.000}";
    }

    private void FinishSkyCompareRun()
    {
        skyFinished = true;
        LightingV7Config.SunEnabled = skyOriginalSun;
        LightingV7Config.TissueEmissiveEnabled = skyOriginalTissue;
        var header = new List<string>
        {
            "Lighting sky compare (diagnostic). Seed=V8-GAMEPLAY-2026, Small; transient session, nothing saved.",
            $"Backbuffer {graphicsDevice.PresentationParameters.BackBufferWidth}x{graphicsDevice.PresentationParameters.BackBufferHeight}; zoom {SkyZoom}; crop {SkyCrop} (below HUD, above hotbar); composites at half scale.",
            "No local light: torches removed; mushroom sources suppressed in V7 and V8; V7 tissue emission off. V7 light buffer values are multiplied back by OverbrightScale.",
            "V7 sun off (F7) makes the sky flood carry full daylight (bounce 1.0); with sun on the flood is x0.6 plus the directional sun.",
            "Layers: " + string.Join("; ", session.LayerDefinitions.Select(l => $"{l.LayerType} {l.StartY}..{l.EndY}")),
            "Tunnel: 56x6 tiles air under a 16-tile solid shell, background Stone everywhere except the opening; small opening 2x2 on rows 2-3, large 6x6. Shaft: 10x28, centred on the last Shallow row, opening = its top 6 rows."
        };
        File.WriteAllLines(Path.Combine(V8GameplayOptions.Output, "compare-summary.txt"), header.Concat(skySummary));
        File.WriteAllLines(Path.Combine(V8GameplayOptions.Output, "compare-checks.txt"), skyChecks);
        if (skyChecks.Any(c => c.StartsWith("FAIL")))
            Environment.ExitCode = 1;
        skyBatch?.Dispose();
        V8GameplayOptions.CaptureFinished = true;
    }

    private void SkyCheck(SkyCase sc, SkyVariant variant, string label, bool passed)
    {
        string line = $"{(passed ? "PASS" : "FAIL")} {sc.Name}{(variant != null ? "/" + variant.Name : "")}: {label}";
        skyChecks.Add(line);
        Console.WriteLine("[Sky compare] " + line);
    }
}
