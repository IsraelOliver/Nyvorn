using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;
using Nyvorn.Source.Engine.Graphics.LightingV9Probe;
using Nyvorn.Source.Gameplay.Items;

namespace Nyvorn.Source.Game.States;

/// <summary>
/// --lighting-v9-gameplay-probe: the gameplay's V9 (PlayingState.V9.cs, same field, configuration and render) on a real,
/// transient session, plus instrumentation: fixed noon, test items, procedural scenes, teleports, captures, bench and a
/// diagnostic HUD. Nothing is saved.
/// </summary>
public partial class PlayingState
{
    private bool v9CaptureRequested;
    private double v9PrepareMs;
    private string v9Status = string.Empty;

    private void InitializeV9Probe()
    {
        if (!V9ProbeOptions.Enabled) return;
        // V9 itself was set up by InitializeV9 with the shipped configuration; the probe's diagnostic flags only adjust it.
        v9Field.GlowSettings = V9BaseGlow();
        v9Field.ForegroundSky = !V9ProbeOptions.NoForegroundSky;
        if (V9ProbeOptions.GlowSun) v9Field.GlowSettings = V9GlowWithSun(v9Field.GlowSettings, .5f);
        // The camera stays the gameplay's own (zoom 2, 3 in interiors); scripted captures and the bench pin zoom 2.
        session.SetWorldTimeOfDay(.5f); // fixed noon: this probe's sky has no day/night cycle
        Directory.CreateDirectory(V9ProbeOptions.Output);
        // Manual-test convenience in this transient session only (never saved): torches, a pickaxe and stone blocks.
        session.StoreItem(ItemId.Torch, 99, preferInventory: false);
        session.StoreItem(ItemId.IronPickaxe, 1, preferInventory: false);
        session.StoreItem(ItemId.StoneBlock, 99, preferInventory: false);
        FindV9ProbeScenes();
        if (V9ProbeOptions.Capture) InitializeV9ProbeCapture();
        if (V9ProbeOptions.Bench) InitializeV9ProbeBench();
        Console.WriteLine("[V9 probe] active: V6/V7/V8 bypassed; transient session, nothing saved. " + v9SceneReport);
    }

    private void UpdateV9ProbeInput()
    {
        if (!V9ProbeOptions.Enabled) return;
        session.SetWorldTimeOfDay(.5f);
        if (consoleOpen || V9ProbeOptions.Capture || V9ProbeOptions.Bench) return;
        var keys = Keyboard.GetState();
        bool Pressed(Keys key) => keys.IsKeyDown(key) && !previousConsoleKeyboard.IsKeyDown(key);
        if (Pressed(Keys.F12)) v9CaptureRequested = true;
        int w = graphicsDevice.PresentationParameters.BackBufferWidth, h = graphicsDevice.PresentationParameters.BackBufferHeight;
        if (Pressed(Keys.F5)) TeleportV9(v9Spawn, w, h);
        if (Pressed(Keys.F6) && v9EntranceFound) TeleportV9(V9Stand(v9EntranceRim), w, h);
        if (Pressed(Keys.F7) && v9CaveFound) TeleportV9(V9Stand(v9CaveFloor), w, h);
    }

    /// <summary>The glow's sun from the game's own sun (ComputeSunDirectionRadians / ComputeSunColor: rises on the left at
    /// 0.25, top at noon, sets on the right at 0.75): direction toward the sun (y down) and its colour, decoded to linear
    /// and normalised to max 1. Only the modulation reads it; the world clock and the game's sun are untouched.</summary>
    private V9SkyGlowSettings V9GlowWithSun(V9SkyGlowSettings settings, float timeOfDay)
    {
        float t = timeOfDay % 1f;
        if (t < .25f || t > .75f) return settings.WithoutSun();
        float angle = ComputeSunDirectionRadians(t);
        Vector3 sun = V9LightMath.DecodeSrgb(ComputeSunColor(t));
        sun /= MathF.Max(sun.X, MathF.Max(sun.Y, sun.Z));
        return settings with { Sun = true, ToSun = new Vector2(MathF.Cos(angle), -MathF.Sin(angle)), SunRgb = sun };
    }

