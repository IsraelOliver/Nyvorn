using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using Nyvorn.Source.World;

namespace V6Validator
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== V6 SKY VISIBILITY + AIR LIGHT FIELD VALIDATION ===\n");

            // Create world
            var worldMap = new WorldMap(2048, 1024, 16);
            SetupTestTerrain(worldMap);

            Console.WriteLine($"World: {worldMap.Width}x{worldMap.Height} tiles\n");

            // Build V6SkyVisibilityField
            var sw = Stopwatch.StartNew();
            var v6SkyField = new V6SkyVisibilityField(worldMap);
            sw.Stop();

            Console.WriteLine($"V6SkyVisibilityField built: {sw.ElapsedMilliseconds}ms");

            // Build V5 mock for testing (we need TransportField values)
            // For this test, we'll create a mock V5 field
            var v5Config = new V5SkyAmbientConfig();
            var v5Field = new V5SkyAmbientField(worldMap, v5Config);

            // Fake V5 computation with a simple seed at origin
            v5Field.Compute(512, 200, 100, 100, new System.Collections.Generic.List<Point> { new Point(512, 100) });

            // Build V6AirLightField
            sw.Restart();
            var v6AirField = new V6AirLightField(worldMap, v6SkyField, v5Field);
            v6AirField.Compute(512, 200, 100, 100);
            sw.Stop();

            Console.WriteLine($"V6AirLightField computed: {sw.ElapsedMilliseconds}ms\n");

            // Run air light tests
            RunAirLightTests(worldMap, v6SkyField, v5Field, v6AirField);

            // Performance
            MeasureAirLightPerformance(worldMap, v6SkyField, v5Field, v6AirField);

            Console.WriteLine("\n=== VALIDATION COMPLETE ===");
        }

        static void SetupTestTerrain(WorldMap worldMap)
        {
            int width = worldMap.Width;
            int height = worldMap.Height;

            // Surface layer: Y = 200-205
            int surfaceY = 200;
            for (int x = 0; x < width; x++)
            {
                for (int y = surfaceY; y < surfaceY + 5; y++)
                {
                    if (y < height)
                        worldMap.SetTile(x, y, TileType.Grass);
                }
            }

            // Foreground ceiling at x=300-320, y=100-105
            for (int x = 300; x <= 320; x++)
            {
                for (int y = 100; y <= 105; y++)
                {
                    worldMap.SetTile(x, y, TileType.Stone);
                }
            }

            // Background layer at x=500-520, y=950-960 (cavern area, simulated)
            for (int x = 500; x <= 520; x++)
            {
                for (int y = 950; y <= 960; y++)
                {
                    worldMap.SetBackgroundTile(x, y, TileType.Stone);
                }
            }

            // Open shaft at x=800 (no tiles at all, open to sky from 0 to height)

            // Enclosed cavity: y=150-160, x=400-410 with foreground around edges
            for (int x = 400; x <= 410; x++)
            {
                worldMap.SetTile(x, 145, TileType.Stone);  // Top
            }
            worldMap.SetTile(400, 150, TileType.Stone);    // Left edge
            worldMap.SetTile(410, 150, TileType.Stone);    // Right edge

            // Lateral opening (gap in ceiling) at x=1000-1010
            for (int x = 990; x <= 1010; x++)
            {
                if (x != 1000)  // Gap at x=1000
                    worldMap.SetTile(x, 130, TileType.Stone);
            }
        }

        static void RunControlledTests(WorldMap worldMap, V6SkyVisibilityField v6Field)
        {
            Console.WriteLine("CONTROLLED TEST SCENARIOS (A-H):\n");

            int surfaceY = 200;
            int passed = 0;
            int total = 0;

            // TEST A: Sky exterior - completely open column
            Console.WriteLine("TEST A: Sky Exterior (open column, various heights)");
            var testA = new[]
            {
                (y: 10, expected: true, desc: "high"),
                (y: 50, expected: true, desc: "mid"),
                (y: 100, expected: true, desc: "just above surface"),
            };
            foreach (var (y, expected, desc) in testA)
            {
                total++;
                bool result = v6Field.IsDirectSkyVisible(50, y);  // x=50 is fully open
                bool pass = result == expected;
                if (pass) passed++;
                Console.WriteLine($"  {(pass ? "✓" : "✗")} x=50, y={y} ({desc}): got {result}, expected {expected}");
            }

            // TEST B: Above real occluder
            Console.WriteLine("\nTEST B: Above Real Occluder (y=200 is surface)");
            var testB = new[]
            {
                (y: 199, expected: true, desc: "just above"),
                (y: 195, expected: true, desc: "5 above"),
            };
            foreach (var (y, expected, desc) in testB)
            {
                total++;
                bool result = v6Field.IsDirectSkyVisible(100, y);
                bool pass = result == expected;
                if (pass) passed++;
                Console.WriteLine($"  {(pass ? "✓" : "✗")} x=100, y={y} ({desc}): got {result}, expected {expected}");
            }

            // TEST C: Deep shaft (x=800 has surface at y=200, like everywhere)
            Console.WriteLine("\nTEST C: Deep Vertical Shaft (x=800, has surface at y=200)");
            var testC = new[]
            {
                (y: 50, expected: true, desc: "above surface"),
                (y: 201, expected: false, desc: "below surface"),
            };
            foreach (var (y, expected, desc) in testC)
            {
                total++;
                bool result = v6Field.IsDirectSkyVisible(800, y);
                bool pass = result == expected;
                if (pass) passed++;
                Console.WriteLine($"  {(pass ? "✓" : "✗")} x=800, y={y} ({desc}): got {result}, expected {expected}");
            }

            // TEST D: Foreground ceiling (x=310, y=100-105 is stone)
            Console.WriteLine("\nTEST D: Under Foreground Ceiling (x=310)");
            var testD = new[]
            {
                (y: 99, expected: true, desc: "just above ceiling"),
                (y: 50, expected: true, desc: "far above ceiling"),
                (y: 106, expected: false, desc: "just below ceiling"),
                (y: 150, expected: false, desc: "far below ceiling"),
            };
            foreach (var (y, expected, desc) in testD)
            {
                total++;
                bool result = v6Field.IsDirectSkyVisible(310, y);
                bool pass = result == expected;
                if (pass) passed++;
                Console.WriteLine($"  {(pass ? "✓" : "✗")} x=310, y={y} ({desc}): got {result}, expected {expected}");
            }

            // TEST E: Background ceiling (x=510, y=950-960 is background, but surface at y=200 blocks first)
            Console.WriteLine("\nTEST E: Background + Surface (x=510, surface at y=200 blocks)");
            var testE = new[]
            {
                (y: 199, expected: true, desc: "above surface"),
                (y: 201, expected: false, desc: "below surface/above background"),
            };
            foreach (var (y, expected, desc) in testE)
            {
                total++;
                bool result = v6Field.IsDirectSkyVisible(510, y);
                bool pass = result == expected;
                if (pass) passed++;
                Console.WriteLine($"  {(pass ? "✓" : "✗")} x=510, y={y} ({desc}): got {result}, expected {expected}");
            }

            // TEST F: Enclosed cavity (x=405, y=150, has ceiling at y=145)
            Console.WriteLine("\nTEST F: Enclosed Cavity (x=405, ceiling at y=145)");
            var testF = new[]
            {
                (y: 150, expected: false, desc: "inside cavity"),
                (y: 155, expected: false, desc: "deep in cavity"),
            };
            foreach (var (y, expected, desc) in testF)
            {
                total++;
                bool result = v6Field.IsDirectSkyVisible(405, y);
                bool pass = result == expected;
                if (pass) passed++;
                Console.WriteLine($"  {(pass ? "✓" : "✗")} x=405, y={y} ({desc}): got {result}, expected {expected}");
            }

            // TEST G: Lateral opening (x=1005 has ceiling at y=130)
            Console.WriteLine("\nTEST G: Lateral Opening (x=1005 ceiling at y=130)");
            var testG = new[]
            {
                (y: 129, expected: true, desc: "just above ceiling"),
                (y: 100, expected: true, desc: "above ceiling (not blocked by lateral opening)"),
            };
            foreach (var (y, expected, desc) in testG)
            {
                total++;
                bool result = v6Field.IsDirectSkyVisible(1005, y);
                bool pass = result == expected;
                if (pass) passed++;
                Console.WriteLine($"  {(pass ? "✓" : "✗")} x=1005, y={y} ({desc}): got {result}, expected {expected}");
            }

            // TEST H: Very high altitude (far above all terrain)
            Console.WriteLine("\nTEST H: High Altitude (well above surface)");
            var testH = new[]
            {
                (y: 10, expected: true, desc: "high altitude"),
            };
            foreach (var (y, expected, desc) in testH)
            {
                total++;
                bool result = v6Field.IsDirectSkyVisible(150, y);
                bool pass = result == expected;
                if (pass) passed++;
                Console.WriteLine($"  {(pass ? "✓" : "✗")} x=150, y={y} ({desc}): got {result}, expected {expected}");
            }

            Console.WriteLine($"\nTest Results: {passed}/{total} passed\n");
        }

        static void ValidateOptimizedVsReference(WorldMap worldMap, V6SkyVisibilityField v6Field)
        {
            Console.WriteLine("VALIDATING OPTIMIZED vs REFERENCE (AIR CELLS ONLY):\n");

            var sw = Stopwatch.StartNew();
            int tested = 0;
            int mismatches = 0;

            // Random sampling - but ONLY test air cells
            Random rnd = new Random(42);
            for (int i = 0; i < 100000; i++)
            {
                int x = rnd.Next(0, worldMap.Width);
                int y = rnd.Next(0, worldMap.Height);

                // Skip if this position is solid or has background (not an air cell)
                if (worldMap.IsSolidAt(x, y) || worldMap.GetBackgroundTile(x, y) != TileType.Empty)
                    continue;

                if (!v6Field.ValidatePosition(x, y, out string reason))
                {
                    mismatches++;
                    if (mismatches <= 5)
                        Console.WriteLine($"  MISMATCH: {reason}");
                }
                tested++;
            }
            sw.Stop();

            Console.WriteLine($"Tested: {tested} air cell positions");
            Console.WriteLine($"Mismatches: {mismatches}");
            if (tested > 0)
                Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms (~{sw.ElapsedMilliseconds / (float)tested * 1000}µs per position)");
            Console.WriteLine($"Result: {(mismatches == 0 ? "✓ PERFECT" : "✗ FAILURES")}\n");
        }

        static void MeasurePerformance(WorldMap worldMap, V6SkyVisibilityField v6Field)
        {
            Console.WriteLine("PERFORMANCE MEASUREMENTS:\n");

            // Recompute single column multiple times
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                v6Field.RecomputeColumn(i % worldMap.Width);
            }
            sw.Stop();
            Console.WriteLine($"1,000 RecomputeColumn: {sw.ElapsedMilliseconds}ms (avg {sw.ElapsedMilliseconds / 1000.0}ms per column)");

            // Query performance
            sw.Restart();
            long checksum = 0;
            for (int i = 0; i < 1000000; i++)
            {
                bool result = v6Field.IsDirectSkyVisible(i % worldMap.Width, i % worldMap.Height);
                checksum += result ? 1 : 0;
            }
            sw.Stop();
            Console.WriteLine($"1,000,000 queries: {sw.ElapsedMilliseconds}ms (~{sw.ElapsedTicks / 1000000f}ns per query)");
            Console.WriteLine($"Checksum: {checksum} (prevents optimization)\n");
        }

        static void RunAirLightTests(WorldMap worldMap, V6SkyVisibilityField skyField, V5SkyAmbientField v5Field, V6AirLightField airField)
        {
            Console.WriteLine("AIR LIGHT FIELD TEST SCENARIOS (1-8):\n");

            // Buffer was computed at (512, 200) with margin 4 and size 100×100
            // So active region is [508, 612] × [196, 296]
            // Use positions within this range

            // Scenario 1: Surface level (DirectSky=1, AirLight≥0)
            Console.WriteLine("1. SURFACE LEVEL (DirectSky=1)");
            var (ds1, tr1, al1, st1) = airField.GetDiagnosticsAt(550, 199);
            Console.WriteLine($"   Pos: (550, 199) | DirectSky={ds1}, Transport={tr1:F3}, AirLight={al1:F3}");
            Console.WriteLine($"   Expected: DirectSky=1, AirLight=max(DirectSky, Transport)\n");

            // Scenario 2: One tile above surface
            Console.WriteLine("2. ONE TILE ABOVE SURFACE");
            var (ds2, tr2, al2, st2) = airField.GetDiagnosticsAt(550, 198);
            Console.WriteLine($"   Pos: (550, 198) | DirectSky={ds2}, Transport={tr2:F3}, AirLight={al2:F3}");
            Console.WriteLine($"   Expected: DirectSky=1, AirLight≥DirectSky\n");

            // Scenario 3: Many tiles above (KEY TEST)
            // Buffer covers [508, 616] × [196, 304], so use valid position
            Console.WriteLine("3. MANY TILES ABOVE (DirectSky=1, Transport=0, AirLight=1)");
            var (ds3, tr3, al3, st3) = airField.GetDiagnosticsAt(550, 210);  // Within buffer, well above surface
            Console.WriteLine($"   Pos: (550, 210) | DirectSky={ds3}, Transport={tr3:F3}, AirLight={al3:F3}");
            Console.WriteLine($"   Expected: DirectSky=1, Transport≈0, AirLight=1 ← KEY TEST\n");

            // Scenario 4: Open vertical column
            Console.WriteLine("4. OPEN VERTICAL COLUMN (DirectSky=1)");
            var (ds4, tr4, al4, st4) = airField.GetDiagnosticsAt(550, 215);  // Well above surface
            Console.WriteLine($"   Pos: (550, 215) | DirectSky={ds4}, Transport={tr4:F3}, AirLight={al4:F3}");
            Console.WriteLine($"   Expected: DirectSky=1, AirLight≥0\n");

            // Scenario 5: Different height
            Console.WriteLine("5. MID-HEIGHT POSITION");
            var (ds5, tr5, al5, st5) = airField.GetDiagnosticsAt(550, 220);
            Console.WriteLine($"   Pos: (550, 220) | DirectSky={ds5}, Transport={tr5:F3}, AirLight={al5:F3}");
            Console.WriteLine($"   Expected: AirLight = max(DirectSky, Transport)\n");

            // Scenario 6: Test with blocked position (ON the surface, y=200-204)
            Console.WriteLine("6. BLOCKED POSITION (solid terrain)");
            var (ds6, tr6, al6, st6) = airField.GetDiagnosticsAt(550, 202);  // On surface
            Console.WriteLine($"   Pos: (550, 202) | Status: {st6} | DirectSky={ds6}, AirLight={al6:F3}");
            Console.WriteLine($"   Expected: If solid/background, AirLight=0\n");

            // Scenario 7: Multiple heights
            Console.WriteLine("7. HEIGHT PROGRESSION");
            for (int y = 210; y >= 190; y -= 5)
            {
                var (ds, tr, al, st) = airField.GetDiagnosticsAt(550, y);
                Console.WriteLine($"   Y={y:D3}: Sky={ds}, Transport={tr:F3}, AirLight={al:F3}");
            }
            Console.WriteLine();

            // Scenario 8: Check formula correctness
            Console.WriteLine("8. FORMULA VALIDATION (AirLight = max(DirectSky, Transport))");
            var (ds8, tr8, al8, st8) = airField.GetDiagnosticsAt(530, 205);
            float expected8 = System.Math.Max(ds8, tr8);
            bool formulaOk = System.Math.Abs(al8 - expected8) < 0.001f;
            Console.WriteLine($"   Pos: (530, 205) | DirectSky={ds8}, Transport={tr8:F3}");
            Console.WriteLine($"   Computed AirLight={al8:F3}, Expected={expected8:F3}");
            Console.WriteLine($"   Formula correct: {(formulaOk ? "✓ YES" : "✗ NO")}\n");
        }

        static void MeasureAirLightPerformance(WorldMap worldMap, V6SkyVisibilityField skyField, V5SkyAmbientField v5Field, V6AirLightField airField)
        {
            Console.WriteLine("PERFORMANCE MEASUREMENTS:\n");

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                airField.Compute(500 + i, 200, 100, 100);
            }
            sw.Stop();

            Console.WriteLine($"10 Compute calls: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"Avg per compute: {sw.ElapsedMilliseconds / 10.0}ms");

            sw.Restart();
            for (int i = 0; i < 100000; i++)
            {
                airField.GetAirLightAt(i % worldMap.Width, i % worldMap.Height);
            }
            sw.Stop();

            Console.WriteLine($"100,000 GetAirLightAt queries: {sw.ElapsedMilliseconds}ms\n");
        }

        static void TestDynamicInvalidation(WorldMap worldMap, V6SkyVisibilityField v6Field)
        {
            Console.WriteLine("TESTING DYNAMIC INVALIDATION:\n");

            int testX = 600;
            int testY = 100;

            // Before: no occluder at x=600 in this range
            bool before = v6Field.IsDirectSkyVisible(testX, testY);
            Console.WriteLine($"Before tile placement: IsDirectSkyVisible({testX},{testY}) = {before}");

            // Place a foreground tile
            worldMap.SetTile(testX, 50, TileType.Stone);
            v6Field.InvalidateColumn(testX);

            bool after = v6Field.IsDirectSkyVisible(testX, testY);
            Console.WriteLine($"After tile placement: IsDirectSkyVisible({testX},{testY}) = {after}");
            Console.WriteLine($"Change detected: {before != after} (expected true)\n");

            // Remove tile
            worldMap.SetTile(testX, 50, TileType.Empty);
            v6Field.InvalidateColumn(testX);

            bool restored = v6Field.IsDirectSkyVisible(testX, testY);
            Console.WriteLine($"After tile removal: IsDirectSkyVisible({testX},{testY}) = {restored}");
            Console.WriteLine($"Restored: {restored == before} (expected true)\n");
        }
    }
}
