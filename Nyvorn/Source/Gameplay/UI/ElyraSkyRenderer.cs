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

        // Base (unzoomed) pixel radius of the sun disc the SunRays shader draws - shared with the
        // eclipse occluder below so its darkening overlay still matches the shader's sun size.
        private const float SunRadiusBase = 16f;
        private const float RaySeed = 4.73f;

        // Adjustable sun-shader knobs (SunRays.fx) - tune these to change the look without editing
        // the shader math itself.
        private const float BloomRadiusMultiplier = 7f;  // gaussian sigma, in SunRadius units
        private const float BloomIntensityValue = 0.4f;  // additive strength at the sun's center
        private const float RayReachMultiplier = 24f;    // how far rays extend, in SunRadius units
        private const float RayNoiseAmountValue = 0.55f; // 0 = uniform rays, higher = more irregular
        private const float RayRotationSpeedValue = 0.004f; // radians/sec the ray field slowly turns
        private const float RayShimmerSpeedValue = 0.06f;   // how fast the ray noise drifts/flickers
        private const float CoreIntensityValue = 0.5f;   // fraction of the disc that blows out white

        private readonly Texture2D pixel;
        private readonly Texture2D eclipseOccluderTexture;
        private readonly Effect sunRaysEffect;
        private readonly Effect moonPhaseEffect;

        private static readonly Color DefaultSkyColor = new(102, 190, 255);

        public ElyraSkyRenderer(GraphicsDevice graphicsDevice, Effect sunRaysEffect = null, Effect moonPhaseEffect = null)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
            eclipseOccluderTexture = CreateDiscTexture(graphicsDevice, 20, Color.White);
            this.sunRaysEffect = sunRaysEffect;
            this.moonPhaseEffect = moonPhaseEffect;
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

        public void Draw(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState, float zoom = 1f)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            DrawGradient(spriteBatch, screenWidth, screenHeight, skyState.TopColor, skyState.HorizonColor);
            DrawStars(spriteBatch, screenWidth, screenHeight, skyState);
            DrawCloudBands(spriteBatch, screenWidth, screenHeight, skyState);
            DrawEclipseOcclusion(spriteBatch, screenWidth, screenHeight, skyState, zoom);
            DrawFog(spriteBatch, screenWidth, screenHeight, skyState);
            DrawRainLayers(spriteBatch, screenWidth, screenHeight, skyState, BackRainLayers, zoom);
        }

        // Called separately, after terrain/entities are drawn, so the near rain layers pass in
        // front of trees/rooftops instead of the whole rain effect sitting strictly behind everything.
        public void DrawRainFront(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState, float zoom = 1f)
        {
            if (screenWidth <= 0 || screenHeight <= 0)
                return;

            DrawRainLayers(spriteBatch, screenWidth, screenHeight, skyState, FrontRainLayers, zoom);
        }

        // Self-contained Begin/End using the SunRays pixel shader instead of sprite art - called as
        // its own draw step (not nested inside the caller's default-effect sky batch) because
        // SpriteBatch only supports one Effect per Begin/End pair.
        public void DrawSunGlow(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState, float zoom = 1f)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || skyState.SunOpacity <= 0.01f || sunRaysEffect == null)
                return;

            Vector2 position = GetArcPosition(screenWidth, screenHeight, skyState.SunProgress);
            float sunRadius = SunRadiusBase * zoom;

            // Rays are eclipsed by cloud cover (overcast sky shouldn't show a sunburst) and by
            // "warmth" - how orange the horizon currently is. At midday (blue horizon) warmth is ~0
            // so only the bare glowing disc shows; at sunrise/sunset (already-orange horizon
            // keyframes in WorldEnvironmentSystem) warmth is ~1 and the full burst shows.
            float cloudFade = 1f - MathHelper.Clamp(skyState.CloudOpacity * 0.75f, 0f, 0.85f);
            float warmth = GetSunWarmth(skyState);

            // SpriteBatch does not auto-populate a custom effect's transform - unlike its own
            // built-in default effect, it's on us to compute the same screen-space orthographic
            // projection it would otherwise use, or every vertex collapses to the origin.
            sunRaysEffect.Parameters["MatrixTransform"]?.SetValue(CreateScreenMatrixTransform(spriteBatch.GraphicsDevice));
            sunRaysEffect.Parameters["SunPosition"]?.SetValue(position);
            sunRaysEffect.Parameters["SunRadius"]?.SetValue(sunRadius);
            sunRaysEffect.Parameters["SunColor"]?.SetValue(skyState.SunColor.ToVector4());
            sunRaysEffect.Parameters["RayIntensity"]?.SetValue(cloudFade * warmth);
            sunRaysEffect.Parameters["GlowIntensity"]?.SetValue(skyState.SunOpacity * cloudFade);
            sunRaysEffect.Parameters["Seed"]?.SetValue(RaySeed);
            sunRaysEffect.Parameters["Time"]?.SetValue(skyState.VisualTimeSeconds);
            sunRaysEffect.Parameters["RayReach"]?.SetValue(RayReachMultiplier);
            sunRaysEffect.Parameters["RayNoiseAmount"]?.SetValue(RayNoiseAmountValue);
            sunRaysEffect.Parameters["RayRotationSpeed"]?.SetValue(RayRotationSpeedValue);
            sunRaysEffect.Parameters["RayShimmerSpeed"]?.SetValue(RayShimmerSpeedValue);
            sunRaysEffect.Parameters["CoreIntensity"]?.SetValue(CoreIntensityValue);
            sunRaysEffect.Parameters["Warmth"]?.SetValue(warmth);
            sunRaysEffect.Parameters["WarmTipColor"]?.SetValue(skyState.HorizonColor.ToVector4());
            sunRaysEffect.Parameters["BloomRadius"]?.SetValue(BloomRadiusMultiplier);
            sunRaysEffect.Parameters["BloomIntensity"]?.SetValue(BloomIntensityValue * cloudFade);

            Color drawTint = Color.White * skyState.SunOpacity;
            Rectangle fullScreen = new Rectangle(0, 0, screenWidth, screenHeight);

            // Bloom first (wide, soft, additive - brightens the sky itself) so the sharper core and
            // rays composite on top of an already-lit backdrop instead of sitting as a flat sticker.
            sunRaysEffect.CurrentTechnique = sunRaysEffect.Techniques["SunBloom"];
            spriteBatch.Begin(blendState: BlendState.Additive, samplerState: SamplerState.LinearClamp, effect: sunRaysEffect);
            spriteBatch.Draw(pixel, fullScreen, drawTint);
            spriteBatch.End();

            sunRaysEffect.CurrentTechnique = sunRaysEffect.Techniques["SunRays"];
            spriteBatch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.LinearClamp, effect: sunRaysEffect);
            spriteBatch.Draw(pixel, fullScreen, drawTint);
            spriteBatch.End();
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

        // The sun's glow/rays are drawn by DrawSunGlow (SunRays.fx, its own Begin/End pass) - this
        // just places the eclipse-darkening disc at the same position/size within the normal
        // default-effect sky pass, so it still layers correctly against clouds/fog/stars.
        private void DrawEclipseOcclusion(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState, float zoom)
        {
            if (skyState.SunOpacity <= 0.01f || skyState.EclipseIntensity <= 0.01f)
                return;

            Vector2 position = GetArcPosition(screenWidth, screenHeight, skyState.SunProgress);
            int diameter = (int)(SunRadiusBase * 2f * zoom);
            Rectangle bounds = new(
                (int)(position.X - diameter * 0.5f),
                (int)(position.Y - diameter * 0.5f),
                diameter,
                diameter);

            Rectangle occluder = Inflate(bounds, (int)(10 * zoom));
            occluder.Offset((int)(12f * zoom * (1f - skyState.EclipseIntensity)), (int)(-4f * zoom * skyState.EclipseIntensity));
            spriteBatch.Draw(eclipseOccluderTexture, occluder, new Color(5, 8, 18) * MathHelper.Clamp(skyState.EclipseIntensity, 0f, 1f));
        }

        // Derived straight from the sky's own horizon color instead of duplicating time-of-day
        // logic: WorldEnvironmentSystem's keyframes already go orange at dawn/dusk and blue at
        // midday, so redness-over-blueness at the horizon is a free, always-in-sync warmth signal.
        private static float GetSunWarmth(SkyState skyState)
        {
            float redMinusBlue = (skyState.HorizonColor.R - skyState.HorizonColor.B) / 255f;
            return MathHelper.Clamp(redMinusBlue * 1.8f, 0f, 1f);
        }

        // Self-contained Begin/End using the MoonPhase shader, called as its own draw step (same
        // reason as DrawSunGlow: a custom Effect can't be nested inside the default-effect batch).
        // Far moon drawn first, near moon second, so the near moon naturally occludes the far one
        // on the rare nights their arc positions align - no extra masking code, just draw order.
        public void DrawMoons(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState, float zoom = 1f, float cameraPanX = 0f)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || moonPhaseEffect == null)
                return;

            DrawMoonGlow(spriteBatch, screenWidth, screenHeight, skyState.FarMoon, MoonDefinition.FarMoon, zoom, cameraPanX);
            DrawMoonGlow(spriteBatch, screenWidth, screenHeight, skyState.NearMoon, MoonDefinition.NearMoon, zoom, cameraPanX);
        }

        private void DrawMoonGlow(
            SpriteBatch spriteBatch,
            int screenWidth,
            int screenHeight,
            MoonState moonState,
            MoonDefinition definition,
            float zoom,
            float cameraPanX)
        {
            if (moonState.Opacity <= 0.01f)
                return;

            Vector2 position = GetArcPosition(screenWidth, screenHeight, moonState.Progress);

            // Parallax: distant things drift backward relative to how far the camera has panned,
            // scaled down by ParallaxFactor - the far moon (small factor) barely moves, the near
            // moon (bigger factor) shifts a bit more, unlike the rest of the sky which never scrolls.
            position.X -= cameraPanX * definition.ParallaxFactor;

            float radius = definition.DiscRadius * zoom;

            moonPhaseEffect.Parameters["MatrixTransform"]?.SetValue(CreateScreenMatrixTransform(spriteBatch.GraphicsDevice));
            moonPhaseEffect.Parameters["MoonPosition"]?.SetValue(position);
            moonPhaseEffect.Parameters["MoonRadius"]?.SetValue(radius);
            moonPhaseEffect.Parameters["MoonColor"]?.SetValue(definition.Color.ToVector4());
            moonPhaseEffect.Parameters["Opacity"]?.SetValue(moonState.Opacity);
            moonPhaseEffect.Parameters["Phase01"]?.SetValue(moonState.Phase01);
            moonPhaseEffect.Parameters["ResidualGlow"]?.SetValue(definition.ResidualGlow);
            moonPhaseEffect.Parameters["BloomRadius"]?.SetValue(definition.BloomRadiusMultiplier);
            moonPhaseEffect.Parameters["BloomIntensity"]?.SetValue(definition.BloomIntensity);

            Rectangle fullScreen = new Rectangle(0, 0, screenWidth, screenHeight);

            moonPhaseEffect.CurrentTechnique = moonPhaseEffect.Techniques["MoonBloom"];
            spriteBatch.Begin(blendState: BlendState.Additive, samplerState: SamplerState.LinearClamp, effect: moonPhaseEffect);
            spriteBatch.Draw(pixel, fullScreen, Color.White);
            spriteBatch.End();

            moonPhaseEffect.CurrentTechnique = moonPhaseEffect.Techniques["MoonDisc"];
            spriteBatch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.LinearClamp, effect: moonPhaseEffect);
            spriteBatch.Draw(pixel, fullScreen, Color.White);
            spriteBatch.End();
        }

        private void DrawFog(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState)
        {
            if (skyState.FogOpacity <= 0.01f)
                return;

            int fogHeight = System.Math.Max(48, screenHeight / 9);
            int y = (int)(screenHeight * 0.47f);
            spriteBatch.Draw(pixel, new Rectangle(0, y, screenWidth, fogHeight), skyState.FogColor * skyState.FogOpacity * 0.22f);
        }

        private void DrawRainLayers(SpriteBatch spriteBatch, int screenWidth, int screenHeight, SkyState skyState, RainLayer[] layers, float zoom)
        {
            if (skyState.RainIntensity <= 0.01f)
                return;

            // Wind (0..1, ~0.05 calm to ~0.75 storm-active) steers the fall angle continuously
            // instead of drops always falling straight down.
            float windAngleRadians = (skyState.Wind - 0.4f) * MaxRainWindAngleRadians;
            float sinAngle = MathF.Sin(windAngleRadians);
            float cosAngle = MathF.Cos(windAngleRadians);

            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                DrawRainLayer(spriteBatch, screenWidth, screenHeight, skyState, layers[layerIndex], sinAngle, cosAngle, zoom);
        }

        private void DrawRainLayer(
            SpriteBatch spriteBatch,
            int screenWidth,
            int screenHeight,
            SkyState skyState,
            RainLayer layer,
            float sinAngle,
            float cosAngle,
            float zoom)
        {
            int drops = (int)(layer.DropCount * MathHelper.Clamp(skyState.RainIntensity, 0f, 1f));
            Color rainColor = layer.Tint * MathHelper.Clamp(layer.AlphaScale * (0.55f + skyState.RainIntensity * 0.6f), 0f, 1f);
            float fall = skyState.VisualTimeSeconds * (layer.FallSpeed + (skyState.Wetness * 80f));
            int width = System.Math.Max(1, (int)(layer.Width * zoom));
            int height = (int)(layer.Height * zoom);

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
                    int segX = x + (int)(sinAngle * height * t);
                    int segY = y + (int)(cosAngle * height * t);
                    Color segColor = s == 0 ? rainColor : rainColor * 0.6f;
                    spriteBatch.Draw(pixel, new Rectangle(segX, segY, width, System.Math.Max(2, height / segments)), segColor);
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

        // Mirrors the orthographic projection SpriteBatch's own built-in effect uses for
        // screen-space (identity-transform) draws, since a custom Effect has to be told this
        // explicitly - SpriteBatch only auto-fills its internal default effect's matrix, not ours.
        private static Matrix CreateScreenMatrixTransform(GraphicsDevice graphicsDevice)
        {
            Viewport viewport = graphicsDevice.Viewport;
            Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, 0, -1, out Matrix projection);

            if (graphicsDevice.UseHalfPixelOffset)
            {
                projection.M41 += -0.5f * projection.M11;
                projection.M42 += -0.5f * projection.M22;
            }

            return projection;
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
