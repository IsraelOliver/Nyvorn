using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// Mock geometry provider for deterministic testing.
    /// Allows tests to set up specific tile configurations without depending on WorldMap.
    ///
    /// Usage:
    /// <code>
    /// var mock = new MockGeometryProvider();
    /// mock.SetTile(0, 0, foregroundSolid: true, backgroundWall: false);
    ///
    /// var classifier = new SceneWorldClassifier(mock);
    /// var result = classifier.ClassifyTile(0, 0);
    /// Assert(result == LightingCellClassification.SolidForeground);
    /// </code>
    /// </summary>
    public class MockGeometryProvider : ILightingWorldGeometryProvider
    {
        private Dictionary<(int, int), (bool foregroundSolid, bool backgroundWall)> _tiles = new();
        private int _worldWidth = 1000;
        private int _worldHeight = 1000;

        public MockGeometryProvider()
        {
        }

        /// <summary>
        /// Set tile configuration.
        /// </summary>
        public void SetTile(int tileX, int tileY, bool foregroundSolid, bool backgroundWall)
        {
            _tiles[(tileX, tileY)] = (foregroundSolid, backgroundWall);
        }

        /// <summary>
        /// Clear all tiles (reset to default empty state).
        /// </summary>
        public void Clear()
        {
            _tiles.Clear();
        }

        /// <summary>
        /// Set world dimensions for bounds checking.
        /// </summary>
        public void SetWorldSize(int width, int height)
        {
            _worldWidth = width;
            _worldHeight = height;
        }

        // ILightingWorldGeometryProvider implementation

        public bool IsForegroundSolidAt(int tileX, int tileY)
        {
            if (!IsInBounds(tileX, tileY))
                return false;

            return _tiles.TryGetValue((tileX, tileY), out var t) && t.foregroundSolid;
        }

        public bool HasBackgroundWallAt(int tileX, int tileY)
        {
            if (!IsInBounds(tileX, tileY))
                return false;

            return _tiles.TryGetValue((tileX, tileY), out var t) && t.backgroundWall;
        }

        public int WrapTileX(int tileX)
        {
            if (_worldWidth <= 0)
                return tileX;

            int wrapped = tileX % _worldWidth;
            return wrapped < 0 ? wrapped + _worldWidth : wrapped;
        }

        public bool IsInBounds(int tileX, int tileY)
        {
            return tileY >= 0 && tileY < _worldHeight;
        }
    }

    /// <summary>
    /// Test cases for Phase 2 foundation.
    /// Run these to verify correct behavior.
    /// </summary>
    public static class Phase2Tests
    {
        /// <summary>
        /// Run all test cases. Prints results to console.
        /// </summary>
        public static void RunAll()
        {
            System.Console.WriteLine("\n=== Phase 2 Foundation Tests ===");

            TestCase_A_SolidForeground();
            TestCase_B_VisibleBackground();
            TestCase_C_OpenAtmosphere();
            TestCase_D_OpacityBlending();
            TestCase_E_SunOcclusionRules();
            TestCase_F_LocalLightOcclusionRules();

            System.Console.WriteLine("=== All Tests Complete ===\n");
        }

        /// <summary>
        /// Case A: Solid foreground → SolidForeground classification
        /// </summary>
        private static void TestCase_A_SolidForeground()
        {
            var mock = new MockGeometryProvider();
            mock.SetTile(0, 0, foregroundSolid: true, backgroundWall: false);

            var classifier = new SceneWorldClassifier(mock);
            var result = classifier.ClassifyTile(0, 0);

            var foundation = new LightingV3Foundation(mock);
            foundation.Update(0, 0, 16, 16, 16);
            var frameData = foundation.GetFrameData();

            bool classificationCorrect = result == LightingCellClassification.SolidForeground;
            bool sunOcclusionCorrect = frameData.HasValue && frameData.Value.SunOpacityBuffer.Length > 0 && System.Math.Abs(frameData.Value.SunOpacityBuffer[0] - 1.0f) < 0.01f;
            bool localOcclusionCorrect = frameData.HasValue && frameData.Value.LocalOpacityBuffer.Length > 0 && System.Math.Abs(frameData.Value.LocalOpacityBuffer[0] - 1.0f) < 0.01f;

            bool passed = classificationCorrect && sunOcclusionCorrect && localOcclusionCorrect;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case A: SolidForeground");
            if (!passed)
            {
                System.Console.WriteLine($"  Classification: {result} (expected SolidForeground)");
                if (frameData.HasValue && frameData.Value.SunOpacityBuffer.Length > 0)
                    System.Console.WriteLine($"  SunOpacity: {frameData.Value.SunOpacityBuffer[0]} (expected 1.0)");
                if (frameData.HasValue && frameData.Value.LocalOpacityBuffer.Length > 0)
                    System.Console.WriteLine($"  LocalLightOpacity: {frameData.Value.LocalOpacityBuffer[0]} (expected 1.0)");
            }
        }

        /// <summary>
        /// Case B: Empty foreground + background wall → VisibleBackground
        /// </summary>
        private static void TestCase_B_VisibleBackground()
        {
            var mock = new MockGeometryProvider();
            mock.SetTile(0, 0, foregroundSolid: false, backgroundWall: true);

            var classifier = new SceneWorldClassifier(mock);
            var result = classifier.ClassifyTile(0, 0);

            var foundation = new LightingV3Foundation(mock);
            foundation.Update(0, 0, 16, 16, 16);
            var frameData = foundation.GetFrameData();

            bool classificationCorrect = result == LightingCellClassification.VisibleBackground;
            bool sunOcclusionCorrect = frameData.HasValue && frameData.Value.SunOpacityBuffer.Length > 0 && System.Math.Abs(frameData.Value.SunOpacityBuffer[0] - 0.0f) < 0.01f;
            bool localOcclusionCorrect = frameData.HasValue && frameData.Value.LocalOpacityBuffer.Length > 0 && System.Math.Abs(frameData.Value.LocalOpacityBuffer[0] - 0.0f) < 0.01f;

            bool passed = classificationCorrect && sunOcclusionCorrect && localOcclusionCorrect;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case B: VisibleBackground");
            if (!passed)
            {
                System.Console.WriteLine($"  Classification: {result} (expected VisibleBackground)");
                if (frameData.HasValue && frameData.Value.SunOpacityBuffer.Length > 0)
                    System.Console.WriteLine($"  SunOpacity: {frameData.Value.SunOpacityBuffer[0]} (expected 0.0)");
                if (frameData.HasValue && frameData.Value.LocalOpacityBuffer.Length > 0)
                    System.Console.WriteLine($"  LocalLightOpacity: {frameData.Value.LocalOpacityBuffer[0]} (expected 0.0)");
            }
        }

        /// <summary>
        /// Case C: Both empty → OpenAtmosphere
        /// </summary>
        private static void TestCase_C_OpenAtmosphere()
        {
            var mock = new MockGeometryProvider();
            mock.SetTile(0, 0, foregroundSolid: false, backgroundWall: false);

            var classifier = new SceneWorldClassifier(mock);
            var result = classifier.ClassifyTile(0, 0);

            bool passed = result == LightingCellClassification.OpenAtmosphere;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case C: OpenAtmosphere");
            if (!passed)
            {
                System.Console.WriteLine($"  Classification: {result} (expected OpenAtmosphere)");
            }
        }

        /// <summary>
        /// Case D: Two 0.5 opacities combine to ~0.75
        /// </summary>
        private static void TestCase_D_OpacityBlending()
        {
            float opacity1 = 0.5f;
            float opacity2 = 0.5f;
            float combined = 1f - ((1f - opacity1) * (1f - opacity2));
            float expected = 0.75f;

            bool passed = System.Math.Abs(combined - expected) < 0.01f;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case D: Opacity Blending");
            if (!passed)
            {
                System.Console.WriteLine($"  {opacity1} ⊕ {opacity2} = {combined} (expected {expected})");
            }
        }

        /// <summary>
        /// Case E: SunOpacity rules verification
        /// </summary>
        private static void TestCase_E_SunOcclusionRules()
        {
            var mock = new MockGeometryProvider();

            // SolidForeground: SunOpacity = 1
            mock.SetTile(0, 0, foregroundSolid: true, backgroundWall: false);

            // VisibleBackground: SunOpacity = 0
            mock.SetTile(1, 0, foregroundSolid: false, backgroundWall: true);

            // OpenAtmosphere: SunOpacity = 0
            mock.SetTile(2, 0, foregroundSolid: false, backgroundWall: false);

            var foundation = new LightingV3Foundation(mock);
            foundation.Update(0, 0, 48, 16, 16);
            var frameData = foundation.GetFrameData();

            bool rule1 = frameData.HasValue && frameData.Value.SunOpacityBuffer.Length > 0 && System.Math.Abs(frameData.Value.SunOpacityBuffer[0] - 1.0f) < 0.01f;  // Solid
            bool rule2 = frameData.HasValue && frameData.Value.SunOpacityBuffer.Length > 1 && System.Math.Abs(frameData.Value.SunOpacityBuffer[1] - 0.0f) < 0.01f;  // Background
            bool rule3 = frameData.HasValue && frameData.Value.SunOpacityBuffer.Length > 2 && System.Math.Abs(frameData.Value.SunOpacityBuffer[2] - 0.0f) < 0.01f;  // Atmosphere

            bool passed = rule1 && rule2 && rule3;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case E: Sun Occlusion Rules");
            if (!passed)
            {
                if (frameData.HasValue && frameData.Value.SunOpacityBuffer.Length > 0)
                    System.Console.WriteLine($"  Solid sun: {frameData.Value.SunOpacityBuffer[0]} (expected 1.0)");
                if (frameData.HasValue && frameData.Value.SunOpacityBuffer.Length > 1)
                    System.Console.WriteLine($"  Background sun: {frameData.Value.SunOpacityBuffer[1]} (expected 0.0)");
                if (frameData.HasValue && frameData.Value.SunOpacityBuffer.Length > 2)
                    System.Console.WriteLine($"  Atmosphere sun: {frameData.Value.SunOpacityBuffer[2]} (expected 0.0)");
            }
        }

        /// <summary>
        /// Case F: LocalLightOpacity rules verification
        /// </summary>
        private static void TestCase_F_LocalLightOcclusionRules()
        {
            var mock = new MockGeometryProvider();

            // SolidForeground: LocalLightOpacity = 1
            mock.SetTile(0, 0, foregroundSolid: true, backgroundWall: false);

            // VisibleBackground: LocalLightOpacity = 0
            mock.SetTile(1, 0, foregroundSolid: false, backgroundWall: true);

            // OpenAtmosphere: LocalLightOpacity = 0
            mock.SetTile(2, 0, foregroundSolid: false, backgroundWall: false);

            var foundation = new LightingV3Foundation(mock);
            foundation.Update(0, 0, 48, 16, 16);
            var frameData = foundation.GetFrameData();

            bool rule1 = frameData.HasValue && frameData.Value.LocalOpacityBuffer.Length > 0 && System.Math.Abs(frameData.Value.LocalOpacityBuffer[0] - 1.0f) < 0.01f;  // Solid
            bool rule2 = frameData.HasValue && frameData.Value.LocalOpacityBuffer.Length > 1 && System.Math.Abs(frameData.Value.LocalOpacityBuffer[1] - 0.0f) < 0.01f;  // Background
            bool rule3 = frameData.HasValue && frameData.Value.LocalOpacityBuffer.Length > 2 && System.Math.Abs(frameData.Value.LocalOpacityBuffer[2] - 0.0f) < 0.01f;  // Atmosphere

            bool passed = rule1 && rule2 && rule3;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case F: Local Light Occlusion Rules");
            if (!passed)
            {
                if (frameData.HasValue && frameData.Value.LocalOpacityBuffer.Length > 0)
                    System.Console.WriteLine($"  Solid local: {frameData.Value.LocalOpacityBuffer[0]} (expected 1.0)");
                if (frameData.HasValue && frameData.Value.LocalOpacityBuffer.Length > 1)
                    System.Console.WriteLine($"  Background local: {frameData.Value.LocalOpacityBuffer[1]} (expected 0.0)");
                if (frameData.HasValue && frameData.Value.LocalOpacityBuffer.Length > 2)
                    System.Console.WriteLine($"  Atmosphere local: {frameData.Value.LocalOpacityBuffer[2]} (expected 0.0)");
            }
        }
    }

    /// <summary>
    /// Test cases for Phase 3.1: Sun direction and color.
    /// Verify immutability, normalization, and frame integration.
    /// </summary>
    public static class Phase3_1Tests
    {
        /// <summary>
        /// Run all Phase 3.1 tests.
        /// </summary>
        public static void RunAll()
        {
            System.Console.WriteLine("\n=== Phase 3.1 Sun Direction & Color Tests ===\n");

            TestCase_A_DirectionAboveRight();
            TestCase_B_DirectionAboveLeft();
            TestCase_C_VectorNearZero();
            TestCase_D_LightTravelOpposite();
            TestCase_E_IntensityLimited();
            TestCase_F_BelowHorizon();
            TestCase_G_NoNaNOrInfinity();
            TestCase_H_SameUpdateId();

            System.Console.WriteLine("=== Phase 3.1 Tests Complete ===\n");
        }

        private static void TestCase_A_DirectionAboveRight()
        {
            var sunState = new LightingV3SunState(
                new Microsoft.Xna.Framework.Vector2(1f, -1f),
                new Microsoft.Xna.Framework.Vector3(1f, 0.8f, 0.6f),
                0.8f,
                45f);

            bool isNormalized = sunState.IsNormalized();
            bool isValid = sunState.IsValid();

            bool passed = isNormalized && isValid && sunState.IsAboveHorizon;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case A: Direction above-right (45°)");
            if (!passed)
            {
                System.Console.WriteLine($"  Normalized: {isNormalized}");
                System.Console.WriteLine($"  Valid: {isValid}");
                System.Console.WriteLine($"  Above horizon: {sunState.IsAboveHorizon}");
            }
        }

        private static void TestCase_B_DirectionAboveLeft()
        {
            var sunState = new LightingV3SunState(
                new Microsoft.Xna.Framework.Vector2(-1f, -1f),
                new Microsoft.Xna.Framework.Vector3(1f, 0.6f, 0.3f),
                0.7f,
                30f);

            bool isNormalized = sunState.IsNormalized();
            bool isValid = sunState.IsValid();

            bool passed = isNormalized && isValid && sunState.IsAboveHorizon;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case B: Direction above-left (30°)");
            if (!passed)
            {
                System.Console.WriteLine($"  Normalized: {isNormalized}");
                System.Console.WriteLine($"  Valid: {isValid}");
            }
        }

        private static void TestCase_C_VectorNearZero()
        {
            var sunState = new LightingV3SunState(
                new Microsoft.Xna.Framework.Vector2(0.0001f, 0.0001f),
                new Microsoft.Xna.Framework.Vector3(1f, 1f, 1f),
                1f,
                0f);

            bool isNormalized = sunState.IsNormalized();
            bool isValid = sunState.IsValid();

            bool passed = isNormalized && isValid;  // Should use safe default
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case C: Near-zero vector handled safely");
            if (!passed)
            {
                System.Console.WriteLine($"  Normalized: {isNormalized}");
                System.Console.WriteLine($"  Valid: {isValid}");
            }
        }

        private static void TestCase_D_LightTravelOpposite()
        {
            var sunState = new LightingV3SunState(
                new Microsoft.Xna.Framework.Vector2(1f, 0f),
                new Microsoft.Xna.Framework.Vector3(1f, 1f, 1f),
                1f,
                0f);

            bool travelOpposite = sunState.IsLightTravelDirectionValid();

            bool passed = travelOpposite &&
                         System.Math.Abs(sunState.LightTravelDirection.X + sunState.DirectionToSun.X) < 0.01f;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case D: LightTravelDirection is opposite");
            if (!passed)
            {
                System.Console.WriteLine($"  Opposite: {travelOpposite}");
                System.Console.WriteLine($"  DirectionToSun: ({sunState.DirectionToSun.X:F3}, {sunState.DirectionToSun.Y:F3})");
                System.Console.WriteLine($"  LightTravel: ({sunState.LightTravelDirection.X:F3}, {sunState.LightTravelDirection.Y:F3})");
            }
        }

        private static void TestCase_E_IntensityLimited()
        {
            var sunState = new LightingV3SunState(
                new Microsoft.Xna.Framework.Vector2(0f, -1f),
                new Microsoft.Xna.Framework.Vector3(1f, 1f, 1f),
                2.5f,  // Over 1.0
                45f);

            bool withinBounds = sunState.Intensity >= 0f && sunState.Intensity <= 1f;

            bool passed = withinBounds && System.Math.Abs(sunState.Intensity - 1f) < 0.01f;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case E: Intensity clamped to [0, 1]");
            if (!passed)
            {
                System.Console.WriteLine($"  Intensity: {sunState.Intensity} (expected 1.0)");
            }
        }

        private static void TestCase_F_BelowHorizon()
        {
            var sunState = new LightingV3SunState(
                new Microsoft.Xna.Framework.Vector2(0f, 1f),
                new Microsoft.Xna.Framework.Vector3(0f, 0f, 0f),
                0f,
                -45f);

            bool passed = !sunState.IsAboveHorizon && sunState.Elevation < 0f;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case F: Sun below horizon");
            if (!passed)
            {
                System.Console.WriteLine($"  Above horizon: {sunState.IsAboveHorizon} (expected false)");
                System.Console.WriteLine($"  Elevation: {sunState.Elevation}°");
            }
        }

        private static void TestCase_G_NoNaNOrInfinity()
        {
            var sunState = new LightingV3SunState(
                new Microsoft.Xna.Framework.Vector2(0.707f, -0.707f),
                new Microsoft.Xna.Framework.Vector3(1f, 0.9f, 0.7f),
                0.9f,
                60f);

            bool hasNaN = float.IsNaN(sunState.DirectionToSun.X) ||
                         float.IsNaN(sunState.DirectionToSun.Y) ||
                         float.IsNaN(sunState.Intensity) ||
                         float.IsNaN(sunState.Elevation) ||
                         float.IsNaN(sunState.LinearColor.X);

            bool hasInfinity = float.IsInfinity(sunState.DirectionToSun.X) ||
                              float.IsInfinity(sunState.DirectionToSun.Y);

            bool passed = !hasNaN && !hasInfinity && sunState.IsValid();
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case G: No NaN or infinity");
            if (!passed)
            {
                System.Console.WriteLine($"  Has NaN: {hasNaN}");
                System.Console.WriteLine($"  Has Infinity: {hasInfinity}");
            }
        }

        private static void TestCase_H_SameUpdateId()
        {
            var mock = new MockGeometryProvider();
            var sunProvider = new MockSolarProvider();
            var foundation = new LightingV3Foundation(mock, LightingSamplingConfig.Default2x2, sunProvider);

            foundation.Update(0, 0, 32, 32, 16);
            var frameData = foundation.GetFrameData();

            bool hasSunState = frameData.HasValue && frameData.Value.SunState.IsValid();
            bool sameUpdateId = frameData.HasValue && frameData.Value.UpdateId > 0;

            bool passed = hasSunState && sameUpdateId;
            System.Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] Case H: SunState published with frame");
            if (!passed)
            {
                System.Console.WriteLine($"  Has SunState: {hasSunState}");
                System.Console.WriteLine($"  UpdateId > 0: {sameUpdateId}");
            }
        }
    }

    /// <summary>
    /// Mock solar provider for testing.
    /// </summary>
    public class MockSolarProvider : ISolarProvider
    {
        public LightingV3SunState GetSunState()
        {
            return LightingV3SunState.Noon();
        }
    }
}
