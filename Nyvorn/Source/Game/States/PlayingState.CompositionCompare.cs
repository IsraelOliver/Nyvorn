using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics.LightingV7;
using Nyvorn.Source.Engine.Graphics.LightingV8;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;

namespace Nyvorn.Source.Game.States;

/// <summary>--lighting-composition-compare: diagnostic only, transient session, nothing saved. Controlled reproductions
/// of the multi-torch problems (wooden house with 0/1/2/5 torches, door closed and open; Cavern chamber with a pillar;
/// Shallow chamber with background openings) at noon, with the same camera, zoom and sources, rendered as: V7 (game
/// default, sun on), V8 shipped sum, V8 prototype A (direct accumulated as C + F(1 - C)) and V8 prototype A+B (bounded
/// composition). V8 variants also save their light buffers through the regular V8 capture. Gameplay is untouched.</summary>
public partial class PlayingState
{
    private enum MixScene { House, Cave, Shallow }
    private sealed record MixCase(string Name, MixScene Scene, int Torches, bool DoorOpen);
    private sealed record MixVariant(string Name, string Label, bool V8, V8Composition Composition);

    private static readonly MixVariant[] MixVariants =
    {
        new("v7", "V7 padrao (sol ligado)", false, V8Composition.Sum),
        new("v8-sum", "V8 atual (soma)", true, V8Composition.Sum),
        new("v8-direct-screen", "V8 A: direta C+F(1-C)", true, V8Composition.DirectScreen),
        new("v8-bounded", "V8 A+B: composicao limitada", true, V8Composition.Bounded)
    };

    private const int MixSettleFrames = 120, MixVariantFrames = 20, MixZoom = 3;
    private readonly List<MixCase> mixCases = new();
    private readonly List<string> mixChecks = new(), mixSummary = new();
    private int mixCase = -1, mixVariant, mixFrames;
    private bool mixAdvance = true, mixFinished;
    private Point mixSpawnTile;
    private Vector2 mixCenter, mixPlayer;

    private string MixCaptureLabel => $"{mixCases[mixCase].Name}_{MixVariants[mixVariant].Name}";
    private int MixFramesNeeded => mixVariant == 0 ? MixSettleFrames : MixVariantFrames;
    // True on the draw of the last frame of a V8 variant: DrawV8Gameplay then saves final, albedo and light buffers,
    // and FinishCompositionCompareFrame reads the backbuffer of that same frame.
    private bool ShouldCaptureComposition => V8GameplayOptions.CompositionCompare && !mixFinished && !mixAdvance &&
        mixCase >= 0 && mixCase < mixCases.Count && MixVariants[mixVariant].V8 && mixFrames + 1 == MixFramesNeeded;

    private void InitializeCompositionCompare()
    {
        Directory.CreateDirectory(V8GameplayOptions.Output);
        session.Player.SetDebugFly(true);
        session.Camera.UseBounds = false;
        mixSpawnTile = session.WorldMap.WorldToTile(session.Player.Position);
        foreach (int torches in new[] { 0, 1, 2, 5 }) mixCases.Add(new($"house-{torches}", MixScene.House, torches, false));
        mixCases.Add(new("house-2-open", MixScene.House, 2, true));
        mixCases.Add(new("cave-0", MixScene.Cave, 0, false));
        mixCases.Add(new("cave-1", MixScene.Cave, 1, false));
        mixCases.Add(new("shallow-0", MixScene.Shallow, 0, false));
        mixCases.Add(new("shallow-3", MixScene.Shallow, 3, false));
    }

    private void UpdateCompositionCompare()
    {
        if (!mixAdvance || mixFinished)
            return;

        mixAdvance = false;
        if (mixCase < 0 || mixVariant + 1 >= MixVariants.Length)
        {
            mixCase++;
            mixVariant = 0;
            if (mixCase == mixCases.Count)
            {
                FinishCompositionCompare();
                return;
            }
            BuildMixScene(mixCases[mixCase]);
        }
        else mixVariant++;

        var variant = MixVariants[mixVariant];
        SetLightingPipeline(variant.V8);
        if (variant.V8) v8Renderer.Composition = variant.Composition;
        mixFrames = 0;
        Console.WriteLine($"[Composition compare] {mixCases[mixCase].Name} / {variant.Name}");
    }

    private void PrepareCompositionCompare(int width, int height)
    {
        if (mixFinished || mixCase < 0 || mixCase >= mixCases.Count)
            return;

        session.SetWorldTimeOfDay(.5f);
        session.Camera.Zoom = MixZoom;
        session.Player.TeleportTo(mixPlayer);
        session.Camera.CenterOn(mixCenter, width, height);
    }

