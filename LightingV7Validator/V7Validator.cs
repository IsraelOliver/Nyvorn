using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingV7;
using Nyvorn.Source.World;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.Gameplay.World.Simulation;
using Nyvorn.Source.Engine.Physics.Liquids;

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

            Console.WriteLine($"DirectShadows={LightingV7Config.DirectShadowsEnabled}  DirectRadius={LightingV7Config.DirectRadiusTiles}  " +
                              $"Bounce={LightingV7Config.BounceStrength}  AOStrength={LightingV7Config.AOStrength}\n");

            Scenario11_DirectionalSun();
            Scenario12_WaterPerChannel();
            Scenario13_SkyColourByHour();
            Scenario8_PlatformShadow();
            Scenario9_AmbientOcclusion();
            Scenario10_PlayerBehindBlock();
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
                Console.Write($"{d}t={Total(sys, torchTileX + d, roomY):0.00}  ");
            Console.WriteLine();

            Console.Write("  block R into solid rock (from room edge at x=220): ");
            for (int d = 0; d <= 6; d++)
                Console.Write($"+{d}t={Total(sys, 220 + d, roomY):0.000}  ");
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

            float direct = Total(sys, 195, 198);      // 5 tiles from torch, same room
            float behindWall = Total(sys, 207, 198);  // straight through the 6-tile wall
            float detour = Total(sys, 222, 205);      // top of the far shaft, ~40 tiles of open air

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

            double worst = 0, total = 0, classify = 0, propagate = 0, fill = 0, ao = 0, direct = 0;
            const int frames = 60;
            for (int f = 0; f < frames; f++)
            {
                Frame(sys, map, 200, SurfaceEnd + 30, torchArray);
                total += sys.LastCpuMs;
                classify += sys.LastClassifyMs;
                propagate += sys.LastPropagateMs;
                fill += sys.LastFillMs;
                ao += sys.LastAoMs;
                direct += sys.LastDirectMs;
                if (sys.LastCpuMs > worst) worst = sys.LastCpuMs;
            }

            Console.WriteLine($"  region: {sys.RegionWidth}x{sys.RegionHeight} tiles ({sys.RegionWidth * sys.RegionHeight} cells), sources: {sys.SourceCount}");
            Console.WriteLine($"  avg {total / frames:0.00} ms   worst {worst:0.00} ms   over {frames} frames");
            Console.WriteLine($"  breakdown: classify {classify / frames:0.00} | ao {ao / frames:0.00} | direct {direct / frames:0.00} " +
                              $"| propagate {propagate / frames:0.00} | fill {fill / frames:0.00}  (ms)");
            Console.WriteLine(total / frames < 1.5 ? "  -> PASS\n" : "  -> ABOVE TARGET\n");
        }

        // ---- Scenario 8: a short platform on a back wall must throw a diagonal shadow past it.
        private static void Scenario8_PlatformShadow()
        {
            Header("8. Platform on a back wall (expect: diagonal shadow away from the torch, wall still readable)");

            // Plank at y=200 spanning x 202..205, torch low and to the LEFT of it, so the shadow
            // is thrown up and to the RIGHT - the diagonal cast in the reference art.
            WorldMap map = SolidWorld();
            const int plankY = 200;
            CarveRect(map, 180, plankY - 20, 50, 32);             // open room, background wall kept
            FillRect(map, 202, plankY, 4, 1, TileType.Platform);  // the plank

            // Torch kept well inside DirectRadiusTiles of the probe row, or there is no direct term
            // there to be shadowed in the first place.
            var sys = Build(map);
            Frame(sys, map, 204, plankY - 4, torches: new[] { new Point(198, plankY + 4) });

            int probeY = plankY - 4;
            Console.WriteLine($"  direct term along y={probeY} (7 tiles above the plank):");
            Console.Write("   ");
            for (int x = 198; x <= 218; x += 2)
                Console.Write($"x={x}:{Direct(sys, x, probeY):0.00} ");
            Console.WriteLine();

            float lit = Direct(sys, 200, probeY);
            float shadowed = Direct(sys, 210, probeY);
            float shadowedTotal = Total(sys, 210, probeY);
            Console.WriteLine($"  beside the plank's shadow : direct {lit:0.000}");
            Console.WriteLine($"  inside the plank's shadow : direct {shadowed:0.000}, with bounce {shadowedTotal:0.000}");
            Console.WriteLine($"  -> shadow is {(shadowed <= 0.0001f ? "hard (direct = 0)" : "NOT hard")}; " +
                              $"the bounce leaves {shadowedTotal:0.000} there, so the wall stays readable.\n");
        }

        // ---- Scenario 9: corners and undersides pick up contact darkening.
        private static void Scenario9_AmbientOcclusion()
        {
            Header("9. Ambient occlusion (expect: floor/wall corner and under a platform darker)");

            WorldMap map = SolidWorld();
            int roomY = 200;
            CarveRect(map, 180, roomY - 10, 40, 11);             // room with floor at roomY+1
            FillRect(map, 200, roomY - 4, 4, 1, TileType.Platform);

            var sys = Build(map);
            Frame(sys, map, 200, roomY, torches: Array.Empty<Point>());

            Console.WriteLine($"  open air, no neighbours      : ao {Ao(sys, 190, roomY - 6):0.000}");
            Console.WriteLine($"  resting on the floor         : ao {Ao(sys, 190, roomY):0.000}");
            Console.WriteLine($"  floor/wall inner corner      : ao {Ao(sys, 180, roomY):0.000}");
            Console.WriteLine($"  directly under the platform  : ao {Ao(sys, 201, roomY - 3):0.000}");
            Console.WriteLine($"  -> lower is darker; fully enclosed would be {1f - LightingV7Config.AOStrength:0.000}.\n");
        }

        // ---- Scenario 10: a single block between torch and player puts the player in shadow.
        private static void Scenario10_PlayerBehindBlock()
        {
            Header("10. Player behind one block (expect: player cell in shadow, neighbours lit)");

            WorldMap map = SolidWorld();
            int roomY = 200;
            CarveRect(map, 180, roomY - 8, 40, 16);
            FillRect(map, 200, roomY, 1, 1, TileType.Stone);  // single blocking tile

            var sys = Build(map);
            Frame(sys, map, 200, roomY, torches: new[] { new Point(194, roomY) });

            Console.WriteLine($"  cell right behind the block : direct {Direct(sys, 202, roomY):0.000}  total {Total(sys, 202, roomY):0.000}");
            Console.WriteLine($"  one row above (clear line)  : direct {Direct(sys, 202, roomY - 2):0.000}  total {Total(sys, 202, roomY - 2):0.000}");
            Console.WriteLine("  -> the shadow is a band behind the block, not a darkening of the whole area.\n");
        }

        // ---- Scenario 11: a hole in the surface must throw a slanted shaft, and none at night.
        private static void Scenario11_DirectionalSun()
        {
            Header("11. Directional sun through an opening (expect: slanted shaft, leaning with the hour)");

            // A roofed hall with one hole in the ceiling. The beam enters the hole and lands on the
            // floor; where it lands is the whole point, so that is what gets measured.
            const int ceilingY = SurfaceEnd + 2;   // 90
            const int floorY = SurfaceEnd + 26;    // 114, still inside Shallow
            const int holeX = 200;

            WorldMap map = SolidWorld();
            ClearAbove(map, ceilingY - 1);                                  // open sky right down to the roof
            CarveRect(map, 120, ceilingY + 1, 160, floorY - ceilingY - 1);  // the hall
            CarveRect(map, holeX, ceilingY, 3, 1);                          // hole in the ceiling

            var sys = Build(map);
            int travel = floorY - 1 - ceilingY;

            foreach ((string label, float hour) in new[] { ("09:00", 9f / 24f), ("12:00", 12f / 24f), ("16:00", 16f / 24f), ("23:00", 23f / 24f) })
            {
                FrameAtHour(sys, map, holeX, (ceilingY + floorY) / 2, hour);

                float slope = LightingV7Sky.GetSunSlope(hour);
                int brightestX = -1;
                float brightest = 0f;
                for (int x = 120; x < 280; x++)
                {
                    float value = sys.GetSunAt(x, floorY - 1);
                    if (value > brightest) { brightest = value; brightestX = x; }
                }

                string landing = brightest > 0f
                    ? $"lands at x={brightestX} ({brightestX - holeX:+0;-0;0} from the hole), value {brightest:0.00}"
                    : "no beam";
                Console.WriteLine($"  {label}  slope {slope,5:0.00}  predicted drift {slope * travel,6:0.0}  {landing}");
            }

            Console.WriteLine("  -> the landing point tracks the slant, and 23:00 has no beam at all.\n");
        }

        // ---- Scenario 12: light under water must lose red before blue.
        private static void Scenario12_WaterPerChannel()
        {
            Header("12. Water per-channel decay (expect: red dies first, blue carries deepest)");

            WorldMap map = SolidWorld();
            int surfaceRow = 200;
            CarveRect(map, 180, surfaceRow - 4, 40, 30);

            var liquid = new LiquidSystem(map) { };
            for (int x = 182; x < 218; x++)
                for (int y = surfaceRow; y < surfaceRow + 24; y++)
                    liquid.SetLiquidAt(x * TileSize + 4, y * TileSize + 4, LiquidType.Water, true);

            var sys = Build(map);
            sys.LiquidSystem = liquid;
            Frame(sys, map, 200, surfaceRow, torches: new[] { new Point(200, surfaceRow - 2) });

            Console.WriteLine("  depth below the torch (R / G / B):");
            for (int d = 0; d <= 16; d += 4)
            {
                Vector3 total = sys.GetBlockAt(200, surfaceRow + d) + sys.GetDirectAt(200, surfaceRow + d);
                Console.WriteLine($"   {d,2} tiles: {total.X:0.000} / {total.Y:0.000} / {total.Z:0.000}" +
                                  (total.X > 0.0001f ? $"   B/R = {total.Z / total.X:0.00}x" : ""));
            }
            Console.WriteLine($"  -> the torch itself is B/R {LightingV7Config.TorchColor.Z / LightingV7Config.TorchColor.X:0.00}; " +
                              "B/R climbing with depth is the water shifting the light blue.\n");
        }

        // ---- Scenario 13: the sky colour curve across a day.
        private static void Scenario13_SkyColourByHour()
        {
            Header("13. Sky and sun colour by hour (expect: bright neutral noon, warm dusk, dark night)");

            var clear = DefaultSkyState(rain: 0f, eclipse: 0f, conjunction: 0f);
            foreach ((string label, float hour) in new[]
            {
                ("03:00", 3f / 24f), ("06:00", 6f / 24f), ("09:00", 9f / 24f), ("12:00", 12f / 24f),
                ("17:00", 17f / 24f), ("18:30", 18.5f / 24f), ("21:00", 21f / 24f)
            })
            {
                Vector3 skyColor = LightingV7Sky.GetSkyColor(clear, hour);
                Vector3 sunColor = LightingV7Sky.GetSunColor(clear, hour);
                Console.WriteLine($"   {label}  sky ({skyColor.X:0.00} {skyColor.Y:0.00} {skyColor.Z:0.00})   " +
                                  $"sun ({sunColor.X:0.00} {sunColor.Y:0.00} {sunColor.Z:0.00})");
            }

            Vector3 noonClear = LightingV7Sky.GetSkyColor(clear, 12f / 24f);
            Vector3 noonRain = LightingV7Sky.GetSkyColor(DefaultSkyState(1f, 0f, 0f), 12f / 24f);
            Vector3 noonEclipse = LightingV7Sky.GetSkyColor(DefaultSkyState(0f, 1f, 0f), 12f / 24f);
            Vector3 nightPlain = LightingV7Sky.GetSkyColor(clear, 0f);
            Vector3 nightConjunction = LightingV7Sky.GetSkyColor(DefaultSkyState(0f, 0f, 1f), 0f);

            Console.WriteLine($"   noon clear   {noonClear.Y:0.00} -> full rain {noonRain.Y:0.00} (x{noonRain.Y / noonClear.Y:0.00})");
            Console.WriteLine($"   noon clear   {noonClear.Y:0.00} -> full eclipse {noonEclipse.Y:0.00}");
            Console.WriteLine($"   night plain  {nightPlain.Y:0.00} -> full conjunction {nightConjunction.Y:0.00} (x{nightConjunction.Y / nightPlain.Y:0.00})");
            Console.WriteLine($"   sun at 23:00 is {LightingV7Sky.GetSunColor(clear, 23f / 24f).Length():0.000} (zero = no shafts at night)\n");
        }

        private static SkyState DefaultSkyState(float rain, float eclipse, float conjunction)
        {
            return new SkyState(
                Color.Black, Color.Black, Color.Black, Color.Black, Color.White, Color.Black,
                0f, 0f,
                new MoonState(0f, 0f, 0f), new MoonState(0f, 0f, 0f),
                conjunction,
                0f, 0f, 0f,
                rain, eclipse, 0f, 0f, 0f, 0f);
        }

        private static void FrameAtHour(LightingV7System sys, WorldMap map, int centreTileX, int centreTileY, float hour)
        {
            var skyState = DefaultSkyState(0f, 0f, 0f);
            sys.SunColor = LightingV7Sky.GetSunColor(skyState, hour);
            sys.SunSlope = LightingV7Sky.GetSunSlope(hour);
            sys.SkyColor = LightingV7Sky.GetSkyColor(skyState, hour);

            const int screenW = 1920, screenH = 1080;
            const float zoom = 2f;
            sys.BeginFrame(centreTileX * TileSize - (screenW / zoom) * 0.5f,
                           centreTileY * TileSize - (screenH / zoom) * 0.5f,
                           screenW, screenH, zoom, TileSize);
            sys.EndFrame();
        }

        // ---------- helpers ----------

        private static float Direct(LightingV7System sys, int x, int y) => sys.GetDirectAt(x, y).X;

        private static float Total(LightingV7System sys, int x, int y) => sys.GetBlockAt(x, y).X + sys.GetDirectAt(x, y).X;

        private static float Ao(LightingV7System sys, int x, int y) => sys.GetAmbientOcclusionAt(x, y);

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
