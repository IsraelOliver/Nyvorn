using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

public static class V8GraphicsProbe
{
    // Readback is diagnostic only, never per gameplay frame.
    public static string Run(GraphicsDevice device, SpriteBatch batch, string output)
    {
        using var target = new RenderTarget2D(device, 16, 16, false, SurfaceFormat.Color,
            DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
        using var pixel = new Texture2D(device, 1, 1);
        pixel.SetData(new[] { Color.White });
        using var noColor = new BlendState { ColorWriteChannels = ColorWriteChannels.None };
        using var write = new DepthStencilState {
            DepthBufferEnable = false, StencilEnable = true, StencilFunction = CompareFunction.Always,
            StencilPass = StencilOperation.Replace, ReferenceStencil = 1 };
        using var test = new DepthStencilState {
            DepthBufferEnable = false, StencilEnable = true, StencilFunction = CompareFunction.Equal,
            StencilPass = StencilOperation.Keep, ReferenceStencil = 0 };
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.Stencil, Color.Transparent, 1, 0);
        batch.Begin(blendState: noColor, depthStencilState: write);
        batch.Draw(pixel, new Rectangle(0, 0, 8, 16), Color.White);
        batch.End();
        batch.Begin(blendState: BlendState.Opaque, depthStencilState: test);
        batch.Draw(pixel, new Rectangle(0, 0, 16, 16), Color.White);
        batch.End();
        device.SetRenderTarget(null);
        var data = new Color[256];
        target.GetData(data);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                if (data[y * 16 + x] != (x < 8 ? Color.Transparent : Color.White))
                    throw new InvalidOperationException($"Stencil mask failed at {x},{y}: {data[y * 16 + x]}");
        using (var stream = File.Create(Path.Combine(output, "stencil.png"))) target.SaveAsPng(stream, 16, 16);
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Stencil, Color.Black, 1, 0);
        device.SetRenderTarget(null);
        var after = new Color[256];
        target.GetData(after);
        for (int i = 0; i < data.Length; i++)
            if (data[i] != after[i]) throw new InvalidOperationException("Stencil clear modified color");
        return $"PASS profile={device.GraphicsProfile}; RT={target.DepthStencilFormat}; mask=256/256; stencil-only clear preserves color";
    }
}
