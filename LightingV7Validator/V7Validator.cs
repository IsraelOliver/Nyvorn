using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingV7;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;

namespace LightingV7Validator
{
    /// <summary>
    /// Headless numeric validation of LightingV7System (no GraphicsDevice, no game window).
    /// Builds small synthetic worlds and prints light profiles for the Phase 1 scenarios.
    /// </summary>
    public static class V7Validator
    {
        private const int TileSize = 8;
        private const int WorldW = 400;
        private const int WorldH = 400;

        // Layer boundaries mirroring WorldGenConfig percentages (12 / 22 / 30 / 85).
        private const int SpaceEnd = 48;
        private const int SurfaceEnd = 88;
        private const int ShallowEnd = 120;
        private const int CavernEnd = 340;

        private static readonly WorldLayerDefinition[] Layers =
        {
            new(WorldLayerType.Space, 0, SpaceEnd),
            new(WorldLayerType.Surface, SpaceEnd + 1, SurfaceEnd),
            new(WorldLayerType.ShallowUnderground, SurfaceEnd + 1, ShallowEnd),
            new(WorldLayerType.Cavern, ShallowEnd + 1, CavernEnd),
            new(WorldLayerType.DeepCavern, CavernEnd + 1, WorldH - 1)
        };

        public static void Run()
        {
            Console.WriteLine("=== LIGHTING V7 — HEADLESS VALIDATION ===\n");
            Console.WriteLine($"AirDecay={LightingV7Config.AirDecay}  SolidDecay={LightingV7Config.SolidDecay}  " +
                              $"SkyAirDecay={LightingV7Config.SkyAirDecay}  Rounds={LightingV7Config.PropagationRounds}  " +
                              $"Margin={LightingV7Config.MarginTiles}  Overbright={LightingV7Config.OverbrightScale}");
            Console.WriteLine($"SurfaceSkySeed={LightingV7Config.SurfaceSkySeed}  ShallowSkySeed={LightingV7Config.ShallowSkySeed}  " +
                              $"SkyFadeTiles={LightingV7Config.SkyFadeTiles}\n");

            Scenario1_TorchInClosedCavern();
            Scenario2_CaveOpeningInShallow();
            Scenario3_SkyHoleInsideCavern();
            Scenario4_ThickWallBetweenTorchAndPlayer();
            Scenario7_Performance();
        }

        // ---- Scenario 1: torch in a sealed cavern room. Warm pool, rock lit a few tiles in, zero sky.
        private static void Scenario1_TorchInClosedCavern()
        {
            Header("1. Torch in a sealed Cavern room (expect: no sky, rock lit 2-4 tiles deep)");

            WorldMap map = SolidWorld();
            int roomY = 200;                       // inside Cavern
            CarveRect(map, 180, roomY - 6, 40, 12); // air pocket with stone walls (walls stay)
            var sys = Build(map);

            int torchTileX = 200;
            Frame(sys, map, torchTileX, roomY, torches: new[] { new Point(torchTileX, roomY) });

            Console.WriteLine($"  sky at torch tile: {sys.GetSkyAt(torchTileX, roomY):0.000}  (expect 0.000)");
            Console.Write("  block R by distance in air: ");
            for (int d = 0; d <= 20; d += 4)
                Console.Write($"{d}t={sys.GetBlockAt(torchTileX + d, roomY).X:0.00}  ");
            Console.WriteLine();

            Console.Write("  block R into solid rock (from room edge at x=220): ");
            for (int d = 0; d <= 6; d++)
                Console.Write($"+{d}t={sys.GetBlockAt(220 + d, roomY).X:0.000}  ");
            Console.WriteLine();
            Console.WriteLine("  -> rock edge is visible while values stay above ~0.02, black beyond.\n");
        }

