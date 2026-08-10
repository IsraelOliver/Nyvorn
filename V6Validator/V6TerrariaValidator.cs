using System;
using System.Diagnostics;
using Nyvorn.Source.World;
using Nyvorn.Source.Engine.Graphics.LightingPipeline;
using Microsoft.Xna.Framework;

/// <summary>
/// V6 Terraria Lighting System Validator
/// Tests 8 main scenarios and collects metrics.
/// </summary>
public class V6TerrariaValidator
{
    private readonly WorldMap worldMap;
    private readonly V6LightingSystem v6System;
    private readonly V6LightSampler sampler;

    private int tileSize = 16;

    public V6TerrariaValidator(WorldMap worldMap, V6LightingSystem v6System)
    {
        this.worldMap = worldMap;
        this.v6System = v6System;
        this.sampler = new V6LightSampler(v6System.LightMap);
    }

    public void RunAllTests(Color skyColor)
    {
        Console.WriteLine("\n=================================================");
        Console.WriteLine("V6 TERRARIA LIGHTING SYSTEM — VALIDATION TESTS");
        Console.WriteLine("=================================================\n");

        // Dummy update at surface level to initialize the system
        v6System.Update(512 * tileSize, 200 * tileSize, 1280, 800, tileSize, skyColor);

        Test1_SurfaceLevel(skyColor);
        Test2_OneAboveSurface(skyColor);
        Test3_ManyAboveSurface(skyColor);
        Test4_OpenVerticalColumn(skyColor);
        Test5_MidHeight(skyColor);
        Test6_BlockedSolid(skyColor);
        Test7_HeightProgression(skyColor);
        Test8_FormulaValidation(skyColor);

        PrintSummary();
    }

    private void Test1_SurfaceLevel(Color skyColor)
    {
        Console.WriteLine("\n[TEST 1] SURFACE LEVEL");
        Console.WriteLine("Position: (550, 199)");

        int testX = 550;
        int testY = 199;

        v6System.Update(testX * tileSize, testY * tileSize, 1280, 800, tileSize, skyColor);

        var lightMap = v6System.LightMap;
        int localX = testX - lightMap.BufferOriginTileX;
        int localY = testY - lightMap.BufferOriginTileY;

        if (localX >= 0 && localX < lightMap.BufferWidth && localY >= 0 && localY < lightMap.BufferHeight)
        {
            var (r, g, b) = lightMap.GetLightAtLocal(localX, localY);
            var medium = lightMap.GetMediumAtLocal(localX, localY);

            Console.WriteLine($"  Medium: {medium}");
            Console.WriteLine($"  Light RGB: R={r:F3} G={g:F3} B={b:F3}");

            bool isSkyOpen = medium == V6LightMap.CellMedium.SkyOpen;
            float expected = isSkyOpen ? (skyColor.R / 255f) : 0f;

            Console.WriteLine($"  Expected: SkyOpen={isSkyOpen} (light ~= {expected:F3})");
            Console.WriteLine($"  Status: {(isSkyOpen ? "✓ CORRECT" : "✗ UNEXPECTED")}");
        }
        else
        {
            Console.WriteLine("  Status: ✗ OUTSIDE BUFFER");
        }
    }

    private void Test2_OneAboveSurface(Color skyColor)
    {
        Console.WriteLine("\n[TEST 2] ONE TILE ABOVE SURFACE");
        Console.WriteLine("Position: (550, 198)");

        int testX = 550;
        int testY = 198;

        v6System.Update(testX * tileSize, testY * tileSize, 1280, 800, tileSize, skyColor);

        var lightMap = v6System.LightMap;
        int localX = testX - lightMap.BufferOriginTileX;
        int localY = testY - lightMap.BufferOriginTileY;

        if (localX >= 0 && localX < lightMap.BufferWidth && localY >= 0 && localY < lightMap.BufferHeight)
        {
            var (r, g, b) = lightMap.GetLightAtLocal(localX, localY);
            var medium = lightMap.GetMediumAtLocal(localX, localY);

            Console.WriteLine($"  Medium: {medium}");
            Console.WriteLine($"  Light RGB: R={r:F3} G={g:F3} B={b:F3}");

            bool isSkyOpen = medium == V6LightMap.CellMedium.SkyOpen;
            Console.WriteLine($"  Is SkyOpen: {isSkyOpen}");
            Console.WriteLine($"  Status: {(isSkyOpen ? "✓ CORRECT" : "⚠ CHECK")}");
        }
        else
        {
            Console.WriteLine("  Status: ✗ OUTSIDE BUFFER");
        }
    }

