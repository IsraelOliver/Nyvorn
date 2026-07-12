using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.World.Simulation;
using System;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class ElyraSkyRenderer
    {
        private const int GradientBandHeight = 4;
        private const int StarCount = 130;
        private const float MaxRainWindAngleRadians = 0.55f; // ~31 degrees at full wind

        // 3 parallax bands: distant (drawn behind terrain, in the pre-terrain sky pass) and
        // mid/near (drawn after terrain/entities, so drops pass in front of trees/rooftops).
        // Distant is faint/blue-ish (reads as far away); near is sharper/brighter/faster.
        private static readonly RainLayer[] BackRainLayers =
        {
            new(dropCount: 90, fallSpeed: 220f, width: 1, height: 10, alphaScale: 0.30f, tint: new Color(150, 175, 205), salt: 300)
        };

        private static readonly RainLayer[] FrontRainLayers =
        {
            new(dropCount: 140, fallSpeed: 340f, width: 2, height: 14, alphaScale: 0.55f, tint: new Color(176, 205, 225), salt: 400),
            new(dropCount: 60, fallSpeed: 430f, width: 2, height: 17, alphaScale: 0.75f, tint: new Color(210, 225, 235), salt: 500)
        };

        private readonly Texture2D pixel;
        private readonly Texture2D sunTexture;
        private readonly Texture2D sunFallbackTexture;
        private readonly Texture2D moonTexture;
        private readonly Texture2D eclipseOccluderTexture;

        private static readonly Color DefaultSkyColor = new(102, 190, 255);

        public ElyraSkyRenderer(GraphicsDevice graphicsDevice, Texture2D sunTexture = null)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
            this.sunTexture = sunTexture;
            sunFallbackTexture = CreateDiscTexture(graphicsDevice, 18, Color.White);
            moonTexture = CreateDiscTexture(graphicsDevice, 14, Color.White);
            eclipseOccluderTexture = CreateDiscTexture(graphicsDevice, 20, Color.White);
        }

        public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight)
        {
            Draw(spriteBatch, screenWidth, screenHeight, DefaultSkyColor);
        }

        public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight, Color skyColor)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            spriteBatch.Draw(pixel, new Rectangle(0, 0, screenWidth, screenHeight), skyColor);
        }

        public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            DrawGradient(spriteBatch, screenWidth, screenHeight, skyState.TopColor, skyState.HorizonColor);
            DrawStars(spriteBatch, screenWidth, screenHeight, skyState);
            DrawCloudBands(spriteBatch, screenWidth, screenHeight, skyState);
            DrawMoon(spriteBatch, screenWidth, screenHeight, skyState);
            DrawSun(spriteBatch, screenWidth, screenHeight, skyState);
            DrawFog(spriteBatch, screenWidth, screenHeight, skyState);
            DrawRainLayers(spriteBatch, screenWidth, screenHeight, skyState, BackRainLayers);
        }

        // Called separately, after terrain/entities are drawn, so the near rain layers pass in
        // front of trees/rooftops instead of the whole rain effect sitting strictly behind everything.
        public void DrawRainFront(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            DrawRainLayers(spriteBatch, screenWidth, screenHeight, skyState, FrontRainLayers);
        }

        private void DrawGradient(SpriteBatch spriteBatch, int screenWidth, int screenHeight, Color top, Color horizon)
        {
            for (int y = 0; y < screenHeight; y += GradientBandHeight)
            {
                float amount = MathHelper.Clamp(y / (float)System.Math.Max(1, screenHeight - GradientBandHeight), 0f, 1f);
                amount = amount * amount * (3f - (2f * amount));
                int height = System.Math.Min(GradientBandHeight, screenHeight - y);
                spriteBatch.Draw(pixel, new Rectangle(0, y, screenWidth, height), Color.Lerp(top, horizon, amount));
            }
        }

        private void DrawStars(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            if (skyState.StarOpacity <= 0.01f)
                return;

            int starAreaHeight = System.Math.Max(1, (int)(screenHeight * 0.62f));
            for (int i = 0; i < StarCount; i++)
            {
                float x01 = Hash01(i, 17);
                float y01 = Hash01(i, 41);
                int x = (int)(x01 * screenWidth);
                int y = (int)(y01 * starAreaHeight);
                float shimmer = 0.72f + (0.28f * Hash01(i, 73));
                float alpha = MathHelper.Clamp(skyState.StarOpacity * shimmer, 0f, 1f);
                Color color = (Hash01(i, 89) > 0.82f ? new Color(255, 240, 168) : new Color(210, 230, 255)) * alpha;
                int size = Hash01(i, 101) > 0.88f ? 2 : 1;
                spriteBatch.Draw(pixel, new Rectangle(x, y, size, size), color);

                if (size == 2 || Hash01(i, 113) > 0.93f)
                {
                    spriteBatch.Draw(pixel, new Rectangle(x - 2, y, 1, 1), color * 0.45f);
                    spriteBatch.Draw(pixel, new Rectangle(x + size + 1, y, 1, 1), color * 0.45f);
                    spriteBatch.Draw(pixel, new Rectangle(x, y - 2, 1, 1), color * 0.45f);
                    spriteBatch.Draw(pixel, new Rectangle(x, y + size + 1, 1, 1), color * 0.45f);
                }
            }
        }

        private void DrawCloudBands(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            if (skyState.CloudOpacity <= 0.01f)
                return;

            Color cloudColor = Color.Lerp(new Color(230, 240, 250), new Color(92, 104, 118), skyState.RainIntensity) *
                MathHelper.Clamp(skyState.CloudOpacity * 0.18f, 0f, 0.35f);
            int bandHeight = System.Math.Max(18, screenHeight / 18);
            for (int i = 0; i < 5; i++)
            {
                int y = (int)(screenHeight * (0.09f + (i * 0.055f)));
                int offset = (int)((skyState.VisualTimeSeconds * (6f + i)) % (screenWidth / 3f));
                int x = -screenWidth / 3 + offset;
                spriteBatch.Draw(pixel, new Rectangle(x, y, screenWidth + (screenWidth / 2), bandHeight), cloudColor * (0.72f - i * 0.08f));
            }
        }

        private void DrawSun(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            if (skyState.SunOpacity <= 0.01f)
                return;

            Vector2 position = GetArcPosition(screenWidth, screenHeight, skyState.SunProgress);
            Texture2D texture = sunTexture ?? sunFallbackTexture;
            float scale = sunTexture == null ? 1f : 1.45f;
            int width = (int)(texture.Width * scale);
            int height = (int)(texture.Height * scale);
            Rectangle bounds = new(
                (int)(position.X - width * 0.5f),
                (int)(position.Y - height * 0.5f),
                width,
                height);

            spriteBatch.Draw(sunFallbackTexture, Inflate(bounds, 18), skyState.SunColor * skyState.SunOpacity * 0.14f);
            spriteBatch.Draw(texture, bounds, skyState.SunColor * skyState.SunOpacity);

            if (skyState.EclipseIntensity > 0.01f)
            {
                Rectangle occluder = Inflate(bounds, 10);
                occluder.Offset((int)(12f * (1f - skyState.EclipseIntensity)), (int)(-4f * skyState.EclipseIntensity));
                spriteBatch.Draw(eclipseOccluderTexture, occluder, new Color(5, 8, 18) * MathHelper.Clamp(skyState.EclipseIntensity, 0f, 1f));
            }
        }

        private void DrawMoon(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            if (skyState.MoonOpacity <= 0.01f)
                return;

            Vector2 position = GetArcPosition(screenWidth, screenHeight, skyState.MoonProgress);
            Rectangle bounds = new(
                (int)(position.X - 18),
                (int)(position.Y - 18),
                36,
                36);
            spriteBatch.Draw(moonTexture, Inflate(bounds, 10), skyState.MoonColor * skyState.MoonOpacity * 0.10f);
            spriteBatch.Draw(moonTexture, bounds, skyState.MoonColor * skyState.MoonOpacity);
        }

        private void DrawFog(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            if (skyState.FogOpacity <= 0.01f)
                return;

            int fogHeight = System.Math.Max(48, screenHeight / 9);
            int y = (int)(screenHeight * 0.47f);
            spriteBatch.Draw(pixel, new Rectangle(0, y, screenWidth, fogHeight), skyState.FogColor * skyState.FogOpacity * 0.22f);
        }

        private void DrawRainLayers(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState, RainLayer[] layers)
        {
            if (skyState.RainIntensity <= 0.01f)
                return;

            // Wind (0..1, ~0.05 calm to ~0.75 storm-active) steers the fall angle continuously
            // instead of drops always falling straight down.
            float windAngleRadians = (skyState.Wind - 0.4f) * MaxRainWindAngleRadians;
            float sinAngle = MathF.Sin(windAngleRadians);
            float cosAngle = MathF.Cos(windAngleRadians);

            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                DrawRainLayer(spriteBatch, screenWidth, screenHeight, skyState, layers[layerIndex], sinAngle, cosAngle);
        }

        private void DrawRainLayer(
            SpriteBatch spriteBatch,
            int screenWidth,
            int screenHeight,
            SkyState skyState,
            RainLayer layer,
            float sinAngle,
            float cosAngle)
        {
            int drops = (int)(layer.DropCount * MathHelper.Clamp(skyState.RainIntensity, 0f, 1f));
            Color rainColor = layer.Tint * MathHelper.Clamp(layer.AlphaScale * (0.55f + skyState.RainIntensity * 0.6f), 0f, 1f);
            float fall = skyState.VisualTimeSeconds * (layer.FallSpeed + (skyState.Wetness * 80f));

            for (int i = 0; i < drops; i++)
            {
                int baseX = (int)(Hash01(i, layer.Salt) * (screenWidth + 160)) - 80;
                int baseY = (int)(Hash01(i, layer.Salt + 1) * screenHeight);
                float fallDistance = (baseY + fall + (i * 13)) % (screenHeight + 40);
                int y = (int)fallDistance - 20;
                int x = baseX + (int)(sinAngle * fallDistance);

                // Short diagonal streak leaning with the wind, instead of a fixed vertical rect.
                int segments = 3;
                for (int s = 0; s < segments; s++)
                {
                    float t = s / (float)(segments - 1);
                    int segX = x + (int)(sinAngle * layer.Height * t);
                    int segY = y + (int)(cosAngle * layer.Height * t);
                    Color segColor = s == 0 ? rainColor : rainColor * 0.6f;
                    spriteBatch.Draw(pixel, new Rectangle(segX, segY, layer.Width, System.Math.Max(2, layer.Height / segments)), segColor);
                }
            }
        }

        private readonly struct RainLayer
        {
            public RainLayer(int dropCount, float fallSpeed, int width, int height, float alphaScale, Color tint, int salt)
            {
                DropCount = dropCount;
                FallSpeed = fallSpeed;
                Width = width;
                Height = height;
                AlphaScale = alphaScale;
                Tint = tint;
                Salt = salt;
            }

            public int DropCount { get; }
            public float FallSpeed { get; }
            public int Width { get; }
            public int Height { get; }
            public float AlphaScale { get; }
            public Color Tint { get; }
            public int Salt { get; }
        }

        private static Vector2 GetArcPosition(int screenWidth, int screenHeight, float progress)
        {
            float t = MathHelper.Clamp(progress, 0f, 1f);
            float x = MathHelper.Lerp(screenWidth * 0.10f, screenWidth * 0.90f, t);
            float y = (screenHeight * 0.53f) - (MathF.Sin(t * MathF.PI) * screenHeight * 0.41f);
            return new Vector2(x, y);
        }

        private static Rectangle Inflate(Rectangle rectangle, int amount)
        {
            return new Rectangle(
                rectangle.X - amount,
                rectangle.Y - amount,
                rectangle.Width + (amount * 2),
                rectangle.Height + (amount * 2));
        }

        private static Texture2D CreateDiscTexture(GraphicsDevice graphicsDevice, int radius, Color color)
        {
            int diameter = (radius * 2) + 1;
            Color[] pixels = new Color[diameter * diameter];
            Vector2 center = new(radius, radius);
            for (int y = 0; y < diameter; y++)
            {
                for (int x = 0; x < diameter; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = MathHelper.Clamp(radius + 0.5f - distance, 0f, 1f);
                    pixels[(y * diameter) + x] = color * alpha;
                }
            }

            Texture2D texture = new(graphicsDevice, diameter, diameter);
            texture.SetData(pixels);
            return texture;
        }

        private static float Hash01(int value, int salt)
        {
            unchecked
            {
                uint hash = (uint)(value * 374761393 + salt * 668265263);
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                return (hash & 0x00FFFFFF) / 16777215f;
            }
        }
    }
}
