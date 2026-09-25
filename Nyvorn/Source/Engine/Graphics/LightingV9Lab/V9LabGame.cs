using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Microsoft.Xna.Framework.Input;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Lab;

/// <summary>Opt-in, process-local Game. No playing session or persistence service is constructed.</summary>
public sealed partial class V9LabGame : Microsoft.Xna.Framework.Game
{
    private readonly string output;
    private readonly bool automatic, benchmark, sourceCacheEnabled, occupancyMaskEnabled, verifySourceCache;
    private SpriteBatch batch;
    private SpriteFont font;
    private Effect receiver;
    private Texture2D pixel;
    private V9LabScene scene;
    private V9LightField field;
    private RenderTarget2D final, albedo, entityLit, entityAlbedo;
    private readonly List<Vector2> torches = new();
    private readonly List<V9Light> lights = new();
    private KeyboardState previousKeys;
    private MouseState previousMouse;
    private Vector2 camera;
    private float zoom = 2.5f, verticalVelocity;
    private bool dirty = true, sky = true, showAlbedo, captureRequested;
    private int captureIndex, benchIndex = -1, benchFrame;
    private long lastFrameStart;
    private int persistentLabTexturesAndTargets;
    private readonly List<BenchSample> benchSamples = new();
    private readonly List<object> benchResults = new();
    private static readonly Vector2 FirstTorch = new(244, 164);
    private static readonly string[] Cases = { "base", "sky-open", "sky-closed", "torch-only", "two", "five", "five-reversed", "far", "near-open", "near-closed", "source-moved", "blocker-removed", "blocker-restored", "pan", "zoom2", "zoom3", "player-moved", "all-off", "sealed-five" };
    private static readonly string[] BenchCases = { "one-static", "one-camera", "five-static", "five-camera", "one-source-moving", "five-source-moving" };
    private readonly record struct BenchSample(double CpuMs, double IntervalMs, double FieldMs, bool Focused,
        double EvaluateMs, double RecomposeMs, double ConvertUploadMs, int RecalculatedSources,
        double MaskUpdateMs, bool MaskRebuilt);

