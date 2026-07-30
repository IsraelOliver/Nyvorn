using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Sole proprietor of Lighting V2's GPU resources (RenderTarget2D and Texture2D).
    ///
    /// OWNERSHIP:
    /// - Owns and manages all LightingV2-specific resources
    /// - Never creates resources per-frame
    /// - Implements safe allocation, resize, and disposal
    ///
    /// ALLOCATION STRATEGY:
    /// - RenderTarget2D: recreate when dimensions or format changes (not grow-only)
    /// - Small arrays/buffers: grow-only to avoid per-frame allocations
    /// - All resources initialized to null/empty
    ///
    /// LIFECYCLE:
    /// - Constructor: allocates nothing
    /// - EnsureResources: allocates/validates all resources for current view
    /// - Resize: updates dimensions and recreates incompatible resources
    /// - Dispose: releases all GPU memory
    /// </summary>
    public sealed class LightingV2Resources : IDisposable
    {
        private readonly GraphicsDevice graphicsDevice;
        private bool isDisposed;

        // Scene composition RenderTarget - recreates on resolution change
        private RenderTarget2D sceneRenderTarget;
        private int sceneRenderTargetWidth;
        private int sceneRenderTargetHeight;

        // Individual lighting maps (placeholder, created per-phase)
        private RenderTarget2D ambientSkyLightMap;
        private int ambientMapWidth;
        private int ambientMapHeight;

        private RenderTarget2D directSunlightMap;
        private int sunlightMapWidth;
        private int sunlightMapHeight;

        private RenderTarget2D localEnvironmentLightMap;
        private int localMapWidth;
        private int localMapHeight;

        // Small buffers (grow-only allocation)
        private Color[] lightingCellBuffer = System.Array.Empty<Color>();
        private float[] ambientChannelBuffer = System.Array.Empty<float>();
        private float[] sunlightChannelBuffer = System.Array.Empty<float>();

        public LightingV2Resources(GraphicsDevice graphicsDevice)
        {
            this.graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            isDisposed = false;
        }

        /// <summary>
        /// Ensure all resources are allocated and compatible with current view dimensions.
        /// Idempotent: safe to call every frame.
        /// Recreates resources only if incompatible.
        /// </summary>
        public void EnsureResources(int logicalViewWidth, int logicalViewHeight)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(LightingV2Resources));

            if (logicalViewWidth <= 0 || logicalViewHeight <= 0)
                return;

            // Scene RenderTarget tracks logical view dimensions exactly (not grow-only)
            if (sceneRenderTarget == null ||
                sceneRenderTargetWidth != logicalViewWidth ||
                sceneRenderTargetHeight != logicalViewHeight)
            {
                sceneRenderTarget?.Dispose();
                sceneRenderTarget = new RenderTarget2D(
                    graphicsDevice,
                    logicalViewWidth,
                    logicalViewHeight,
                    false,
                    SurfaceFormat.Color,
                    DepthFormat.None,
                    0,
                    RenderTargetUsage.DiscardContents);
                sceneRenderTargetWidth = logicalViewWidth;
                sceneRenderTargetHeight = logicalViewHeight;
            }

            // Individual lighting maps (grid-based, smaller than scene)
            // Size based on visible tiles + margin - will be determined per-phase
            EnsureAmbientSkyLightMap(logicalViewWidth, logicalViewHeight);
            EnsureDirectSunlightMap(logicalViewWidth, logicalViewHeight);
            EnsureLocalEnvironmentLightMap(logicalViewWidth, logicalViewHeight);

            // Small buffers with grow-only strategy
            EnsureColorBuffer(ref lightingCellBuffer, logicalViewWidth * logicalViewHeight);
        }

        /// <summary>
        /// Handle viewport resize - recreates resources as needed.
        /// </summary>
        public void OnResize(int logicalViewWidth, int logicalViewHeight)
        {
            EnsureResources(logicalViewWidth, logicalViewHeight);
        }

        /// <summary>
        /// Scene composition RenderTarget - recreates when dimensions change.
        /// </summary>
        public RenderTarget2D SceneRenderTarget
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(LightingV2Resources));
                return sceneRenderTarget;
            }
        }

        /// <summary>
        /// Ambient sky light map - recreates when tile grid dimensions change.
        /// </summary>
        public RenderTarget2D AmbientSkyLightMap
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(LightingV2Resources));
                return ambientSkyLightMap;
            }
        }

        /// <summary>
        /// Direct sunlight map - recreates when tile grid dimensions change.
        /// </summary>
        public RenderTarget2D DirectSunlightMap
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(LightingV2Resources));
                return directSunlightMap;
            }
        }

        /// <summary>
        /// Local environment light map (torches, etc) - recreates when tile grid dimensions change.
        /// </summary>
        public RenderTarget2D LocalEnvironmentLightMap
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(LightingV2Resources));
                return localEnvironmentLightMap;
            }
        }

        /// <summary>
        /// Small work buffer for lighting calculations (grow-only).
        /// </summary>
        public Color[] LightingCellBuffer
        {
            get
            {
                if (isDisposed)
                    throw new ObjectDisposedException(nameof(LightingV2Resources));
                return lightingCellBuffer;
            }
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            sceneRenderTarget?.Dispose();
            ambientSkyLightMap?.Dispose();
            directSunlightMap?.Dispose();
            localEnvironmentLightMap?.Dispose();

            sceneRenderTarget = null;
            ambientSkyLightMap = null;
            directSunlightMap = null;
            localEnvironmentLightMap = null;
            lightingCellBuffer = System.Array.Empty<Color>();
            ambientChannelBuffer = System.Array.Empty<float>();
            sunlightChannelBuffer = System.Array.Empty<float>();

            isDisposed = true;
        }

        private void EnsureAmbientSkyLightMap(int viewWidth, int viewHeight)
        {
            int mapWidth = viewWidth / 16; // Estimated tile grid size
            int mapHeight = viewHeight / 16;
            if (mapWidth < 1) mapWidth = 1;
            if (mapHeight < 1) mapHeight = 1;

            if (ambientSkyLightMap == null ||
                ambientMapWidth != mapWidth ||
                ambientMapHeight != mapHeight)
            {
                ambientSkyLightMap?.Dispose();
                ambientSkyLightMap = new RenderTarget2D(
                    graphicsDevice, mapWidth, mapHeight, false, SurfaceFormat.Vector4,
                    DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                ambientMapWidth = mapWidth;
                ambientMapHeight = mapHeight;
            }
        }

        private void EnsureDirectSunlightMap(int viewWidth, int viewHeight)
        {
            int mapWidth = viewWidth / 16;
            int mapHeight = viewHeight / 16;
            if (mapWidth < 1) mapWidth = 1;
            if (mapHeight < 1) mapHeight = 1;

            if (directSunlightMap == null ||
                sunlightMapWidth != mapWidth ||
                sunlightMapHeight != mapHeight)
            {
                directSunlightMap?.Dispose();
                directSunlightMap = new RenderTarget2D(
                    graphicsDevice, mapWidth, mapHeight, false, SurfaceFormat.Vector4,
                    DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                sunlightMapWidth = mapWidth;
                sunlightMapHeight = mapHeight;
            }
        }

        private void EnsureLocalEnvironmentLightMap(int viewWidth, int viewHeight)
        {
            int mapWidth = viewWidth / 16;
            int mapHeight = viewHeight / 16;
            if (mapWidth < 1) mapWidth = 1;
            if (mapHeight < 1) mapHeight = 1;

            if (localEnvironmentLightMap == null ||
                localMapWidth != mapWidth ||
                localMapHeight != mapHeight)
            {
                localEnvironmentLightMap?.Dispose();
                localEnvironmentLightMap = new RenderTarget2D(
                    graphicsDevice, mapWidth, mapHeight, false, SurfaceFormat.Vector4,
                    DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                localMapWidth = mapWidth;
                localMapHeight = mapHeight;
            }
        }

        private void EnsureColorBuffer(ref Color[] buffer, int requiredSize)
        {
            if (buffer.Length < requiredSize)
                buffer = new Color[requiredSize];
        }
    }
}