    private void TeleportV9(Vector2 position, int width, int height)
    {
        session.Player.TeleportTo(position);
        session.Camera.CenterOn(position - new Vector2(0, 12), width, height);
    }

    private void DrawV9Probe(SpriteBatch batch, int width, int height, float frameSeconds)
    {
        long prepare = Stopwatch.GetTimestamp();
        if (V9ProbeOptions.Capture) PrepareV9ProbeState(width, height);
        if (V9ProbeOptions.Bench) PrepareV9ProbeBench(width, height);
        v9PrepareMs = Stopwatch.GetElapsedTime(prepare).TotalMilliseconds;

        // The gameplay's own V9 frame (PlayingState.V9.cs).
        var loops = RenderV9Frame(batch, width, height, out Rectangle viewport);

        bool capture = v9CaptureRequested || ShouldCaptureV9State;
        if (capture)
        {
            graphicsDevice.SetRenderTarget(v9Albedo);
            graphicsDevice.Clear(Color.Black);
            DrawV9Scene(batch, width, height, loops, viewport, lit: false);
            graphicsDevice.SetRenderTarget(null);
            SaveV9ProbeCapture(V9ProbeOptions.Capture ? V9StateLabel : "manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            v9CaptureRequested = false;
        }

        PresentV9Frame(batch, width, height, loops, () =>
        {
            var s = v9Field.Last;
            string natural = v9Field.IsBackplane ? "natural: sky backplane (shipped)" : v9Field.NaturalModel == V9NaturalModel.Exposure
                ? $"natural: exposure ({v9Field.ExposedCells} exposed cells, {s.NaturalEntries} openings)" : $"natural: legacy sky points ({s.SkySamples})";
            batch.DrawString(consoleFont, $"LIGHTING: V9 gameplay probe | window {s.FieldWidth}x{s.FieldHeight} @ ({s.Origin.X},{s.Origin.Y}) | {natural} | torches {s.Torches} | recentres {v9Field.Recenters} | F5 surface F6 entrance F7 closed cave F12 capture", new Vector2(10, 48), Color.Yellow);
            batch.DrawString(consoleFont, $"V9 CPU natural: exposure {s.ExposureMs:0.00} / openings {s.EntriesMs:0.00} / evaluate {s.NaturalEvaluateMs:0.00} ({s.NaturalRecalculated}) / direct {s.DirectMs:0.00} | torches: collect {s.CollectMs:0.00} / evaluate {s.EvaluateMs:0.00} ({s.Recalculated})", new Vector2(10, 72), Color.Cyan);
            batch.DrawString(consoleFont, $"V9 CPU: region {s.RegionMs:0.00} / occupancy {s.OccupancyMs:0.00} / recompose {s.RecomposeMs:0.00} / half+upload {s.UploadMs:0.00} | field {v9FieldMs:0.00} | V9 draw {v9DrawMs:0.00} ms | frame {frameSeconds * 1000:0.00} ms {v9Status}", new Vector2(10, 96), Color.Cyan);
            var g = v9Field.GlowSettings;
            string depth = v9Field.IsBackplane ? $"sky fades over rows {v9Field.ShallowLayer.EndY + 1 - V9ProbeBackplane.DepthTransitionTiles}..{v9Field.ShallowLayer.EndY + 1}" : $"Shallow fade {g.ShallowFadeTiles} tiles";
            batch.DrawString(consoleFont, $"FG sky {(v9Field.ForegroundSky ? "on" : "off")} | Sky glow {(g.Enabled ? "on" : "off")}{(g.Sun ? " + sun" : "")} (reach {g.ReachPixels} px, peak {g.PeakScale:0.##}x open sky, {depth}) | seeds {s.SkyGlowSeeds}, lit {s.SkyGlowPixels} px | classify {s.SkyClassifyMs:0.00} / propagate {s.SkyPropagateMs:0.00} / value {s.SkyValueMs:0.00} ms", new Vector2(10, 120), Color.Cyan);
            DrawLightingStatsHud(batch, 144);
        });

        if (V9ProbeOptions.Capture) FinishV9ProbeState(capture);
        if (V9ProbeOptions.Bench) FinishV9ProbeBenchFrame(frameSeconds);
    }
}
