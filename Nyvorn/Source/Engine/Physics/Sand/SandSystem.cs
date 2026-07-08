using System;
using Nyvorn.Source.Engine.Physics.Liquids;
using Nyvorn.Source.World;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;

namespace Nyvorn.Source.Engine.Physics.Sand
{
    public class SandSystem
    {
        private const int MaxActiveSandUpdatesPerTick = 8000;

        private readonly WorldMap worldMap;
        private readonly HashSet<long> occupiedSand = new();
        private readonly Dictionary<int, SortedSet<int>> occupiedSandRows = new();
        private readonly Dictionary<int, SortedSet<int>> occupiedSandColumns = new();
        private readonly HashSet<long> activeSandKeys = new();

        private readonly List<Point> activeSand = new();

        public int Width { get; }
        public int Height { get; }
        public int TileSize { get; }
        public LiquidSystem LiquidSystem { get; set; }

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
                LiquidSystem?.WakeAreaAroundPixel(pixelX, pixelY);
            }

            if (value && !oldValue)
            {
                AddActiveSand(pixelX, pixelY);
                WakeNeighbors(pixelX, pixelY);
                LiquidSystem?.WakeAreaAroundPixel(pixelX, pixelY);
            }
        }

        public int AddSettledSandRectangle(int pixelX, int pixelY, int width, int height, bool wakeOpenEdges = false)
        {
            if (width <= 0 || height <= 0)
                return 0;

            int startY = Math.Max(0, pixelY);
            int endY = Math.Min(Height - 1, pixelY + height - 1);
            if (startY > endY)
                return 0;

            int added = 0;
            for (int y = startY; y <= endY; y++)
            {
                for (int rawX = pixelX; rawX < pixelX + width; rawX++)
                {
                    int x = WrapPixelX(rawX);
                    if (!CanPlaceGeneratedSandAt(x, y))
                        continue;

                    long key = CreatePixelKey(x, y);
                    if (!occupiedSand.Add(key))
                        continue;

                    AddOccupiedPixel(x, y);
                    added++;
                }
            }

            if (wakeOpenEdges)
                WakeGeneratedSandRectangle(pixelX, startY, width, endY - startY + 1);

            return added;
        }

        private bool CanPlaceGeneratedSandAt(int pixelX, int pixelY)
        {
            if (!IsInBounds(pixelX, pixelY))
                return false;

            if (occupiedSand.Contains(CreatePixelKey(pixelX, pixelY)))
                return false;

            if (LiquidSystem?.HasLiquidAt(pixelX, pixelY) == true)
                return false;

            int tileX = pixelX / TileSize;
            int tileY = pixelY / TileSize;
            return !worldMap.IsSolidAt(tileX, tileY);
        }

        private void WakeGeneratedSandRectangle(int pixelX, int pixelY, int width, int height)
        {
            int startY = Math.Max(0, pixelY);
            int endY = Math.Min(Height - 1, pixelY + height - 1);
            if (startY > endY)
                return;

            for (int y = startY; y <= endY; y++)
            {
                for (int rawX = pixelX; rawX < pixelX + width; rawX++)
                {
                    int x = WrapPixelX(rawX);
                    long key = CreatePixelKey(x, y);
                    if (!occupiedSand.Contains(key))
                        continue;

                    if (CanMoveTo(x, y + 1) ||
                        CanMoveTo(x - 1, y + 1) ||
                        CanMoveTo(x + 1, y + 1))
                    {
                        AddActiveSand(x, y);
                    }
                }
            }
        }

        private bool CanMoveTo(int x, int y)
        {
            if (!IsInBounds(x, y))
                return false;

            if (occupiedSand.Contains(CreatePixelKey(x, y)))
                return false;

            if (LiquidSystem?.HasLiquidAt(x, y) == true)
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
            LiquidSystem?.WakeAreaAroundPixel(fromX, fromY);
            LiquidSystem?.WakeAreaAroundPixel(toX, toY);

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
        public void TickFast()
        {
            int processed = 0;
            for (int i = activeSand.Count - 1; i >= 0 && processed < MaxActiveSandUpdatesPerTick; i--, processed++)
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
            int clampedMinX = Math.Max(0, minPixelX);
            int clampedMaxX = Math.Min(Width - 1, maxPixelX);
            if (clampedMinX > clampedMaxX || clampedMinY > clampedMaxY)
                yield break;

            int? runStart = null;
            int runY = 0;
            int previousX = int.MinValue;

            for (int x = clampedMinX; x <= clampedMaxX; x++)
            {
                if (!occupiedSandColumns.TryGetValue(x, out SortedSet<int> column) || column.Count == 0)
                {
                    if (runStart.HasValue)
                    {
                        yield return new Rectangle(runStart.Value, runY, previousX - runStart.Value + 1, 1);
                        runStart = null;
                    }

                    continue;
                }

                int y = column.Min;
                if (y < clampedMinY || y > clampedMaxY)
                {
                    if (runStart.HasValue)
                    {
                        yield return new Rectangle(runStart.Value, runY, previousX - runStart.Value + 1, 1);
                        runStart = null;
                    }

                    continue;
                }

                if (!runStart.HasValue)
                {
                    runStart = x;
                    runY = y;
                    previousX = x;
                    continue;
                }

                if (y != runY || x != previousX + 1)
                {
                    yield return new Rectangle(runStart.Value, runY, previousX - runStart.Value + 1, 1);
                    runStart = x;
                    runY = y;
                    previousX = x;
                    continue;
                }

                previousX = x;
            }

            if (runStart.HasValue)
                yield return new Rectangle(runStart.Value, runY, previousX - runStart.Value + 1, 1);
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

        public bool TryGetSurfaceSupportY(
            int minPixelX,
            int maxPixelX,
            float referenceY,
            float maxDistance,
            int minWidth,
            int maxStepPerColumn,
            out int supportY)
        {
            supportY = 0;
            if (minPixelX > maxPixelX || Width <= 0 || minWidth <= 0 || maxDistance < 0f)
                return false;

            bool foundSupport = false;
            int bestSupportY = 0;
            float bestDistance = float.MaxValue;
            int currentGroupStartX = 0;
            int currentGroupEndX = 0;
            int currentGroupBestY = 0;
            float currentGroupBestDistance = float.MaxValue;
            int previousSurfaceY = 0;
            bool hasOpenGroup = false;

            for (int rawX = minPixelX; rawX <= maxPixelX; rawX++)
            {
                if (!TryGetTopSandY(rawX, out int candidateSurfaceY) ||
                    Math.Abs(candidateSurfaceY - referenceY) > maxDistance)
                {
                    CloseGroupIfValid();
                    continue;
                }

                if (!hasOpenGroup)
                {
                    OpenGroup(rawX, candidateSurfaceY);
                    continue;
                }

                if (Math.Abs(candidateSurfaceY - previousSurfaceY) <= maxStepPerColumn)
                {
                    currentGroupEndX = rawX;
                    RegisterGroupCandidate(candidateSurfaceY);
                    previousSurfaceY = candidateSurfaceY;
                    continue;
                }

                CloseGroupIfValid();
                OpenGroup(rawX, candidateSurfaceY);
            }

            CloseGroupIfValid();

            if (!foundSupport)
                return false;

            supportY = bestSupportY;
            return true;

            void OpenGroup(int rawX, int topY)
            {
                hasOpenGroup = true;
                currentGroupStartX = rawX;
                currentGroupEndX = rawX;
                currentGroupBestY = topY;
                currentGroupBestDistance = Math.Abs(topY - referenceY);
                previousSurfaceY = topY;
            }

            void RegisterGroupCandidate(int candidateY)
            {
                float distance = Math.Abs(candidateY - referenceY);
                if (distance < currentGroupBestDistance ||
                    (distance == currentGroupBestDistance && candidateY < currentGroupBestY))
                {
                    currentGroupBestY = candidateY;
                    currentGroupBestDistance = distance;
                }
            }

            void CloseGroupIfValid()
            {
                if (!hasOpenGroup)
                    return;

                int groupWidth = currentGroupEndX - currentGroupStartX + 1;
                if (groupWidth >= minWidth &&
                    (currentGroupBestDistance < bestDistance ||
                    (currentGroupBestDistance == bestDistance && currentGroupBestY < bestSupportY)))
                {
                    bestSupportY = currentGroupBestY;
                    bestDistance = currentGroupBestDistance;
                    foundSupport = true;
                }

                hasOpenGroup = false;
            }
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

        public int RemoveSandInRectangle(int pixelX, int pixelY, int width, int height, int maxPixels = int.MaxValue)
        {
            if (width <= 0 || height <= 0 || maxPixels <= 0 || Width <= 0 || Height <= 0)
                return 0;

            int minY = Math.Max(0, pixelY);
            int maxY = Math.Min(Height - 1, pixelY + height - 1);
            if (minY > maxY)
                return 0;

            int rawMinX = pixelX;
            int rawMaxX = pixelX + width - 1;
            int removed = 0;
            List<int> candidates = new(width);

            for (int y = minY; y <= maxY && removed < maxPixels; y++)
            {
                if (!occupiedSandRows.TryGetValue(y, out SortedSet<int> row) || row.Count == 0)
                    continue;

                candidates.Clear();
                CollectWrappedRange(row, rawMinX, rawMaxX, maxPixels - removed, candidates);

                for (int i = 0; i < candidates.Count && removed < maxPixels; i++)
                {
                    int x = candidates[i];
                    long key = CreatePixelKey(x, y);
                    if (!occupiedSand.Remove(key))
                        continue;

                    RemoveOccupiedPixel(x, y);
                    activeSandKeys.Remove(key);
                    WakeNeighbors(x, y);
                    LiquidSystem?.WakeAreaAroundPixel(x, y);
                    removed++;
                }
            }

            return removed;
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

        private void CollectWrappedRange(SortedSet<int> row, int rawMinX, int rawMaxX, int maxCount, List<int> targets)
        {
            int currentRawStartX = rawMinX;
            while (currentRawStartX <= rawMaxX && targets.Count < maxCount)
            {
                int wrappedStartX = WrapPixelX(currentRawStartX);
                int segmentMaxLength = Width - wrappedStartX;
                int currentRawEndX = Math.Min(rawMaxX, currentRawStartX + segmentMaxLength - 1);
                int wrappedEndX = wrappedStartX + (currentRawEndX - currentRawStartX);

                foreach (int x in row.GetViewBetween(wrappedStartX, wrappedEndX))
                {
                    targets.Add(x);
                    if (targets.Count >= maxCount)
                        break;
                }

                currentRawStartX = currentRawEndX + 1;
            }
        }
    }
}
