using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;
using Nyvorn.Source.Engine.Graphics.LightingV9Probe;
using Nyvorn.Source.World.Decorations;
using Nyvorn.Source.World.Generation;

namespace Nyvorn.Source.Game.States;

/// <summary>
/// V9 lighting in gameplay, the default pipeline (V9Gameplay). Only V9 computes and composes the frame; V6/V7/V8 are
/// bypassed, as V8 bypasses them. Receivers are drawn with neutral albedo per class into a screen layer, then lit by one
/// screen quad whose vertices are window-local world positions, so the unchanged V9LabReceiver shader reads World = world -
/// window origin and applies the lab's per-pixel math. The gameplay probe (PlayingState.V9Probe*.cs) runs this same code
/// and only adds its instrumentation.
/// </summary>
public partial class PlayingState
{
    private bool v9Active;
    private V9ProbeField v9Field;
    private Effect v9Receiver;
    private RenderTarget2D v9Final, v9Layer, v9Albedo, v9BackdropLit;
    private double v9FieldMs, v9DrawMs;

    private void InitializeV9()
    {
        if (!V9Gameplay.Enabled) return;
        v9Active = true;
        // Neutral background albedo: never the legacy 104/255 tint of V7/V8 presentation.
        session.WorldMap.NeutralLightingAlbedo = true;
        v9Receiver = content.Load<Effect>("effects/V9LabReceiver");
        // The shipped configuration; only the probe's diagnostic flags may choose another natural model.
        v9Field = V9Gameplay.CreateField(graphicsDevice, session.LayerDefinitions, V9ProbeOptions.NaturalModel);
        if (V9ProbeOptions.Enabled) return;
        Console.WriteLine($"[Lighting] V9 active ({v9Field.NaturalModel}); V6/V7/V8 bypassed.");
        if (V9Gameplay.Smoke) InitializeV9Smoke();
    }

    private void DrawV9Gameplay(SpriteBatch batch, int width, int height, float frameSeconds)
    {
        if (V9Gameplay.Smoke) PrepareV9SmokeFrame();
        var loops = RenderV9Frame(batch, width, height, out _);
        if (V9Gameplay.Smoke) CaptureV9SmokeFrame(); // the lit frame, before the HUD, like the probe's captures
        PresentV9Frame(batch, width, height, loops, () =>
        {
            batch.DrawString(consoleFont, $"LIGHTING: V9 | natural: sky backplane | torches {v9Field.Last.Torches} | zoom {session.Camera.Zoom:0.##}", new Vector2(10, 48), Color.Yellow);
            batch.DrawString(consoleFont, $"V9 CPU: field {v9FieldMs:0.00} ms | draw {v9DrawMs:0.00} ms (CPU + submission) | frame {frameSeconds * 1000:0.00} ms", new Vector2(10, 72), Color.Cyan);
            DrawLightingStatsHud(batch, 96);
        });
        if (V9Gameplay.Smoke) FinishV9SmokeFrame(frameSeconds);
    }

    /// <summary>Updates the field for the current view and draws the lit scene into v9Final. Returns the world loops.</summary>
    private IReadOnlyList<int> RenderV9Frame(SpriteBatch batch, int width, int height, out Rectangle viewport)
    {
        long start = Stopwatch.GetTimestamp();
        var loops = GetVisibleLoopOffsets(width, session.WorldMap.PixelWidth);
        foreach (int loop in loops) session.PrepareTerrainRender(graphicsDevice, width, height, loop * session.WorldMap.PixelWidth);
        long fieldStart = Stopwatch.GetTimestamp();
        viewport = V9Viewport(width, height);
        v9Field.Update(session.WorldMap, session.TorchRuntimeSystem.Torches, viewport);
        v9FieldMs = Stopwatch.GetElapsedTime(fieldStart).TotalMilliseconds;
        EnsureV9Targets(width, height);
        graphicsDevice.SetRenderTarget(v9Final);
        graphicsDevice.Clear(Color.Black);
        DrawV9Scene(batch, width, height, loops, viewport, lit: true);
        graphicsDevice.SetRenderTarget(null);
        v9DrawMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds; // CPU work + driver submission, NOT GPU duration
        return loops;
    }