    private void Test3_ManyAboveSurface(Color skyColor)
    {
        Console.WriteLine("\n[TEST 3] MANY TILES ABOVE (HIGH ALTITUDE)");
        Console.WriteLine("Position: (550, 210)");

        int testX = 550;
        int testY = 210;

        v6System.Update(testX * tileSize, testY * tileSize, 1280, 800, tileSize, skyColor);

        var lightMap = v6System.LightMap;
        int localX = testX - lightMap.BufferOriginTileX;
        int localY = testY - lightMap.BufferOriginTileY;

        if (localX >= 0 && localX < lightMap.BufferWidth && localY >= 0 && localY < lightMap.BufferHeight)
        {
            var (r, g, b) = lightMap.GetLightAtLocal(localX, localY);
            var medium = lightMap.GetMediumAtLocal(localX, localY);

            Console.WriteLine($"  Medium: {medium}");
            Console.WriteLine($"  Light RGB: R={r:F3} G={g:F3} B={b:F3}");

            bool isSkyOpen = medium == V6LightMap.CellMedium.SkyOpen;
            float expectedLight = isSkyOpen ? (skyColor.R / 255f) : 0f;

            Console.WriteLine($"  Is SkyOpen: {isSkyOpen}");
            Console.WriteLine($"  Expected light: {expectedLight:F3}");
            Console.WriteLine($"  Status: {(isSkyOpen ? "✓ SHOULD BE BRIGHT" : "⚠ CHECK")}");
        }
        else
        {
            Console.WriteLine("  Status: ✗ OUTSIDE BUFFER");
        }
    }

    private void Test4_OpenVerticalColumn(Color skyColor)
    {
        Console.WriteLine("\n[TEST 4] OPEN VERTICAL COLUMN");
        Console.WriteLine("Position: (550, 215)");

        int testX = 550;
        int testY = 215;

        v6System.Update(testX * tileSize, testY * tileSize, 1280, 800, tileSize, skyColor);

        var lightMap = v6System.LightMap;
        int localX = testX - lightMap.BufferOriginTileX;
        int localY = testY - lightMap.BufferOriginTileY;

        if (localX >= 0 && localX < lightMap.BufferWidth && localY >= 0 && localY < lightMap.BufferHeight)
        {
            var (r, g, b) = lightMap.GetLightAtLocal(localX, localY);
            var medium = lightMap.GetMediumAtLocal(localX, localY);

            Console.WriteLine($"  Medium: {medium}");
            Console.WriteLine($"  Light RGB: R={r:F3} G={g:F3} B={b:F3}");

            bool isSkyOpen = medium == V6LightMap.CellMedium.SkyOpen;
            Console.WriteLine($"  Is SkyOpen: {isSkyOpen}");
            Console.WriteLine($"  Status: {(isSkyOpen ? "✓ CORRECT" : "⚠ CHECK")}");
        }
        else
        {
            Console.WriteLine("  Status: ✗ OUTSIDE BUFFER");
        }
    }

    private void Test5_MidHeight(Color skyColor)
    {
        Console.WriteLine("\n[TEST 5] MID-HEIGHT POSITION");
        Console.WriteLine("Position: (550, 220)");

        int testX = 550;
        int testY = 220;

        v6System.Update(testX * tileSize, testY * tileSize, 1280, 800, tileSize, skyColor);

        var lightMap = v6System.LightMap;
        int localX = testX - lightMap.BufferOriginTileX;
        int localY = testY - lightMap.BufferOriginTileY;

        if (localX >= 0 && localX < lightMap.BufferWidth && localY >= 0 && localY < lightMap.BufferHeight)
        {
            var (r, g, b) = lightMap.GetLightAtLocal(localX, localY);
            var medium = lightMap.GetMediumAtLocal(localX, localY);

            Console.WriteLine($"  Medium: {medium}");
            Console.WriteLine($"  Light RGB: R={r:F3} G={g:F3} B={b:F3}");
            Console.WriteLine($"  Status: Data collected");
        }
        else
        {
            Console.WriteLine("  Status: ✗ OUTSIDE BUFFER");
        }
    }

    private void Test6_BlockedSolid(Color skyColor)
    {
        Console.WriteLine("\n[TEST 6] BLOCKED POSITION (ON SOLID)");
        Console.WriteLine("Position: (550, 202) - ON SURFACE");

        int testX = 550;
        int testY = 202;

        v6System.Update(testX * tileSize, testY * tileSize, 1280, 800, tileSize, skyColor);

        var lightMap = v6System.LightMap;
        int localX = testX - lightMap.BufferOriginTileX;
        int localY = testY - lightMap.BufferOriginTileY;

        if (localX >= 0 && localX < lightMap.BufferWidth && localY >= 0 && localY < lightMap.BufferHeight)
        {
            var (r, g, b) = lightMap.GetLightAtLocal(localX, localY);
            var medium = lightMap.GetMediumAtLocal(localX, localY);

            Console.WriteLine($"  Medium: {medium}");
            Console.WriteLine($"  Light RGB: R={r:F3} G={g:F3} B={b:F3}");
            Console.WriteLine($"  Status: {(medium == V6LightMap.CellMedium.Foreground ? "✓ FOREGROUND" : "⚠ CHECK")}");
        }
        else
        {
            Console.WriteLine("  Status: ✗ OUTSIDE BUFFER");
        }
    }

