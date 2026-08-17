using System;
using Microsoft.Xna.Framework;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// V6 Surface Prototype: Reproduces V5 Surface Lighting Only
    ///
    /// Simplified for ETAPA 1:
    /// - SkyOpen: Receives full sky color RGB
    /// - Foreground Surface: Depth-based attenuation (no propagation)
    /// - Background: Neutral/dark (ignored this stage)
    ///
    /// V6LightMap stores WORLD RGB (what light exists there).
    /// V6LightMapRenderer converts to VISUAL MASK (how to render it).
    /// </summary>
    public sealed class V6LightingSystem
    {
        private readonly WorldMap worldMap;
        private readonly V6LightMap lightMap;

        // Door occlusion callback (optional)
        private Func<int, int, bool> isDoorBlockingTile;

        // P2-E-L1: Layer definitions for sky transmission cutoff
        private Nyvorn.Source.World.Generation.WorldLayerDefinition[] layerDefinitions;

        // ETAPA 7: Artificial light sources callback
        private Func<System.Collections.Generic.IEnumerable<ArtificialLightSource>> getArtificialLightSources;

        // ETAPA 7: Artificial light debug stats
        private int artificialSourcesThisFrame = 0;
        private int artificialCandidateTilesThisFrame = 0;
        private int artificialInsideRadiusThisFrame = 0;
        private int artificialLosTestsThisFrame = 0;
        private int artificialLosStepsThisFrame = 0;
        private int artificialTilesWrittenThisFrame = 0;
        private long artificialLightMs = 0;

        // ETAPA 5.3: Performance instrumentation (temporary)
        private int doorOcclusionQueriesThisFrame = 0;
        private int doorBlockingCallsThisFrame = 0;
        private System.Diagnostics.Stopwatch doorOcclusionStopwatch = new();
        private int numberOfClosedDoorsLastFrame = 0;

        // Depth propagation for foreground (grown-only, reused each frame)
        private int[] foregroundDepth = Array.Empty<int>();

        // Background intensity buffer (scratch, grown-only, reused each frame)
        private float[] backgroundIntensity = Array.Empty<float>();

        // BFS queue for background propagation (grown-only, reused each frame)
        private int[] queueX = Array.Empty<int>();
        private int[] queueY = Array.Empty<int>();

        // ETAPA 5.3: Edge occlusion cache (grown-only, reused each frame)
        private bool[] blockedRightEdges = Array.Empty<bool>();  // (x,y) -> (x+1,y)
        private bool[] blockedDownEdges = Array.Empty<bool>();   // (x,y) -> (x,y+1)

        // Performance tracking
        private long totalClassifyMs = 0;
        private long totalComputeMs = 0;
        private long totalBackgroundMs = 0;
        private int computeCount = 0;

        // Statistics
        private int skyOpenSourceCount = 0;
        private int foregroundCount = 0;
        private int backgroundSeedCount = 0;
        private int backgroundPropagatedCount = 0;

        public V6LightMap LightMap => lightMap;
        public int SkyOpenSourceCount => skyOpenSourceCount;
        public int ForegroundCount => foregroundCount;
        public int BackgroundSeedCount => backgroundSeedCount;
        public int BackgroundPropagatedCount => backgroundPropagatedCount;

        public long AvgClassifyMs => computeCount > 0 ? totalClassifyMs / computeCount : 0;
        public long AvgComputeMs => computeCount > 0 ? totalComputeMs / computeCount : 0;
        public long AvgBackgroundMs => computeCount > 0 ? totalBackgroundMs / computeCount : 0;
        public int ArtificialSourcesThisFrame => artificialSourcesThisFrame;
        public int ArtificialCandidateTilesThisFrame => artificialCandidateTilesThisFrame;
        public int ArtificialInsideRadiusThisFrame => artificialInsideRadiusThisFrame;
        public int ArtificialLosTestsThisFrame => artificialLosTestsThisFrame;
        public int ArtificialLosStepsThisFrame => artificialLosStepsThisFrame;
        public int ArtificialTilesWrittenThisFrame => artificialTilesWrittenThisFrame;
        public long ArtificialLightMs => artificialLightMs;

        public V6LightingSystem(WorldMap worldMap)
        {
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
            this.lightMap = new V6LightMap();
            this.isDoorBlockingTile = null;
        }

        public void SetDoorOcclusionCallback(Func<int, int, bool> doorBlockingQuery)
        {
            this.isDoorBlockingTile = doorBlockingQuery;
        }

        public void SetArtificialLightSources(Func<System.Collections.Generic.IEnumerable<ArtificialLightSource>> getter)
        {
            this.getArtificialLightSources = getter;
        }

        // P2-E-L1: Set layer definitions for sky transmission cutoff
        public void SetLayerDefinitions(Nyvorn.Source.World.Generation.WorldLayerDefinition[] definitions)
        {
            this.layerDefinitions = definitions;
        }

        public void RebuildEdgeOcclusionCache()
        {
            // ETAPA 5.3: Rebuild edge occlusion cache from door positions
            // This replaces per-query checks with a pre-computed cache
            // Should be called once per frame or when doors change state

            int width = lightMap.BufferWidth;
            int height = lightMap.BufferHeight;
            int cellCount = width * height;

            if (blockedRightEdges.Length < cellCount)
                blockedRightEdges = new bool[cellCount];
            if (blockedDownEdges.Length < cellCount)
                blockedDownEdges = new bool[cellCount];

            // Clear cache
            System.Array.Clear(blockedRightEdges, 0, cellCount);
            System.Array.Clear(blockedDownEdges, 0, cellCount);

            // No query method available — edge cache cannot be built without door access
            // This is a placeholder; actual implementation would iterate doors
            // For now, keep using callback-based checks but count them for measurement
        }

        private bool CanLightPassBetween(int fromX, int fromY, int toX, int toY)
        {
            // ETAPA 5.3: Count queries for performance measurement
            doorOcclusionQueriesThisFrame++;

            // Light cannot pass if a closed door blocks one of the cells
            if (isDoorBlockingTile != null)
            {
                doorBlockingCallsThisFrame += 2;  // Two calls below
                bool fromBlocked = isDoorBlockingTile.Invoke(fromX, fromY);
                bool toBlocked = isDoorBlockingTile.Invoke(toX, toY);

                // If either cell is blocked by a closed door, light cannot pass
                return !fromBlocked && !toBlocked;
            }

            return true;
        }

        public void Update(int cameraScreenX, int cameraScreenY, int screenWidth, int screenHeight,
                          int tileSize, Color skyColor)
        {
            // ETAPA 5.3: Reset performance counters
            doorOcclusionQueriesThisFrame = 0;
            doorBlockingCallsThisFrame = 0;
            doorOcclusionStopwatch.Restart();

            var swTotal = System.Diagnostics.Stopwatch.StartNew();

            // Calculate active buffer region
            int cameraTileX = cameraScreenX / tileSize;
            int cameraTileY = cameraScreenY / tileSize;
            int cameraTileWidth = (screenWidth + tileSize - 1) / tileSize;
            int cameraTileHeight = (screenHeight + tileSize - 1) / tileSize;

            int margin = V6LightingConfig.LightMarginTiles;
            int originX = cameraTileX - margin;
            int originY = cameraTileY - margin;
            int width = cameraTileWidth + (margin * 2);
            int height = cameraTileHeight + (margin * 2);

            lightMap.Resize(originX, originY, width, height);

            int cellCount = width * height;
            if (foregroundDepth.Length < cellCount)
                foregroundDepth = new int[cellCount];
            if (backgroundIntensity.Length < cellCount)
                backgroundIntensity = new float[cellCount];
            if (queueX.Length < cellCount)
            {
                queueX = new int[cellCount];
                queueY = new int[cellCount];
            }

            // STEP 1: Classify medium
            var swClassify = System.Diagnostics.Stopwatch.StartNew();
            ClassifyMedium();
            swClassify.Stop();
            totalClassifyMs += swClassify.ElapsedMilliseconds;

            // STEP 2: Compute foreground depth + apply sky color (ETAPA 1)
            var swCompute = System.Diagnostics.Stopwatch.StartNew();
            ComputeSurfacePrototype(skyColor);
            swCompute.Stop();
            totalComputeMs += swCompute.ElapsedMilliseconds;

            // STEP 3: Compute background glow (ETAPA 2)
            var swBackground = System.Diagnostics.Stopwatch.StartNew();
            ComputeBackgroundGlow(width, height, skyColor);
            swBackground.Stop();
            totalBackgroundMs += swBackground.ElapsedMilliseconds;

            // STEP 4: Apply indirect background light to adjacent foreground (ETAPA 2.5)
            ApplyBackgroundIndirectToForeground(width, height, originX, originY);

            // STEP 5: Apply artificial lights (ETAPA 7 — Torch)
            var swArtificial = System.Diagnostics.Stopwatch.StartNew();
            ApplyArtificialLights(width, height, originX, originY, tileSize);
            swArtificial.Stop();
            artificialLightMs = swArtificial.ElapsedMilliseconds;

            doorOcclusionStopwatch.Stop();

            // ETAPA 5.3: Log performance metrics
            if (computeCount % 30 == 0)  // Log every 30 frames
            {
                string logLine = $"[V6 PERF] Frame {computeCount}: " +
                    $"DoorQueries={doorOcclusionQueriesThisFrame} " +
                    $"DoorBlockingCalls={doorBlockingCallsThisFrame} " +
                    $"ClosedDoors={numberOfClosedDoorsLastFrame} " +
                    $"Buffer={width}x{height} " +
                    $"DoorOcclusionMs={doorOcclusionStopwatch.ElapsedMilliseconds}ms";

                try
                {
                    string logPath = "v6_perf_log.txt";
                    System.IO.File.AppendAllText(logPath, logLine + Environment.NewLine);
                }
                catch { }
            }

            computeCount++;
        }

        private void ClassifyMedium()
        {
            int width = lightMap.BufferWidth;
            int height = lightMap.BufferHeight;
            int originX = lightMap.BufferOriginTileX;
            int originY = lightMap.BufferOriginTileY;

            var mediumMask = lightMap.MediumMask;

            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    int worldX = originX + localX;
                    int worldY = originY + localY;
                    int index = (localY * width) + localX;

                    bool isSolid = worldMap.IsSolidAt(worldX, worldY);
                    bool hasBackground = worldMap.GetBackgroundTile(worldX, worldY) != TileType.Empty;

                    V6LightMap.CellMedium medium;

                    if (isSolid)
                    {
                        medium = V6LightMap.CellMedium.Foreground;
                    }
                    else if (hasBackground)
                    {
                        medium = V6LightMap.CellMedium.Background;
                    }
                    else
                    {
                        medium = V6LightMap.CellMedium.SkyOpen;
                    }

                    mediumMask[index] = medium;
                }
            }
        }

        private void ComputeSurfacePrototype(Color skyColor)
        {
            int width = lightMap.BufferWidth;
            int height = lightMap.BufferHeight;
            int originX = lightMap.BufferOriginTileX;
            int originY = lightMap.BufferOriginTileY;

            var lightR = lightMap.LightR;
            var lightG = lightMap.LightG;
            var lightB = lightMap.LightB;
            var mediumMask = lightMap.MediumMask;

            float skyR = skyColor.R / 255f;
            float skyG = skyColor.G / 255f;
            float skyB = skyColor.B / 255f;

            skyOpenSourceCount = 0;
            foregroundCount = 0;

            // V5 approved foreground depth weights (Surface layer)
            float[] depthWeights = new[] { 1.00f, 0.65f, 0.35f, 0.15f };

            // STEP 1: Inject sky color into SkyOpen cells (P2-E-L1: with sky transmission cutoff)
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    int index = (localY * width) + localX;

                    if (mediumMask[index] == V6LightMap.CellMedium.SkyOpen)
                    {
                        // P2-E-L1: Apply sky transmission factor based on world Y
                        int worldTileY = originY + localY;
                        float transmission = SkyTransmissionHelper.GetSkyTransmissionAtWorldY(worldTileY, layerDefinitions);

                        lightR[index] = skyR * transmission;
                        lightG[index] = skyG * transmission;
                        lightB[index] = skyB * transmission;
                        foregroundDepth[index] = -1;  // Mark as SkyOpen
                        skyOpenSourceCount++;
                    }
                    else
                    {
                        foregroundDepth[index] = int.MaxValue;  // Mark as not yet computed
                    }
                }
            }

            // STEP 2: Compute foreground depth (distance to nearest SkyOpen)
            ComputeForegroundDepth(width, height, originX, originY);

            // STEP 3: Apply depth weights to foreground, store RGB (P2-E-L1: with sky transmission cutoff)
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    int index = (localY * width) + localX;

                    if (mediumMask[index] == V6LightMap.CellMedium.Foreground)
                    {
                        int depth = foregroundDepth[index];
                        float weight = 0f;

                        if (depth >= 0 && depth < depthWeights.Length)
                            weight = depthWeights[depth];
                        // else: depth >= 4 or unreachable → weight = 0 (black)

                        // P2-E-L1: Apply sky transmission factor based on this foreground's world Y
                        int worldTileY = originY + localY;
                        float transmission = SkyTransmissionHelper.GetSkyTransmissionAtWorldY(worldTileY, layerDefinitions);

                        lightR[index] = skyR * weight * transmission;
                        lightG[index] = skyG * weight * transmission;
                        lightB[index] = skyB * weight * transmission;
                        foregroundCount++;
                    }
                    else if (mediumMask[index] == V6LightMap.CellMedium.Background)
                    {
                        // ETAPA 1: Background stays dark (Black)
                        lightR[index] = 0f;
                        lightG[index] = 0f;
                        lightB[index] = 0f;
                    }
                }
            }
        }

        private void ComputeForegroundDepth(int width, int height, int originX, int originY)
        {
            var mediumMask = lightMap.MediumMask;

            // Multi-pass BFS to propagate depth from SkyOpen into foreground
            // Only foreground cells adjacent to SkyOpen (or to closer foreground) receive depth
            // ETAPA 5.2: Respect door occlusion

            for (int pass = 0; pass < 4; pass++)  // Max 4 layers
            {
                bool changed = false;

                for (int localY = 0; localY < height; localY++)
                {
                    for (int localX = 0; localX < width; localX++)
                    {
                        int index = (localY * width) + localX;

                        if (mediumMask[index] != V6LightMap.CellMedium.Foreground)
                            continue;

                        if (foregroundDepth[index] <= pass)
                            continue;  // Already has closer depth

                        // Check 4-neighbors for SkyOpen or closer foreground
                        int minAdjacentDepth = int.MaxValue;
                        int worldX = originX + localX;
                        int worldY = originY + localY;

                        // Up
                        if (localY > 0 && CanLightPassBetween(worldX, worldY, worldX, worldY - 1))
                        {
                            int upIdx = ((localY - 1) * width) + localX;
                            if (mediumMask[upIdx] == V6LightMap.CellMedium.SkyOpen)
                                minAdjacentDepth = 0;
                            else if (mediumMask[upIdx] == V6LightMap.CellMedium.Foreground && foregroundDepth[upIdx] < pass)
                                minAdjacentDepth = System.Math.Min(minAdjacentDepth, foregroundDepth[upIdx] + 1);
                        }

                        // Down
                        if (localY < height - 1 && CanLightPassBetween(worldX, worldY, worldX, worldY + 1))
                        {
                            int downIdx = ((localY + 1) * width) + localX;
                            if (mediumMask[downIdx] == V6LightMap.CellMedium.SkyOpen)
                                minAdjacentDepth = 0;
                            else if (mediumMask[downIdx] == V6LightMap.CellMedium.Foreground && foregroundDepth[downIdx] < pass)
                                minAdjacentDepth = System.Math.Min(minAdjacentDepth, foregroundDepth[downIdx] + 1);
                        }

                        // Left
                        if (localX > 0 && CanLightPassBetween(worldX, worldY, worldX - 1, worldY))
                        {
                            int leftIdx = (localY * width) + (localX - 1);
                            if (mediumMask[leftIdx] == V6LightMap.CellMedium.SkyOpen)
                                minAdjacentDepth = 0;
                            else if (mediumMask[leftIdx] == V6LightMap.CellMedium.Foreground && foregroundDepth[leftIdx] < pass)
                                minAdjacentDepth = System.Math.Min(minAdjacentDepth, foregroundDepth[leftIdx] + 1);
                        }

                        // Right
                        if (localX < width - 1 && CanLightPassBetween(worldX, worldY, worldX + 1, worldY))
                        {
                            int rightIdx = (localY * width) + (localX + 1);
                            if (mediumMask[rightIdx] == V6LightMap.CellMedium.SkyOpen)
                                minAdjacentDepth = 0;
                            else if (mediumMask[rightIdx] == V6LightMap.CellMedium.Foreground && foregroundDepth[rightIdx] < pass)
                                minAdjacentDepth = System.Math.Min(minAdjacentDepth, foregroundDepth[rightIdx] + 1);
                        }

                        if (minAdjacentDepth < foregroundDepth[index])
                        {
                            foregroundDepth[index] = minAdjacentDepth;
                            changed = true;
                        }
                    }
                }

                if (!changed)
                    break;
            }
        }


        private void ComputeBackgroundGlow(int width, int height, Color skyColor)
        {
            int cellCount = width * height;
            Array.Clear(backgroundIntensity, 0, cellCount);

            var mediumMask = lightMap.MediumMask;
            int originX = lightMap.BufferOriginTileX;
            int originY = lightMap.BufferOriginTileY;

            float seedIntensity = V6LightingConfig.BackgroundSeedIntensity * V6LightingConfig.BackgroundInitialStrength;
            float falloff = V6LightingConfig.BackgroundFalloff;
            float softCap = V6LightingConfig.BackgroundSoftCap;

            backgroundSeedCount = 0;
            backgroundPropagatedCount = 0;

            // STEP 1: Detect seeds - Background cells adjacent to SkyOpen
            int queueHead = 0;
            int queueTail = 0;

            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    int index = (localY * width) + localX;

                    if (mediumMask[index] != V6LightMap.CellMedium.Background)
                        continue;

                    // Check 4 neighbors for SkyOpen (ETAPA 5.2: respect door occlusion)
                    bool adjacentToSkyOpen = false;
                    int worldX = originX + localX;
                    int worldY = originY + localY;

                    // Up
                    if (localY > 0 && mediumMask[((localY - 1) * width) + localX] == V6LightMap.CellMedium.SkyOpen &&
                        CanLightPassBetween(worldX, worldY, worldX, worldY - 1))
                        adjacentToSkyOpen = true;
                    // Down
                    if (localY < height - 1 && mediumMask[((localY + 1) * width) + localX] == V6LightMap.CellMedium.SkyOpen &&
                        CanLightPassBetween(worldX, worldY, worldX, worldY + 1))
                        adjacentToSkyOpen = true;
                    // Left
                    if (localX > 0 && mediumMask[(localY * width) + (localX - 1)] == V6LightMap.CellMedium.SkyOpen &&
                        CanLightPassBetween(worldX, worldY, worldX - 1, worldY))
                        adjacentToSkyOpen = true;
                    // Right
                    if (localX < width - 1 && mediumMask[(localY * width) + (localX + 1)] == V6LightMap.CellMedium.SkyOpen &&
                        CanLightPassBetween(worldX, worldY, worldX + 1, worldY))
                        adjacentToSkyOpen = true;

                    if (adjacentToSkyOpen)
                    {
                        backgroundIntensity[index] = seedIntensity;
                        queueX[queueTail] = localX;
                        queueY[queueTail] = localY;
                        queueTail++;
                        backgroundSeedCount++;
                    }
                }
            }

            // STEP 2: BFS propagation through Background cells only
            while (queueHead < queueTail)
            {
                int localX = queueX[queueHead];
                int localY = queueY[queueHead];
                queueHead++;

                int index = (localY * width) + localX;
                float currentIntensity = backgroundIntensity[index];

                float candidateIntensity = currentIntensity - falloff;
                if (candidateIntensity <= 0.01f)
                    continue;

                // Check 4 neighbors (ETAPA 5.2: respect door occlusion)
                int currentWorldX = originX + localX;
                int currentWorldY = originY + localY;
                PropagateBackgroundToNeighbor(localX - 1, localY, currentWorldX, currentWorldY, width, height, originX, originY, mediumMask, candidateIntensity, ref queueHead, ref queueTail);
                PropagateBackgroundToNeighbor(localX + 1, localY, currentWorldX, currentWorldY, width, height, originX, originY, mediumMask, candidateIntensity, ref queueHead, ref queueTail);
                PropagateBackgroundToNeighbor(localX, localY - 1, currentWorldX, currentWorldY, width, height, originX, originY, mediumMask, candidateIntensity, ref queueHead, ref queueTail);
                PropagateBackgroundToNeighbor(localX, localY + 1, currentWorldX, currentWorldY, width, height, originX, originY, mediumMask, candidateIntensity, ref queueHead, ref queueTail);
            }

            // STEP 3: Write Background RGB to LightMap (P2-E-L1: with sky transmission cutoff)
            float skyR = skyColor.R / 255f;
            float skyG = skyColor.G / 255f;
            float skyB = skyColor.B / 255f;

            var lightR = lightMap.LightR;
            var lightG = lightMap.LightG;
            var lightB = lightMap.LightB;

            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    int index = (localY * width) + localX;

                    if (mediumMask[index] != V6LightMap.CellMedium.Background)
                        continue;

                    float intensity = backgroundIntensity[index];
                    intensity = System.Math.Clamp(intensity, 0f, softCap);
                    float normalized = intensity / softCap;

                    // P2-E-L1: Apply sky transmission factor based on this background's world Y
                    int worldTileY = originY + localY;
                    float transmission = SkyTransmissionHelper.GetSkyTransmissionAtWorldY(worldTileY, layerDefinitions);

                    lightR[index] = skyR * normalized * transmission;
                    lightG[index] = skyG * normalized * transmission;
                    lightB[index] = skyB * normalized * transmission;

                    if (intensity > 0f)
                        backgroundPropagatedCount++;
                }
            }
        }

        private void PropagateBackgroundToNeighbor(int localX, int localY,
                                                   int currentWorldX, int currentWorldY,
                                                   int width, int height,
                                                   int originX, int originY,
                                                   V6LightMap.CellMedium[] mediumMask,
                                                   float candidateIntensity,
                                                   ref int queueHead, ref int queueTail)
        {
            if (localX < 0 || localX >= width || localY < 0 || localY >= height)
                return;

            int index = (localY * width) + localX;

            // Only propagate to other Background cells
            if (mediumMask[index] != V6LightMap.CellMedium.Background)
                return;

            // ETAPA 5.2: Check if closed door blocks light passage between current and neighbor
            int neighborWorldX = originX + localX;
            int neighborWorldY = originY + localY;

            if (!CanLightPassBetween(currentWorldX, currentWorldY, neighborWorldX, neighborWorldY))
                return;

            // Only update if candidate is better (MAX propagation)
            if (candidateIntensity <= backgroundIntensity[index])
                return;

            backgroundIntensity[index] = candidateIntensity;
            queueX[queueTail] = localX;
            queueY[queueTail] = localY;
            queueTail++;
            backgroundPropagatedCount++;
        }

        private void ApplyBackgroundIndirectToForeground(int width, int height, int originX, int originY)
        {
            var mediumMask = lightMap.MediumMask;
            var lightR = lightMap.LightR;
            var lightG = lightMap.LightG;
            var lightB = lightMap.LightB;

            float indirectStrength = V6LightingConfig.ForegroundIndirectFromBackgroundStrength;

            // ETAPA 2.5: For each foreground cell, check adjacent background cells for indirect light
            // ETAPA 5.2: Respect door occlusion
            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    int index = (localY * width) + localX;

                    if (mediumMask[index] != V6LightMap.CellMedium.Foreground)
                        continue;

                    // Store original foreground light
                    float existingR = lightR[index];
                    float existingG = lightG[index];
                    float existingB = lightB[index];

                    // Find strongest background neighbor
                    float maxBackgroundR = 0f;
                    float maxBackgroundG = 0f;
                    float maxBackgroundB = 0f;
                    float maxIntensity = 0f;

                    int worldX = originX + localX;
                    int worldY = originY + localY;

                    // Check 4 neighbors for background
                    if (CanLightPassBetween(worldX, worldY, worldX - 1, worldY))
                        CheckBackgroundNeighbor(localX - 1, localY, width, height, mediumMask, lightR, lightG, lightB,
                                               ref maxBackgroundR, ref maxBackgroundG, ref maxBackgroundB, ref maxIntensity);
                    if (CanLightPassBetween(worldX, worldY, worldX + 1, worldY))
                        CheckBackgroundNeighbor(localX + 1, localY, width, height, mediumMask, lightR, lightG, lightB,
                                               ref maxBackgroundR, ref maxBackgroundG, ref maxBackgroundB, ref maxIntensity);
                    if (CanLightPassBetween(worldX, worldY, worldX, worldY - 1))
                        CheckBackgroundNeighbor(localX, localY - 1, width, height, mediumMask, lightR, lightG, lightB,
                                               ref maxBackgroundR, ref maxBackgroundG, ref maxBackgroundB, ref maxIntensity);
                    if (CanLightPassBetween(worldX, worldY, worldX, worldY + 1))
                        CheckBackgroundNeighbor(localX, localY + 1, width, height, mediumMask, lightR, lightG, lightB,
                                               ref maxBackgroundR, ref maxBackgroundG, ref maxBackgroundB, ref maxIntensity);

                    // Apply indirect light using MAX per channel (never subtract)
                    if (maxIntensity > 0.01f)
                    {
                        float indirectR = maxBackgroundR * indirectStrength;
                        float indirectG = maxBackgroundG * indirectStrength;
                        float indirectB = maxBackgroundB * indirectStrength;

                        lightR[index] = System.Math.Max(existingR, indirectR);
                        lightG[index] = System.Math.Max(existingG, indirectG);
                        lightB[index] = System.Math.Max(existingB, indirectB);
                    }
                }
            }
        }

        private void CheckBackgroundNeighbor(int localX, int localY, int width, int height,
                                            V6LightMap.CellMedium[] mediumMask,
                                            float[] lightR, float[] lightG, float[] lightB,
                                            ref float maxR, ref float maxG, ref float maxB, ref float maxIntensity)
        {
            if (localX < 0 || localX >= width || localY < 0 || localY >= height)
                return;

            int index = (localY * width) + localX;

            if (mediumMask[index] != V6LightMap.CellMedium.Background)
                return;

            // Calculate intensity of this background cell
            float r = lightR[index];
            float g = lightG[index];
            float b = lightB[index];
            float intensity = (r + g + b) / 3f;  // Simple intensity measure

            if (intensity > maxIntensity)
            {
                maxIntensity = intensity;
                maxR = r;
                maxG = g;
                maxB = b;
            }
        }

        private void ApplyArtificialLights(int width, int height, int originX, int originY, int tileSize)
        {
            artificialSourcesThisFrame = 0;
            artificialCandidateTilesThisFrame = 0;
            artificialInsideRadiusThisFrame = 0;
            artificialLosTestsThisFrame = 0;
            artificialLosStepsThisFrame = 0;
            artificialTilesWrittenThisFrame = 0;

            if (getArtificialLightSources == null)
                return;

            var sources = getArtificialLightSources();

            // Fast path: check if there are any sources
            var sourceList = new System.Collections.Generic.List<ArtificialLightSource>();
            foreach (var s in sources)
                sourceList.Add(s);

            if (sourceList.Count == 0)
                return;

            var lightR = lightMap.LightR;
            var lightG = lightMap.LightG;
            var lightB = lightMap.LightB;
            var mediumMask = lightMap.MediumMask;

            float airAttenuationPerTile = V6LightingConfig.PointLightAirAttenuationPerTile;
            float foregroundAttenuation = V6LightingConfig.PointLightForegroundObstacleAttenuation;
            float doorAttenuation = V6LightingConfig.PointLightDoorObstacleAttenuation;

            // Process each point light source
            foreach (var source in sourceList)
            {
                artificialSourcesThisFrame++;

                int sourceTileX = (int)(source.PositionPixels.X / tileSize);
                int sourceTileY = (int)(source.PositionPixels.Y / tileSize);

                // Calculate local scan rectangle
                int minTileX = sourceTileX - source.RadiusTiles;
                int maxTileX = sourceTileX + source.RadiusTiles;
                int minTileY = sourceTileY - source.RadiusTiles;
                int maxTileY = sourceTileY + source.RadiusTiles;

                // Clamp to buffer bounds
                int minLocalX = System.Math.Max(0, minTileX - originX);
                int maxLocalX = System.Math.Min(width - 1, maxTileX - originX);
                int minLocalY = System.Math.Max(0, minTileY - originY);
                int maxLocalY = System.Math.Min(height - 1, maxTileY - originY);

                // Preserve source color ratio
                float sourceR = source.ColorRGB.R / 255f;
                float sourceG = source.ColorRGB.G / 255f;
                float sourceB = source.ColorRGB.B / 255f;
                float sourceIntensity = source.Intensity;

                float radiusPixels = source.RadiusTiles * tileSize;
                float radiusSquared = radiusPixels * radiusPixels;

                // Local scan
                for (int localY = minLocalY; localY <= maxLocalY; localY++)
                {
                    for (int localX = minLocalX; localX <= maxLocalX; localX++)
                    {
                        artificialCandidateTilesThisFrame++;

                        int worldTileX = originX + localX;
                        int worldTileY = originY + localY;
                        float tileCenterPixelX = (worldTileX + 0.5f) * tileSize;
                        float tileCenterPixelY = (worldTileY + 0.5f) * tileSize;

                        // Distance-based air attenuation
                        float dx = tileCenterPixelX - source.PositionPixels.X;
                        float dy = tileCenterPixelY - source.PositionPixels.Y;
                        float distanceSquared = dx * dx + dy * dy;

                        if (distanceSquared > radiusSquared)
                            continue;

                        artificialInsideRadiusThisFrame++;

                        float distance = (float)System.Math.Sqrt(distanceSquared);
                        float airAttenuation = distance / tileSize * airAttenuationPerTile;

                        // Ray-cast for obstacle attenuation
                        artificialLosTestsThisFrame++;
                        float obstacleAttenuation = ComputeObstacleAttenuation(
                            sourceTileX, sourceTileY, worldTileX, worldTileY,
                            originX, originY, width, height, mediumMask,
                            foregroundAttenuation, doorAttenuation
                        );

                        // Total energy remaining
                        float totalAttenuation = airAttenuation + obstacleAttenuation;
                        float remainingEnergy = sourceIntensity - totalAttenuation;

                        if (remainingEnergy <= 0f)
                            continue;

                        // P2-B1: Apply falloff curve to remaining energy
                        float finalEnergy = remainingEnergy;
                        if (sourceIntensity > 0f)
                        {
                            float normalizedEnergy = System.Math.Clamp(
                                remainingEnergy / sourceIntensity,
                                0f,
                                1f
                            );

                            float curvedEnergy = MathF.Pow(
                                normalizedEnergy,
                                V6LightingConfig.TorchLightFalloffExponent
                            );

                            finalEnergy = curvedEnergy * sourceIntensity;
                        }

                        // P2-B2: Color shaping based on energy (temperature gradient)
                        float finalR = sourceR;
                        float finalG = sourceG;
                        float finalB = sourceB;

                        if (source.UseColorShaping && sourceIntensity > 0f)
                        {
                            float energy01 = System.Math.Clamp(
                                finalEnergy / sourceIntensity,
                                0f,
                                1f
                            );

                            float colorExponent = source.ColorCoreExponent > 0f
                                ? source.ColorCoreExponent
                                : 1f;

                            float coreBlend = MathF.Pow(energy01, colorExponent);

                            float coreR = source.CoreColorRGB.R / 255f;
                            float coreG = source.CoreColorRGB.G / 255f;
                            float coreB = source.CoreColorRGB.B / 255f;

                            finalR = MathHelper.Lerp(sourceR, coreR, coreBlend);
                            finalG = MathHelper.Lerp(sourceG, coreG, coreBlend);
                            finalB = MathHelper.Lerp(sourceB, coreB, coreBlend);
                        }

                        // Apply light: preserve shaped RGB, scale by final curved energy
                        int cellIndex = (localY * width) + localX;
                        float contributionR = finalR * finalEnergy;
                        float contributionG = finalG * finalEnergy;
                        float contributionB = finalB * finalEnergy;

                        // P2-B3: Apply output multiplier for flicker/pulsing effects
                        float outputMultiplier = source.OutputMultiplier > 0f
                            ? source.OutputMultiplier
                            : 1f;

                        contributionR *= outputMultiplier;
                        contributionG *= outputMultiplier;
                        contributionB *= outputMultiplier;

                        ApplyBoundedAdd(ref lightR[cellIndex], contributionR);
                        ApplyBoundedAdd(ref lightG[cellIndex], contributionG);
                        ApplyBoundedAdd(ref lightB[cellIndex], contributionB);
                        artificialTilesWrittenThisFrame++;
                    }
                }
            }
        }

        private float ComputeObstacleAttenuation(int sourceTileX, int sourceTileY, int targetTileX, int targetTileY,
            int originX, int originY, int width, int height, V6LightMap.CellMedium[] mediumMask,
            float foregroundAttenuation, float doorAttenuation)
        {
            // Weighted line traversal (Xiaolin-Wu-like)
            // Distributes obstacle attenuation proportionally to ray coverage in each cell

            float sourceX = sourceTileX + 0.5f;
            float sourceY = sourceTileY + 0.5f;
            float targetX = targetTileX + 0.5f;
            float targetY = targetTileY + 0.5f;

            float dx = targetX - sourceX;
            float dy = targetY - sourceY;

            float attenuation = 0f;

            // Handle zero-distance case
            if (System.Math.Abs(dx) < 0.001f && System.Math.Abs(dy) < 0.001f)
                return 0f;

            // Directional correction factor for angle-independent attenuation
            float maxDir = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
            float directionCorrection = maxDir > 0 ? 1f / maxDir : 1f;

            // Xiaolin-Wu-like anti-aliased line
            float length = (float)System.Math.Sqrt(dx * dx + dy * dy);
            float numSteps = length;  // Approximate steps needed

            int stepCount = System.Math.Max((int)System.Math.Ceiling(numSteps), 1);

            for (int step = 0; step < stepCount; step++)
            {
                artificialLosStepsThisFrame++;

                // Position along the line (0 = source, 1 = target)
                float t = stepCount > 1 ? (float)step / (stepCount - 1) : 0.5f;

                // Current position on the line in floating point
                float currentX = sourceX + dx * t;
                float currentY = sourceY + dy * t;

                // Get the cell containing this point and the adjacent cell
                int cellX = (int)System.Math.Floor(currentX);
                int cellY = (int)System.Math.Floor(currentY);

                // Calculate coverage weights (how much the line covers each cell)
                float fracX = currentX - cellX;
                float fracY = currentY - cellY;

                // Simple coverage: prefer primary cell
                float coverage1 = 1f - System.Math.Max(fracX, fracY) * 0.5f;
                float coverage2 = 1f - coverage1;

                // Check primary cell
                ApplyWeightedCellAttenuation(cellX, cellY, originX, originY, width, height, mediumMask,
                    coverage1 * directionCorrection, foregroundAttenuation, doorAttenuation, ref attenuation,
                    cellX + (fracX > 0.5 ? 1 : 0), cellY + (fracY > 0.5 ? 1 : 0), targetTileX, targetTileY);

                // Check adjacent cell
                int adjX = cellX + (fracX > 0.5 ? 1 : -1);
                int adjY = cellY + (fracY > 0.5 ? 1 : -1);

                if (adjX != cellX || adjY != cellY)
                {
                    ApplyWeightedCellAttenuation(adjX, adjY, originX, originY, width, height, mediumMask,
                        coverage2 * directionCorrection, foregroundAttenuation, doorAttenuation, ref attenuation,
                        adjX, adjY, targetTileX, targetTileY);
                }

                // Early exit if attenuation is already severe enough
                if (attenuation >= 1.0f)
                    break;
            }

            return System.Math.Min(attenuation, 1.0f);  // Clamp to 1.0
        }

        private void ApplyWeightedCellAttenuation(int cellX, int cellY, int originX, int originY, int width, int height,
            V6LightMap.CellMedium[] mediumMask, float coverage, float foregroundAttenuation, float doorAttenuation,
            ref float totalAttenuation, int prevX, int prevY, int targetX, int targetY)
        {
            // Skip target cell itself (it receives light as a receiver, not an obstacle)
            if (cellX == targetX && cellY == targetY)
                return;

            // Check buffer bounds
            int localX = cellX - originX;
            int localY = cellY - originY;

            if (localX < 0 || localX >= width || localY < 0 || localY >= height)
                return;

            int cellIndex = (localY * width) + localX;
            V6LightMap.CellMedium medium = mediumMask[cellIndex];

            // Apply obstacle attenuation weighted by coverage
            if (medium == V6LightMap.CellMedium.Foreground)
            {
                totalAttenuation += coverage * foregroundAttenuation;
            }

            // Check door between previous and current
            if (System.Math.Abs(cellX - prevX) <= 1 && System.Math.Abs(cellY - prevY) <= 1)
            {
                if (!CanLightPassBetween(prevX, prevY, cellX, cellY))
                {
                    totalAttenuation += coverage * doorAttenuation;
                }
            }
        }

        private void ApplyBoundedAdd(ref float channel, float addition)
        {
            // Formula: result = base + addition * (1 - base)
            channel = channel + (addition * (1f - channel));

            // Clamp to [0..1]
            if (channel < 0f) channel = 0f;
            if (channel > 1f) channel = 1f;
        }

        public string GetDiagnosticsString()
        {
            int avgStepsPerRay = artificialLosTestsThisFrame > 0 ? artificialLosStepsThisFrame / artificialLosTestsThisFrame : 0;
            return $"V6 PointLight: {artificialSourcesThisFrame} src | {artificialCandidateTilesThisFrame} cand | {artificialInsideRadiusThisFrame} radius | {artificialLosTestsThisFrame} rays ({avgStepsPerRay} weighted-steps avg) | {artificialTilesWrittenThisFrame} lit | {artificialLightMs}ms";
        }

        public void Dispose()
        {
            // No resources to release
        }
    }
}
