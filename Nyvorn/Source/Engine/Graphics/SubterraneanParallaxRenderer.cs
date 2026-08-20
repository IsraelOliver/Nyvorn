using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World.Generation;
using System;

namespace Nyvorn.Source.Engine.Graphics
{
    /// <summary>
    /// P2-E-BG2: Renders subterranean parallax backgrounds for Cavern and DeepCavern layers.
    ///
    /// Uses 6 parallax layers with depth-based factors:
    /// Layer 1 (parallax 0.1) = farthest
    /// Layer 2 (parallax 0.2)
    /// Layer 4 (parallax 0.4)
    /// Layer 5 (parallax 0.6)
    /// Layer 7 (parallax 0.8)
    /// Layer 9 (parallax 1.0) = nearest
    ///
    /// World-anchored per layer (Cavern and DeepCavern stay in their world Y bounds).
    /// Horizontal parallax only; vertical parallax disabled for stability.
    /// </summary>
    public class SubterraneanParallaxRenderer
    {
        private readonly Texture2D[] _parallaxTextures;  // 6 layers in order 1,2,4,5,7,9
        private readonly float[] _parallaxFactors;        // Corresponding parallax factors
        private readonly WorldLayerDefinition[] _layerDefinitions;
        private readonly Camera2D _camera;
        private readonly int _tileSize;

        // Parallax factors: lower = farther, higher = nearer
        private static readonly float[] ParallaxFactors = { 0.1f, 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };

        // P2-E-BG3: Color tints for subterranean backdrop darkness
        // RGB multipliers applied to each biome's parallax layers
        // S4.1-BG: Debug tints - visually distinct for validation
        public Color CavernTint { get; set; } = new Color(80, 115, 140);   // Debug: cold blue-petrol
        public Color DeepTint { get; set; } = new Color(140, 80, 60);      // Debug: warm red-earth

        public SubterraneanParallaxRenderer(
            Texture2D[] parallaxTextures,
            WorldLayerDefinition[] layerDefinitions,
            Camera2D camera,
            int tileSize)
        {
            if (parallaxTextures == null || parallaxTextures.Length != 6)
                throw new ArgumentException("Must provide exactly 6 parallax textures");
            if (layerDefinitions == null)
                throw new ArgumentNullException(nameof(layerDefinitions));
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            _parallaxTextures = parallaxTextures;
            _layerDefinitions = layerDefinitions;
            _camera = camera;
            _tileSize = tileSize;
            _parallaxFactors = ParallaxFactors;
        }

        /// <summary>
        /// Draw parallax layers for the active subterranean environment only.
        ///
        /// S3-BG: Cavern parallax renders full-viewport (no band clipping).
        /// S4-BG: DeepCavern parallax renders full-viewport (no band clipping).
        /// S4.2-BG: Draw only the active layer (Cavern or DeepCavern), not both.
        ///
        /// Backbuffer must be active. Called AFTER DrawAtmosphericBackground, BEFORE PixelComposite.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, int screenW, int screenH, WorldLayerType activeLayer, float alpha = 1f)
        {
            if (_layerDefinitions.Length == 0 || _camera == null)
                return;

            // S4.2-BG: Only draw the active parallax environment
            if (activeLayer != WorldLayerType.Cavern && activeLayer != WorldLayerType.DeepCavern)
                return;  // No subterranean parallax for non-cave layers

            float cameraPanX = _camera.Position.X;
            Color layerTint = activeLayer == WorldLayerType.Cavern ? CavernTint : DeepTint;

            // S6.2-BG: Apply alpha to tint for crossfade (Cave ↔ Deep)
            // S6.2.1-BG: Fix premultiplied alpha mismatch (BlendState.AlphaBlend requires RGB * alpha)
            float alphaValue = System.Math.Clamp(alpha, 0f, 1f);
            Color tintWithAlpha = layerTint * alphaValue;

            // Draw all 6 parallax layers for FULL VIEWPORT of the active layer only
            for (int i = 0; i < 6; i++)
            {
                if (_parallaxTextures[i] == null)
                    continue;

                DrawParallaxLayer(
                    spriteBatch,
                    _parallaxTextures[i],
                    screenW,
                    0,          // screenTop = 0 (top of viewport)
                    screenH,    // drawHeight = full screen height (no clipping)
                    _parallaxFactors[i],
                    cameraPanX,
                    _camera.Zoom,
                    tintWithAlpha);
            }
        }

        /// <summary>
        /// Draw a single parallax layer using tiling to fill the viewport horizontally.
        /// parallaxFactor: 0.0 = no movement, 1.0 = full camera movement
        /// tint: Color to modulate the texture (P2-E-BG3: for ambient darkness)
        /// </summary>
        private static void DrawParallaxLayer(
            SpriteBatch spriteBatch,
            Texture2D texture,
            int screenWidth,
            int screenY,
            int screenHeight,
            float parallaxFactor,
            float cameraPanX,
            float zoom,
            Color tint)
        {
            if (screenHeight <= 0)
                return;

            int destWidth = (int)(texture.Width * zoom);
            int destHeight = screenHeight;

            // Calculate horizontal scroll based on parallax factor
            float scrollX = -(cameraPanX * parallaxFactor) % destWidth;
            if (scrollX > 0f)
                scrollX -= destWidth;

            // Tile the texture horizontally to fill the screen (P2-E-BG3: with tint applied)
            spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
            for (int x = (int)scrollX; x < screenWidth; x += destWidth)
            {
                spriteBatch.Draw(
                    texture,
                    new Rectangle(x, screenY, destWidth, destHeight),
                    tint);
            }
            spriteBatch.End();
        }
    }
}