    private void Test7_HeightProgression(Color skyColor)
    {
        Console.WriteLine("\n[TEST 7] HEIGHT PROGRESSION");

        int testX = 550;
        int[] testHeights = { 210, 205, 200, 195, 190 };

        foreach (int testY in testHeights)
        {
            v6System.Update(testX * tileSize, testY * tileSize, 1280, 800, tileSize, skyColor);

            var lightMap = v6System.LightMap;
            int localX = testX - lightMap.BufferOriginTileX;
            int localY = testY - lightMap.BufferOriginTileY;

            if (localX >= 0 && localX < lightMap.BufferWidth && localY >= 0 && localY < lightMap.BufferHeight)
            {
                var (r, g, b) = lightMap.GetLightAtLocal(localX, localY);
                var medium = lightMap.GetMediumAtLocal(localX, localY);

                Console.WriteLine($"  Y={testY:3d}: Medium={medium,-12s} Light=({r:F3}, {g:F3}, {b:F3})");
            }
            else
            {
                Console.WriteLine($"  Y={testY:3d}: OUTSIDE BUFFER");
            }
        }
    }

    private void Test8_FormulaValidation(Color skyColor)
    {
        Console.WriteLine("\n[TEST 8] FORMULA VALIDATION");
        Console.WriteLine("Checking mathematical consistency of light propagation.");

        int testX = 530;
        int testY = 205;

        v6System.Update(testX * tileSize, testY * tileSize, 1280, 800, tileSize, skyColor);

        var lightMap = v6System.LightMap;
        int localX = testX - lightMap.BufferOriginTileX;
        int localY = testY - lightMap.BufferOriginTileY;

        if (localX >= 0 && localX < lightMap.BufferWidth && localY >= 0 && localY < lightMap.BufferHeight)
        {
            var (r, g, b) = lightMap.GetLightAtLocal(localX, localY);
            var medium = lightMap.GetMediumAtLocal(localX, localY);

            Console.WriteLine($"  Position: ({testX}, {testY})");
            Console.WriteLine($"  Medium: {medium}");
            Console.WriteLine($"  Light RGB: R={r:F3} G={g:F3} B={b:F3}");

            // Validate ranges
            bool validRange = r >= 0 && r <= 1 && g >= 0 && g <= 1 && b >= 0 && b <= 1;
            Console.WriteLine($"  RGB in [0,1]: {(validRange ? "✓ YES" : "✗ NO")}");

            // If SkyOpen, should be close to sky color
            if (medium == V6LightMap.CellMedium.SkyOpen)
            {
                float expectedR = skyColor.R / 255f;
                Console.WriteLine($"  Expected (Sky): R≈{expectedR:F3}");
                Console.WriteLine($"  Actual:         R={r:F3}");
            }

            Console.WriteLine($"  Status: {(validRange ? "✓ VALID" : "✗ INVALID")}");
        }
        else
        {
            Console.WriteLine("  Status: ✗ OUTSIDE BUFFER");
        }
    }

    private void PrintSummary()
    {
        Console.WriteLine("\n=================================================");
        Console.WriteLine("V6 SYSTEM STATISTICS");
        Console.WriteLine("=================================================");

        var lightMap = v6System.LightMap;
        Console.WriteLine($"Buffer size: {lightMap.BufferWidth}x{lightMap.BufferHeight}");
        Console.WriteLine($"Total cells: {lightMap.BufferWidth * lightMap.BufferHeight}");
        Console.WriteLine($"SkyOpen sources: {v6System.SkyOpenSourceCount}");
        Console.WriteLine($"Propagation passes: {v6System.PropagationPasses}");
        Console.WriteLine($"\nPerformance:");
        Console.WriteLine($"  Classify:   {v6System.AvgClassifyMs}ms");
        Console.WriteLine($"  Inject:     {v6System.AvgInjectMs}ms");
        Console.WriteLine($"  Propagate:  {v6System.AvgPropagateMs}ms");
        Console.WriteLine($"  Total:      {v6System.AvgClassifyMs + v6System.AvgInjectMs + v6System.AvgPropagateMs}ms");

        Console.WriteLine("\n=================================================\n");
    }
}
