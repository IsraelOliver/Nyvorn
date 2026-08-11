using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// V6 Light Map Renderer: Converts light data to texture for visualization.
    ///
    /// Modes:
    /// - LIGHT: RGB intensity (blue gradient for debug)
    /// - SOURCES: SkyOpen=cyan, Background=blue, Foreground=red
    /// </summary>
    public sealed class V6LightMapRenderer
    {
        public enum DebugMode
        {
            Off = 0,
            LightIntensity = 1,
            SourceClassification = 2
        }

        private readonly GraphicsDevice graphicsDevice;
        private readonly V6LightMap lightMap;

        private Texture2D debugTexture;
        private Color[] debugTextureData = Array.Empty<Color>();

        private DebugMode currentMode = DebugMode.Off;

        public Texture2D DebugTexture => debugTexture;
        public DebugMode CurrentMode => currentMode;

        public V6LightMapRenderer(GraphicsDevice graphicsDevice, V6LightMap lightMap)
        {
            this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            this.lightMap = lightMap ?? throw new ArgumentNullException(nameof(lightMap));
        }

        public void CycleDebugMode()
        {
            currentMode = (DebugMode)(((int)currentMode + 1) % 3);
        }

        private Texture2D productionTexture;
        private Color[] productionTextureData = Array.Empty<Color>();

        public Texture2D ProductionTexture => productionTexture;

        public void Update()
        {
            // Always update production texture (for rendering in game)
            UpdateProductionTexture();

            // Only update debug texture if debug mode is ON
            if (currentMode == DebugMode.Off)
            {
                if (debugTexture != null)
                {
                    debugTexture.Dispose();
                    debugTexture = null;
                }
                return;
            }

            int width = lightMap.BufferWidth;
            int height = lightMap.BufferHeight;
            int cellCount = width * height;

            // Allocate color buffer if needed
            if (debugTextureData.Length < cellCount)
                debugTextureData = new Color[cellCount];

            // Fill color buffer based on mode
            if (currentMode == DebugMode.LightIntensity)
                GenerateLightIntensityColors(width, height, cellCount);
            else if (currentMode == DebugMode.SourceClassification)
                GenerateSourceClassificationColors(width, height, cellCount);

            // Create or update texture
            if (debugTexture == null || debugTexture.Width != width || debugTexture.Height != height)
            {
                debugTexture?.Dispose();
                debugTexture = new Texture2D(graphicsDevice, width, height);
            }

            debugTexture.SetData(debugTextureData);
        }

        private void UpdateProductionTexture()
        {
            int width = lightMap.BufferWidth;
            int height = lightMap.BufferHeight;
            int cellCount = width * height;

            if (productionTextureData.Length < cellCount)
                productionTextureData = new Color[cellCount];

            var lightR = lightMap.LightR;
            var lightG = lightMap.LightG;
            var lightB = lightMap.LightB;
            var mediumMask = lightMap.MediumMask;

            for (int i = 0; i < cellCount; i++)
            {
                Color maskColor;

                if (mediumMask[i] == V6LightMap.CellMedium.SkyOpen)
                {
                    // Sky-open cells: use white mask (no darkening from MultiplyBlend)
                    maskColor = Color.White;
                }
                else if (mediumMask[i] == V6LightMap.CellMedium.Foreground ||
                         mediumMask[i] == V6LightMap.CellMedium.Background)
                {
                    // Foreground + Background: convert RGB to color mask (preserves saturation)
                    float r = lightR[i];
                    float g = lightG[i];
                    float b = lightB[i];

                    int ri = (int)Math.Clamp(r * 255, 0, 255);
                    int gi = (int)Math.Clamp(g * 255, 0, 255);
                    int bi = (int)Math.Clamp(b * 255, 0, 255);

                    // Enforce minimum light level (50% brightness) - doors don't create pitch-black shadows
                    ri = Math.Max(ri, 127);
                    gi = Math.Max(gi, 127);
                    bi = Math.Max(bi, 127);

                    maskColor = new Color(ri, gi, bi, 255);
                }
                else
                {
                    // Unknown: neutral
                    maskColor = Color.White;
                }

                productionTextureData[i] = maskColor;
            }

            // Create or update texture
            if (productionTexture == null || productionTexture.Width != width || productionTexture.Height != height)
            {
                productionTexture?.Dispose();
                productionTexture = new Texture2D(graphicsDevice, width, height);
            }

            productionTexture.SetData(productionTextureData);
        }

        private void GenerateLightIntensityColors(int width, int height, int cellCount)
        {
            var lightR = lightMap.LightR;
            var lightG = lightMap.LightG;
            var lightB = lightMap.LightB;

            for (int i = 0; i < cellCount; i++)
            {
                float r = lightR[i];
                float g = lightG[i];
                float b = lightB[i];

                // Show actual world light RGB (not the rendered mask)
                // This demonstrates LightMap contains meaningful color data
                int ri = (int)Math.Clamp(r * 255, 0, 255);
                int gi = (int)Math.Clamp(g * 255, 0, 255);
                int bi = (int)Math.Clamp(b * 255, 0, 255);

                debugTextureData[i] = new Color(ri, gi, bi, 255);
            }
        }

        private void GenerateSourceClassificationColors(int width, int height, int cellCount)
        {
            var mediumMask = lightMap.MediumMask;

            for (int i = 0; i < cellCount; i++)
            {
                Color cellColor = mediumMask[i] switch
                {
                    V6LightMap.CellMedium.SkyOpen => new Color(0, 255, 255, 255),       // Cyan
                    V6LightMap.CellMedium.Background => new Color(0, 100, 200, 255),    // Dark blue
                    V6LightMap.CellMedium.Foreground => new Color(150, 50, 50, 255),    // Dark red
                    V6LightMap.CellMedium.Water => new Color(50, 150, 200, 255),        // Water blue
                    _ => new Color(50, 50, 50, 255)                                     // Unknown
                };

                debugTextureData[i] = cellColor;
            }
        }

        public void Dispose()
        {
            debugTexture?.Dispose();
            debugTexture = null;
        }
    }
}
