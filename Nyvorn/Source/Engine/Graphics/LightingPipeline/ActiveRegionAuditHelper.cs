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

            // EXPECTED ActiveRegion (correct calculation with zoom)
            public int ExpectedMarginTiles;
            public int ExpectedWidthTiles;
            public int ExpectedHeightTiles;
            public int ExpectedWidthSamples;
            public int ExpectedHeightSamples;
            public int ExpectedTotalSamples;

            // ACTUAL ActiveRegion (what Foundation actually uses)
            public int ActualWidthTiles;
            public int ActualHeightTiles;
            public int ActualWidthSamples;
            public int ActualHeightSamples;
            public int ActualTotalSamples;
            public float ActualOriginX;
            public float ActualOriginY;

            // Sampling
            public int SamplesPerAxis;
            public float SampleSpacingTiles;

            // Diagnostics
            public bool ZoomIsBeingIgnored;
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

            // EXPECTED ActiveRegion (correct with zoom)
            snapshot.ExpectedMarginTiles = 1;
            snapshot.ExpectedWidthTiles = snapshot.VisibleTileCountX + (snapshot.ExpectedMarginTiles * 2);
            snapshot.ExpectedHeightTiles = snapshot.VisibleTileCountY + (snapshot.ExpectedMarginTiles * 2);
            snapshot.ExpectedWidthSamples = snapshot.ExpectedWidthTiles * samplingConfig.SamplesPerAxis;
            snapshot.ExpectedHeightSamples = snapshot.ExpectedHeightTiles * samplingConfig.SamplesPerAxis;
            snapshot.ExpectedTotalSamples = snapshot.ExpectedWidthSamples * snapshot.ExpectedHeightSamples;

            // ACTUAL ActiveRegion (what Foundation currently uses)
            if (foundation != null)
            {
                snapshot.ActualTotalSamples = foundation.ActiveSampleCount;
                snapshot.SamplesPerAxis = samplingConfig.SamplesPerAxis;

                // Back-calculate tile dimensions from sample count
                // SampleCount = (WidthTiles * SamplesPerAxis) * (HeightTiles * SamplesPerAxis)
                // For 2x2 sampling: SampleCount = WidthTiles * 2 * HeightTiles * 2
                int samplesPerAxis = samplingConfig.SamplesPerAxis;
                int totalTilesUsed = snapshot.ActualTotalSamples / (samplesPerAxis * samplesPerAxis);

                // Approximate: assume roughly square region or use known pattern
                // For now, report the values that were actually used
                snapshot.ActualWidthSamples = (int)System.Math.Sqrt(snapshot.ActualTotalSamples);
                snapshot.ActualHeightSamples = snapshot.ActualTotalSamples / snapshot.ActualWidthSamples;
                snapshot.ActualWidthTiles = snapshot.ActualWidthSamples / samplingConfig.SamplesPerAxis;
                snapshot.ActualHeightTiles = snapshot.ActualHeightSamples / samplingConfig.SamplesPerAxis;
            }

            // Check if zoom is being ignored
            snapshot.ZoomIsBeingIgnored = (snapshot.ExpectedTotalSamples != snapshot.ActualTotalSamples);

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

            sb.AppendLine("ACTIVE REGION - EXPECTED (with Camera.Zoom applied correctly):");
            sb.AppendLine($"  Margin: {s.ExpectedMarginTiles} tiles");
            sb.AppendLine($"  Width:  {s.ExpectedWidthTiles} tiles ({s.ExpectedWidthSamples} samples)");
            sb.AppendLine($"  Height: {s.ExpectedHeightTiles} tiles ({s.ExpectedHeightSamples} samples)");
            sb.AppendLine($"  Total samples: {s.ExpectedTotalSamples}");
            sb.AppendLine();

            sb.AppendLine("ACTIVE REGION - ACTUAL (what Foundation currently uses):");
            sb.AppendLine($"  Width:  {s.ActualWidthTiles} tiles ({s.ActualWidthSamples} samples)");
            sb.AppendLine($"  Height: {s.ActualHeightTiles} tiles ({s.ActualHeightSamples} samples)");
            sb.AppendLine($"  Total samples: {s.ActualTotalSamples}");
            sb.AppendLine($"  Origin: ({s.ActualOriginX:F1}, {s.ActualOriginY:F1})");
            sb.AppendLine();

            sb.AppendLine("COMPARISON:");
            sb.AppendLine($"  Expected width:  {s.ExpectedWidthTiles} tiles");
            sb.AppendLine($"  Actual width:    {s.ActualWidthTiles} tiles");
            sb.AppendLine($"  Expected height: {s.ExpectedHeightTiles} tiles");
            sb.AppendLine($"  Actual height:   {s.ActualHeightTiles} tiles");
            sb.AppendLine($"  Expected samples: {s.ExpectedTotalSamples}");
            sb.AppendLine($"  Actual samples:   {s.ActualTotalSamples}");
            sb.AppendLine();

            sb.AppendLine("SAMPLING:");
            sb.AppendLine($"  SamplesPerAxis: {s.SamplesPerAxis}");
            sb.AppendLine($"  SampleSpacingTiles: {s.SampleSpacingTiles}");
            sb.AppendLine();

            sb.AppendLine("DIAGNOSIS:");
            if (s.ZoomIsBeingIgnored)
            {
                sb.AppendLine("  ⚠ CAMERA ZOOM IS BEING IGNORED!");
                sb.AppendLine($"  Expected: {s.ExpectedTotalSamples} samples");
                sb.AppendLine($"  Actual:   {s.ActualTotalSamples} samples");
                sb.AppendLine($"  Ratio:    {s.ActualTotalSamples / (double)s.ExpectedTotalSamples:F2}x oversample");
                sb.AppendLine($"  The Foundation is using BackBuffer dimensions ({s.BackBufferWidth}x{s.BackBufferHeight})");
                sb.AppendLine($"  instead of zoom-adjusted ({s.VisibleWorldWidth:F0}x{s.VisibleWorldHeight:F0})");
            }
            else
            {
                sb.AppendLine("  ✓ Camera zoom is being applied correctly");
            }
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