    private void FinishCompositionCompareFrame()
    {
        if (mixFinished || mixAdvance || mixCase < 0 || mixCase >= mixCases.Count)
            return;
        if (++mixFrames < MixFramesNeeded)
            return;

        CaptureMixVariant();
        mixAdvance = true;
    }

    private void BuildMixScene(MixCase mc)
    {
        var map = session.WorldMap;
        int size = map.TileSize;
        var torches = new List<TorchSaveData>();
        var doors = new List<DoorSaveData>();
        Point wall, dark;

        switch (mc.Scene)
        {
            case MixScene.House:
            {
                // Wooden house on the surface: 26 x 10 interior with a wooden back wall, door in the right wall, open air
                // (no background) all around, so the exterior sky reaches the walls and, with the door open, the room.
                int left = map.WrapTileX(mixSpawnTile.X + 160), floor = mixSpawnTile.Y;
                Fill(left - 10, floor - 20, 48, 20, TileType.Empty, TileType.Empty);
                Fill(left - 10, floor, 48, 4, TileType.Dirt, TileType.Empty);
                Fill(left, floor - 11, 28, 12, TileType.Wood, TileType.Wood);        // shell: roof, walls and floor
                Fill(left + 1, floor - 10, 26, 10, TileType.Empty, TileType.Wood);   // interior
                Fill(left + 27, floor - 3, 1, 3, TileType.Empty, TileType.Wood);     // doorway, occupied by the door
                doors.Add(new DoorSaveData { TileX = map.WrapTileX(left + 27), TileY = floor - 3, IsOpen = mc.DoorOpen });
                int[] spots = mc.Torches switch { 1 => new[] { 7 }, 2 => new[] { 7, 19 }, 5 => new[] { 3, 8, 13, 18, 23 }, _ => Array.Empty<int>() };
                foreach (int x in spots) AddTorch(left + x, floor - 1);
                mixPlayer = new Vector2((left + 11) * size, (floor - 4) * size);
                mixCenter = new Vector2((left + 14) * size, (floor - 6) * size);
                wall = new Point(left + 13, floor - 7);
                dark = new Point(left + 25, floor - 9);
                break;
            }
            case MixScene.Cave:
            {
                // Cavern chamber (no sky by layer) with a stone pillar that casts the geometric shadow.
                var cavern = session.LayerDefinitions.First(l => l.LayerType == WorldLayerType.Cavern);
                int left = map.WrapTileX(mixSpawnTile.X + 300), top = cavern.StartY + 30;
                Fill(left, top, 44, 24, TileType.Stone, TileType.Stone);
                Fill(left + 2, top + 2, 40, 20, TileType.Empty, TileType.Stone);
                Fill(left + 22, top + 14, 2, 8, TileType.Stone, TileType.Stone);
                if (mc.Torches > 0) AddTorch(left + 12, top + 21);
                mixPlayer = new Vector2((left + 16) * size, (top + 17) * size);
                mixCenter = new Vector2((left + 22) * size, (top + 12) * size);
                wall = new Point(left + 16, top + 14);
                dark = new Point(left + 28, top + 19); // behind the pillar
                break;
            }
            default:
            {
                // Shallow chamber near the top of the layer with three 2 x 2 background openings and optional torches.
                var shallow = session.LayerDefinitions.First(l => l.LayerType == WorldLayerType.ShallowUnderground);
                int left = map.WrapTileX(mixSpawnTile.X + 440), top = shallow.StartY + 4;
                Fill(left, top, 48, 22, TileType.Stone, TileType.Stone);
                Fill(left + 2, top + 2, 44, 18, TileType.Empty, TileType.Stone);
                foreach (int x in new[] { 8, 22, 36 }) Fill(left + x, top + 3, 2, 2, TileType.Empty, TileType.Empty);
                if (mc.Torches > 0) foreach (int x in new[] { 14, 24, 32 }) AddTorch(left + x, top + 19);
                mixPlayer = new Vector2((left + 21) * size, (top + 15) * size);
                mixCenter = new Vector2((left + 24) * size, (top + 11) * size);
                wall = new Point(left + 18, top + 12);
                dark = new Point(left + 44, top + 17); // corner far from openings and torches
                break;
            }
        }

        session.TorchRuntimeSystem.Restore(torches);
        session.DoorRuntimeSystem.Restore(doors);
        MixCheck(mc, null, $"torches placed {session.TorchRuntimeSystem.Torches.Count} (expected {mc.Torches})",
            session.TorchRuntimeSystem.Torches.Count == mc.Torches);
        if (mc.Scene == MixScene.House)
            MixCheck(mc, null, $"one door, open = {mc.DoorOpen}",
                session.DoorRuntimeSystem.Doors.Count == 1 && session.DoorRuntimeSystem.Doors[0].IsOpen == mc.DoorOpen);
        mixSummary.Add($"{mc.Name}: layer {LayerName(wall.Y)}; wall sample world px {wall.X * size + 4},{wall.Y * size + 4}; " +
            $"dark sample world px {dark.X * size + 4},{dark.Y * size + 4}; player world px {mixPlayer.X},{mixPlayer.Y}");

        void Fill(int x0, int y0, int w, int h, TileType fg, TileType bg)
        {
            session.SandSystem.RemoveSandInRectangle(x0 * size, y0 * size, w * size, h * size);
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    map.SetTile(map.WrapTileX(x), y, fg);
                    map.SetBackgroundTile(map.WrapTileX(x), y, bg);
                }
        }

