using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World.Generation;
using System;

namespace Nyvorn.Source.Engine.Graphics
{
    /// <summary>
    /// P2-E-BG0: DEBUG ONLY — Renders temporary flat-color backdrops for Cavern and DeepCavern.
    ///
    /// This is a visual-only debugging tool. It draws NO-OP rectangles on the backbuffer
    /// to prove the layer depth concept works geometrically.
    ///
    /// Does NOT affect lighting, classification, or any game system.
    /// Does NOT allocate resources (uses 1x1 pixel texture from graphicsDevice).
    /// </summary>
    public class SubterraneanBackdropDebugRenderer
    {
        private readonly WorldLayerDefinition[] _layerDefinitions;
        private readonly Camera2D _camera;
        private readonly int _tileSize;

        // Debug colors - INTENTIONALLY BRIGHT for visibility
        private static readonly Color CavernDebugColor = new Color(90, 60, 40, 255);      // Brown
        private static readonly Color DeepDebugColor = new Color(55, 50, 45, 255);        // Darker gray-brown

        public SubterraneanBackdropDebugRenderer(
            WorldLayerDefinition[] layerDefinitions,
            Camera2D camera,
            int tileSize)
        {
            _layerDefinitions = layerDefinitions ?? Array.Empty<WorldLayerDefinition>();
            _camera = camera;
            _tileSize = tileSize;
        }

        /// <summary>
        /// Draw debug backdrop bands for Cavern and DeepCavern layers.
        /// World-anchored: bands appear at correct world Y, not based on camera center.
        ///
        /// Called from PlayingState.DrawGameplayWorld() AFTER DrawAtmosphericBackground,
        /// BEFORE PixelComposite/WorldColorRenderTarget are composited.
        ///
        /// Backbuffer must be active (SetRenderTarget(null)).
        /// No Clear() has happened since PHASE 1A, so sky is still visible below.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, int screenW, int screenH)
        {
            if (_layerDefinitions.Length == 0 || _camera == null)
                return;

            // Create 1x1 white pixel for drawing rectangles
            var pixelTexture = new Texture2D(graphicsDevice, 1, 1);
            pixelTexture.SetData(new[] { Color.White });

            float cameraWorldTop = _camera.Position.Y;
            float cameraWorldBottom = _camera.Position.Y + (screenH / _camera.Zoom);
            float cameraWorldLeft = _camera.Position.X;
            float cameraWorldRight = _camera.Position.X + (screenW / _camera.Zoom);

            // For each layer, check if it's visible and draw a band
            foreach (var layer in _layerDefinitions)
            {
                // Skip non-subterranean layers
                if (layer.LayerType != WorldLayerType.Cavern && layer.LayerType != WorldLayerType.DeepCavern)
                    continue;

                // Layer bounds in world pixels
                float layerWorldTop = layer.StartY * _tileSize;
                float layerWorldBottom = (layer.EndY + 1) * _tileSize;

                // Clip to visible viewport
                float visibleWorldTop = Math.Max(layerWorldTop, cameraWorldTop);
                float visibleWorldBottom = Math.Min(layerWorldBottom, cameraWorldBottom);

                if (visibleWorldTop >= visibleWorldBottom)
                    continue;  // Layer not visible in viewport

                // Convert world coordinates to screen coordinates
                // screenY = (worldY - cameraTop) * zoom
                float screenTop = (visibleWorldTop - cameraWorldTop) * _camera.Zoom;
                float screenBottom = (visibleWorldBottom - cameraWorldTop) * _camera.Zoom;

                int drawX = 0;
                int drawY = (int)screenTop;
                int drawW = screenW;
                int drawH = (int)(screenBottom - screenTop);

                Color color = GetColorForLayer(layer.LayerType);

                spriteBatch.Begin(samplerState: SamplerState.PointClamp);
                spriteBatch.Draw(
                    pixelTexture,
                    new Rectangle(drawX, drawY, drawW, drawH),
                    color);
                spriteBatch.End();
            }

            pixelTexture.Dispose();
        }

        private Color GetColorForLayer(WorldLayerType layerType)
        {
            return layerType switch
            {
                WorldLayerType.Cavern => CavernDebugColor,
                WorldLayerType.DeepCavern => DeepDebugColor,
                _ => Color.Transparent
            };
        }
    }
}
