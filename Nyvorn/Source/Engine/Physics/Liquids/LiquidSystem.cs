using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nyvorn.Source.Engine.Physics.Liquids
{
    public sealed class LiquidSystem
    {
        public const int CellSize = 8;

        private const byte SnapshotVersion = 2;
        private const byte LegacyCellSnapshotVersion = 1;
        private const int MaxAmount = 255;
        private const int DownFlowPerTick = 64;
        private const int SideFlowPerTick = 64;
        private const int MinSideFlowDifference = 1;
        private const int SurfaceLineHeight = 2;
        private const int MaxPoolSettleCells = 4096;
        private const int PoolSettleAmountPerTick = 64;
        private const int MinVisibleAmount = MaxAmount / 8;

        private readonly WorldMap worldMap;
        private readonly Dictionary<long, int> waterAmounts = new();
        private readonly Dictionary<int, SortedSet<int>> waterRows = new();
        private readonly HashSet<long> activeWaterKeys = new();
        private readonly List<Point> activeWater = new();
        private readonly HashSet<long> settledPoolKeysThisTick = new();

        private int totalWaterAmount;
        private int revision;
        private int persistedRevision;

        public LiquidSystem(WorldMap worldMap)
        {
            this.worldMap = worldMap;

            TileSize = worldMap.TileSize;
            Width = worldMap.Width * TileSize;
            Height = worldMap.Height * TileSize;
            CellWidth = worldMap.Width;
            CellHeight = worldMap.Height;
        }

        public SandSystem SandSystem { get; set; }
        public int Width { get; }
        public int Height { get; }
        public int CellWidth { get; }
        public int CellHeight { get; }
        public int TileSize { get; }
        public int CellCount => waterAmounts.Count;
        public int ActiveCellCount => activeWater.Count;
        public int TotalWaterAmount => totalWaterAmount;
        public float TotalTileVolume => totalWaterAmount / (float)MaxAmount;
        public bool HasUnsavedChanges => revision != persistedRevision;

        public bool HasLiquidAt(int pixelX, int pixelY)
        {
            if (!TryPixelToCell(pixelX, pixelY, out int cellX, out int cellY))
                return false;

            int amount = GetAmount(cellX, cellY);
            if (amount <= 0)
                return false;

            int waterHeight = AmountToPixelHeight(amount);
            int waterTop = ((cellY + 1) * TileSize) - waterHeight;
            return pixelY >= waterTop;
        }

        public bool SetLiquidAt(int pixelX, int pixelY, LiquidType liquidType, bool value)
        {
            if (liquidType != LiquidType.Water ||
                !TryPixelToCell(pixelX, pixelY, out int cellX, out int cellY))
            {
                return false;
            }

            bool changed;
            if (value)
            {
                if (!CanOccupyCell(cellX, cellY))
                    return false;

                changed = SetAmount(cellX, cellY, MaxAmount);
            }
            else
            {
                changed = SetAmount(cellX, cellY, 0);
            }

            if (!changed)
                return false;

            revision++;
            WakeNeighbors(cellX, cellY);
            WakeSandAboveCell(cellX, cellY);
            return true;
        }

        public void TickFast()
        {
            if (activeWater.Count != activeWaterKeys.Count)
                CompactActiveWaterList();

            settledPoolKeysThisTick.Clear();
            int initialCount = activeWater.Count;
            for (int i = initialCount - 1; i >= 0; i--)
            {
                if (i >= activeWater.Count)
                    continue;

                Point current = activeWater[i];
                long currentKey = CreateCellKey(current.X, current.Y);
                if (!waterAmounts.ContainsKey(currentKey))
                {
                    activeWaterKeys.Remove(currentKey);
                    activeWater.RemoveAt(i);
                    continue;
                }

                TickWaterCell(current.X, current.Y);
                if (!ShouldStayActive(current.X, current.Y))
                {
                    activeWaterKeys.Remove(currentKey);
                    activeWater.RemoveAt(i);
                }
            }
        }

        public byte[] ExportSnapshot()
        {
            if (waterAmounts.Count == 0)
                return Array.Empty<byte>();

            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);

            writer.Write(SnapshotVersion);
            writer.Write(TileSize);
            writer.Write(waterAmounts.Count);
            foreach (KeyValuePair<long, int> entry in waterAmounts)
            {
                DecodeCellKey(entry.Key, out int cellX, out int cellY);
                writer.Write(cellX);
                writer.Write(cellY);
                writer.Write((byte)LiquidType.Water);
                writer.Write((byte)Math.Clamp(entry.Value, 0, MaxAmount));
            }

            writer.Flush();
            return stream.ToArray();
        }

        public void ImportSnapshot(byte[] snapshot)
        {
            ClearInternal(markDirty: false);
            if (snapshot == null || snapshot.Length == 0)
            {
                MarkPersisted();
                return;
            }

            try
            {
                using MemoryStream stream = new(snapshot);
                using BinaryReader reader = new(stream);

                byte version = reader.ReadByte();
                int savedCellSize = reader.ReadInt32();
                if (version == LegacyCellSnapshotVersion)
                    ImportLegacyCellSnapshot(reader, savedCellSize);
                else if (version == SnapshotVersion && savedCellSize == TileSize)
                    ImportTileAmountSnapshot(reader);
            }
            catch
            {
                ClearInternal(markDirty: false);
            }

            WakeAllUnsettledWater();
            MarkPersisted();
        }

        public void MarkPersisted()
        {
            persistedRevision = revision;
        }

        public int Clear()
        {
            int count = waterAmounts.Count;
            ClearInternal(markDirty: count > 0);
            return count;
        }

        public void WakeAreaAroundTile(int tileX, int tileY)
        {
            for (int cellY = tileY - 1; cellY <= tileY + 1; cellY++)
            {
                for (int cellX = tileX - 1; cellX <= tileX + 1; cellX++)
                    AddActiveWater(cellX, cellY);
            }
        }

        public void WakeAreaAroundPixel(int pixelX, int pixelY)
        {
            if (!TryPixelToCell(pixelX, pixelY, out int cellX, out int cellY))
                return;

            WakeNeighbors(cellX, cellY);
        }

        public bool HasLiquidInRectangle(int pixelX, int pixelY, int width, int height)
        {
            if (width <= 0 || height <= 0 || Width <= 0 || Height <= 0)
                return false;

            int minCellY = Math.Max(0, PixelToCellY(pixelY));
            int maxCellY = Math.Min(CellHeight - 1, PixelToCellY(pixelY + height - 1));
            if (minCellY > maxCellY)
                return false;

            int rawMinCellX = PixelToRawCellX(pixelX);
            int rawMaxCellX = PixelToRawCellX(pixelX + width - 1);
            Rectangle query = new(pixelX, pixelY, width, height);
            for (int cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                if (!waterRows.TryGetValue(cellY, out SortedSet<int> row) || row.Count == 0)
                    continue;

                int currentRawStartX = rawMinCellX;
                while (currentRawStartX <= rawMaxCellX)
                {
                    int wrappedStartX = WrapCellX(currentRawStartX);
                    int segmentMaxLength = CellWidth - wrappedStartX;
                    int currentRawEndX = Math.Min(rawMaxCellX, currentRawStartX + segmentMaxLength - 1);
                    int wrappedEndX = wrappedStartX + (currentRawEndX - currentRawStartX);
                    int drawOffsetX = currentRawStartX - wrappedStartX;

                    foreach (int cellX in row.GetViewBetween(wrappedStartX, wrappedEndX))
                    {
                        Rectangle waterBounds = CreateWaterRectangle(cellX, cellY);
                        waterBounds.X += drawOffsetX;
                        if (waterBounds.Intersects(query))
                            return true;
                    }

                    currentRawStartX = currentRawEndX + 1;
                }
            }

            return false;
        }

        public IEnumerable<Rectangle> GetVisibleSegments(int minPixelX, int maxPixelX, int minPixelY, int maxPixelY)
        {
            if (minPixelX > maxPixelX || minPixelY > maxPixelY)
                yield break;

            int minCellX = Math.Max(0, PixelToRawCellX(minPixelX));
            int maxCellX = Math.Min(CellWidth - 1, PixelToRawCellX(maxPixelX));
            int minCellY = Math.Max(0, PixelToCellY(minPixelY));
            int maxCellY = Math.Min(CellHeight - 1, PixelToCellY(maxPixelY));
            if (minCellX > maxCellX || minCellY > maxCellY)
                yield break;

            Rectangle visible = new(minPixelX, minPixelY, maxPixelX - minPixelX + 1, maxPixelY - minPixelY + 1);
            for (int cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                if (!waterRows.TryGetValue(cellY, out SortedSet<int> row) || row.Count == 0)
                    continue;

                foreach (int cellX in row.GetViewBetween(minCellX, maxCellX))
                {
                    Rectangle waterBounds = CreateWaterRectangle(cellX, cellY);
                    Rectangle clipped = Rectangle.Intersect(waterBounds, visible);
                    if (!clipped.IsEmpty)
                        yield return clipped;
                }
            }
        }

        public IEnumerable<Rectangle> GetVisibleSurfaceSegments(int minPixelX, int maxPixelX, int minPixelY, int maxPixelY)
        {
            if (minPixelX > maxPixelX || minPixelY > maxPixelY)
                yield break;

            int minCellX = Math.Max(0, PixelToRawCellX(minPixelX));
            int maxCellX = Math.Min(CellWidth - 1, PixelToRawCellX(maxPixelX));
            int minCellY = Math.Max(0, PixelToCellY(minPixelY));
            int maxCellY = Math.Min(CellHeight - 1, PixelToCellY(maxPixelY));
            if (minCellX > maxCellX || minCellY > maxCellY)
                yield break;

            Rectangle visible = new(minPixelX, minPixelY, maxPixelX - minPixelX + 1, maxPixelY - minPixelY + 1);
            for (int cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                if (!waterRows.TryGetValue(cellY, out SortedSet<int> row) || row.Count == 0)
                    continue;

                foreach (int cellX in row.GetViewBetween(minCellX, maxCellX))
                {
                    if (GetAmount(cellX, cellY - 1) > 0)
                        continue;

                    Rectangle waterBounds = CreateWaterRectangle(cellX, cellY);
                    int surfaceHeight = Math.Min(SurfaceLineHeight, waterBounds.Height);
                    Rectangle surfaceBounds = new(waterBounds.X, waterBounds.Y, waterBounds.Width, surfaceHeight);
                    Rectangle clipped = Rectangle.Intersect(surfaceBounds, visible);
                    if (!clipped.IsEmpty)
                        yield return clipped;
                }
            }
        }

        private void ImportTileAmountSnapshot(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int cellX = reader.ReadInt32();
                int cellY = reader.ReadInt32();
                LiquidType liquidType = (LiquidType)reader.ReadByte();
                int amount = reader.ReadByte();
                if (liquidType != LiquidType.Water ||
                    amount <= 0 ||
                    !IsCellInBounds(cellX, cellY) ||
                    !CanOccupyCell(cellX, cellY))
                {
                    continue;
                }

                SetAmount(cellX, cellY, amount);
            }
        }

        private void ImportLegacyCellSnapshot(BinaryReader reader, int legacyCellSize)
        {
            if (legacyCellSize <= 0)
                return;

            int count = reader.ReadInt32();
            int cellsPerTileX = Math.Max(1, TileSize / legacyCellSize);
            int legacyCellsPerTile = Math.Max(1, cellsPerTileX * cellsPerTileX);
            int amountPerLegacyCell = Math.Max(1, (int)MathF.Ceiling(MaxAmount / (float)legacyCellsPerTile));
            Dictionary<long, int> accumulatedAmounts = new();

            for (int i = 0; i < count; i++)
            {
                int legacyCellX = reader.ReadInt32();
                int legacyCellY = reader.ReadInt32();
                LiquidType liquidType = (LiquidType)reader.ReadByte();
                if (liquidType != LiquidType.Water)
                    continue;

                int cellX = WrapCellX((legacyCellX * legacyCellSize) / TileSize);
                int cellY = (legacyCellY * legacyCellSize) / TileSize;
                if (!IsCellInBounds(cellX, cellY) || !CanOccupyCell(cellX, cellY))
                    continue;

                long key = CreateCellKey(cellX, cellY);
                accumulatedAmounts.TryGetValue(key, out int amount);
                accumulatedAmounts[key] = Math.Min(MaxAmount, amount + amountPerLegacyCell);
            }

            foreach (KeyValuePair<long, int> entry in accumulatedAmounts)
            {
                DecodeCellKey(entry.Key, out int cellX, out int cellY);
                SetAmount(cellX, cellY, entry.Value);
            }
        }

        private void WakeAllUnsettledWater()
        {
            foreach (long key in waterAmounts.Keys)
            {
                DecodeCellKey(key, out int cellX, out int cellY);
                if (ShouldStayActive(cellX, cellY))
                    AddActiveWater(cellX, cellY);
            }
        }

        private void TickWaterCell(int cellX, int cellY)
        {
            int amount = GetAmount(cellX, cellY);
            if (amount <= 0)
                return;

            bool moved = false;
            if (TryTransferWater(cellX, cellY, cellX, cellY + 1, Math.Min(amount, DownFlowPerTick)))
            {
                moved = true;
                amount = GetAmount(cellX, cellY);
            }

            if (amount <= 0)
                return;

            int firstDx = ((cellX + cellY + revision) & 1) == 0 ? -1 : 1;
            int secondDx = -firstDx;

            if (!CanAcceptWater(cellX, cellY + 1))
            {
                if (TryTransferWater(cellX, cellY, cellX + firstDx, cellY + 1, Math.Min(amount, DownFlowPerTick)))
                {
                    moved = true;
                    amount = GetAmount(cellX, cellY);
                }

                if (amount <= 0)
                    return;

                if (TryTransferWater(cellX, cellY, cellX + secondDx, cellY + 1, Math.Min(amount, DownFlowPerTick)))
                {
                    moved = true;
                    amount = GetAmount(cellX, cellY);
                }
            }

            if (amount > MinSideFlowDifference)
            {
                if (((cellX + cellY + revision) & 1) == 0)
                {
                    moved |= TryEqualizeSide(cellX, cellY, -1);
                    moved |= TryEqualizeSide(cellX, cellY, 1);
                }
                else
                {
                    moved |= TryEqualizeSide(cellX, cellY, 1);
                    moved |= TryEqualizeSide(cellX, cellY, -1);
                }
            }

            if (!moved)
                TrySettleLocalPool(cellX, cellY);
        }

        private bool TrySettleLocalPool(int startCellX, int startCellY)
        {
            long seedKey = CreateCellKey(WrapCellX(startCellX), startCellY);
            if (settledPoolKeysThisTick.Contains(seedKey) ||
                GetAmount(startCellX, startCellY) <= 0)
            {
                return false;
            }

            List<Point> poolCells = new();
            HashSet<long> poolKeys = new();
            Queue<Point> pending = new();
            EnqueueWaterCell(startCellX, startCellY);

            while (pending.Count > 0)
            {
                Point current = pending.Dequeue();
                poolCells.Add(current);
                if (poolCells.Count > MaxPoolSettleCells)
                {
                    foreach (long key in poolKeys)
                        settledPoolKeysThisTick.Add(key);

                    return false;
                }

                EnqueueWaterCell(current.X - 1, current.Y);
                EnqueueWaterCell(current.X + 1, current.Y);
                EnqueueWaterCell(current.X, current.Y - 1);
                EnqueueWaterCell(current.X, current.Y + 1);
            }

            foreach (long key in poolKeys)
                settledPoolKeysThisTick.Add(key);

            if (poolCells.Count < 2 || !IsPoolSettled(poolCells))
                return false;

            return RedistributePoolSurface(poolCells, poolKeys);

            void EnqueueWaterCell(int cellX, int cellY)
            {
                cellX = WrapCellX(cellX);
                if (!IsCellInBounds(cellX, cellY) || GetAmount(cellX, cellY) <= 0)
                    return;

                long key = CreateCellKey(cellX, cellY);
                if (poolKeys.Add(key))
                    pending.Enqueue(new Point(cellX, cellY));
            }
        }

        private bool IsPoolSettled(List<Point> poolCells)
        {
            foreach (Point cell in poolCells)
            {
                if (CanAcceptWater(cell.X, cell.Y + 1) ||
                    CanAcceptWater(cell.X - 1, cell.Y + 1) ||
                    CanAcceptWater(cell.X + 1, cell.Y + 1))
                {
                    return false;
                }
            }

            return true;
        }

        private bool RedistributePoolSurface(List<Point> poolCells, HashSet<long> poolKeys)
        {
            Dictionary<int, PoolColumnState> columnsByX = new();
            long totalAmount = 0;
            foreach (Point cell in poolCells)
            {
                int amount = GetAmount(cell.X, cell.Y);
                totalAmount += amount;
                int columnX = WrapCellX(cell.X);
                if (!columnsByX.TryGetValue(columnX, out PoolColumnState column))
                {
                    column = new PoolColumnState(columnX, cell.Y);
                    columnsByX[columnX] = column;
                }

                column.BottomY = Math.Max(column.BottomY, cell.Y);
            }

            if (totalAmount <= 0 || columnsByX.Count <= 1)
                return false;

            List<PoolColumnState> columns = new(columnsByX.Values);
            int minSurfaceUnit = int.MaxValue;
            int maxSurfaceUnit = 0;
            foreach (PoolColumnState column in columns)
            {
                column.TopLimitY = FindPoolColumnTopLimit(column.X, column.BottomY);
                column.CapacityAmount = ((column.BottomY - column.TopLimitY) + 1) * MaxAmount;
                column.BottomUnit = (column.BottomY + 1) * MaxAmount;
                column.TopUnit = column.TopLimitY * MaxAmount;
                minSurfaceUnit = Math.Min(minSurfaceUnit, column.TopUnit);
                maxSurfaceUnit = Math.Max(maxSurfaceUnit, column.BottomUnit);
            }

            int targetSurfaceUnit = FindSurfaceUnitForVolume(columns, totalAmount, minSurfaceUnit, maxSurfaceUnit);
            long desiredTotal = 0;
            foreach (PoolColumnState column in columns)
            {
                column.DesiredAmount = GetColumnAmountAtSurface(column, targetSurfaceUnit);
                desiredTotal += column.DesiredAmount;
            }

            BalanceDesiredColumnAmounts(columns, totalAmount, ref desiredTotal);

            Dictionary<long, int> targetAmounts = new();
            foreach (PoolColumnState column in columns)
            {
                int remaining = column.DesiredAmount;
                for (int y = column.BottomY; y >= column.TopLimitY && remaining > 0; y--)
                {
                    int amount = Math.Min(MaxAmount, remaining);
                    long key = CreateCellKey(column.X, y);
                    if (waterAmounts.ContainsKey(key) && !poolKeys.Contains(key))
                        return false;

                    targetAmounts[key] = amount;
                    remaining -= amount;
                }
            }

            return ApplyGradualPoolRelaxation(poolKeys, targetAmounts);
        }

        private int FindPoolColumnTopLimit(int cellX, int bottomY)
        {
            int topY = bottomY;
            while (topY > 0 && CanOccupyCell(cellX, topY - 1))
                topY--;

            return topY;
        }

        private bool ApplyGradualPoolRelaxation(
            HashSet<long> poolKeys,
            Dictionary<long, int> targetAmounts)
        {
            List<PoolAmountDelta> donors = new();
            List<PoolAmountDelta> receivers = new();
            HashSet<long> comparedKeys = new(poolKeys);
            foreach (long key in targetAmounts.Keys)
                comparedKeys.Add(key);

            foreach (long key in comparedKeys)
            {
                waterAmounts.TryGetValue(key, out int currentAmount);
                targetAmounts.TryGetValue(key, out int targetAmount);
                if (currentAmount == targetAmount)
                    continue;

                DecodeCellKey(key, out int cellX, out int cellY);
                if (currentAmount > targetAmount)
                    donors.Add(new PoolAmountDelta(key, cellX, cellY, currentAmount - targetAmount));
                else
                    receivers.Add(new PoolAmountDelta(key, cellX, cellY, targetAmount - currentAmount));
            }

            if (donors.Count == 0 || receivers.Count == 0)
                return false;

            donors.Sort((a, b) =>
            {
                int y = a.Y.CompareTo(b.Y);
                return y != 0 ? y : a.X.CompareTo(b.X);
            });
            receivers.Sort((a, b) =>
            {
                int y = b.Y.CompareTo(a.Y);
                return y != 0 ? y : a.X.CompareTo(b.X);
            });

            int donorIndex = 0;
            int receiverIndex = 0;
            int remainingBudget = PoolSettleAmountPerTick;
            bool changed = false;
            HashSet<long> changedKeys = new();

            while (remainingBudget > 0 &&
                   donorIndex < donors.Count &&
                   receiverIndex < receivers.Count)
            {
                PoolAmountDelta donor = donors[donorIndex];
                PoolAmountDelta receiver = receivers[receiverIndex];
                int flow = Math.Min(remainingBudget, Math.Min(donor.RemainingAmount, receiver.RemainingAmount));
                if (flow <= 0)
                {
                    if (donor.RemainingAmount <= 0)
                        donorIndex++;
                    if (receiver.RemainingAmount <= 0)
                        receiverIndex++;
                    continue;
                }

                int donorAmount = GetAmount(donor.X, donor.Y);
                int receiverAmount = GetAmount(receiver.X, receiver.Y);
                int actualFlow = Math.Min(flow, Math.Min(donorAmount, MaxAmount - receiverAmount));
                if (actualFlow <= 0)
                {
                    if (donorAmount <= 0)
                        donorIndex++;
                    if (receiverAmount >= MaxAmount)
                        receiverIndex++;
                    continue;
                }

                if (!SetAmount(donor.X, donor.Y, donorAmount - actualFlow))
                {
                    donorIndex++;
                    continue;
                }

                if (!SetAmount(receiver.X, receiver.Y, receiverAmount + actualFlow))
                {
                    SetAmount(donor.X, donor.Y, donorAmount);
                    receiverIndex++;
                    continue;
                }

                donor.RemainingAmount -= actualFlow;
                receiver.RemainingAmount -= actualFlow;
                remainingBudget -= actualFlow;
                changed = true;
                changedKeys.Add(donor.Key);
                changedKeys.Add(receiver.Key);

                if (donor.RemainingAmount <= 0)
                    donorIndex++;
                if (receiver.RemainingAmount <= 0)
                    receiverIndex++;
            }

            if (!changed)
                return false;

            revision++;
            foreach (long key in changedKeys)
            {
                DecodeCellKey(key, out int cellX, out int cellY);
                WakeNeighbors(cellX, cellY);
                WakeSandAboveCell(cellX, cellY);
            }

            return true;
        }

        private int FindSurfaceUnitForVolume(
            List<PoolColumnState> columns,
            long totalAmount,
            int minSurfaceUnit,
            int maxSurfaceUnit)
        {
            int low = minSurfaceUnit;
            int high = maxSurfaceUnit;
            int best = minSurfaceUnit;
            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                long volume = GetPoolVolumeAtSurface(columns, mid);
                if (volume >= totalAmount)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return best;
        }

        private long GetPoolVolumeAtSurface(List<PoolColumnState> columns, int surfaceUnit)
        {
            long volume = 0;
            foreach (PoolColumnState column in columns)
                volume += GetColumnAmountAtSurface(column, surfaceUnit);

            return volume;
        }

        private static int GetColumnAmountAtSurface(PoolColumnState column, int surfaceUnit)
        {
            return Math.Clamp(column.BottomUnit - surfaceUnit, 0, column.CapacityAmount);
        }

        private static void BalanceDesiredColumnAmounts(
            List<PoolColumnState> columns,
            long totalAmount,
            ref long desiredTotal)
        {
            long excess = desiredTotal - totalAmount;
            while (excess > 0)
            {
                bool changed = false;
                foreach (PoolColumnState column in columns)
                {
                    if (excess <= 0)
                        break;

                    if (column.DesiredAmount <= 0)
                        continue;

                    column.DesiredAmount--;
                    desiredTotal--;
                    excess--;
                    changed = true;
                }

                if (!changed)
                    break;
            }

            long missing = totalAmount - desiredTotal;
            while (missing > 0)
            {
                bool changed = false;
                foreach (PoolColumnState column in columns)
                {
                    if (missing <= 0)
                        break;

                    if (column.DesiredAmount >= column.CapacityAmount)
                        continue;

                    column.DesiredAmount++;
                    desiredTotal++;
                    missing--;
                    changed = true;
                }

                if (!changed)
                    break;
            }
        }

        private bool TryEqualizeSide(int cellX, int cellY, int direction)
        {
            int targetX = WrapCellX(cellX + direction);
            int amount = GetAmount(cellX, cellY);
            int targetAmount = GetAmount(targetX, cellY);
            int difference = amount - targetAmount;
            if (difference <= MinSideFlowDifference || !CanAcceptWater(targetX, cellY))
                return false;

            int flow = Math.Min(SideFlowPerTick, Math.Max(1, difference / 2));
            return TryTransferWater(cellX, cellY, targetX, cellY, flow);
        }

        private bool TryTransferWater(int fromX, int fromY, int toX, int toY, int requestedAmount)
        {
            fromX = WrapCellX(fromX);
            toX = WrapCellX(toX);
            if (requestedAmount <= 0 ||
                !IsCellInBounds(fromX, fromY) ||
                !CanAcceptWater(toX, toY))
            {
                return false;
            }

            int fromAmount = GetAmount(fromX, fromY);
            int toAmount = GetAmount(toX, toY);
            int flow = Math.Min(requestedAmount, Math.Min(fromAmount, MaxAmount - toAmount));
            if (flow <= 0)
                return false;

            if (!SetAmount(fromX, fromY, fromAmount - flow))
                return false;

            if (!SetAmount(toX, toY, toAmount + flow))
            {
                SetAmount(fromX, fromY, fromAmount);
                return false;
            }

            revision++;
            WakeNeighbors(fromX, fromY);
            WakeNeighbors(toX, toY);
            WakeSandAboveCell(fromX, fromY);
            WakeSandAboveCell(toX, toY);
            return true;
        }

        private bool ShouldStayActive(int cellX, int cellY)
        {
            int amount = GetAmount(cellX, cellY);
            if (amount <= 0)
                return false;

            return CanAcceptWater(cellX, cellY + 1) ||
                   CanAcceptWater(cellX - 1, cellY + 1) ||
                   CanAcceptWater(cellX + 1, cellY + 1) ||
                   CanEqualizeSide(cellX, cellY, -1) ||
                   CanEqualizeSide(cellX, cellY, 1);
        }

        private bool CanEqualizeSide(int cellX, int cellY, int direction)
        {
            int targetX = WrapCellX(cellX + direction);
            return CanAcceptWater(targetX, cellY) &&
                   GetAmount(cellX, cellY) - GetAmount(targetX, cellY) > MinSideFlowDifference;
        }

        private bool CanAcceptWater(int cellX, int cellY)
        {
            cellX = WrapCellX(cellX);
            return IsCellInBounds(cellX, cellY) &&
                   CanOccupyCell(cellX, cellY) &&
                   GetAmount(cellX, cellY) < MaxAmount;
        }

        private bool CanOccupyCell(int cellX, int cellY)
        {
            cellX = WrapCellX(cellX);
            return IsCellInBounds(cellX, cellY) &&
                   !worldMap.IsSolidAt(cellX, cellY) &&
                   !IsSandCell(cellX, cellY);
        }

        private bool IsSandCell(int cellX, int cellY)
        {
            return SandSystem?.HasSandInRectangle(
                cellX * TileSize,
                cellY * TileSize,
                TileSize,
                TileSize) == true;
        }

        private int GetAmount(int cellX, int cellY)
        {
            cellX = WrapCellX(cellX);
            return IsCellInBounds(cellX, cellY) &&
                   waterAmounts.TryGetValue(CreateCellKey(cellX, cellY), out int amount)
                ? amount
                : 0;
        }

        private bool SetAmount(int cellX, int cellY, int amount)
        {
            cellX = WrapCellX(cellX);
            if (!IsCellInBounds(cellX, cellY))
                return false;

            amount = Math.Clamp(amount, 0, MaxAmount);
            if (amount > 0 && !CanOccupyCell(cellX, cellY))
                return false;

            long key = CreateCellKey(cellX, cellY);
            waterAmounts.TryGetValue(key, out int oldAmount);
            if (oldAmount == amount)
                return false;

            if (oldAmount > 0)
            {
                totalWaterAmount -= oldAmount;
                if (amount == 0)
                {
                    waterAmounts.Remove(key);
                    RemoveOccupiedCell(cellX, cellY);
                    activeWaterKeys.Remove(key);
                }
            }

            if (amount > 0)
            {
                waterAmounts[key] = amount;
                totalWaterAmount += amount;
                if (oldAmount == 0)
                    AddOccupiedCell(cellX, cellY);
            }

            return true;
        }

        private void AddActiveWater(int cellX, int cellY)
        {
            cellX = WrapCellX(cellX);
            if (!IsCellInBounds(cellX, cellY) || GetAmount(cellX, cellY) <= 0)
                return;

            long key = CreateCellKey(cellX, cellY);
            if (activeWaterKeys.Add(key))
                activeWater.Add(new Point(cellX, cellY));
        }

        private void WakeNeighbors(int cellX, int cellY)
        {
            for (int y = cellY - 1; y <= cellY + 1; y++)
            {
                for (int x = cellX - 1; x <= cellX + 1; x++)
                    AddActiveWater(x, y);
            }
        }

        private void WakeSandAboveCell(int cellX, int cellY)
        {
            SandSystem?.WakeAreaAboveTile(WrapCellX(cellX), cellY);
        }

        private void ClearInternal(bool markDirty)
        {
            waterAmounts.Clear();
            waterRows.Clear();
            activeWater.Clear();
            activeWaterKeys.Clear();
            totalWaterAmount = 0;
            if (markDirty)
                revision++;
        }

        private void CompactActiveWaterList()
        {
            activeWaterKeys.Clear();
            for (int i = activeWater.Count - 1; i >= 0; i--)
            {
                Point current = activeWater[i];
                long key = CreateCellKey(current.X, current.Y);
                if (!waterAmounts.ContainsKey(key) || !activeWaterKeys.Add(key))
                    activeWater.RemoveAt(i);
            }
        }

        private bool TryPixelToCell(int pixelX, int pixelY, out int cellX, out int cellY)
        {
            cellX = WrapCellX(PixelToRawCellX(pixelX));
            cellY = PixelToCellY(pixelY);
            return IsCellInBounds(cellX, cellY);
        }

        private int PixelToRawCellX(int pixelX)
        {
            return (int)MathF.Floor(pixelX / (float)TileSize);
        }

        private int PixelToCellY(int pixelY)
        {
            return (int)MathF.Floor(pixelY / (float)TileSize);
        }

        private bool IsCellInBounds(int cellX, int cellY)
        {
            return cellX >= 0 && cellY >= 0 && cellX < CellWidth && cellY < CellHeight;
        }

        private int WrapCellX(int cellX)
        {
            if (CellWidth <= 0)
                return 0;

            int wrapped = cellX % CellWidth;
            return wrapped < 0 ? wrapped + CellWidth : wrapped;
        }

        private long CreateCellKey(int cellX, int cellY)
        {
            return ((long)cellY << 32) | (uint)cellX;
        }

        private void DecodeCellKey(long key, out int cellX, out int cellY)
        {
            cellX = (int)(key & 0xFFFFFFFF);
            cellY = (int)(key >> 32);
        }

        private void AddOccupiedCell(int cellX, int cellY)
        {
            if (!waterRows.TryGetValue(cellY, out SortedSet<int> row))
            {
                row = new SortedSet<int>();
                waterRows[cellY] = row;
            }

            row.Add(cellX);
        }

        private void RemoveOccupiedCell(int cellX, int cellY)
        {
            if (!waterRows.TryGetValue(cellY, out SortedSet<int> row))
                return;

            row.Remove(cellX);
            if (row.Count == 0)
                waterRows.Remove(cellY);
        }

        private Rectangle CreateWaterRectangle(int cellX, int cellY)
        {
            int waterHeight = AmountToPixelHeight(GetAmount(cellX, cellY));
            return new Rectangle(
                cellX * TileSize,
                ((cellY + 1) * TileSize) - waterHeight,
                TileSize,
                waterHeight);
        }

        private int AmountToPixelHeight(int amount)
        {
            if (amount < MinVisibleAmount)
                return 0;

            return Math.Clamp((int)MathF.Floor((amount / (float)MaxAmount) * TileSize), 1, TileSize);
        }

        private sealed class PoolColumnState
        {
            public PoolColumnState(int x, int bottomY)
            {
                X = x;
                BottomY = bottomY;
            }

            public int X { get; }
            public int BottomY { get; set; }
            public int TopLimitY { get; set; }
            public int CapacityAmount { get; set; }
            public int BottomUnit { get; set; }
            public int TopUnit { get; set; }
            public int DesiredAmount { get; set; }
        }

        private sealed class PoolAmountDelta
        {
            public PoolAmountDelta(long key, int x, int y, int remainingAmount)
            {
                Key = key;
                X = x;
                Y = y;
                RemainingAmount = remainingAmount;
            }

            public long Key { get; }
            public int X { get; }
            public int Y { get; }
            public int RemainingAmount { get; set; }
        }
    }
}