        void AddTorch(int x, int y)
        {
            var torch = new TorchInstance(new Point(map.WrapTileX(x), y), 0, size);
            torches.Add(new TorchSaveData { PositionX = torch.Position.X, PositionY = torch.Position.Y, PoleFrameIndex = 0 });
        }
    }

    private void CaptureMixVariant()
    {
        var mc = mixCases[mixCase];
        var variant = MixVariants[mixVariant];
        var pp = graphicsDevice.PresentationParameters;
        var final = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
        graphicsDevice.GetBackBufferData(final);
        for (int i = 0; i < final.Length; i++) final[i].A = 255;
        using (var texture = new Texture2D(graphicsDevice, pp.BackBufferWidth, pp.BackBufferHeight))
        {
            texture.SetData(final);
            V8Capture.Save(texture, Path.Combine(V8GameplayOptions.Output, $"{MixCaptureLabel}_frame.png"));
        }

        var view = session.Camera.GetViewMatrix();
        var sky = session.EnvironmentSystem.SkyState;
        string state = $"time {session.TimeOfDay01:0.0000}; AmbientLight {sky.AmbientLight}; view offset {view.M41:0.##},{view.M42:0.##}; zoom {session.Camera.Zoom}";
        if (variant.V8)
        {
            int torchSources = v8Sources.Count(s => s.Radius > 0);
            MixCheck(mc, variant, $"V8 active, composition {v8Renderer.Composition}; sources {v8Sources.Count} (torches {torchSources})",
                ActiveLightingLabel == "V8" && v8Renderer.Composition == variant.Composition && torchSources == mc.Torches);
        }
        else
        {
            V8Capture.Save(session.ViewCoordinator.GetPixelLightBuffer(), Path.Combine(V8GameplayOptions.Output, $"{MixCaptureLabel}_v7light.png"));
            MixCheck(mc, variant, $"V7 active; sun {(LightingV7Config.SunEnabled ? "on" : "off")}; tissue {(LightingV7Config.TissueEmissiveEnabled ? "on" : "off")}; " +
                $"point sources {v7Lighting.SourceCount}", ActiveLightingLabel == "V7");
        }
        mixSummary.Add($"{mc.Name}/{variant.Name}: {state}");
    }

    private void FinishCompositionCompare()
    {
        mixFinished = true;
        var header = new[]
        {
            "Composition compare (diagnostic). Seed=V8-GAMEPLAY-2026, Small; transient session, nothing saved. Noon, zoom 3, same camera per case.",
            "Variants: V7 game default (sun on, tissue on; torches and mushrooms as point sources) | V8 sum (shipped) | " +
                "V8 A: direct accumulated as C + F(1 - C) | V8 A+B: bounded composition T = D + A(1 - D), L = S + T(1 - S).",
            "Both pipelines keep their own mushroom sources (same world content). V7 ignores doors for light (its own behaviour)."
        };
        File.WriteAllLines(Path.Combine(V8GameplayOptions.Output, "mix-summary.txt"), header.Concat(mixSummary));
        File.WriteAllLines(Path.Combine(V8GameplayOptions.Output, "mix-checks.txt"), mixChecks);
        if (mixChecks.Any(c => c.StartsWith("FAIL")))
            Environment.ExitCode = 1;
        V8GameplayOptions.CaptureFinished = true;
    }

    private void MixCheck(MixCase mc, MixVariant variant, string label, bool passed)
    {
        string line = $"{(passed ? "PASS" : "FAIL")} {mc.Name}{(variant != null ? "/" + variant.Name : "")}: {label}";
        mixChecks.Add(line);
        Console.WriteLine("[Composition compare] " + line);
    }
}
