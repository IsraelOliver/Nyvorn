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
        private readonly Dictionary<int, SortedSet<int>> occupiedSandRows = new();
        private readonly Dictionary<int, SortedSet<int>> occupiedSandColumns = new();
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
                if (occupiedSand.Add(key))
                    AddOccupiedPixel(pixelX, pixelY);
            }
            else
            {
                if (occupiedSand.Remove(key))
                    RemoveOccupiedPixel(pixelX, pixelY);
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
            RemoveOccupiedPixel(fromX, fromY);
            occupiedSand.Add(toKey);
            AddOccupiedPixel(toX, toY);
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
            occupiedSandRows.Clear();
            occupiedSandColumns.Clear();
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

                AddOccupiedPixel(pixelX, pixelY);
            }

            foreach (long key in occupiedSand)
            {
                DecodePixelKey(key, out int pixelX, out int pixelY);
                if (CanMoveTo(pixelX, pixelY + 1) ||
                    CanMoveTo(pixelX - 1, pixelY + 1) ||
                    CanMoveTo(pixelX + 1, pixelY + 1))
                {
                    AddActiveSand(pixelX, pixelY);
                }
            }
        }

        public void WakeAreaAboveTile(int tileX, int tileY)
        {
            int startPixelX = tileX * TileSize;
            int startPixelY = tileY * TileSize;

            for (int pixelY = startPixelY - 1; pixelY >= startPixelY - TileSize; pixelY--)
            {
                for (int pixelX = startPixelX - 1; pixelX <= startPixelX + TileSize; pixelX++)
                    AddActiveSand(WrapPixelX(pixelX), pixelY);
            }
        }

        public IEnumerable<Rectangle> GetVisibleSegments(int minPixelX, int maxPixelX, int minPixelY, int maxPixelY)
        {
            if (minPixelX > maxPixelX || minPixelY > maxPixelY)
                yield break;

            int clampedMinY = Math.Max(0, minPixelY);
            int clampedMaxY = Math.Min(Height - 1, maxPixelY);
            for (int y = clampedMinY; y <= clampedMaxY; y++)
            {
                if (!occupiedSandRows.TryGetValue(y, out SortedSet<int> row) || row.Count == 0)
                    continue;

                int? runStart = null;
                int previousX = int.MinValue;
                foreach (int x in row.GetViewBetween(Math.Max(0, minPixelX), Math.Min(Width - 1, maxPixelX)))
                {
                    if (!runStart.HasValue)
                    {
                        runStart = x;
                        previousX = x;
                        continue;
                    }

                    if (x == previousX + 1)
                    {
                        previousX = x;
                        continue;
                    }

                    yield return new Rectangle(runStart.Value, y, previousX - runStart.Value + 1, 1);
                    runStart = x;
                    previousX = x;
                }

                if (runStart.HasValue)
                    yield return new Rectangle(runStart.Value, y, previousX - runStart.Value + 1, 1);
            }
        }

        public IEnumerable<Rectangle> GetVisibleTopEdgeSegments(int minPixelX, int maxPixelX, int minPixelY, int maxPixelY)
        {
            if (minPixelX > maxPixelX || minPixelY > maxPixelY)
                yield break;

            int clampedMinY = Math.Max(0, minPixelY);
            int clampedMaxY = Math.Min(Height - 1, maxPixelY);
            for (int y = clampedMinY; y <= clampedMaxY; y++)
            {
                if (!occupiedSandRows.TryGetValue(y, out SortedSet<int> row) || row.Count == 0)
                    continue;

                occupiedSandRows.TryGetValue(y - 1, out SortedSet<int> aboveRow);

                int? runStart = null;
                int previousX = int.MinValue;
                foreach (int x in row.GetViewBetween(Math.Max(0, minPixelX), Math.Min(Width - 1, maxPixelX)))
                {
                    bool isTopEdge = aboveRow == null || !aboveRow.Contains(x);
                    if (!isTopEdge)
                    {
                        if (runStart.HasValue)
                        {
                            yield return new Rectangle(runStart.Value, y, previousX - runStart.Value + 1, 1);
                            runStart = null;
                        }

                        continue;
                    }

                    if (!runStart.HasValue)
                    {
                        runStart = x;
                        previousX = x;
                        continue;
                    }

                    if (x == previousX + 1)
                    {
                        previousX = x;
                        continue;
                    }

                    yield return new Rectangle(runStart.Value, y, previousX - runStart.Value + 1, 1);
                    runStart = x;
                    previousX = x;
                }

                if (runStart.HasValue)
                    yield return new Rectangle(runStart.Value, y, previousX - runStart.Value + 1, 1);
            }
        }

        public bool TryGetTopSandY(int pixelX, out int surfaceY)
        {
            surfaceY = 0;
            if (Width <= 0)
                return false;

            int wrappedPixelX = WrapPixelX(pixelX);
            if (!occupiedSandColumns.TryGetValue(wrappedPixelX, out SortedSet<int> column) || column.Count == 0)
                return false;

            surfaceY = column.Min;
            return true;
        }

        public bool TryGetWalkableSurfaceY(int minPixelX, int maxPixelX, out int surfaceY)
        {
            surfaceY = 0;
            if (minPixelX > maxPixelX || Width <= 0)
                return false;

            bool foundSurface = false;
            int bestSurfaceY = int.MaxValue;
            int currentRawX = minPixelX;

            while (currentRawX <= maxPixelX)
            {
                int wrappedStartX = WrapPixelX(currentRawX);
                int segmentMaxLength = Width - wrappedStartX;
                int currentRawEndX = Math.Min(maxPixelX, currentRawX + segmentMaxLength - 1);
                int wrappedEndX = wrappedStartX + (currentRawEndX - currentRawX);

                for (int wrappedX = wrappedStartX; wrappedX <= wrappedEndX; wrappedX++)
                {
                    if (!TryGetTopSandY(wrappedX, out int candidateSurfaceY) || candidateSurfaceY >= bestSurfaceY)
                        continue;

                    bestSurfaceY = candidateSurfaceY;
                    foundSurface = true;
                }

                currentRawX = currentRawEndX + 1;
            }

            if (!foundSurface)
                return false;

            surfaceY = bestSurfaceY;
            return true;
        }

        public bool HasSandInRectangle(int pixelX, int pixelY, int width, int height)
        {
            if (width <= 0 || height <= 0 || Width <= 0 || Height <= 0)
                return false;

            int minY = Math.Max(0, pixelY);
            int maxY = Math.Min(Height - 1, pixelY + height - 1);
            if (minY > maxY)
                return false;

            int rawMinX = pixelX;
            int rawMaxX = pixelX + width - 1;

            for (int y = minY; y <= maxY; y++)
            {
                if (!occupiedSandRows.TryGetValue(y, out SortedSet<int> row) || row.Count == 0)
                    continue;

                if (IntersectsWrappedRange(row, rawMinX, rawMaxX))
                    return true;
            }

            return false;
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

        private int WrapPixelX(int pixelX)
        {
            if (Width <= 0)
                return 0;

            int wrapped = pixelX % Width;
            return wrapped < 0 ? wrapped + Width : wrapped;
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

        private void AddOccupiedPixel(int pixelX, int pixelY)
        {
            if (!occupiedSandRows.TryGetValue(pixelY, out SortedSet<int> row))
            {
                row = new SortedSet<int>();
                occupiedSandRows[pixelY] = row;
            }

            row.Add(pixelX);

            if (!occupiedSandColumns.TryGetValue(pixelX, out SortedSet<int> column))
            {
                column = new SortedSet<int>();
                occupiedSandColumns[pixelX] = column;
            }

            column.Add(pixelY);
        }

        private void RemoveOccupiedPixel(int pixelX, int pixelY)
        {
            if (!occupiedSandRows.TryGetValue(pixelY, out SortedSet<int> row))
                return;

            row.Remove(pixelX);
            if (row.Count == 0)
                occupiedSandRows.Remove(pixelY);

            if (!occupiedSandColumns.TryGetValue(pixelX, out SortedSet<int> column))
                return;

            column.Remove(pixelY);
            if (column.Count == 0)
                occupiedSandColumns.Remove(pixelX);
        }

        private bool IntersectsWrappedRange(SortedSet<int> row, int rawMinX, int rawMaxX)
        {
            int currentRawStartX = rawMinX;
            while (currentRawStartX <= rawMaxX)
            {
                int wrappedStartX = WrapPixelX(currentRawStartX);
                int segmentMaxLength = Width - wrappedStartX;
                int currentRawEndX = Math.Min(rawMaxX, currentRawStartX + segmentMaxLength - 1);
                int wrappedEndX = wrappedStartX + (currentRawEndX - currentRawStartX);

                if (row.GetViewBetween(wrappedStartX, wrappedEndX).Count > 0)
                    return true;

                currentRawStartX = currentRawEndX + 1;
            }

            return false;
        }
    }
}
