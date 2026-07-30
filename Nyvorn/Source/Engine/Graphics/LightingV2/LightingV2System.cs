using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingV2
{
    /// <summary>
    /// Lighting V2 system - coordinates light calculations without performing rendering.
    ///
    /// RESPONSIBILITIES:
    /// - Orchestrates per-frame light computations
    /// - Manages dirty flags and incremental updates
    /// - Does NOT render, create resources, or know about gameplay details
    /// - Does NOT depend on PlayingState or similar high-level systems
    ///
    /// DEPENDENCIES (via interfaces only):
    /// - IWorldDataProvider: query tiles
    /// - ISunCycleProvider: query sun position and color
    /// - ILocalLightRegistry: enumerate light sources
    /// - IMapChangeListener: react to tile changes
    ///
    /// LIFECYCLE:
    /// - Initialize: set up providers and settings
    /// - Update: compute light data (to be filled in by subphases)
    /// - Resize: propagate viewport changes
    /// - GetActiveRegion: expose computed lighting region
    /// </summary>
    public sealed class LightingV2System : IMapChangeListener
    {
        private readonly IWorldDataProvider worldDataProvider;
        private readonly ISunCycleProvider sunCycleProvider;
        private readonly ILocalLightRegistry localLightRegistry;
        private readonly LightingV2Settings settings;
        private readonly LightingV2DebugState debugState;

        private Vector2 lastCameraPosition;
        private bool isDirty;
        private int activeRegionTileX;
        private int activeRegionTileY;
        private int activeRegionTileWidth;
        private int activeRegionTileHeight;

        public LightingV2System(
            IWorldDataProvider worldDataProvider,
            ISunCycleProvider sunCycleProvider,
            ILocalLightRegistry localLightRegistry,
            LightingV2Settings settings = null,
            LightingV2DebugState debugState = null)
        {
            this.worldDataProvider = worldDataProvider ?? throw new ArgumentNullException(nameof(worldDataProvider));
            this.sunCycleProvider = sunCycleProvider ?? throw new ArgumentNullException(nameof(sunCycleProvider));
            this.localLightRegistry = localLightRegistry ?? throw new ArgumentNullException(nameof(localLightRegistry));
            this.settings = settings ?? new LightingV2Settings();
            this.debugState = debugState ?? new LightingV2DebugState();

            lastCameraPosition = Vector2.Zero;
            isDirty = true;
        }

        /// <summary>
        /// Active region for lighting computation - all light maps cover this area.
        /// Updated once per frame in Update().
        /// </summary>
        public int ActiveRegionTileX => activeRegionTileX;
        public int ActiveRegionTileY => activeRegionTileY;
        public int ActiveRegionTileWidth => activeRegionTileWidth;
        public int ActiveRegionTileHeight => activeRegionTileHeight;

        /// <summary>
        /// Determine if active region needs recalculation.
        /// </summary>
        public bool IsDirty => isDirty;

        /// <summary>
        /// Update light calculations based on current world state.
        /// Subphases (AmbientSkyLight, DirectSunlight, LocalLights) fill in specific maps.
        /// PHASE 1: structure only, no actual computation.
        /// </summary>
        public void Update(
            float dt,
            Vector2 cameraPosition,
            float cameraZoom,
            int screenWidth,
            int screenHeight)
        {
            if (worldDataProvider == null || screenWidth <= 0 || screenHeight <= 0 || cameraZoom <= 0f)
                return;

            // Compute active region based on camera view
            ComputeActiveRegion(cameraPosition, cameraZoom, screenWidth, screenHeight);

            // Check dirty flags for optimization (PHASE 12)
            EvaluateDirtyFlags(cameraPosition);

            // Reset metrics
            debugState?.ResetMetrics();

            // Subphases will fill in actual calculations here (PHASES 3+)
            // For now, this is just framework
        }

        /// <summary>
        /// Handle map change - mark as dirty so next Update recalculates.
        /// </summary>
        public void OnForegroundTileChanged(int tileX, int tileY)
        {
            isDirty = true;
        }

        /// <summary>
        /// Handle background tile change - mark as dirty.
        /// </summary>
        public void OnBackgroundTileChanged(int tileX, int tileY)
        {
            isDirty = true;
        }

        private void ComputeActiveRegion(Vector2 cameraPosition, float cameraZoom, int screenWidth, int screenHeight)
        {
            int tileSize = worldDataProvider.TileSize;

            // Compute visible tile range
            float viewWidth = screenWidth / cameraZoom;
            float viewHeight = screenHeight / cameraZoom;

            int startTileX = (int)MathF.Floor(cameraPosition.X / tileSize) - settings.PropagationMarginTiles;
            int endTileX = (int)MathF.Ceiling((cameraPosition.X + viewWidth) / tileSize) + settings.PropagationMarginTiles;
            int startTileY = Math.Clamp(
                (int)MathF.Floor(cameraPosition.Y / tileSize) - settings.PropagationMarginTiles,
                0,
                worldDataProvider.WorldHeight - 1);
            int endTileY = Math.Clamp(
                (int)MathF.Ceiling((cameraPosition.Y + viewHeight) / tileSize) + settings.PropagationMarginTiles,
                0,
                worldDataProvider.WorldHeight - 1);

            if (endTileX < startTileX || endTileY < startTileY)
            {
                activeRegionTileWidth = 0;
                activeRegionTileHeight = 0;
                return;
            }

            activeRegionTileX = startTileX;
            activeRegionTileY = startTileY;
            activeRegionTileWidth = endTileX - startTileX + 1;
            activeRegionTileHeight = endTileY - startTileY + 1;
        }

        private void EvaluateDirtyFlags(Vector2 cameraPosition)
        {
            // PHASE 12: implement proper dirty flagging
            // For now, mark dirty if camera moved significantly
            float distance = Vector2.Distance(cameraPosition, lastCameraPosition);
            if (distance > settings.CameraMovementThresholdTiles * worldDataProvider.TileSize)
            {
                isDirty = true;
                lastCameraPosition = cameraPosition;
            }

            // Mark dirty if sun direction changed significantly (PHASE 4)
            // For now, just placeholder
        }
    }
}