    /// <summary>The lit frame, then world-space UI and the HUD, both outside the lighting and outside captures.
    /// <paramref name="lightingHud"/> draws the pipeline's own lines inside the HUD batch.</summary>
    private void PresentV9Frame(SpriteBatch batch, int width, int height, IReadOnlyList<int> loops, Action lightingHud)
    {
        batch.Begin(samplerState: SamplerState.PointClamp);
        batch.Draw(v9Final, Vector2.Zero, Color.White);
        batch.End();
        foreach (int loop in loops)
        {
            batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Matrix.CreateTranslation(loop * session.WorldMap.PixelWidth, 0, 0) * session.Camera.GetViewMatrix());
            session.DrawTerrainOverlay(batch);
            batch.End();
        }
        batch.Begin(samplerState: SamplerState.PointClamp);
        session.DrawHud(batch, width, height);
        if (minimapVisible) session.DrawMinimap(batch, width, height, minimapTissueMode);
        playerHubUI.Draw(batch, session.WorkbenchRuntimeSystem.GetNearbyCraftTier() | session.FurnaceRuntimeSystem.GetNearbyCraftTier());
        if (showFps) DrawFpsCounter(batch);
        if (consoleOpen) DrawConsole(batch, width);
        lightingHud();
        batch.End();
    }

    /// <summary>Viewport in the camera frame, in world pixels (the view matrix rounds the camera position).</summary>
    private Rectangle V9Viewport(int width, int height)
    {
        Vector2 topLeft = Vector2.Transform(Vector2.Zero, Matrix.Invert(session.Camera.GetViewMatrix()));
        return new Rectangle((int)MathF.Floor(topLeft.X), (int)MathF.Floor(topLeft.Y),
            (int)MathF.Ceiling(width / session.Camera.Zoom) + 1, (int)MathF.Ceiling(height / session.Camera.Zoom) + 1);
    }

    private void DrawV9Scene(SpriteBatch batch, int width, int height, IReadOnlyList<int> loops, Rectangle viewport, bool lit)
    {
        // Exterior presentation, unlit like the lab's marked band: the painted sky (and surface mountains) is drawn first;
        // a black cover then hides it below each column's first solid tile, except where the field's single visible-sky
        // definition says the sky is what lies behind (V9ProbeField.SkyVisibleAt): Space/Surface OpenAtmosphere that the
        // natural system reaches, and the Shallow's background openings (faded with their weight). Foreground, background,
        // sealed Surface air and OpenAtmosphere in Cavern/Deep stay covered. The cover only shrinks where it existed before.
        if (v9Field.IsBackplane) DrawV9Backdrop(batch, width, height, lit);
        else
        {
            batch.Begin(samplerState: SamplerState.PointClamp);
            session.DrawSky(batch, width, height);
            if (session.GetPlayerWorldLayer() is WorldLayerType.Surface or WorldLayerType.ShallowUnderground)
                session.DrawParallaxMountains(batch, width, height);
            batch.End();
        }
        int firstColumn = V9ProbeSky.FloorDiv(viewport.Left, 8), lastColumn = V9ProbeSky.FloorDiv(viewport.Right, 8);
        int[] skyFloor = v9Field.SkyFloors(session.WorldMap, firstColumn, lastColumn - firstColumn + 1);
        batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: session.Camera.GetViewMatrix());
        bool shallowSky = v9Field.NaturalModel != V9NaturalModel.PointSamples;
        int lastRow = V9ProbeSky.FloorDiv(viewport.Bottom, 8);
        // Backplane occlusion follows the receiver sprites' alpha, not full black tile rectangles.
        for (int c = firstColumn; c <= lastColumn && !V9ProbeOptions.NoSkyMask && !v9Field.IsBackplane; c++)
        {
            int top = skyFloor[c - firstColumn] * 8;
            if (top >= viewport.Bottom) continue;
            if (!shallowSky) { batch.Draw(consolePixel, new Rectangle(c * 8, top, 8, viewport.Bottom - top + 1), Color.Black); continue; }
            // Exposure model: per tile, from the classification (not "everything below the first solid").
            int runStart = Math.Max(top / 8, V9ProbeSky.FloorDiv(viewport.Top, 8)); // rows above the view are never seen
            float runAlpha = V9MaskAlpha(c, runStart);
            for (int r = runStart + 1; r <= lastRow + 1; r++)
            {
                float alpha = r > lastRow ? -1 : V9MaskAlpha(c, r);
                if (alpha == runAlpha) continue;
                if (runAlpha > 0) batch.Draw(consolePixel, new Rectangle(c * 8, runStart * 8, 8, (r - runStart) * 8), Color.Black * runAlpha);
                runStart = r;
                runAlpha = alpha;
            }
        }
        batch.End();
        // Cover alpha of one tile below its column's first solid: 0 = the painted sky shows.
        // Cover = 1 - the field's single visible-sky definition (O; Shallow opening at its weight; Space/Surface
        // OpenAtmosphere only where the natural system reaches it; nothing in Cavern/Deep; never foreground/background).
        float V9MaskAlpha(int column, int row) => 1 - v9Field.SkyVisibleAt(column, row);

        V9Receivers(batch, width, height, loops, lit, 1f, offset =>
            session.DrawTreeDecorations(batch, width, height, offset, TreeRenderLayer.Back, Color.White));
        // Background walls: receivers at the lab's 0.14 response, never blockers.
        V9Receivers(batch, width, height, loops, lit, V9LabSettings.BackgroundResponse, offset =>
        {
            Matrix transform = Matrix.CreateTranslation(offset, 0, 0) * session.Camera.GetViewMatrix();
            Vector2 a = Vector2.Transform(Vector2.Zero, Matrix.Invert(transform)), b = Vector2.Transform(new Vector2(width, height), Matrix.Invert(transform));
            int size = session.WorldMap.TileSize;
            session.WorldMap.DrawBackground(batch, (int)MathF.Floor(a.X / size) - 1, (int)MathF.Floor(b.X / size) + 1,
                Math.Max(0, (int)MathF.Floor(a.Y / size) - 1), (int)MathF.Floor(b.Y / size) + 1, Color.White);
        });
        V9Receivers(batch, width, height, loops, lit, 1f, offset =>
        {
            session.DrawTreeDecorations(batch, width, height, offset, TreeRenderLayer.Front, Color.White);
            session.DrawWater(batch, width, height, offset);
            session.DrawTerrainBase(batch, width, height, offset);
            session.DoorRuntimeSystem.Draw(batch, openOnly: false);
            session.PlatformRuntimeSystem.Draw(batch);
            session.WorkbenchRuntimeSystem.Draw(batch); session.FurnaceRuntimeSystem.Draw(batch);
            session.ChairRuntimeSystem.Draw(batch); session.TableRuntimeSystem.Draw(batch);
            session.DoorRuntimeSystem.Draw(batch, openOnly: true);
        });
        V9Receivers(batch, width, height, loops, lit, 1f, offset =>
        {
            session.DrawLoopedWorldEntities(batch, width, height, offset, neutralEntityLightSampler, neutralEntityLightSampler, drawTorchFlames: false);
            session.DrawEntities(batch, neutralEntityLightSampler);
            session.DrawTissueCore(batch, width, height, offset);
        });
        // Only the original flame pixels are emissive: no halo, bloom or second illumination (lab order: last).
        foreach (int loop in loops)
        {
            batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Matrix.CreateTranslation(loop * session.WorldMap.PixelWidth, 0, 0) * session.Camera.GetViewMatrix());
            session.TorchRuntimeSystem.DrawFlames(batch, session.EnvironmentSystem.SkyState.VisualTimeSeconds);
            batch.End();
        }
    }

    /// <summary>One receiver class: neutral albedo into a screen layer, then lit by the unchanged V9 shader.</summary>
    private void V9Receivers(SpriteBatch batch, int width, int height, IReadOnlyList<int> loops, bool lit, float response, Action<float> draw)
    {
        RenderTarget2D target = lit ? v9Final : v9Albedo;
        if (lit) { graphicsDevice.SetRenderTarget(v9Layer); graphicsDevice.Clear(Color.Transparent); }
        foreach (int loop in loops)
        {
            float offset = loop * session.WorldMap.PixelWidth;
            batch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend,
                transformMatrix: Matrix.CreateTranslation(offset, 0, 0) * session.Camera.GetViewMatrix());
            draw(offset);
            batch.End();
        }
        if (!lit) return;
        ComposeV9Layer(batch, width, height, target, response);
    }

    // Light cave backdrops before the full-image fade, using the existing timed layer transition.
    private void DrawV9Backdrop(SpriteBatch batch, int width, int height, bool lit)
    {
        RenderTarget2D target = lit ? v9Final : v9Albedo;
        DrawBackdrop(session.GetCurrentParallax(), 1f);
        if (session.IsParallaxTransitioning())
            DrawBackdrop(session.GetTargetParallax(), session.GetTransitionProgress());

        void DrawBackdrop(ParallaxType type, float alpha)
        {
            graphicsDevice.SetRenderTarget(v9Layer);
            graphicsDevice.Clear(Color.Black);
            if (type == ParallaxType.Mountains)
            {
                batch.Begin(samplerState: SamplerState.PointClamp);
                session.DrawSky(batch, width, height);
                session.DrawParallaxMountains(batch, width, height);
                batch.End();
            }
            else
                session.ViewCoordinator.DrawSubterraneanParallax(batch, width, height,
                    type == ParallaxType.Cave ? WorldLayerType.Cavern : WorldLayerType.DeepCavern);
            Texture2D backdrop = v9Layer;
            if (lit && type != ParallaxType.Mountains)
            {
                graphicsDevice.SetRenderTarget(v9BackdropLit);
                graphicsDevice.Clear(Color.Black);
                ComposeV9Layer(batch, width, height, v9BackdropLit, V9LabSettings.BackgroundResponse);
                backdrop = v9BackdropLit;
            }
            graphicsDevice.SetRenderTarget(target);
            batch.Begin(samplerState: SamplerState.PointClamp);
            batch.Draw(backdrop, Vector2.Zero, Color.White * alpha);
            batch.End();
        }
    }

    private void ComposeV9Layer(SpriteBatch batch, int width, int height, RenderTarget2D target, float response)
    {
        graphicsDevice.SetRenderTarget(target);
        Matrix view = session.Camera.GetViewMatrix();
        Vector2 topLeft = Vector2.Transform(Vector2.Zero, Matrix.Invert(view));
        Point origin = v9Field.Origin;
        v9Receiver.Parameters["MatrixTransform"].SetValue(Matrix.CreateTranslation(origin.X, origin.Y, 0) * view *
            Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1));
        v9Receiver.Parameters["WorldSize"].SetValue(new Vector2(v9Field.Width, v9Field.Height));
        v9Receiver.Parameters["Response"].SetValue(response);
        v9Receiver.Parameters["Irradiance"].SetValue(v9Field.Texture);
        batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp, depthStencilState: DepthStencilState.None,
            rasterizerState: RasterizerState.CullNone, effect: v9Receiver);
        batch.Draw(v9Layer, topLeft - origin.ToVector2(), null, Color.White, 0f, Vector2.Zero, 1f / session.Camera.Zoom, SpriteEffects.None, 0f);
        batch.End();
    }

    private void EnsureV9Targets(int width, int height)
    {
        if (v9Final != null && v9Final.Width == width && v9Final.Height == height) return;
        v9Final?.Dispose(); v9Layer?.Dispose(); v9Albedo?.Dispose(); v9BackdropLit?.Dispose();
        v9Final = new RenderTarget2D(graphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        v9Layer = new RenderTarget2D(graphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        v9Albedo = new RenderTarget2D(graphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        v9BackdropLit = new RenderTarget2D(graphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    }

    private void DisposeV9()
    {
        v9Field?.Dispose(); v9Final?.Dispose(); v9Layer?.Dispose(); v9Albedo?.Dispose(); v9BackdropLit?.Dispose();
    }
}
