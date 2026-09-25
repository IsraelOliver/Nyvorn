using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Graphics.LightingV8;
using Nyvorn.Source.World.Decorations;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.Game.States;

public partial class PlayingState
{
    private V8LightingRenderer v8Renderer;
    private RenderTarget2D v8Final, v8Albedo;
    private readonly List<V8Light> v8Sources = new();
    private bool v8CaptureRequested;
    private double v8CpuMs;

    // --lighting-v8 only selects the initial pipeline; F4 can activate V8 later (PlayingState.LightingToggle).
    private void InitializeV8()
    {
        if (V8GameplayOptions.Enabled) SetLightingPipeline(true);
        if (V8GameplayOptions.CaptureWorld) InitializeV8WorldCapture();
        if (V8GameplayOptions.BackgroundProbe) InitializeV8BackgroundProbe();
        if (V8GameplayOptions.ToggleBench) InitializeLightingToggleBench();
        if (V8GameplayOptions.PerfBench) InitializeV8PerfBench();
        if (V8GameplayOptions.SkyCompare) InitializeSkyCompare();
        if (V8GameplayOptions.CompositionCompare) InitializeCompositionCompare();
    }

    // Created on first activation and kept across toggles until OnExit; never recreated by switching.
    private void EnsureV8Renderer()
    {
        if (v8Renderer != null) return;
        long start = Stopwatch.GetTimestamp();
        Directory.CreateDirectory(V8GameplayOptions.Output);
        using (var probeBatch = new SpriteBatch(graphicsDevice))
            V8GraphicsProbe.Run(graphicsDevice, probeBatch, V8GameplayOptions.Output);
        v8Renderer = new V8LightingRenderer(graphicsDevice, content) {
            Sand = session.SandSystem,
            GeometryRegionStep = 64,
            Composition = V8GameplayOptions.Composition, // Sum unless --v8-composition is given (diagnostic)
            // V6 reference: by day exterior receivers take AmbientLight x 1; the background wall keeps SkyIntensity.
            AmbientContext = new V8AmbientContext { Layers = session.LayerDefinitions, ReceiverDaySkyIntensity = 1f,
                // Background visual range approved at 12 tiles; --v8-bg-range overrides it for diagnostics.
                BackgroundSkyRangeTiles = V8GameplayOptions.BackgroundSkyRange ?? 12f,
                SkyDiagonalTransport = !V8GameplayOptions.SkyFourNeighbours }
        };
        v8InitMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Console.WriteLine($"[V8] renderer created once in {v8InitMs:0.0} ms: direct + sky + local; V6/V7 bypassed while V8 is active");
    }

    private void UpdateV8Input()
    {
        if (!v8Active || consoleOpen) return;
        var keys = Keyboard.GetState();
        if (keys.IsKeyDown(Keys.F12) && !previousConsoleKeyboard.IsKeyDown(Keys.F12)) v8CaptureRequested = true;
    }

    private void CollectV8Sources(Rectangle view)
    {
        v8Sources.Clear();
        // Shipped direct radius: 14 tiles (112 px), approved on 2026-09-16. --v8-torch-radius overrides it for diagnostics
        // and touches only the direct term: the falloff is (1 - d/R)^2, so R is both the support and the curve's shape.
        float torchRadius = (V8GameplayOptions.TorchDirectRadiusTiles ?? 14f) * session.WorldMap.TileSize;
        // Local fill: shipped radius 18 tiles (144 px), approved on 2026-09-23. --v8-torch-local-radius changes only the
        // radius. The loss per tile is tileSize / AmbientRadius, so the radius is also the slope of the fill's falloff.
        float torchLocalRadius = (V8GameplayOptions.TorchLocalRadiusTiles ?? 18f) * session.WorldMap.TileSize;
        // Local intensity: the V8Light default unless --v8-torch-local-intensity is given (diagnostic only).
        float? torchLocalIntensity = V8GameplayOptions.TorchLocalIntensity;
        foreach (var torch in session.TorchRuntimeSystem.Torches)
        {
            var light = new V8Light(torch.LightOrigin, new Vector3(1f, .65f, .28f), torchRadius, AmbientRadius: torchLocalRadius);
            if (torchLocalIntensity is float intensity) light = light with { AmbientIntensity = intensity };
            Add(light);
        }
        // Perf bench only: exact torch counts. Gameplay always includes decoration sources.
        if (!v8SuppressDecorationSources)
        foreach (var decoration in session.WorldMap.SurfaceDecorations)
            if (decoration.Type == SurfaceDecorationType.Mushroom)
                Add(new V8Light(decoration.Tile.ToVector2() * session.WorldMap.TileSize + new Vector2(4),
                    new Vector3(.2f, .8f, 1), 0, .12f, 128));
        void Add(V8Light light)
        {
            // Choose a nearby image for culling only; renderer keeps canonical fractional positions.
            float x = light.Position.X + MathF.Round((view.Center.X - light.Position.X) / session.WorldMap.PixelWidth) * session.WorldMap.PixelWidth;
            float reach = MathF.Max(light.Radius, light.AmbientRadius) + v8Renderer.FaceReach + 2;
            if (x + reach >= view.Left && x - reach <= view.Right &&
                light.Position.Y + reach >= view.Top && light.Position.Y - reach <= view.Bottom) v8Sources.Add(light);
        }
    }

