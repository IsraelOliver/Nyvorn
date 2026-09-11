using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Engine.Graphics.LightingV7
{
    /// <summary>
    /// F12 helper: saves worldRT, lightRT and the final back buffer as PNG files in screenshots/.
    /// Allocates on purpose (only runs on key press).
    /// </summary>
    public static class LightingV7Screenshot
    {
        public const string Directory = "screenshots";

        /// <summary>
        /// Saves the three images. Each one is attempted independently, so a backend that refuses
        /// GetBackBufferData still leaves the world and light captures on disk.
        /// Returns a short report line.
        /// </summary>
        public static string SaveAll(GraphicsDevice graphicsDevice, Texture2D worldRT, Texture2D lightRT, int screenWidth, int screenHeight)
        {
            string dir;
            string prefix;
            try
            {
                dir = Path.GetFullPath(Directory);
                System.IO.Directory.CreateDirectory(dir);
                prefix = Path.Combine(dir, $"lighting_{DateTime.Now:yyyyMMdd_HHmmss}");
            }
            catch (Exception ex)
            {
                return "ERR could not create screenshots folder: " + ex.Message;
            }

            string ok = string.Empty;
            string failed = string.Empty;

            Attempt("world", () => SaveTextureRegion(graphicsDevice, worldRT, screenWidth, screenHeight, prefix + "_world.png"), worldRT != null);
            Attempt("light", () => SaveTextureRegion(graphicsDevice, lightRT, screenWidth, screenHeight, prefix + "_light.png"), lightRT != null);
            Attempt("final", () => SaveBackBuffer(graphicsDevice, prefix + "_final.png"), true);

            void Attempt(string name, Action action, bool available)
            {
                if (!available)
                {
                    failed += " " + name + "(null)";
                    return;
                }

                try
                {
                    action();
                    ok += " " + name;
                }
                catch (Exception ex)
                {
                    failed += $" {name}({ex.GetType().Name})";
                }
            }

            string report = prefix + " ->" + (ok.Length > 0 ? ok : " nothing");
            if (failed.Length > 0)
                report += "  FAILED:" + failed;
            return report;
        }

        private static void SaveTextureRegion(GraphicsDevice graphicsDevice, Texture2D source, int w, int h, string path)
        {
            w = Math.Min(w, source.Width);
            h = Math.Min(h, source.Height);
            Color[] data = new Color[w * h];
            source.GetData(0, new Rectangle(0, 0, w, h), data, 0, data.Length);
            for (int i = 0; i < data.Length; i++) data[i].A = 255; // opaque for viewing
            WritePng(graphicsDevice, data, w, h, path);
        }

        private static void SaveBackBuffer(GraphicsDevice graphicsDevice, string path)
        {
            int bw = graphicsDevice.PresentationParameters.BackBufferWidth;
            int bh = graphicsDevice.PresentationParameters.BackBufferHeight;
            Color[] data = new Color[bw * bh];
            graphicsDevice.GetBackBufferData(data);
            for (int i = 0; i < data.Length; i++) data[i].A = 255;
            WritePng(graphicsDevice, data, bw, bh, path);
        }

        private static void WritePng(GraphicsDevice graphicsDevice, Color[] data, int w, int h, string path)
        {
            using Texture2D temp = new(graphicsDevice, w, h, false, SurfaceFormat.Color);
            temp.SetData(data);
            using FileStream stream = File.Create(path);
            temp.SaveAsPng(stream, w, h);
        }
    }
}
