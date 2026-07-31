using System;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Phase 3.2A deterministic tests for sun visibility field.
    /// 14 test cases covering ray marching, world wrap, occlusion rules.
    /// </summary>
    public static class Phase3_2ATests
    {
        public static void RunAll()
        {
            System.Console.WriteLine("\n=== Phase 3.2A Sun Visibility Tests ===\n");

            TestCase_A_CompletelyFree();
            TestCase_B_ForegroundBlocks();
            TestCase_C_BackgroundWallTransparent();
            TestCase_D_SingleOpacity();
            TestCase_E_DoubleOpacity();
            TestCase_F_DiagonalRay();
            TestCase_G_WorldWrapSeam();
            TestCase_H_SunBelowHorizon();
            TestCase_I_SampleInsideForeground();
            TestCase_J_BlockerOutsideRegion();
            TestCase_K_NearlyHorizontal();
            TestCase_L_NoNaNInfinity();
            TestCase_M_BufferIndependence();
            TestCase_N_UpdateIdConsistency();

            System.Console.WriteLine("=== Phase 3.2A Tests Complete ===\n");
        }

        private static Vector2 Normalize(Vector2 v)
        {
            float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
            if (len < 0.0001f) return v;
            return new Vector2(v.X / len, v.Y / len);
        }

        private static void TestCase_A_CompletelyFree()
        {
            var mock = new MockGeometryProvider();
            var visibility = SunVisibilityRayMarcher.ComputeVisibility(
                worldX: 100f, worldY: 100f,
                sunDirection: Normalize(new Vector2(1f, -1f)),
                sunIntensity: 1.0f,
                sunAboveHorizon: true,
                geometryProvider: mock,
                worldWidthTiles: 1000, worldHeightTiles: 1000, tileSize: 16);

            bool passes = Math.Abs(visibility - 1.0f) < 0.01f;
            System.Console.WriteLine($"[A] Completely free: {(passes ? "✓" : "✗")} (visibility={visibility:F3})");
        }

        private static void TestCase_B_ForegroundBlocks()
        {
            var mock = new MockGeometryProvider();
            mock.SetTile(10, 5, foregroundSolid: true, backgroundWall: false);
            var visibility = SunVisibilityRayMarcher.ComputeVisibility(
                worldX: 100f, worldY: 100f,
                sunDirection: Normalize(new Vector2(0.577f, -0.577f)),  // 45° up-right
                sunIntensity: 1.0f,
                sunAboveHorizon: true,
                geometryProvider: mock,
                worldWidthTiles: 1000, worldHeightTiles: 1000, tileSize: 16);

            bool passes = visibility < 0.5f;
            System.Console.WriteLine($"[B] Foreground blocks: {(passes ? "✓" : "✗")} (visibility={visibility:F3})");
        }

        private static void TestCase_C_BackgroundWallTransparent()
        {
            var mock = new MockGeometryProvider();
            mock.SetTile(10, 5, foregroundSolid: false, backgroundWall: true);
            var visibility = SunVisibilityRayMarcher.ComputeVisibility(
                worldX: 100f, worldY: 100f,
                sunDirection: Normalize(new Vector2(0.577f, -0.577f)),
                sunIntensity: 1.0f,
                sunAboveHorizon: true,
                geometryProvider: mock,
                worldWidthTiles: 1000, worldHeightTiles: 1000, tileSize: 16);

            bool passes = Math.Abs(visibility - 1.0f) < 0.01f;
            System.Console.WriteLine($"[C] Background wall transparent: {(passes ? "✓" : "✗")} (visibility={visibility:F3})");
        }

        private static void TestCase_D_SingleOpacity()
        {
            // This would require opacity provider - for now, test structure
            System.Console.WriteLine($"[D] Single opacity [0.5]: [Structural test - requires opacity provider]");
        }

        private static void TestCase_E_DoubleOpacity()
        {
            System.Console.WriteLine($"[E] Double opacity: [Structural test - requires opacity provider]");
        }

        private static void TestCase_F_DiagonalRay()
        {
            var mock = new MockGeometryProvider();
            var visibility = SunVisibilityRayMarcher.ComputeVisibility(
                worldX: 0f, worldY: 0f,
                sunDirection: Normalize(new Vector2(0.707f, -0.707f)),  // 45° diagonal
                sunIntensity: 1.0f,
                sunAboveHorizon: true,
                geometryProvider: mock,
                worldWidthTiles: 100, worldHeightTiles: 100, tileSize: 16);

            bool passes = Math.Abs(visibility - 1.0f) < 0.01f;
            System.Console.WriteLine($"[F] Diagonal ray: {(passes ? "✓" : "✗")} (visibility={visibility:F3})");
        }

        private static void TestCase_G_WorldWrapSeam()
        {
            var mock = new MockGeometryProvider();
            // Ray starts near left edge, direction goes left (should wrap)
            var visibility = SunVisibilityRayMarcher.ComputeVisibility(
                worldX: 16f, worldY: 100f,
                sunDirection: Normalize(new Vector2(-0.707f, -0.707f)),  // 45° up-left (wrapping)
                sunIntensity: 1.0f,
                sunAboveHorizon: true,
                geometryProvider: mock,
                worldWidthTiles: 100, worldHeightTiles: 100, tileSize: 16);

            bool passes = !float.IsNaN(visibility) && !float.IsInfinity(visibility);
            System.Console.WriteLine($"[G] World wrap seam: {(passes ? "✓" : "✗")} (visibility={visibility:F3})");
        }

        private static void TestCase_H_SunBelowHorizon()
        {
            var mock = new MockGeometryProvider();
            var visibility = SunVisibilityRayMarcher.ComputeVisibility(
                worldX: 100f, worldY: 100f,
                sunDirection: Normalize(new Vector2(0, 1)),  // Points down
                sunIntensity: 0.5f,
                sunAboveHorizon: false,  // Below horizon
                geometryProvider: mock,
                worldWidthTiles: 1000, worldHeightTiles: 1000, tileSize: 16);

            bool passes = Math.Abs(visibility - 0.0f) < 0.01f;
            System.Console.WriteLine($"[H] Sun below horizon: {(passes ? "✓" : "✗")} (visibility={visibility:F3})");
        }

        private static void TestCase_I_SampleInsideForeground()
        {
            var mock = new MockGeometryProvider();
            mock.SetTile(6, 6, foregroundSolid: true, backgroundWall: false);
            var visibility = SunVisibilityRayMarcher.ComputeVisibility(
                worldX: 100f, worldY: 100f,  // Inside tile 6,6
                sunDirection: Normalize(new Vector2(0.577f, -0.577f)),
                sunIntensity: 1.0f,
                sunAboveHorizon: true,
                geometryProvider: mock,
                worldWidthTiles: 1000, worldHeightTiles: 1000, tileSize: 16);

            bool passes = Math.Abs(visibility - 0.0f) < 0.01f;
            System.Console.WriteLine($"[I] Sample inside foreground: {(passes ? "✓" : "✗")} (visibility={visibility:F3})");
        }

        private static void TestCase_J_BlockerOutsideRegion()
        {
            System.Console.WriteLine($"[J] Blocker outside region: [Requires region-aware testing]");
        }

        private static void TestCase_K_NearlyHorizontal()
        {
            var mock = new MockGeometryProvider();
            // Sun direction almost horizontal (elevation < 5°)
            var visibility = SunVisibilityRayMarcher.ComputeVisibility(
                worldX: 100f, worldY: 100f,
                sunDirection: Normalize(new Vector2(0.99f, -0.1f)),  // Very flat
                sunIntensity: 1.0f,
                sunAboveHorizon: true,
                geometryProvider: mock,
                worldWidthTiles: 1000, worldHeightTiles: 1000, tileSize: 16);

            bool passes = Math.Abs(visibility - 0.0f) < 0.01f;
            System.Console.WriteLine($"[K] Nearly horizontal: {(passes ? "✓" : "✗")} (visibility={visibility:F3})");
        }

        private static void TestCase_L_NoNaNInfinity()
        {
            var mock = new MockGeometryProvider();
            var visibility = SunVisibilityRayMarcher.ComputeVisibility(
                worldX: float.MaxValue * 0.5f, worldY: 100f,  // Large coordinate
                sunDirection: Normalize(new Vector2(0.577f, -0.577f)),
                sunIntensity: 1.0f,
                sunAboveHorizon: true,
                geometryProvider: mock,
                worldWidthTiles: 1000, worldHeightTiles: 1000, tileSize: 16);

            bool passes = !float.IsNaN(visibility) && !float.IsInfinity(visibility) && visibility >= 0 && visibility <= 1;
            System.Console.WriteLine($"[L] No NaN/infinity: {(passes ? "✓" : "✗")} (visibility={visibility:F3})");
        }

        private static void TestCase_M_BufferIndependence()
        {
            // Test that front and back slots have independent buffers
            var slot1 = new LightingV3FrameSlot(256, 1024);
            var slot2 = new LightingV3FrameSlot(256, 1024);

            bool independent = !ReferenceEquals(slot1.SunVisibilityBuffer, slot2.SunVisibilityBuffer);
            System.Console.WriteLine($"[M] Buffer independence: {(independent ? "✓" : "✗")}");
        }

        private static void TestCase_N_UpdateIdConsistency()
        {
            // Test that FrameData carries correct UpdateId
            var slot = new LightingV3FrameSlot(256, 1024);
            slot.FrameId = 42;

            var frameData = new LightingV3FrameData(slot);
            bool matches = frameData.UpdateId == 42;
            System.Console.WriteLine($"[N] UpdateId consistency: {(matches ? "✓" : "✗")} (frameData.UpdateId={frameData.UpdateId})");
        }
    }
}
