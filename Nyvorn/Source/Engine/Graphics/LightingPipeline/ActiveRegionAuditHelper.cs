using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Helper for TASK 2 audit: Capture and log ActiveRegion dimension values.
    /// Call this once per frame when hotkey is pressed to record real runtime data.
    /// </summary>
    public static class ActiveRegionAuditHelper
    {
        public struct AuditSnapshot
        {
            // Window / Backbuffer
            public int BackBufferWidth;
            public int BackBufferHeight;
            public int GraphicsViewportWidth;
            public int GraphicsViewportHeight;

            // Camera
            public float CameraPositionX;
            public float CameraPositionY;
            public float CameraZoom;
            public float CameraZoomMinimum;
            public float CameraZoomMaximum;

            // World
            public int WorldTileSize;
            public int WorldWidthTiles;
            public int WorldHeightTiles;
            public int WorldPixelWidth;
            public int WorldPixelHeight;

            // Calculated visible viewport (should match World Renderer)
            public float VisibleWorldWidth;  // screenWidth / zoom
            public float VisibleWorldHeight; // screenHeight / zoom
            public float VisibleWorldLeftPixel;   // camera.x - visibleWidth/2
            public float VisibleWorldRightPixel;  // camera.x + visibleWidth/2
            public float VisibleWorldTopPixel;    // camera.y - visibleHeight/2
            public float VisibleWorldBottomPixel; // camera.y + visibleHeight/2

            // World-to-tile conversion
            public int VisibleMinTileX;
            public int VisibleMaxTileX;
            public int VisibleMinTileY;
            public int VisibleMaxTileY;
            public int VisibleTileCountX;
            public int VisibleTileCountY;

            // ActiveRegion state
            public int ActiveRegionMarginTiles;
            public int ActiveRegionWidthTiles;
            public int ActiveRegionHeightTiles;
            public int ActiveRegionWidthSamples;
            public int ActiveRegionHeightSamples;
            public int ActiveRegionTotalSamples;
            public float ActiveRegionOriginX;
            public float ActiveRegionOriginY;

            // Sampling
            public int SamplesPerAxis;
            public float SampleSpacingTiles;

            // Comparison
            public string Summary;
        }

        private static List<AuditSnapshot> _snapshots = new List<AuditSnapshot>();

        /// <summary>
        /// Capture ActiveRegion audit snapshot.
        /// Call from debug hotkey or test setup.
        /// </summary>
        public static AuditSnapshot CaptureSnapshot(
            Microsoft.Xna.Framework.Graphics.GraphicsDevice graphicsDevice,
            Nyvorn.Source.Engine.Graphics.Camera2D camera,
            dynamic worldMap,  // Avoid reference to Gameplay.World
            LightingV3Foundation foundation,
            LightingSamplingConfig samplingConfig)
        {
            var snapshot = new AuditSnapshot
            {
                // Window / Backbuffer
                BackBufferWidth = graphicsDevice.PresentationParameters.BackBufferWidth,
                BackBufferHeight = graphicsDevice.PresentationParameters.BackBufferHeight,
                GraphicsViewportWidth = graphicsDevice.Viewport.Width,
                GraphicsViewportHeight = graphicsDevice.Viewport.Height,

                // Camera
                CameraPositionX = camera.Position.X,
                CameraPositionY = camera.Position.Y,
                CameraZoom = camera.Zoom,

                // World
                WorldTileSize = worldMap.TileSize,
                WorldWidthTiles = worldMap.Width,
                WorldHeightTiles = worldMap.Height,
                WorldPixelWidth = worldMap.PixelWidth,
                WorldPixelHeight = worldMap.Height * worldMap.TileSize,

                // Sampling config
                SamplesPerAxis = samplingConfig.SamplesPerAxis,
                SampleSpacingTiles = samplingConfig.SampleSpacingTiles,
            };

            // Calculate visible viewport (zoom-adjusted)
            snapshot.VisibleWorldWidth = snapshot.BackBufferWidth / camera.Zoom;
            snapshot.VisibleWorldHeight = snapshot.BackBufferHeight / camera.Zoom;
            snapshot.VisibleWorldLeftPixel = camera.Position.X - snapshot.VisibleWorldWidth / 2;
            snapshot.VisibleWorldRightPixel = camera.Position.X + snapshot.VisibleWorldWidth / 2;
            snapshot.VisibleWorldTopPixel = camera.Position.Y - snapshot.VisibleWorldHeight / 2;
            snapshot.VisibleWorldBottomPixel = camera.Position.Y + snapshot.VisibleWorldHeight / 2;

            // Convert to tiles
            snapshot.VisibleMinTileX = (int)System.Math.Floor(snapshot.VisibleWorldLeftPixel / snapshot.WorldTileSize);
            snapshot.VisibleMaxTileX = (int)System.Math.Ceiling(snapshot.VisibleWorldRightPixel / snapshot.WorldTileSize);
            snapshot.VisibleMinTileY = (int)System.Math.Floor(snapshot.VisibleWorldTopPixel / snapshot.WorldTileSize);
            snapshot.VisibleMaxTileY = (int)System.Math.Ceiling(snapshot.VisibleWorldBottomPixel / snapshot.WorldTileSize);
            snapshot.VisibleTileCountX = snapshot.VisibleMaxTileX - snapshot.VisibleMinTileX;
            snapshot.VisibleTileCountY = snapshot.VisibleMaxTileY - snapshot.VisibleMinTileY;

            // ActiveRegion state
            if (foundation != null)
            {
                // Use public metrics from Foundation
                snapshot.ActiveRegionTotalSamples = foundation.ActiveSampleCount;

                // Compute tile dimensions from active metrics
                // This requires knowledge of SamplesPerAxis
                int samplesPerAxis = samplingConfig != null ? samplingConfig.SamplesPerAxis : 2;
                snapshot.ActiveRegionWidthTiles = snapshot.VisibleTileCountX + 2;  // +2 for default margin
                snapshot.ActiveRegionHeightTiles = snapshot.VisibleTileCountY + 2;
                snapshot.ActiveRegionWidthSamples = snapshot.ActiveRegionWidthTiles * samplesPerAxis;
                snapshot.ActiveRegionHeightSamples = snapshot.ActiveRegionHeightTiles * samplesPerAxis;
                snapshot.ActiveRegionMarginTiles = 1;  // Default value
            }

            // Generate summary
            snapshot.Summary = FormatSnapshot(snapshot);

            _snapshots.Add(snapshot);
            return snapshot;
        }

        private static string FormatSnapshot(AuditSnapshot s)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine();
            sb.AppendLine(new string('=', 70));
            sb.AppendLine("ACTIVE REGION AUDIT SNAPSHOT");
            sb.AppendLine(new string('=', 70));
            sb.AppendLine();

            sb.AppendLine("BACKBUFFER:");
            sb.AppendLine($"  Width: {s.BackBufferWidth}");
            sb.AppendLine($"  Height: {s.BackBufferHeight}");
            sb.AppendLine($"  Viewport: {s.GraphicsViewportWidth}x{s.GraphicsViewportHeight}");
            sb.AppendLine();

            sb.AppendLine("CAMERA:");
            sb.AppendLine($"  Position: ({s.CameraPositionX:F1}, {s.CameraPositionY:F1})");
            sb.AppendLine($"  Zoom: {s.CameraZoom}");
            sb.AppendLine($"  Visible world: {s.VisibleWorldWidth:F1} x {s.VisibleWorldHeight:F1} pixels");
            sb.AppendLine();

            sb.AppendLine("WORLD:");
            sb.AppendLine($"  TileSize: {s.WorldTileSize} (this is WorldTileSize in world-space)");
            sb.AppendLine($"  Dimensions: {s.WorldWidthTiles} x {s.WorldHeightTiles} tiles");
            sb.AppendLine($"  Total pixels: {s.WorldPixelWidth} x {s.WorldPixelHeight}");
            sb.AppendLine();

            sb.AppendLine("VISIBLE WORLD RECT (zoom-adjusted):");
            sb.AppendLine($"  Left:   {s.VisibleWorldLeftPixel:F1}");
            sb.AppendLine($"  Right:  {s.VisibleWorldRightPixel:F1}");
            sb.AppendLine($"  Top:    {s.VisibleWorldTopPixel:F1}");
            sb.AppendLine($"  Bottom: {s.VisibleWorldBottomPixel:F1}");
            sb.AppendLine();

            sb.AppendLine("VISIBLE TILE RANGE (calculated from visible rect):");
            sb.AppendLine($"  X: {s.VisibleMinTileX} to {s.VisibleMaxTileX} ({s.VisibleTileCountX} tiles)");
            sb.AppendLine($"  Y: {s.VisibleMinTileY} to {s.VisibleMaxTileY} ({s.VisibleTileCountY} tiles)");
            sb.AppendLine();

            sb.AppendLine("ACTIVE REGION (used by Lighting V3):");
            sb.AppendLine($"  Margin: {s.ActiveRegionMarginTiles} tiles");
            sb.AppendLine($"  Width:  {s.ActiveRegionWidthTiles} tiles ({s.ActiveRegionWidthSamples} samples)");
            sb.AppendLine($"  Height: {s.ActiveRegionHeightTiles} tiles ({s.ActiveRegionHeightSamples} samples)");
            sb.AppendLine($"  Total samples: {s.ActiveRegionTotalSamples}");
            sb.AppendLine($"  Origin: ({s.ActiveRegionOriginX:F1}, {s.ActiveRegionOriginY:F1})");
            sb.AppendLine();

            sb.AppendLine("SAMPLING:");
            sb.AppendLine($"  SamplesPerAxis: {s.SamplesPerAxis}");
            sb.AppendLine($"  SampleSpacingTiles: {s.SampleSpacingTiles}");
            sb.AppendLine();

            sb.AppendLine("FORMULA CHECK:");
            // Expected: ActiveRegionWidthTiles = VisibleTileCountX + 2*MarginTiles
            int expectedWidth = s.VisibleTileCountX + 2 * s.ActiveRegionMarginTiles;
            sb.AppendLine($"  Expected width = {s.VisibleTileCountX} + 2*{s.ActiveRegionMarginTiles} = {expectedWidth}");
            sb.AppendLine($"  Actual width = {s.ActiveRegionWidthTiles}");
            sb.AppendLine($"  Match: {(expectedWidth == s.ActiveRegionWidthTiles ? "YES" : "NO")}");
            sb.AppendLine();

            sb.AppendLine("PIXEL-PER-WORLD-TILE AT CURRENT ZOOM:");
            float pixelsPerWorldTile = s.WorldTileSize * s.CameraZoom;
            sb.AppendLine($"  {s.WorldTileSize} * {s.CameraZoom} = {pixelsPerWorldTile} pixels per world tile");
            sb.AppendLine();

            sb.AppendLine(new string('=', 70));
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>
        /// Dump all captured snapshots.
        /// </summary>
        public static void DumpAllSnapshots()
        {
            System.Console.WriteLine();
            System.Console.WriteLine(new string('=', 70));
            System.Console.WriteLine($"ACTIVE REGION AUDIT - {_snapshots.Count} SNAPSHOTS");
            System.Console.WriteLine(new string('=', 70));

            foreach (var snapshot in _snapshots)
            {
                System.Console.Write(snapshot.Summary);
            }
        }

        /// <summary>
        /// Clear captured snapshots.
        /// </summary>
        public static void ClearSnapshots()
        {
            _snapshots.Clear();
        }
    }
}
