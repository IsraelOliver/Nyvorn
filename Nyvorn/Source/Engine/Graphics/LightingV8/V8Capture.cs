using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

public static class V8Capture
{
    public static Color[] Read(Texture2D texture)
    {
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        return pixels;
    }
    public static void Save(Texture2D texture, string path)
    {
        using var stream = File.Create(path);
        // Keep RGBA; unlike LightingV7Screenshot this never overwrites alpha.
        texture.SaveAsPng(stream, texture.Width, texture.Height);
    }
}