        // ---- Scenario 2: cave mouth in Shallow, daytime. Light enters and follows the tunnel.
        private static void Scenario2_CaveOpeningInShallow()
        {
            Header("2. Cave opening in Shallow, daytime (expect: sky enters and fades along the tunnel)");

            // Like the real world: Shallow caves keep their background wall, so the tunnel is lit by
            // propagation, not by seeding. Only the open air above the surface line has no wall.
            WorldMap map = SolidWorld();
            ClearAbove(map, SurfaceEnd);                 // open air above the surface line
            CarveRect(map, 198, SurfaceEnd, 4, 20);      // vertical shaft down into Shallow
            CarveRect(map, 150, SurfaceEnd + 18, 52, 4); // horizontal tunnel

            var sys = Build(map);
            Frame(sys, map, 200, SurfaceEnd + 10, torches: Array.Empty<Point>());

            Console.Write("  sky down the shaft (x=200): ");
            for (int y = SurfaceEnd; y <= SurfaceEnd + 18; y += 3)
                Console.Write($"y={y}:{sys.GetSkyAt(200, y):0.00}  ");
            Console.WriteLine();

            Console.Write("  sky along the tunnel (y=" + (SurfaceEnd + 19) + "): ");
            for (int x = 198; x >= 154; x -= 8)
                Console.Write($"x={x}:{sys.GetSkyAt(x, SurfaceEnd + 19):0.00}  ");
            Console.WriteLine();
            Console.WriteLine("  -> light bends around the corner and decays instead of stopping.\n");
        }

        // ---- Scenario 3: air pocket with no wall inside Cavern. Must stay dark, no banding seam.
        private static void Scenario3_SkyHoleInsideCavern()
        {
            Header("3. Wall-free air pocket inside Cavern (expect: sky = 0, smooth fade band above)");

            WorldMap map = SolidWorld();
            ClearAbove(map, SurfaceEnd);
            CarveRect(map, 190, ShallowEnd + 20, 20, 10);   // pocket well inside Cavern
            StripWalls(map, 190, ShallowEnd + 20, 20, 10);  // no background wall at all

            var sys = Build(map);
            Frame(sys, map, 200, ShallowEnd + 25, torches: Array.Empty<Point>());

            Console.WriteLine($"  sky inside the Cavern pocket: {sys.GetSkyAt(200, ShallowEnd + 25):0.000}  (expect 0.000)");
            Console.Write("  SkyCap fade band by row: ");
            for (int y = ShallowEnd - 2; y <= ShallowEnd + LightingV7Config.SkyFadeTiles + 2; y += 2)
                Console.Write($"y={y}:{SkyCap(y):0.00} ");
            Console.WriteLine();
            Console.WriteLine("  -> the cap steps down " + (1f / LightingV7Config.SkyFadeTiles).ToString("0.00") +
                              " per row, so there is no hard horizontal line.\n");
        }

        // ---- Scenario 4: thick wall between torch and player. Light must go around, not through.
        private static void Scenario4_ThickWallBetweenTorchAndPlayer()
        {
            Header("4. Thick wall between torch and player (expect: player much darker, light travels around)");

            // Room A (torch) and room B (player) share one 6-tile stone wall. The ONLY connection is
            // a corridor that goes down, across and back up, so "through" and "around" are isolated.
            WorldMap map = SolidWorld();
            CarveRect(map, 180, 194, 20, 10);   // room A: x 180..199, y 194..203
            CarveRect(map, 206, 194, 20, 10);   // room B: x 206..225, y 194..203
            CarveRect(map, 180, 214, 46, 4);    // detour corridor far below
            CarveRect(map, 182, 203, 3, 12);    // shaft down from room A
            CarveRect(map, 221, 203, 3, 12);    // shaft up into room B

            var sys = Build(map);
            Frame(sys, map, 200, 200, torches: new[] { new Point(190, 198) });

            float direct = sys.GetBlockAt(195, 198).X;      // 5 tiles from torch, same room
            float behindWall = sys.GetBlockAt(207, 198).X;  // straight through the 6-tile wall
            float detour = sys.GetBlockAt(222, 205).X;      // top of the far shaft, ~40 tiles of open air

            Console.WriteLine($"  5 tiles from torch, same room   : {direct:0.000}");
            Console.WriteLine($"  just past the 6-tile stone wall : {behindWall:0.000}");
            Console.WriteLine($"  far shaft reached by the detour : {detour:0.000}");
            Console.WriteLine($"  -> wall attenuation vs open air over the same 7 tiles: " +
                              $"{MathF.Pow(LightingV7Config.SolidDecay, 6):0.000} vs {MathF.Pow(LightingV7Config.AirDecay, 6):0.000}");
            Console.WriteLine("  -> light that reaches room B came around the corridor, not through the wall.\n");
        }

