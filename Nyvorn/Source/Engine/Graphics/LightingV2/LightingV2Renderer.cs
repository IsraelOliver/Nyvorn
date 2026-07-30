using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Renderer for Lighting V2 - handles all GPU-side scene composition and effects.
    ///
    /// PHASE 2 RESPONSIBILITIES (Composition Neutral):
    /// - Phase 0: Prepare scene RenderTarget (clear)
    /// - Phase 1: Draw atmosphere (sky, sun, moons)
    /// - Phase 2: Draw world (terrain, decorations, water)
    /// - Phase 3: Draw entities (player, enemies)
    /// - Phase 5: Composite scene to backbuffer (with neutral white light)
    ///
    /// PHASE 2 CONSTRAINTS:
    /// - Does NOT calculate light propagation
    /// - Does NOT manage resources
    /// - Uses Color.White for all lighting (neutral)
    /// - Does NOT call legacy lighting system
    /// - Preserves camera, zoom, wrapping
    ///
    /// LIFECYCLE:
    /// - Constructor: initialize empty
    /// - OnResize: propagate viewport changes to resources
    /// - Phase0_BeginSceneRender: set scene RenderTarget as active
    /// - Phase1_DrawAtmosphere: render sky
    /// - Phase2_DrawWorld: render terrain, decorations, water
    /// - Phase3_DrawEntities: render player, enemies
    /// - EndSceneRender: restore backbuffer
    /// - Phase5_DrawSceneToBackbuffer: present scene to screen
    /// </summary>
    public sealed class LightingV2Renderer
    {
        private readonly GraphicsDevice graphicsDevice;
        private readonly LightingV2Resources resources;
        private readonly LightingV2DebugState debugState;

        private int lastViewportWidth;
        private int lastViewportHeight;
        private bool debugSceneRenderTarget = false;

        public LightingV2Renderer(
            GraphicsDevice graphicsDevice,
            LightingV2Resources resources,
            LightingV2DebugState debugState = null)
        {
            this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            this.debugState = debugState ?? new LightingV2DebugState();

            lastViewportWidth = 0;
            lastViewportHeight = 0;
        }

        /// <summary>Enable or disable debug visualization of scene RenderTarget.</summary>
        public bool DebugSceneRenderTarget
        {
            get => debugSceneRenderTarget;
            set => debugSceneRenderTarget = value;
        }

        /// <summary>Handle viewport resize - ensure resources are compatible.</summary>
        public void OnResize(int logicalViewWidth, int logicalViewHeight)
        {
            if (logicalViewWidth <= 0 || logicalViewHeight <= 0)
                return;

            if (logicalViewWidth != lastViewportWidth || logicalViewHeight != lastViewportHeight)
            {
                resources.OnResize(logicalViewWidth, logicalViewHeight);
                lastViewportWidth = logicalViewWidth;
                lastViewportHeight = logicalViewHeight;
            }
        }

        /// <summary>
        /// PHASE 0: Prepare scene RenderTarget for rendering.
        /// Set as active render target and clear to black.
        /// </summary>
        public void Phase0_BeginSceneRender()
        {
            RenderTarget2D sceneTarget = resources.SceneRenderTarget;
            if (sceneTarget == null)
                return;

            graphicsDevice.SetRenderTarget(sceneTarget);
            graphicsDevice.Clear(Color.Black);
        }

        /// <summary>
        /// End scene rendering and restore backbuffer.
        /// Called after all world-space content is rendered.
        /// </summary>
        public void EndSceneRender()
        {
            graphicsDevice.SetRenderTarget(null);
        }

        /// <summary>
        /// PHASE 5: Composite scene RenderTarget to backbuffer with neutral lighting.
        /// In PHASE 2, lighting is always white (no occlusion).
        /// Uses PointClamp sampling to preserve pixel art aesthetics.
        /// </summary>
        public void Phase5_CompositeSceneToBackbuffer(SpriteBatch spriteBatch, Rectangle destRect)
        {
            RenderTarget2D sceneTarget = resources.SceneRenderTarget;
            if (sceneTarget == null || spriteBatch == null)
                return;

            spriteBatch.Begin(
                sortMode: SpriteSortMode.Immediate,
                blendState: BlendState.Opaque,
                samplerState: SamplerState.PointClamp,
                effect: null);
            spriteBatch.Draw(sceneTarget, destRect, Color.White);
            spriteBatch.End();
        }

        /// <summary>
        /// Debug: Visualize the scene RenderTarget on screen for verification.
        /// Used to validate composition, transparency, wrapping, and camera tracking.
        /// </summary>
        public void DrawDebugSceneRenderTarget(SpriteBatch spriteBatch, Rectangle screenBounds)
        {
            if (!debugSceneRenderTarget || spriteBatch == null)
                return;

            RenderTarget2D sceneTarget = resources.SceneRenderTarget;
            if (sceneTarget == null)
                return;

            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
            spriteBatch.Draw(sceneTarget, screenBounds, Color.White);
            spriteBatch.End();
        }

        /// <summary>
        /// Visualize a debug lighting layer for development (PHASES 3+).
        /// Not implemented in PHASE 2.
        /// </summary>
        public void DrawDebugLightingLayer(SpriteBatch spriteBatch, Rectangle screenBounds, LightingDebugLayer layer)
        {
            if (layer == LightingDebugLayer.None || spriteBatch == null)
                return;

            // PHASE 3+: Implement per-layer visualization
            // - AmbientSkyLight: show ambient map
            // - DirectSunlight: show sunlight map
            // - LocalEnvironmentLight: show torch map
            // - etc.
        }
    }
}
