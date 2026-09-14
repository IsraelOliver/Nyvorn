using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

public sealed class V8DiagnosticGame : Microsoft.Xna.Framework.Game
{
    private readonly string output;
    private readonly bool probeOnly, automatic, ambientMode;
    private SpriteBatch batch;
    private V8LightingRenderer lighting;
    private V8DiagnosticScene scene;
    private readonly Camera2D camera = new();
    private RenderTarget2D albedo, final, entityAlbedo, entityLit;
    private Texture2D exteriorPixel;
    private V8Validation validation;
    private V8AmbientValidation ambientValidation;
    private KeyboardState previous;
    private int caseIndex;
    private Vector2 viewOrigin;
    private readonly string[] cases = { "primary", "off", "two", "reverse", "second", "right", "door-open", "moved", "zoom3", "wrap", "offscreen", "subpixel", "finite-radius", "platform-removed" };

    public V8DiagnosticGame(string[] args)
    {
        var graphics = new GraphicsDeviceManager(this);
        graphics.PreferredBackBufferWidth = 768;
        graphics.PreferredBackBufferHeight = 512;
        graphics.HardwareModeSwitch = false;
        Content.RootDirectory = "Content";
        Window.Title = "Nyvorn V8 — direct-only diagnostic";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        probeOnly = Array.Exists(args, a => a == "--v8-probe");
        automatic = Array.Exists(args, a => a == "--v8-capture");
        ambientMode = Array.Exists(args, a => a == "--v8-ambient");
        if (ambientMode) cases = new[] { "local", "local-moved", "local-wrap", "local-off", "sky-sealed", "sky-window",
            "sky-moved", "sky-fissure", "sky-interior-void", "sky-deep-void", "local-cavern", "local-deep", "local-deep-off",
            "combined", "corner", "door-closed", "door-open" };
        output = Path.GetFullPath("screenshots/v8");
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--v8-output") output = Path.GetFullPath(args[i + 1]);
        Directory.CreateDirectory(output);
    }
    protected override void LoadContent()
    {
        batch = new SpriteBatch(GraphicsDevice);
        try
        {
            Content.Load<Effect>("effects/V8Direct");
            string result = V8GraphicsProbe.Run(GraphicsDevice, batch, output);
            File.WriteAllText(Path.Combine(output, "probe.txt"), result);
            Console.WriteLine(result);
            if (probeOnly) { Exit(); return; }
            // Nothing is built before the stencil contract has passed on this device.
            lighting = new V8LightingRenderer(GraphicsDevice, Content);
            scene = new V8DiagnosticScene(Content);
            validation = new V8Validation(output, scene);
            if (ambientMode)
            {
                exteriorPixel = new Texture2D(GraphicsDevice, 1, 1);
                exteriorPixel.SetData(new[] { Color.White });
                ambientValidation = new V8AmbientValidation(output, scene);
                lighting.AmbientContext = scene.ConfigureAmbient("combined");
                Window.Title = "Nyvorn V8 — ambient diagnostic (F5 switches fixture)";
            }
            viewOrigin = scene.Origin;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            throw;
        }
    }
    protected override void Update(GameTime gameTime)
    {
        if (scene == null) return;
        var keys = Keyboard.GetState();
        if (keys.IsKeyDown(Keys.Escape)) Exit();
        if (!automatic)
        {
            float step = 40f * (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (keys.IsKeyDown(Keys.Left)) scene.EntityPosition -= new Vector2(step, 0);
            if (keys.IsKeyDown(Keys.Right)) scene.EntityPosition += new Vector2(step, 0);
            if (keys.IsKeyDown(Keys.Up)) scene.EntityPosition -= new Vector2(0, step);
            if (keys.IsKeyDown(Keys.Down)) scene.EntityPosition += new Vector2(0, step);
            if (keys.IsKeyDown(Keys.A)) viewOrigin -= new Vector2(step, 0);
            if (keys.IsKeyDown(Keys.D)) viewOrigin += new Vector2(step, 0);
            if (keys.IsKeyDown(Keys.W)) viewOrigin -= new Vector2(0, step);
            if (keys.IsKeyDown(Keys.S)) viewOrigin += new Vector2(0, step);
            if (Pressed(Keys.D0)) scene.Lights.Clear();
            if (Pressed(Keys.D1)) { scene.Lights.Clear(); scene.Lights.Add(scene.First); }
            if (Pressed(Keys.D2)) { scene.Lights.Clear(); scene.Lights.Add(scene.First); scene.Lights.Add(scene.Second); }
            if (Pressed(Keys.D3)) scene.Lights.Reverse();
            if (Pressed(Keys.OemPlus)) camera.Zoom = Math.Min(4, camera.Zoom + 1);
            if (Pressed(Keys.OemMinus)) camera.Zoom = Math.Max(1, camera.Zoom - 1);
            if (Pressed(Keys.F12)) caseIndex = -1;
            if (ambientMode && Pressed(Keys.F5))
            {
                caseIndex = (Math.Max(0, caseIndex) + 1) % cases.Length;
                ConfigureAmbientCase(cases[caseIndex]);
                Window.Title = "Nyvorn V8 ambient — " + cases[caseIndex];
            }
        }
        bool Pressed(Keys key) => keys.IsKeyDown(key) && !previous.IsKeyDown(key);
        previous = keys;
    }
    private void ConfigureCase(string name)
    {
        if (ambientMode) { ConfigureAmbientCase(name); return; }
        camera.Zoom = 2;
        viewOrigin = scene.Origin;
        scene.Doors.Clear(); scene.Doors.Add(scene.ClosedDoor);
        scene.Lights.Clear(); scene.Lights.Add(scene.First);
        if (name == "off") scene.Lights.Clear();
        if (name == "two" || name == "reverse") scene.Lights.Add(scene.Second);
        if (name == "reverse") scene.Lights.Reverse();
        if (name == "second") { scene.Lights.Clear(); scene.Lights.Add(scene.Second); }
        if (name == "right" || name == "door-open") { scene.Lights.Clear(); scene.Lights.Add(scene.RightRoom); }
        if (name == "door-open") scene.Doors[0] = new(scene.ClosedDoor.Tile, 8, isOpen: true);
        if (name == "moved") viewOrigin += new Vector2(17.35f, -11.2f);
        if (name == "zoom3") camera.Zoom = 3;
        if (name == "wrap") viewOrigin += new Vector2(scene.Map.PixelWidth, 0);
        if (name == "offscreen") viewOrigin += new Vector2(112, 0);
        if (name == "subpixel") scene.Lights[0] = scene.First with { Position = scene.First.Position + new Vector2(.5f, .25f) };
        if (name == "finite-radius") scene.Lights[0] = scene.First with { Radius = 32 };
        if (name == "platform-removed") scene.Platforms.Clear();
    }
    private void ConfigureAmbientCase(string name)
    {
        camera.Zoom = 2; viewOrigin = scene.Origin;
        lighting.AmbientContext = scene.ConfigureAmbient(name);
        if (name.EndsWith("moved")) viewOrigin += new Vector2(17.35f, -11.2f);
        if (name == "local-wrap") viewOrigin += new Vector2(scene.Map.PixelWidth, 0);
    }
    protected override void Draw(GameTime gameTime)
    {
        if (scene == null) return;
        try
        {
            int w = GraphicsDevice.PresentationParameters.BackBufferWidth, h = GraphicsDevice.PresentationParameters.BackBufferHeight;
            EnsureTargets(w, h);
            string name = automatic ? cases[caseIndex] : "interactive";
            if (automatic) ConfigureCase(name);
            camera.CenterOn(viewOrigin + new Vector2(w, h) / (2 * camera.Zoom), w, h);
            Matrix view = camera.GetViewMatrix();
            Rectangle visible = V8LightingRenderer.VisibleBounds(view, w, h);
            lighting.Render(scene.Map, scene.Platforms, scene.Doors, scene.Lights, visible);
            int resources = lighting.ResourceCreations;
            if (automatic)
            {
                // A repeated unchanged frame must neither create GPU resources nor rebuild geometry.
                int generation = lighting.Geometry.Generation;
                lighting.Render(scene.Map, scene.Platforms, scene.Doors, scene.Lights, visible);
                if (ambientMode) ambientValidation.Check(name + "/stable-resources", lighting.ResourceCreations == resources && generation == lighting.Geometry.Generation);
                else validation.Check(name + "/stable-resources", lighting.ResourceCreations == resources && generation == lighting.Geometry.Generation);
            }
            RenderScene(albedo, false, view, visible, w, h);
            RenderScene(final, true, view, visible, w, h);
            RenderEntity(entityAlbedo, false, view, visible, w, h);
            RenderEntity(entityLit, true, view, visible, w, h);
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);
            batch.Begin(samplerState: SamplerState.PointClamp);
            batch.Draw(final, Vector2.Zero, Color.White);
            batch.End();
            if (automatic || caseIndex == -1)
            {
                Save(name, view, visible);
                if (automatic)
                {
                    if (ambientMode) ambientValidation.Observe(name, lighting, albedo, final, entityAlbedo, entityLit, view);
                    else validation.Observe(name, lighting, albedo, final, entityAlbedo, entityLit, view);
                    if (++caseIndex == cases.Length)
                    {
                        if (ambientMode) ambientValidation.Finish(); else validation.Finish();
                        Console.WriteLine("V8 captures and checks: " + output);
                        Exit();
                    }
                }
                else caseIndex = 0;
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            throw;
        }
    }
    private void RenderScene(RenderTarget2D target, bool lit, Matrix view, Rectangle visible, int w, int h)
    {
        GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(Color.Transparent);
        if (ambientMode)
        {
            batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: view);
            scene.DrawExterior(batch, visible, lighting.AmbientContext, exteriorPixel);
            batch.End();
        }
        Begin(lit, false, view, w, h); scene.DrawBackground(batch, visible); batch.End();
        Begin(lit, true, view, w, h); scene.DrawForeground(batch, visible); batch.End();
        Begin(lit, false, view, w, h); scene.DrawEntities(batch, visible); batch.End();
        GraphicsDevice.SetRenderTarget(null);
    }
    private void RenderEntity(RenderTarget2D target, bool lit, Matrix view, Rectangle visible, int w, int h)
    {
        GraphicsDevice.SetRenderTarget(target); GraphicsDevice.Clear(Color.Transparent);
        Begin(lit, false, view, w, h);
        // Also captures torches; validation restricts its checks to the enemy's actual alpha mask.
        scene.DrawEntities(batch, visible); batch.End();
        GraphicsDevice.SetRenderTarget(null);
    }
    private void Begin(bool lit, bool foreground, Matrix view, int w, int h)
    {
        if (lit) lighting.BeginReceivers(batch, view, w, h, foreground);
        else batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: view);
    }
    private void Save(string name, Matrix view, Rectangle visible)
    {
        V8Capture.Save(albedo, Path.Combine(output, name + "_albedo.png"));
        V8Capture.Save(lighting.Direct, Path.Combine(output, name + "_direct.png"));
        if (ambientMode)
        {
            V8Capture.Save(lighting.Ambient.SkyTexture, Path.Combine(output, name + "_sky.png"));
            V8Capture.Save(lighting.Ambient.LocalTexture, Path.Combine(output, name + "_local.png"));
        }
        V8Capture.Save(lighting.Foreground, Path.Combine(output, name + "_foreground.png"));
        V8Capture.Save(final, Path.Combine(output, name + "_final.png"));
        V8Capture.Save(entityAlbedo, Path.Combine(output, name + "_entity_albedo.png"));
        V8Capture.Save(entityLit, Path.Combine(output, name + "_entity_lit.png"));
        File.WriteAllText(Path.Combine(output, name + "_frame.txt"),
            $"profile={GraphicsDevice.GraphicsProfile}\nview={view}\nvisible={visible}\nlightBounds={lighting.LightBounds}\nzoom={camera.Zoom}\nentity={scene.EntityPosition}\nlights={string.Join(';', scene.Lights)}\n");
    }
    private void EnsureTargets(int w, int h)
    {
        if (albedo != null && albedo.Width == w && albedo.Height == h) return;
        albedo?.Dispose(); final?.Dispose(); entityAlbedo?.Dispose(); entityLit?.Dispose();
        albedo = Target(); final = Target(); entityAlbedo = Target(); entityLit = Target();
        RenderTarget2D Target() => new(GraphicsDevice, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    }
    protected override void UnloadContent()
    {
        lighting?.Dispose(); albedo?.Dispose(); final?.Dispose(); entityAlbedo?.Dispose(); entityLit?.Dispose();
        exteriorPixel?.Dispose();
        batch?.Dispose(); base.UnloadContent();
    }
}