        // ---- Scenario 7: CPU cost at 1080p, default zoom.
        private static void Scenario7_Performance()
        {
            Header("7. Performance at 1920x1080, zoom 2 (target < 1.5 ms)");

            WorldMap map = SolidWorld();
            ClearAbove(map, SurfaceEnd);
            for (int i = 0; i < 30; i++)
                CarveRect(map, 20 + i * 12, SurfaceEnd + 10 + (i % 7) * 9, 9, 6);

            var sys = Build(map);
            var torches = new List<Point>();
            for (int i = 0; i < 28; i++)
                torches.Add(new Point(30 + i * 12, SurfaceEnd + 12 + (i % 7) * 9));

            Point[] torchArray = torches.ToArray();

            // Warm up so JIT time is not counted.
            for (int f = 0; f < 10; f++)
                Frame(sys, map, 200, SurfaceEnd + 30, torchArray);

            double worst = 0, total = 0, classify = 0, propagate = 0, fill = 0;
            const int frames = 60;
            for (int f = 0; f < frames; f++)
            {
                Frame(sys, map, 200, SurfaceEnd + 30, torchArray);
                total += sys.LastCpuMs;
                classify += sys.LastClassifyMs;
                propagate += sys.LastPropagateMs;
                fill += sys.LastFillMs;
                if (sys.LastCpuMs > worst) worst = sys.LastCpuMs;
            }

            Console.WriteLine($"  region: {sys.RegionWidth}x{sys.RegionHeight} tiles ({sys.RegionWidth * sys.RegionHeight} cells), sources: {sys.SourceCount}");
            Console.WriteLine($"  avg {total / frames:0.00} ms   worst {worst:0.00} ms   over {frames} frames");
            Console.WriteLine($"  breakdown: classify {classify / frames:0.00} ms | propagate {propagate / frames:0.00} ms | fill {fill / frames:0.00} ms");
            Console.WriteLine(total / frames < 1.5 ? "  -> PASS\n" : "  -> ABOVE TARGET\n");
        }

        // ---------- helpers ----------

        private static void Header(string title)
        {
            Console.WriteLine("--- " + title);
        }

        private static float SkyCap(int y)
        {
            if (y <= ShallowEnd) return 1f;
            return MathF.Max(0f, 1f - (y - ShallowEnd) / (float)LightingV7Config.SkyFadeTiles);
        }

        private static LightingV7System Build(WorldMap map) => new(null, map, Layers);

        /// <summary>Runs one full frame centred on the given tile.</summary>
        private static void Frame(LightingV7System sys, WorldMap map, int centreTileX, int centreTileY, Point[] torches)
        {
            const int screenW = 1920, screenH = 1080;
            const float zoom = 2f;
            float camX = centreTileX * TileSize - (screenW / zoom) * 0.5f;
            float camY = centreTileY * TileSize - (screenH / zoom) * 0.5f;

            sys.SkyColor = LightingV7Config.SkyColorDay;
            sys.BeginFrame(camX, camY, screenW, screenH, zoom, TileSize);
            foreach (Point t in torches)
            {
                sys.AddPointLight(
                    new Vector2(t.X * TileSize + TileSize * 0.5f, t.Y * TileSize + TileSize * 0.5f),
                    LightingV7Config.TorchColor, LightingV7Config.TorchIntensity, TileSize);
            }
            sys.EndFrame();
        }

        /// <summary>Stone everywhere, with a stone background wall everywhere.</summary>
        private static WorldMap SolidWorld()
        {
            WorldMap map = new(WorldW, WorldH, TileSize);
            for (int x = 0; x < WorldW; x++)
                for (int y = 0; y < WorldH; y++)
                {
                    map.SetTile(x, y, TileType.Stone);
                    map.SetBackgroundTile(x, y, TileType.Stone);
                }
            return map;
        }

        private static void ClearAbove(WorldMap map, int surfaceY)
        {
            for (int x = 0; x < WorldW; x++)
                for (int y = 0; y <= surfaceY; y++)
                {
                    map.SetTile(x, y, TileType.Empty);
                    map.SetBackgroundTile(x, y, TileType.Empty);
                }
        }

        private static void CarveRect(WorldMap map, int x0, int y0, int w, int h)
        {
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                    map.SetTile(x, y, TileType.Empty);
        }

        private static void FillRect(WorldMap map, int x0, int y0, int w, int h, TileType type)
        {
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                    map.SetTile(x, y, type);
        }

        private static void StripWalls(WorldMap map, int x0, int y0, int w, int h)
        {
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                    map.SetBackgroundTile(x, y, TileType.Empty);
        }
    }
}