    public V9LabGame(string[] args)
    {
        automatic = Array.Exists(args, a => a == "--v9-capture");
        benchmark = Array.Exists(args, a => a == "--v9-bench");
        // Approved default of this lab only (bit-exact against the reference). The original full evaluation
        // stays available explicitly; gameplay is untouched. --v9-source-cache remains accepted, now redundant.
        bool noSourceCache = Array.Exists(args, a => a == "--v9-no-source-cache");
        if (noSourceCache && Array.Exists(args, a => a == "--v9-source-cache")) throw new ArgumentException("--v9-source-cache and --v9-no-source-cache contradict each other.");
        sourceCacheEnabled = !noSourceCache;
        // Also approved as this lab's default, on top of the cache only. --v9-no-occupancy-mask keeps cache without mask;
        // --v9-occupancy-mask remains accepted, now redundant.
        bool explicitMask = Array.Exists(args, a => a == "--v9-occupancy-mask"), noMask = Array.Exists(args, a => a == "--v9-no-occupancy-mask");
        if (explicitMask && noMask) throw new ArgumentException("--v9-occupancy-mask and --v9-no-occupancy-mask contradict each other.");
        if (explicitMask && noSourceCache) throw new ArgumentException("--v9-occupancy-mask is a variant of the source-cache path; do not combine it with --v9-no-source-cache.");
        occupancyMaskEnabled = sourceCacheEnabled && !noMask;
        verifySourceCache = Array.Exists(args, a => a == "--v9-cache-verify");
        if (verifySourceCache && (automatic || benchmark)) throw new ArgumentException("Run --v9-cache-verify separately from capture/benchmark; it compares both paths with readback.");
        output = Path.GetFullPath("screenshots/v9-lab");
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--v9-output") output = Path.GetFullPath(args[i + 1]);
        Directory.CreateDirectory(output);
        var graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1200, PreferredBackBufferHeight = 680,
            HardwareModeSwitch = false, SynchronizeWithVerticalRetrace = !benchmark
        };
        if (benchmark) { IsFixedTimeStep = false; InactiveSleepTime = TimeSpan.Zero; }
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Nyvorn V9-Lab | A/D move, Space jump | arrows camera | click torch | O opening | B blocker | 0/1/2/5 lights | +/- zoom | C albedo | F12 capture";
    }

    protected override void LoadContent()
    {
        try
        {
            File.WriteAllText(Path.Combine(output, "checks.txt"), V9LabChecks.Run());
            batch = new SpriteBatch(GraphicsDevice);
            font = Content.Load<SpriteFont>("ui/UIFont");
            receiver = Content.Load<Effect>("effects/V9LabReceiver");
            scene = new V9LabScene(Content);
            field = new V9LightField(GraphicsDevice, sourceCacheEnabled, occupancyMaskEnabled);
            pixel = new Texture2D(GraphicsDevice, 1, 1); pixel.SetData(new[] { Color.White });
            int w = GraphicsDevice.PresentationParameters.BackBufferWidth, h = GraphicsDevice.PresentationParameters.BackBufferHeight;
            final = Target(w, h); albedo = Target(w, h); entityLit = Target(w, h); entityAlbedo = Target(w, h);
            persistentLabTexturesAndTargets = 6; // Inventory, not an allocation counter. Excludes content, SpriteBatch buffers and capture-only temporary textures.
            // Readback >1 explicitly checks that sampling input does not lose HDR at storage.
            using (var probe = new Texture2D(GraphicsDevice, 1, 1, false, SurfaceFormat.HalfVector4))
            {
                probe.SetData(new[] { new HalfVector4(4, .5f, .125f, 1) });
                var read = new HalfVector4[1]; probe.GetData(read);
                if (Math.Abs(read[0].ToVector4().X - 4) > .01) throw new InvalidOperationException("HalfVector4 HDR storage probe failed.");
            }
            File.WriteAllText(Path.Combine(output, "environment.txt"),
                $"UTC={DateTime.UtcNow:O}\nMonoGame={typeof(GraphicsDevice).Assembly.GetName().Version}\nBackend=DesktopGL\nProfile={GraphicsDevice.GraphicsProfile}\nAdapter={GraphicsDevice.Adapter.Description}\nBackbuffer={w}x{h} {GraphicsDevice.PresentationParameters.BackBufferFormat}\nIrradiance=HalfVector4 sample-only, storage probe 4.0 passed\nOutput=Color (encoded sRGB by shader)\nVSync={!benchmark}; fixedStep={IsFixedTimeStep}\nFocusAtLoad={IsActive}\nNo session/save/config constructed.\n");
            ConfigureCase("base");
            File.AppendAllText(Path.Combine(output, "environment.txt"), $"SourceCache={sourceCacheEnabled}; OccupancyMask={occupancyMaskEnabled}; CacheVerification={verifySourceCache}\n");
            if (benchmark && !automatic) StartBench(0);
        }
        catch (Exception e) { RecordFailure(e); throw; }
    }

    private RenderTarget2D Target(int w, int h) => new(GraphicsDevice, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

    private void ResetScene()
    {
        field?.InvalidateSourceCache();
        scene.SetOpening(true); scene.SetBlocker(true); scene.PlayerFeet = new(214, 222);
        torches.Clear(); torches.Add(FirstTorch); camera = Vector2.Zero; zoom = 2.5f; sky = true; dirty = true;
        scene.Animator.Update(0, Vector2.Zero, 0, true, false);
    }
    private void SetTorchCount(int count)
    {
        Vector2 origin = torches.Count > 0 ? torches[0] : FirstTorch;
        torches.Clear();
        for (int i = 0; i < count; i++) torches.Add(origin + new Vector2((i % 3) * 9, (i / 3) * 10));
        dirty = true;
    }
    private void ConfigureCase(string name)
    {
        ResetScene();
        if (name.StartsWith("sky-") || name == "all-off") torches.Clear();
        if (name is "sky-closed" or "near-closed" or "sealed-five" or "all-off") scene.SetOpening(false);
        if (name is "torch-only" or "all-off") sky = false;
        if (name == "two") SetTorchCount(2);
        if (name is "five" or "five-reversed" or "sealed-five") SetTorchCount(5);
        if (name == "five-reversed") torches.Reverse();
        if (name == "far") torches.Add(new Vector2(416, 136));
        if (name.StartsWith("near-")) { torches[0] = new(118, 103); scene.PlayerFeet = new(137, 129); }
        if (name == "source-moved") torches[0] = new(342, 150);
        if (name == "blocker-removed") scene.SetBlocker(false);
        if (name == "pan") camera = new(17.2f, -8.4f);
        if (name == "zoom2") zoom = 2;
        if (name == "zoom3") { zoom = 3; camera = new(40, 12); }
        if (name == "player-moved") scene.PlayerFeet = new(274, 222);
        RebuildLights();
    }
    private void RebuildLights()
    {
        lights.Clear();
        if (sky)
            for (int i = 0; i < 5; i++) lights.Add(new V9Light(new Vector2(86 + i * 12, 28), V9LabSettings.SkyLinearRgb, V9LabSettings.SkySamplePower, V9LabSettings.SkyRadiusPixels));
        foreach (var p in torches) lights.Add(new V9Light(p, V9LabSettings.FireLinearRgb, V9LabSettings.TorchPower, V9LabSettings.TorchRadiusPixels));
    }
    private Matrix View => Matrix.CreateTranslation(-camera.X, -camera.Y, 0) * Matrix.CreateScale(zoom, zoom, 1);

    protected override void Update(GameTime gameTime)
    {
        if (scene == null) return;
        var keys = Keyboard.GetState(); var mouse = Mouse.GetState();
        if (keys.IsKeyDown(Keys.Escape)) Exit();
        if (!automatic && !verifySourceCache && benchIndex < 0)
        {
            float dt = Math.Min(.04f, (float)gameTime.ElapsedGameTime.TotalSeconds);
            if (Pressed(Keys.O)) { scene.SetOpening(!scene.OpeningOpen); dirty = true; }
            if (Pressed(Keys.B)) { scene.SetBlocker(!scene.BlockerPresent); dirty = true; }
            if (Pressed(Keys.D0)) SetTorchCount(0);
            if (Pressed(Keys.D1)) SetTorchCount(1);
            if (Pressed(Keys.D2)) SetTorchCount(2);
            if (Pressed(Keys.D5)) SetTorchCount(5);
            if (Pressed(Keys.R)) ResetScene();
            if (Pressed(Keys.C)) showAlbedo = !showAlbedo;
            if (Pressed(Keys.F12)) captureRequested = true;
            if (Pressed(Keys.OemPlus) || Pressed(Keys.Add)) zoom = Math.Min(4, zoom + .5f);
            if (Pressed(Keys.OemMinus) || Pressed(Keys.Subtract)) zoom = Math.Max(1.5f, zoom - .5f);
            camera += new Vector2((keys.IsKeyDown(Keys.Right) ? 1 : 0) - (keys.IsKeyDown(Keys.Left) ? 1 : 0),
                (keys.IsKeyDown(Keys.Down) ? 1 : 0) - (keys.IsKeyDown(Keys.Up) ? 1 : 0)) * 85 * dt;
            if (mouse.LeftButton == ButtonState.Pressed && previousMouse.LeftButton != ButtonState.Pressed && IsActive)
            {
                Vector2 p = Vector2.Transform(new Vector2(mouse.X, mouse.Y), Matrix.Invert(View));
                if (!scene.Solid((int)MathF.Floor(p.X / 8), (int)MathF.Floor(p.Y / 8)))
                {
                    if (torches.Count == 0) torches.Add(p); else torches[0] = p;
                    dirty = true;
                }
            }
            int direction = (keys.IsKeyDown(Keys.D) ? 1 : 0) - (keys.IsKeyDown(Keys.A) ? 1 : 0);
            bool grounded = Collides(scene.PlayerFeet + new Vector2(0, 1));
            if (Pressed(Keys.Space) && grounded) verticalVelocity = -130;
            verticalVelocity = Math.Min(180, verticalVelocity + 360 * dt);
            MovePlayer(new Vector2(direction * 62 * dt, 0));
            if (!MovePlayer(new Vector2(0, verticalVelocity * dt))) verticalVelocity = 0;
            scene.Animator.Update(dt, new Vector2(direction * 62, verticalVelocity), direction, grounded, false);
        }
        previousKeys = keys; previousMouse = mouse;
        bool Pressed(Keys key) => keys.IsKeyDown(key) && !previousKeys.IsKeyDown(key);
    }
    private bool MovePlayer(Vector2 delta)
    {
        // Small diagnostic collision controller, independent of session/inventory/combat.
        int steps = Math.Max(1, (int)MathF.Ceiling(delta.Length()));
        for (int i = 0; i < steps; i++)
        {
            Vector2 next = scene.PlayerFeet + delta / steps;
            if (Collides(next)) return false;
            scene.PlayerFeet = next;
        }
        return true;
    }
    private bool Collides(Vector2 feet)
    {
        for (int y = (int)MathF.Floor((feet.Y - 24) / 8); y <= (int)MathF.Floor((feet.Y - .1f) / 8); y++)
            for (int x = (int)MathF.Floor((feet.X - 5) / 8); x <= (int)MathF.Floor((feet.X + 5) / 8); x++)
                if (scene.Solid(x, y)) return true;
        return false;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (scene == null) return;
        try
        {
            if (verifySourceCache) { RunSourceCacheVerification(); Exit(); return; }
            long start = Stopwatch.GetTimestamp();
            double interval = lastFrameStart == 0 ? 0 : Stopwatch.GetElapsedTime(lastFrameStart, start).TotalMilliseconds;
            lastFrameStart = start;
            if (automatic && benchIndex < 0) ConfigureCase(Cases[captureIndex]);
            if (benchIndex >= 0) AdvanceBenchScene();
            double buildMs = 0;
            if (dirty) { RebuildLights(); field.Rebuild(scene, lights); dirty = false; buildMs = field.LastBuildMilliseconds; }
            Render(final, true, false);
            if (showAlbedo || captureRequested || (automatic && benchIndex < 0)) Render(albedo, false, false);
            GraphicsDevice.SetRenderTarget(null); GraphicsDevice.Clear(Color.Black);
            batch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Opaque);
            batch.Draw(showAlbedo ? albedo : final, Vector2.Zero, Color.White); batch.End();
            if (!automatic && benchIndex < 0)
            {
                batch.Begin(samplerState: SamplerState.PointClamp);
                batch.Draw(pixel, new Rectangle(0, 0, 1200, 23), new Color(0, 0, 0, 210));
                batch.DrawString(font, $"V9 LAB  |  {torches.Count} torch  |  opening {(scene.OpeningOpen ? "open" : "closed")}  |  zoom {zoom:0.0}  |  O/B: geometry  C: raw  F12: capture  R: reset", new Vector2(10, 4), Color.White, 0, Vector2.Zero, .45f, SpriteEffects.None, 0);
                batch.End();
            }
            double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (benchIndex >= 0) FinishBenchFrame(cpuMs, interval, buildMs);
            else if (captureRequested || automatic)
            {
                Render(entityLit, true, true); Render(entityAlbedo, false, true);
                GraphicsDevice.SetRenderTarget(null);
                string name = automatic ? Cases[captureIndex] : "manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                V9LabEvidence.Capture(output, name, final, albedo, entityLit, entityAlbedo, field, View, scene, lights, zoom, camera);
                captureRequested = false;
                if (automatic && ++captureIndex == Cases.Length)
                {
                    if (benchmark) StartBench(0); else Exit();
                }
            }
        }
        catch (Exception e) { RecordFailure(e); throw; }
    }
    private void Render(RenderTarget2D target, bool lit, bool playerOnly)
    {
        GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(playerOnly ? Color.Transparent : Color.Black);
        Matrix view = View;
        if (!playerOnly)
        {
            // A visible exterior backdrop is a separate distant plane, not illumination inside the cave.
            batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: view);
            batch.Draw(pixel, new Rectangle(0, 0, V9LabScene.Width, scene.OpeningBounds.Top), new Color(97, 145, 179));
            scene.DrawExterior(batch);
            batch.End();
            Begin(lit, view, V9LabSettings.BackgroundResponse); scene.DrawBackground(batch); batch.End();
            Begin(lit, view, 1); scene.DrawTerrain(batch); scene.DrawTorches(batch, torches, false); batch.End();
        }
        Begin(lit, view, 1); scene.DrawPlayer(batch); batch.End();
        if (!playerOnly)
        {
            // Only original flame pixels are emissive. No halo, bloom or player boost.
            batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: view);
            scene.DrawTorches(batch, torches, true); batch.End();
        }
    }
    private void Begin(bool lit, Matrix view, float response)
    {
        if (lit)
        {
            receiver.Parameters["MatrixTransform"].SetValue(view * Matrix.CreateOrthographicOffCenter(0, final.Width, final.Height, 0, 0, 1));
            receiver.Parameters["WorldSize"].SetValue(new Vector2(V9LabScene.Width, V9LabScene.Height));
            receiver.Parameters["Response"].SetValue(response);
            receiver.Parameters["Irradiance"].SetValue(field.Texture);
        }
        batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp, depthStencilState: DepthStencilState.None,
            rasterizerState: RasterizerState.CullNone, effect: lit ? receiver : null, transformMatrix: view);
    }

    private void StartBench(int index)
    {
        benchIndex = index; benchFrame = 0; benchSamples.Clear(); ResetScene();
        SetTorchCount(BenchCases[index].StartsWith("five") ? 5 : 1);
    }
    private void AdvanceBenchScene()
    {
        string name = BenchCases[benchIndex];
        if (name.EndsWith("camera")) camera = new Vector2(MathF.Sin(benchFrame * .07f) * 20, MathF.Cos(benchFrame * .07f) * 8);
        if (name.EndsWith("source-moving"))
        {
            torches[0] = FirstTorch + new Vector2(MathF.Sin(benchFrame * .07f) * 12, MathF.Cos(benchFrame * .07f) * 6); dirty = true;
        }
    }
    private void FinishBenchFrame(double cpuMs, double intervalMs, double buildMs)
    {
        const int warmup = 30, samples = 120;
        if (benchFrame >= warmup) benchSamples.Add(new(cpuMs, intervalMs, buildMs, IsActive,
            buildMs > 0 ? field.LastEvaluateMilliseconds : 0, buildMs > 0 ? field.LastRecomposeMilliseconds : 0,
            buildMs > 0 ? field.LastConvertUploadMilliseconds : 0, buildMs > 0 ? field.LastRecalculatedSources : 0,
            buildMs > 0 ? field.LastMaskUpdateMilliseconds : 0, buildMs > 0 && field.LastMaskRebuilt));
        if (++benchFrame < warmup + samples) return;
        benchResults.Add(new { Case = BenchCases[benchIndex], Warmup = warmup, Samples = samples, Width = final.Width, Height = final.Height,
            Zoom = zoom, TorchCount = torches.Count, SkySamples = 5, FocusedSamples = benchSamples.Count(s => s.Focused),
            CpuDrawSubmissionMs = Stats(benchSamples.Select(s => s.CpuMs)), FrameStartIntervalMs = Stats(benchSamples.Select(s => s.IntervalMs)),
            CpuFieldAndUploadMs = Stats(benchSamples.Select(s => s.FieldMs)), PersistentLabTexturesAndTargets = persistentLabTexturesAndTargets,
            SourceCache = sourceCacheEnabled, SourceCachePayloadBytes = field.SourceCachePayloadBytes,
            CpuEvaluateVisibilityMs = Stats(benchSamples.Select(s => s.EvaluateMs)),
            CpuRecomposeMs = Stats(benchSamples.Select(s => s.RecomposeMs)),
            CpuConvertUploadMs = Stats(benchSamples.Select(s => s.ConvertUploadMs)),
            RecalculatedSources = Stats(benchSamples.Select(s => (double)s.RecalculatedSources)),
            OccupancyMask = occupancyMaskEnabled, OccupancyMaskBytes = field.OccupancyMaskBytes,
            CpuMaskUpdateMs = Stats(benchSamples.Select(s => s.MaskUpdateMs)), MaskRebuildsInSamples = benchSamples.Count(s => s.MaskRebuilt),
            MaskBuildsInProcess = field.OccupancyMask?.Builds ?? 0, MaskFirstBuildMs = field.OccupancyMask?.FirstBuildMilliseconds ?? 0,
            Note = "CPU timing only. Frame interval includes update, driver/present scheduling; not GPU time. Readback/capture excluded." });
        if (++benchIndex < BenchCases.Length) StartBench(benchIndex);
        else
        {
            File.WriteAllText(Path.Combine(output, "benchmark.json"), JsonSerializer.Serialize(benchResults, new JsonSerializerOptions { WriteIndented = true }));
            Exit();
        }
    }
    private static object Stats(IEnumerable<double> values)
    {
        double[] a = values.OrderBy(v => v).ToArray();
        return new { Mean = a.Average(), Median = a[a.Length / 2], P95 = a[(int)((a.Length - 1) * .95)], Min = a[0], Max = a[^1] };
    }
    private void RecordFailure(Exception e) => File.WriteAllText(Path.Combine(output, "failure.txt"), e.ToString());
    protected override void UnloadContent()
    {
        GraphicsDevice.SetRenderTarget(null);
        field?.Dispose(); pixel?.Dispose(); final?.Dispose(); albedo?.Dispose(); entityLit?.Dispose(); entityAlbedo?.Dispose(); batch?.Dispose();
        base.UnloadContent();
    }
}