    private void DrawV8Gameplay(SpriteBatch batch, int width, int height, float frameSeconds)
    {
        if (V8GameplayOptions.CaptureWorld) PrepareV8WorldCapture(width, height);
        if (V8GameplayOptions.BackgroundProbe) PrepareV8BackgroundProbe(width, height);
        if (V8GameplayOptions.PerfBench) PrepareV8PerfBench(width, height, frameSeconds);
        Matrix view = session.Camera.GetViewMatrix();
        Rectangle visible = V8LightingRenderer.VisibleBounds(view, width, height);
        var loops = GetVisibleLoopOffsets(width, session.WorldMap.PixelWidth);
        foreach (int loop in loops) session.PrepareTerrainRender(graphicsDevice, width, height, loop * session.WorldMap.PixelWidth);
        long start = Stopwatch.GetTimestamp();
        CollectV8Sources(visible);
        v8RenderFrames++;
        v8Renderer.AmbientContext.SkyState = session.EnvironmentSystem.SkyState;
        v8Renderer.AmbientContext.NightStrength = session.WorldNightStrength;
        v8Renderer.Render(session.WorldMap, session.PlatformRuntimeSystem.Platforms,
            session.DoorRuntimeSystem.Doors, v8Sources, visible);
        v8CpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds; // CPU work + driver submission, NOT GPU duration
        EnsureV8FrameTargets(width, height);
        graphicsDevice.SetRenderTarget(v8Final);
        graphicsDevice.Clear(Color.Black);
        // Distant scenery belongs to presentation, not local receivers. No sun glow or weather overlay.
        batch.Begin(samplerState: SamplerState.PointClamp);
        session.DrawSky(batch, width, height);
        if (session.GetPlayerWorldLayer() is WorldLayerType.Surface or WorldLayerType.ShallowUnderground)
            session.DrawParallaxMountains(batch, width, height);
        batch.End();
        DrawSubterraneanParallaxLayers(batch, width, height);
        DrawV8Receivers(batch, width, height, loops, false);
        batch.Begin(samplerState: SamplerState.PointClamp);
        session.DrawHud(batch, width, height);
        if (minimapVisible) session.DrawMinimap(batch, width, height, minimapTissueMode);
        playerHubUI.Draw(batch, session.WorkbenchRuntimeSystem.GetNearbyCraftTier() | session.FurnaceRuntimeSystem.GetNearbyCraftTier());
        if (consoleOpen) DrawConsole(batch, width);
        batch.DrawString(consoleFont, $"LIGHTING: V8 direct + ambient | F4: switch to {InactiveLightingLabel} | F12 capture | {v8Sources.Count} sources | zoom {session.Camera.Zoom:0.##}", new Vector2(10, 48), Color.Yellow);
        batch.DrawString(consoleFont, $"V8 CPU + submission {v8CpuMs:0.00} ms (geometry {v8Renderer.LastGeometryMs:0.00} / direct {v8Renderer.LastDirectMs:0.00} / ambient {v8Renderer.LastAmbientMs:0.00} [grid {v8Renderer.Ambient.LastFieldMs:0.00} recon {v8Renderer.Ambient.LastReconstructMs:0.00} upload {v8Renderer.Ambient.LastUploadMs:0.00}] / faces {v8Renderer.LastFacesMs:0.00}) | frame {frameSeconds * 1000:0.00} ms | {V8WorldCaptureLabel}", new Vector2(10, 72), Color.Cyan);
        DrawLightingStatsHud(batch, 96);
        batch.End();
        graphicsDevice.SetRenderTarget(null);
        bool capture = v8CaptureRequested || ShouldCaptureV8WorldFrame || ShouldCaptureV8BackgroundProbe || ShouldCaptureComposition;
        if (capture)
        {
            graphicsDevice.SetRenderTarget(v8Albedo); graphicsDevice.Clear(Color.Transparent);
            DrawV8Receivers(batch, width, height, loops, true);
            graphicsDevice.SetRenderTarget(null);
            SaveV8GameplayFrame(V8GameplayOptions.CompositionCompare ? MixCaptureLabel : V8GameplayOptions.CaptureWorld ? V8WorldCaptureLabel :
                V8GameplayOptions.BackgroundProbe ? V8BackgroundProbeLabel : DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), view);
            v8CaptureRequested = false;
        }
        batch.Begin(samplerState: SamplerState.PointClamp); batch.Draw(v8Final, Vector2.Zero, Color.White); batch.End();
        if (V8GameplayOptions.CaptureWorld) FinishV8WorldFrame(frameSeconds, capture);
        if (V8GameplayOptions.BackgroundProbe) FinishV8BackgroundProbe(capture);
        if (V8GameplayOptions.PerfBench) FinishV8PerfBench();
    }

    private void DrawV8Receivers(SpriteBatch batch, int width, int height, IReadOnlyList<int> loops, bool albedo)
    {
        foreach (int loop in loops)
        {
            float offset = loop * session.WorldMap.PixelWidth;
            Matrix transform = Matrix.CreateTranslation(offset, 0, 0) * session.Camera.GetViewMatrix();
            void Begin(bool foreground = false, bool background = false)
            {
                if (albedo) batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
                else v8Renderer.BeginReceivers(batch, transform, width, height, foreground, offset, background);
            }
            Begin();
            session.DrawTreeDecorations(batch, width, height, offset, TreeRenderLayer.Back, Color.White);
            batch.End();
            // The background wall keeps its own sky intensity: its own batch, same draw order.
            Begin(background: true);
            var bounds = V8LightingRenderer.VisibleBounds(transform, width, height);
            int size = session.WorldMap.TileSize;
            session.WorldMap.DrawBackground(batch, (int)MathF.Floor(bounds.Left / (float)size) - 2,
                (int)MathF.Ceiling(bounds.Right / (float)size) + 2, Math.Max(0, bounds.Top / size - 2), bounds.Bottom / size + 2, Color.White);
            batch.End();
            Begin();
            session.DrawTreeDecorations(batch, width, height, offset, TreeRenderLayer.Front, Color.White);
            session.DrawWater(batch, width, height, offset);
            batch.End();
            // Minimal emission: draw the existing flame sprite before opaque surfaces so they hide it.
            // No halo, post-lightening or per-sprite second illumination.
            batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
            session.TorchRuntimeSystem.DrawFlames(batch, session.EnvironmentSystem.SkyState.VisualTimeSeconds);
            batch.End();
            Begin(true);
            session.DrawTerrainBase(batch, width, height, offset);
            session.DoorRuntimeSystem.Draw(batch, openOnly: false);
            session.PlatformRuntimeSystem.Draw(batch);
            batch.End();
            Begin();
            session.WorkbenchRuntimeSystem.Draw(batch); session.FurnaceRuntimeSystem.Draw(batch);
            session.ChairRuntimeSystem.Draw(batch); session.TableRuntimeSystem.Draw(batch);
            session.DoorRuntimeSystem.Draw(batch, openOnly: true);
            session.DrawLoopedWorldEntities(batch, width, height, offset, neutralEntityLightSampler, neutralEntityLightSampler, drawTorchFlames: false);
            session.DrawEntities(batch, neutralEntityLightSampler);
            // Tissue gameplay remains active; its visible core is a receiver, with no halo/field darkening.
            session.DrawTissueCore(batch, width, height, offset);
            batch.End();
            batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
            session.DrawTerrainOverlay(batch);
            batch.End();
        }
    }

    private void EnsureV8FrameTargets(int width, int height)
    {
        if (v8Final != null && v8Final.Width == width && v8Final.Height == height) return;
        v8Final?.Dispose(); v8Albedo?.Dispose();
        v8Final = new RenderTarget2D(graphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        v8Albedo = new RenderTarget2D(graphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    }

    private void SaveV8GameplayFrame(string name, Matrix view)
    {
        string prefix = Path.Combine(V8GameplayOptions.Output, name);
        V8Capture.Save(v8Final, prefix + "_final.png"); V8Capture.Save(v8Albedo, prefix + "_albedo.png");
        V8Capture.Save(v8Renderer.Direct, prefix + "_direct.png"); V8Capture.Save(v8Renderer.Foreground, prefix + "_foreground.png");
        V8Capture.Save(v8Renderer.Ambient.SkyTexture, prefix + "_sky.png"); V8Capture.Save(v8Renderer.Ambient.LocalTexture, prefix + "_local.png");
        File.WriteAllText(prefix + "_frame.txt", $"Pipeline=V8; profile={graphicsDevice.GraphicsProfile}; resolution={v8Final.Width}x{v8Final.Height}; zoom={session.Camera.Zoom}; sources={v8Sources.Count}\n" +
            $"World={session.WorldMap.Width}x{session.WorldMap.Height}; player={session.Player.Position}; time={session.TimeOfDay01}; view={view}; lightBounds={v8Renderer.LightBounds}\n" +
            $"CPU+submission={v8CpuMs:F3}ms; GPU time not measured. Sky={session.EnvironmentSystem.SkyState.AmbientLight}\n" + string.Join("\n", v8Sources));
    }
    private void DisposeV8() { session.WorldMap.NeutralLightingAlbedo = false; v8Renderer?.Dispose(); v8Final?.Dispose(); v8Albedo?.Dispose(); }
}
