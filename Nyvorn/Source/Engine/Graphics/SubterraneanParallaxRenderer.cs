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
        public Color CavernTint { get; set; } = new Color(115, 128, 140);  // (0.45, 0.50, 0.55)
        public Color DeepTint { get; set; } = new Color(64, 77, 89);       // (0.25, 0.30, 0.35)

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
        /// Draw parallax layers for Cavern and DeepCavern.
        /// World-anchored: each layer stays within its bounds regardless of camera.
        /// Backbuffer must be active. Called AFTER DrawAtmosphericBackground, BEFORE PixelComposite.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, int screenW, int screenH)
        {
            if (_layerDefinitions.Length == 0 || _camera == null)
                return;

            float cameraWorldTop = _camera.Position.Y;
            float cameraWorldBottom = _camera.Position.Y + (screenH / _camera.Zoom);
            float cameraPanX = _camera.Position.X;

            // For each subterranean layer (Cavern, DeepCavern)
            foreach (var layer in _layerDefinitions)
            {
                if (layer.LayerType != WorldLayerType.Cavern && layer.LayerType != WorldLayerType.DeepCavern)
                    continue;

                // Layer bounds in world pixels
                float layerWorldTop = layer.StartY * _tileSize;
                float layerWorldBottom = (layer.EndY + 1) * _tileSize;

                // Clip to visible viewport
                float visibleWorldTop = Math.Max(layerWorldTop, cameraWorldTop);
                float visibleWorldBottom = Math.Min(layerWorldBottom, cameraWorldBottom);

                if (visibleWorldTop >= visibleWorldBottom)
                    continue;  // Layer not visible

                // Convert world coordinates to screen coordinates
                float screenTop = (visibleWorldTop - cameraWorldTop) * _camera.Zoom;
                float screenBottom = (visibleWorldBottom - cameraWorldTop) * _camera.Zoom;
                int drawHeight = (int)(screenBottom - screenTop);

                // Determine tint for this layer type (P2-E-BG3)
                Color layerTint = layer.LayerType == WorldLayerType.Cavern ? CavernTint : DeepTint;

                // Draw all 6 parallax layers for this band
                for (int i = 0; i < 6; i++)
                {
                    if (_parallaxTextures[i] == null)
                        continue;

                    DrawParallaxLayer(
                        spriteBatch,
                        _parallaxTextures[i],
                        screenW,
                        (int)screenTop,
                        drawHeight,
                        _parallaxFactors[i],
                        cameraPanX,
                        _camera.Zoom,
                        layerTint);
                }
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
