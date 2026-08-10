using System;
using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Engine.Graphics.LightingPipeline
{
    /// <summary>
    /// V6 Light Map: RGB illumination per tile in active buffer region.
    ///
    /// Storage:
    /// - float[] lightR, lightG, lightB (separate channels for clarity)
    /// - uint[] mediumMask (SkyOpen/Background/Foreground classification)
    ///
    /// Semantics:
    /// - SkyOpen cells permanently locked to SkyState.AmbientLight
    /// - Background/Foreground cells receive propagated light
    /// - No allocations per frame (grown-only buffer)
    /// </summary>
    public sealed class V6LightMap
    {
        public enum CellMedium : byte
        {
            SkyOpen = 0,      // foreground empty, background empty
            Background = 1,   // foreground empty, background present
            Foreground = 2,   // foreground solid
            Water = 3         // reserved for water tiles
        }

        // RGB light values
        private float[] lightR = Array.Empty<float>();
        private float[] lightG = Array.Empty<float>();
        private float[] lightB = Array.Empty<float>();

        // Medium classification
        private CellMedium[] mediumMask = Array.Empty<CellMedium>();

        // Buffer metadata
        private int bufferOriginTileX;
        private int bufferOriginTileY;
        private int bufferWidth;
        private int bufferHeight;

        public int BufferOriginTileX => bufferOriginTileX;
        public int BufferOriginTileY => bufferOriginTileY;
        public int BufferWidth => bufferWidth;
        public int BufferHeight => bufferHeight;

        public float[] LightR => lightR;
        public float[] LightG => lightG;
        public float[] LightB => lightB;
        public CellMedium[] MediumMask => mediumMask;

        public void Resize(int originX, int originY, int width, int height)
        {
            bufferOriginTileX = originX;
            bufferOriginTileY = originY;
            bufferWidth = width;
            bufferHeight = height;

            int cellCount = width * height;

            // Resize arrays if needed (grown-only)
            if (lightR.Length < cellCount)
                lightR = new float[cellCount];
            if (lightG.Length < cellCount)
                lightG = new float[cellCount];
            if (lightB.Length < cellCount)
                lightB = new float[cellCount];
            if (mediumMask.Length < cellCount)
                mediumMask = new CellMedium[cellCount];
        }

        public void Clear(Color darkColor)
        {
            int cellCount = bufferWidth * bufferHeight;

            float r = darkColor.R / 255f;
            float g = darkColor.G / 255f;
            float b = darkColor.B / 255f;

            for (int i = 0; i < cellCount; i++)
            {
                lightR[i] = r;
                lightG[i] = g;
                lightB[i] = b;
                mediumMask[i] = CellMedium.Foreground;  // Default to dark
            }
        }

        public void SetCell(int localX, int localY, CellMedium medium, float r, float g, float b)
        {
            if (localX < 0 || localX >= bufferWidth || localY < 0 || localY >= bufferHeight)
                return;

            int index = (localY * bufferWidth) + localX;
            lightR[index] = r;
            lightG[index] = g;
            lightB[index] = b;
            mediumMask[index] = medium;
        }

        public void GetCell(int localX, int localY, out CellMedium medium, out float r, out float g, out float b)
        {
            medium = CellMedium.Foreground;
            r = g = b = 0f;

            if (localX < 0 || localX >= bufferWidth || localY < 0 || localY >= bufferHeight)
                return;

            int index = (localY * bufferWidth) + localX;
            medium = mediumMask[index];
            r = lightR[index];
            g = lightG[index];
            b = lightB[index];
        }

        public (float r, float g, float b) GetLightAtLocal(int localX, int localY)
        {
            if (localX < 0 || localX >= bufferWidth || localY < 0 || localY >= bufferHeight)
                return (0f, 0f, 0f);

            int index = (localY * bufferWidth) + localX;
            return (lightR[index], lightG[index], lightB[index]);
        }

        public (float r, float g, float b) GetLightAtWorld(int worldX, int worldY)
        {
            int localX = worldX - bufferOriginTileX;
            int localY = worldY - bufferOriginTileY;
            return GetLightAtLocal(localX, localY);
        }

        public CellMedium GetMediumAtLocal(int localX, int localY)
        {
            if (localX < 0 || localX >= bufferWidth || localY < 0 || localY >= bufferHeight)
                return CellMedium.Foreground;

            int index = (localY * bufferWidth) + localX;
            return mediumMask[index];
        }
    }
}
