using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.World.Simulation
{
    // Terraria-style light propagation: a multi-source BFS flood-fill seeded from every tile with a
    // clear vertical line to the sky (WorldMap.HasOpenSkyAbove - already cheap, since it stops at the
    // first solid tile scanning upward), decaying outward through neighbors at two different rates
    // depending on whether the neighbor is solid or open. Unlike a per-column vertical fade, this lets
    // light bend around corners and bleed a little way under an overhang or into a cave mouth instead
    // of cutting off sharply at the surface line.
    //
    // Light is tracked as three independent channels (R, G, B), each running the exact same BFS/decay
    // - this is what makes colors mix correctly where sources overlap (a warm torch seed decaying
    // next to a cool night-sky seed) without any special blending logic: at any given tile, each
    // channel independently ends up holding whichever nearby source reached it strongest, and reading
    // all three back together produces the right mixed color, the same trick Terraria's colored
    // lighting uses.
    //
    // Two separate results come out of the same per-frame computation:
    //  - lightR/G/B ("combined"): sky color + occlusion + point lights mixed together, used to tint
    //    solid tiles (GetLightAt/CopyLightGridTo). A sky-exposed tile is only as bright/colored as the
    //    sky currently is (dim and blue at night), and a buried one is darker still, proportional to
    //    how walled-off it is.
    //  - glowR/G/B ("point lights only", no sky seed at all): how much extra light a torch alone adds
    //    at this tile, zero anywhere out of reach of one (CopyGlowGridTo). This is what lets a torch
    //    visibly punch through the night's full-screen darkness overlay (DrawNightOverlay) even out in
    //    the open, where there's no solid tile for the combined value to tint - see the additive glow
    //    pass in PlayingSessionViewCoordinator/PlayingState.
    //
    // Recomputed from scratch every frame for the visible area + PropagationMarginTiles - there's no
    // persistence or incremental (dirty-region) update yet. Simple on purpose for this first pass;
    // if profiling ever shows this is too expensive, the natural next step is only recomputing when
    // the camera moves far enough or a tile actually changes nearby, not a rewrite of the algorithm.
    public sealed class WorldLightingSystem
    {
        // Sky/occlusion pass: gentle decay so light can bend a good distance sideways into a cave
        // mouth or under an overhang before fully fading.
        private const float OpenTileLightDecay = 0.02f;
        private const float SolidTileLightDecay = 0.12f;
        // Glow-only pass (see CopyGlowGridTo): much steeper, especially through open air. A torch
        // standing in open sky has nothing above it to block the sky-pass's gentle decay, so with the
        // same 0.02 it travelled a full 50 tiles straight up into open air before fading out - an
        // unobstructed beam reaching way off the top of the screen instead of a contained glow bubble
        // around the torch. This pass only ever needs to look local (a few tiles), so it can afford a
        // much faster falloff. Additive blending has no natural ceiling the way multiply does, so on
        // top of the steep falloff the seed itself is capped well under full strength (see
        // SeedPointLightsInto) - full-strength additive color stacked on an already-lit tile looked
        // like a flat, blown-out patch pasted over the scene rather than something actually emitting
        // light into it.
        private const float GlowOpenTileDecay = 0.18f;
        private const float GlowSolidTileDecay = 0.5f;
        private const float GlowPeakIntensity = 0.45f;
        private const int PropagationMarginTiles = 12;

        // Placeholder torch color until real light sources with their own configurable color exist -
        // a warm, slightly orange flame.
        private static readonly Color PointLightColor = new(255, 150, 60);

        private readonly WorldMap worldMap;
        private readonly Queue<int> propagationQueue = new();
        private readonly List<Vector2> pointLightPositions = new();

        private float[] lightR = Array.Empty<float>();
        private float[] lightG = Array.Empty<float>();
        private float[] lightB = Array.Empty<float>();
        private float[] glowR = Array.Empty<float>();
        private float[] glowG = Array.Empty<float>();
        private float[] glowB = Array.Empty<float>();
        private int bufferOriginTileX;
        private int bufferOriginTileY;
        private int bufferWidth;
        private int bufferHeight;

        public WorldLightingSystem(WorldMap worldMap)
        {
            this.worldMap = worldMap;
        }

        // Set once after SandSystem exists (PlayingSession.InitializeSandSystem, mirroring how other
        // coordinators pick it up). Loose sand occupies tile-grid cells WorldMap considers "open" -
        // without this, a sand dune read as open air for occlusion purposes, so light never dimmed
        // going deeper into it and only started fading once it hit actual solid ground underneath,
        // reading as an odd shadow seam a few tiles below the dune's surface instead of the dune
        // itself gradually darkening like any other material would.
        public SandSystem SandSystem { get; set; }

        // Called once per frame before Update, with the current world-space position of every
        // active point light (e.g. TorchRuntimeSystem.GetLightSourcePositions()). Point lights use
        // the exact same BFS/decay as the sky-exposed seeds below - a torch is just another seed at
        // its own color, so it lights a cave the same way a shaft to the surface would.
        public void SetPointLights(IEnumerable<Vector2> worldPositions)
        {
            pointLightPositions.Clear();
            if (worldPositions != null)
                pointLightPositions.AddRange(worldPositions);
        }

        // skyColor is the current ambient sky color (white at noon, dark blue at night, warm at
        // sunset) - the seed color for anything with a clear line to the sky. Using the real color
        // (instead of always seeding pure white) is what lets a torch actually stand out against the
        // dark at night: without it, outdoor tiles were always "fully lit" for occlusion purposes
        // regardless of the clock, so there was nothing for a torch placed outdoors to visibly
        // brighten or tint warm.
        public void Update(float dt, Vector2 cameraPosition, float cameraZoom, int screenWidth, int screenHeight, Color skyColor)
        {
            if (worldMap == null || screenWidth <= 0 || screenHeight <= 0 || cameraZoom <= 0f)
                return;

            int tileSize = worldMap.TileSize;
            // Rounded up to a whole multiple of the tile size (instead of the raw screenWidth/Zoom,
            // which is almost never an exact multiple) so the tile-window width/height comes out the
            // same every frame regardless of the camera's sub-tile position. Without this, floor/ceil
            // against a non-aligned view width made the window oscillate by one tile as the camera
            // moved, which made the caller's light texture think its size changed every frame.
            float viewWidth = MathF.Ceiling((screenWidth / cameraZoom) / tileSize) * tileSize;
            float viewHeight = MathF.Ceiling((screenHeight / cameraZoom) / tileSize) * tileSize;

            int startTileX = (int)MathF.Floor(cameraPosition.X / tileSize) - PropagationMarginTiles;
            int endTileX = (int)MathF.Ceiling((cameraPosition.X + viewWidth) / tileSize) + PropagationMarginTiles;
            int startTileY = Math.Clamp((int)MathF.Floor(cameraPosition.Y / tileSize) - PropagationMarginTiles, 0, worldMap.Height - 1);
            int endTileY = Math.Clamp((int)MathF.Ceiling((cameraPosition.Y + viewHeight) / tileSize) + PropagationMarginTiles, 0, worldMap.Height - 1);

            if (endTileX < startTileX || endTileY < startTileY)
                return;

            bufferOriginTileX = startTileX;
            bufferOriginTileY = startTileY;
            bufferWidth = endTileX - startTileX + 1;
            bufferHeight = endTileY - startTileY + 1;

            int cellCount = bufferWidth * bufferHeight;
            if (lightR.Length < cellCount)
            {
                lightR = new float[cellCount];
                lightG = new float[cellCount];
                lightB = new float[cellCount];
                glowR = new float[cellCount];
                glowG = new float[cellCount];
                glowB = new float[cellCount];
            }

            Array.Clear(lightR, 0, cellCount);
            Array.Clear(lightG, 0, cellCount);
            Array.Clear(lightB, 0, cellCount);
            propagationQueue.Clear();
            SeedSkyExposedTiles(skyColor);
            SeedPointLightsInto(lightR, lightG, lightB, 1f);
            Propagate(lightR, lightG, lightB, OpenTileLightDecay, SolidTileLightDecay);

            Array.Clear(glowR, 0, cellCount);
            Array.Clear(glowG, 0, cellCount);
            Array.Clear(glowB, 0, cellCount);
            propagationQueue.Clear();
            SeedPointLightsInto(glowR, glowG, glowB, GlowPeakIntensity);
            Propagate(glowR, glowG, glowB, GlowOpenTileDecay, GlowSolidTileDecay);
        }

        // tileX/tileY are raw (unwrapped) world tile coordinates - the same space the camera position
        // passed to Update lives in. Returns Color.White (neutral, no tint) for anything outside the
        // last-computed window, a safe default.
        public Color GetLightAt(int tileX, int tileY)
        {
            int localX = tileX - bufferOriginTileX;
            int localY = tileY - bufferOriginTileY;
            if (localX < 0 || localX >= bufferWidth || localY < 0 || localY >= bufferHeight)
                return Color.White;

            int index = (localY * bufferWidth) + localX;
            return ToPerceptualColor(lightR[index], lightG[index], lightB[index]);
        }

        // Bounds of the last window computed by Update, in the same raw tile-coordinate space as
        // GetLightAt - lets a renderer map the grid back onto world/pixel space for a smooth
        // texture-based draw (Fase 4) instead of querying tile-by-tile.
        public int WindowOriginTileX => bufferOriginTileX;
        public int WindowOriginTileY => bufferOriginTileY;
        public int WindowWidth => bufferWidth;
        public int WindowHeight => bufferHeight;

        // Fills destination (row-major, same layout as the internal buffer) with each tile's light
        // color, for a renderer to upload into a texture and let GPU bilinear sampling smooth the
        // tile-to-tile transitions instead of hard per-tile edges.
        //
        // Open tiles with nothing physically occupying them are forced to full white here regardless
        // of their actual physics value. Nothing is ever drawn as terrain for a truly open tile, so
        // once this grid is stretched into one big rectangle and multiplied over the whole screen, its
        // texel would otherwise darken/tint whatever's visible behind it - sky, background, water -
        // even though there's no terrain there for it to apply to. Solid tiles AND loose sand (both
        // covered by IsAttenuatingAt - either way there's a sprite/pixel drawn under this overlay) show
        // their real, sky- and torch-aware color instead, which is also what lets sand's shading come
        // from this same stretched-and-bilinear-filtered texture instead of a separate per-pixel CPU
        // tint - the tile-to-tile fade is the GPU's linear sampling, not extra draw calls. Point-light-
        // only glow (see CopyGlowGridTo) is a separate, additive pass that's exactly what covers open
        // air instead.
        //
        // NOTE: letting an open tile bordering a wall keep its own real value (instead of always
        // forcing white) was tried here to reduce the boundary glow described above - it helped the
        // night direction (wall brightened by a hard white neighbor) but left the day direction
        // (wall's own warm color bleeding out into open air) essentially unchanged, since that bleed
        // comes from the wall's color itself, not from the neighbor's forced value. Reverted; fixing
        // the day direction for real needs the overlay to never land on open-air pixels at all
        // (geometry/stencil-based masking), not a change to what value open tiles hold.
        public void CopyLightGridTo(Color[] destination)
        {
            for (int localY = 0; localY < bufferHeight; localY++)
            {
                int tileY = bufferOriginTileY + localY;
                for (int localX = 0; localX < bufferWidth; localX++)
                {
                    int tileX = worldMap.WrapTileX(bufferOriginTileX + localX);
                    int index = (localY * bufferWidth) + localX;
                    destination[index] = IsAttenuatingAt(tileX, tileY)
                        ? ToPerceptualColor(lightR[index], lightG[index], lightB[index])
                        : Color.White;
                }
            }
        }

        // Fills destination with each tile's point-light-only glow color (no sky mixed in, zero
        // anywhere out of reach of a point light) - meant to be drawn with an additive blend over the
        // whole screen (sky, terrain, entities alike), since it only ever adds light back in and never
        // darkens anything. This is what lets a torch punch through DrawNightOverlay's full-screen
        // darkness even out in the open, where there's no solid tile for CopyLightGridTo's masked
        // multiply to apply to.
        public void CopyGlowGridTo(Color[] destination)
        {
            int cellCount = bufferWidth * bufferHeight;
            for (int i = 0; i < cellCount; i++)
                destination[i] = ToColor(glowR[i], glowG[i], glowB[i]);
        }

        private static Color ToColor(float r, float g, float b)
        {
            return new Color(
                (byte)(Math.Clamp(r, 0f, 1f) * 255f),
                (byte)(Math.Clamp(g, 0f, 1f) * 255f),
                (byte)(Math.Clamp(b, 0f, 1f) * 255f),
                (byte)255);
        }

        // A tile-by-tile /debug light readout of a sand dune showed a perfectly smooth linear falloff
        // (8,7,6,5,4,3,2,1,0), yet visually the sand looked fully bright almost all the way down, then
        // snapped to black - the math was fine, human brightness perception isn't linear (a bright
        // color barely looks different at 90% strength, but the same-sized numeric drop right near
        // zero reads as a sudden cutoff). A square-root curve lifts the low-to-mid range so the fade
        // reads as gradual across the tile's whole depth instead of "unchanged, then suddenly dark".
        // Only used for the combined (sky+occlusion+torch) pass that solid tiles/sand render with -
        // the point-light-only glow pass (ToColor, additive) was tuned separately and left linear.
        private const float PerceptualGamma = 0.5f;

        private static Color ToPerceptualColor(float r, float g, float b)
        {
            return new Color(
                (byte)(MathF.Pow(Math.Clamp(r, 0f, 1f), PerceptualGamma) * 255f),
                (byte)(MathF.Pow(Math.Clamp(g, 0f, 1f), PerceptualGamma) * 255f),
                (byte)(MathF.Pow(Math.Clamp(b, 0f, 1f), PerceptualGamma) * 255f),
                (byte)255);
        }

        // HasOpenSkyAbove scans from a tile all the way up to the true world top (y=0) unless it
        // hits something solid first - fine for an occasional call, but calling it once per tile in
        // this window (thousands of them) meant a full scan-to-the-top for every open-air tile,
        // worst-case exactly when the player is standing on the surface in daylight (the single most
        // common situation in the game). Calling it once per COLUMN instead - only at the window's
        // top edge - then walking down within the window with a cheap O(1) IsSolidAt check per tile
        // gives the identical result at a fraction of the cost.
        //
        // Everything with a clear line to the sky - open air and the solid tile it eventually hits -
        // seeds at skyColor, not a hardcoded white. Open tiles' seed value only matters for physics
        // (letting light travel through open air to reach a shadowed pocket) since CopyLightGridTo
        // re-forces them to white for rendering regardless of this value.
        private void SeedSkyExposedTiles(Color skyColor)
        {
            float skyR = skyColor.R / 255f;
            float skyG = skyColor.G / 255f;
            float skyB = skyColor.B / 255f;

            for (int localX = 0; localX < bufferWidth; localX++)
            {
                int tileX = worldMap.WrapTileX(bufferOriginTileX + localX);

                // Checking HasOpenSkyAbove only at the window's top row assumes that row is itself
                // open air with nothing above it - true for ordinary terrain, but wrong for a dune/
                // hill tall enough to reach into or above the window's margin: the window's top row
                // then sits INSIDE that feature, and the check reads "blocked" even though the
                // feature's real peak (out of view, further up) is almost certainly exposed to open
                // sky - there's no such thing as a roof floating above a dune in this game. Treat the
                // window-top tile already being solid/sand as exposure on its own, short-circuiting
                // before the (more expensive) HasOpenSkyAbove scan rather than wrongly reading the
                // whole column below as unlit.
                bool topRowExposed = IsAttenuatingAt(tileX, bufferOriginTileY) || worldMap.HasOpenSkyAbove(tileX, bufferOriginTileY);
                if (!topRowExposed)
                    continue;

                for (int localY = 0; localY < bufferHeight; localY++)
                {
                    int tileY = bufferOriginTileY + localY;
                    int index = (localY * bufferWidth) + localX;
                    lightR[index] = skyR;
                    lightG[index] = skyG;
                    lightB[index] = skyB;
                    propagationQueue.Enqueue(index);

                    if (IsAttenuatingAt(tileX, tileY))
                        break;
                }
            }
        }

        // True for a solid tile OR a tile-grid cell filled with loose sand - either way, something
        // physically occupies this tile that should absorb/block light like material, rather than the
        // tile reading as open air just because WorldMap's tile grid alone doesn't know about sand.
        private bool IsAttenuatingAt(int tileX, int tileY)
        {
            if (worldMap.IsSolidAt(tileX, tileY))
                return true;

            if (SandSystem == null)
                return false;

            int tileSize = worldMap.TileSize;
            int centerPixelX = (tileX * tileSize) + (tileSize / 2);
            int centerPixelY = (tileY * tileSize) + (tileSize / 2);
            return SandSystem.HasSandAt(centerPixelX, centerPixelY);
        }

        private void SeedPointLightsInto(float[] r, float[] g, float[] b, float peakIntensity)
        {
            float pointR = (PointLightColor.R / 255f) * peakIntensity;
            float pointG = (PointLightColor.G / 255f) * peakIntensity;
            float pointB = (PointLightColor.B / 255f) * peakIntensity;

            for (int i = 0; i < pointLightPositions.Count; i++)
            {
                Vector2 worldPosition = pointLightPositions[i];
                int tileX = (int)MathF.Floor(worldPosition.X / worldMap.TileSize);
                int tileY = (int)MathF.Floor(worldPosition.Y / worldMap.TileSize);
                int localX = tileX - bufferOriginTileX;
                int localY = tileY - bufferOriginTileY;
                if (localX < 0 || localX >= bufferWidth || localY < 0 || localY >= bufferHeight)
                    continue;

                int index = (localY * bufferWidth) + localX;
                bool improved = false;
                if (pointR > r[index]) { r[index] = pointR; improved = true; }
                if (pointG > g[index]) { g[index] = pointG; improved = true; }
                if (pointB > b[index]) { b[index] = pointB; improved = true; }

                if (improved)
                    propagationQueue.Enqueue(index);
            }
        }

        private void Propagate(float[] r, float[] g, float[] b, float openDecay, float solidDecay)
        {
            while (propagationQueue.Count > 0)
            {
                int index = propagationQueue.Dequeue();
                int localX = index % bufferWidth;
                int localY = index / bufferWidth;

                float currentR = r[index];
                float currentG = g[index];
                float currentB = b[index];

                TryPropagateTo(r, g, b, localX - 1, localY, currentR, currentG, currentB, openDecay, solidDecay);
                TryPropagateTo(r, g, b, localX + 1, localY, currentR, currentG, currentB, openDecay, solidDecay);
                TryPropagateTo(r, g, b, localX, localY - 1, currentR, currentG, currentB, openDecay, solidDecay);
                TryPropagateTo(r, g, b, localX, localY + 1, currentR, currentG, currentB, openDecay, solidDecay);
            }
        }

        private void TryPropagateTo(
            float[] r, float[] g, float[] b, int localX, int localY,
            float currentR, float currentG, float currentB, float openDecay, float solidDecay)
        {
            if (localX < 0 || localX >= bufferWidth || localY < 0 || localY >= bufferHeight)
                return;

            int tileX = worldMap.WrapTileX(bufferOriginTileX + localX);
            int tileY = bufferOriginTileY + localY;
            float decay = IsAttenuatingAt(tileX, tileY) ? solidDecay : openDecay;

            float candidateR = currentR - decay;
            float candidateG = currentG - decay;
            float candidateB = currentB - decay;
            if (candidateR <= 0f && candidateG <= 0f && candidateB <= 0f)
                return;

            int index = (localY * bufferWidth) + localX;
            bool improved = false;
            if (candidateR > r[index]) { r[index] = candidateR; improved = true; }
            if (candidateG > g[index]) { g[index] = candidateG; improved = true; }
            if (candidateB > b[index]) { b[index] = candidateB; improved = true; }

            if (improved)
                propagationQueue.Enqueue(index);
        }
    }
}
