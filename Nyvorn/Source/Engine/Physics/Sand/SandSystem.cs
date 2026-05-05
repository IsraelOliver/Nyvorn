using System;
using Nyvorn.Source.World;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;

namespace Nyvorn.Source.Engine.Physics.Sand
{
    public class SandSystem
    {
        private readonly WorldMap worldMap;
        private readonly HashSet<long> occupiedSand = new();
        private readonly HashSet<long> activeSandKeys = new();

        private readonly List<Point> activeSand = new();

        public int Width { get; }
        public int Height { get; }
        public int TileSize { get; }

        private readonly Random random = new();

        public SandSystem(WorldMap worldMap)
        {
            this.worldMap = worldMap;

            TileSize = worldMap.TileSize;
            Width = worldMap.Width * TileSize;
            Height = worldMap.Height * TileSize;
        }

        public bool HasSandAt(int pixelX, int pixelY)
        {
            return IsInBounds(pixelX, pixelY) && occupiedSand.Contains(CreatePixelKey(pixelX, pixelY));
        }

        public void SetSandAt(int pixelX, int pixelY, bool value)
        {
            if (!IsInBounds(pixelX, pixelY))
                return;

            long key = CreatePixelKey(pixelX, pixelY);
            bool oldValue = occupiedSand.Contains(key);

            if (value)
            {
                occupiedSand.Add(key);
            }
            else
            {
                occupiedSand.Remove(key);
                activeSandKeys.Remove(key);
                WakeNeighbors(pixelX, pixelY);
            }

            if (value && !oldValue)
            {
                AddActiveSand(pixelX, pixelY);
                WakeNeighbors(pixelX, pixelY);
            }
        }

        private bool CanMoveTo(int x, int y)
        {
            if (!IsInBounds(x, y))
                return false;

            if (occupiedSand.Contains(CreatePixelKey(x, y)))
                return false;

            int tileX = x / TileSize;
            int tileY = y / TileSize;
            return !worldMap.IsSolidAt(tileX, tileY);
        }

        private Point MoveSand(int fromX, int fromY, int toX, int toY)
        {
            long fromKey = CreatePixelKey(fromX, fromY);
            long toKey = CreatePixelKey(toX, toY);

            occupiedSand.Remove(fromKey);
            occupiedSand.Add(toKey);
            activeSandKeys.Remove(fromKey);
            WakeNeighbors(fromX, fromY);
            WakeNeighbors(toX, toY);

            return new Point(toX, toY);
        }

        private Point TryMoveSandDown(int x, int y)
        {
            // 1. Tenta cair reto
            if (CanMoveTo(x, y + 1))
                return MoveSand(x, y, x, y + 1);

            // 2. Randomiza qual diagonal testar primeiro
            bool tryLeftFirst = random.Next(2) == 0;

            int firstDx = tryLeftFirst ? -1 : 1;
            int secondDx = tryLeftFirst ? 1 : -1;

            // 3. Tenta primeira diagonal
            if (CanMoveTo(x + firstDx, y + 1))
                return MoveSand(x, y, x + firstDx, y + 1);

            // 4. Tenta segunda diagonal
            if (CanMoveTo(x + secondDx, y + 1))
                return MoveSand(x, y, x + secondDx, y + 1);

            // 5. Não conseguiu mover
            return new Point(x, y);
        }
        public void Update(float dt)
        {
            for (int i = activeSand.Count - 1; i >= 0; i--)
            {
                Point current = activeSand[i];
                long currentKey = CreatePixelKey(current.X, current.Y);

                if (!occupiedSand.Contains(currentKey))
                {
                    activeSandKeys.Remove(currentKey);
                    activeSand.RemoveAt(i);
                    continue;
                }

                Point newPosition = TryMoveSandDown(current.X, current.Y);
                if (newPosition == current)
                {
                    activeSandKeys.Remove(currentKey);
                    activeSand.RemoveAt(i);
                    continue;
                }

                long newKey = CreatePixelKey(newPosition.X, newPosition.Y);
                activeSandKeys.Add(newKey);
                activeSand[i] = newPosition;
            }
        }

        public byte[] ExportSnapshot()
        {
            if (occupiedSand.Count == 0)
                return Array.Empty<byte>();

            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);

            writer.Write(occupiedSand.Count);
            foreach (long key in occupiedSand)
            {
                DecodePixelKey(key, out int pixelX, out int pixelY);
                writer.Write(pixelX);
                writer.Write(pixelY);
            }

            writer.Flush();
            return stream.ToArray();
        }

        public void ImportSnapshot(byte[] snapshot)
        {
            occupiedSand.Clear();
            activeSand.Clear();
            activeSandKeys.Clear();

            if (snapshot == null || snapshot.Length == 0)
                return;

            using MemoryStream stream = new(snapshot);
            using BinaryReader reader = new(stream);

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int pixelX = reader.ReadInt32();
                int pixelY = reader.ReadInt32();

                if (!IsInBounds(pixelX, pixelY))
                    continue;

                long key = CreatePixelKey(pixelX, pixelY);
                if (!occupiedSand.Add(key))
                    continue;

                AddActiveSand(pixelX, pixelY);
            }
        }

        private void AddActiveSand(int pixelX, int pixelY)
        {
            if (!IsInBounds(pixelX, pixelY))
                return;

            long key = CreatePixelKey(pixelX, pixelY);
            if (!occupiedSand.Contains(key))
                return;

            if (activeSandKeys.Add(key))
                activeSand.Add(new Point(pixelX, pixelY));
        }

        private void WakeNeighbors(int pixelX, int pixelY)
        {
            AddActiveSand(pixelX, pixelY - 1);
            AddActiveSand(pixelX - 1, pixelY - 1);
            AddActiveSand(pixelX + 1, pixelY - 1);
            AddActiveSand(pixelX - 1, pixelY);
            AddActiveSand(pixelX + 1, pixelY);
        }

        private bool IsInBounds(int pixelX, int pixelY)
        {
            return pixelX >= 0 && pixelY >= 0 && pixelX < Width && pixelY < Height;
        }

        private long CreatePixelKey(int pixelX, int pixelY)
        {
            return ((long)pixelY << 32) | (uint)pixelX;
        }

        private void DecodePixelKey(long key, out int pixelX, out int pixelY)
        {
            pixelX = (int)(key & 0xFFFFFFFF);
            pixelY = (int)(key >> 32);
        }
    }
}
