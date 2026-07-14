using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Physics.Sand;
using Nyvorn.Source.World;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Nyvorn.Source.Engine.Physics.Liquids
{
    public sealed class LiquidSystem
    {
        private const byte SnapshotVersion = 3;
        private const byte LegacyTileAmountSnapshotVersion = 2;
        private const byte LegacyCellSnapshotVersion = 1;
        private const int LegacyMaxAmount = 255;

        private readonly WorldMap worldMap;
        private readonly LiquidRules rules;
        private readonly Dictionary<long, LiquidCell> cells = new();
        private readonly Dictionary<int, SortedSet<int>> occupiedRows = new();
        private readonly HashSet<long> activeCellKeys = new();
        private readonly List<long> activeCells = new();
        private readonly HashSet<WorldChunkCoord> activeSimulationChunks = new();
        private readonly HashSet<WorldChunkCoord> nextActiveSimulationChunks = new();
        private readonly Dictionary<WorldChunkCoord, int> wokenChunkTicks = new();
        private readonly List<WorldChunkCoord> expiredWokenChunks = new();

        private long totalLiquidAmount;
        private int revision;
        private int persistedRevision;
        private long simulationTick;
        private int transfersThisTick;

        public LiquidSystem(WorldMap worldMap, LiquidRules rules = null)
        {
            this.worldMap = worldMap ?? throw new ArgumentNullException(nameof(worldMap));
            this.rules = rules ?? LiquidRules.CreateDefault();

            TileSize = worldMap.TileSize;
            Width = worldMap.Width * TileSize;
            Height = worldMap.Height * TileSize;
            CellWidth = worldMap.Width;
            CellHeight = worldMap.Height;
            LastStats = new LiquidSimulationStats(0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        public SandSystem SandSystem { get; set; }
        public LiquidRules Rules => rules;
        public int Width { get; }
        public int Height { get; }
        public int CellWidth { get; }
        public int CellHeight { get; }
        public int TileSize { get; }
        public int CellSize => TileSize;
        public int CellCount => cells.Count;
        public int ActiveCellCount => activeCells.Count;
        public long TotalWaterAmount => totalLiquidAmount;
        public float TotalTileVolume => totalLiquidAmount / (float)rules.MaxLiquidAmount;
        public bool HasUnsavedChanges => revision != persistedRevision;
        public LiquidSimulationStats LastStats { get; private set; }

        public void SetActiveSimulationChunks(IReadOnlyList<WorldChunkCoord> chunks)
        {
            nextActiveSimulationChunks.Clear();

            if (chunks != null)
            {
                for (int i = 0; i < chunks.Count; i++)
                {
                    WorldChunkCoord normalized = NormalizeChunk(chunks[i]);
                    if (!nextActiveSimulationChunks.Add(normalized))
                        continue;

                    if (!activeSimulationChunks.Contains(normalized))
                        WakeChunkCells(normalized);
                }
            }

            activeSimulationChunks.Clear();
            foreach (WorldChunkCoord chunk in nextActiveSimulationChunks)
                activeSimulationChunks.Add(chunk);
        }

        public bool HasLiquidAt(int pixelX, int pixelY)
        {
            if (!TryPixelToCell(pixelX, pixelY, out int cellX, out int cellY))
                return false;

            int amount = GetLiquidAmountAtTile(cellX, cellY);
            if (amount <= 0)
                return false;

            int liquidHeight = AmountToPixelHeight(amount);
            if (liquidHeight <= 0)
                return false;

            int liquidTop = ((cellY + 1) * TileSize) - liquidHeight;
            return pixelY >= liquidTop;
        }

        public bool HasLiquidInRectangle(int pixelX, int pixelY, int width, int height)
            => GetLiquidCoverage(new Rectangle(pixelX, pixelY, width, height)) > 0f;

        public float GetSubmergedRatio(Rectangle bounds)
            => GetLiquidCoverage(bounds);

        public float GetLiquidCoverage(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0 || Width <= 0 || Height <= 0)
                return 0f;

            int minCellY = Math.Max(0, PixelToCellY(bounds.Y));
            int maxCellY = Math.Min(CellHeight - 1, PixelToCellY(bounds.Bottom - 1));
            if (minCellY > maxCellY)
                return 0f;

            int rawMinCellX = PixelToRawCellX(bounds.X);
            int rawMaxCellX = PixelToRawCellX(bounds.Right - 1);
            long queryArea = (long)bounds.Width * bounds.Height;
            long coveredArea = 0;

            for (int cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                if (!occupiedRows.TryGetValue(cellY, out SortedSet<int> row) || row.Count == 0)
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
                        Rectangle liquidBounds = CreateLiquidRectangle(cellX, cellY, bleedEdges: false);
                        if (liquidBounds.Height <= 0)
                            continue;

                        liquidBounds.X += drawOffsetX * TileSize;
                        Rectangle intersection = Rectangle.Intersect(liquidBounds, bounds);
                        if (intersection.IsEmpty)
                            continue;

                        coveredArea += (long)intersection.Width * intersection.Height;
                        if (coveredArea >= queryArea)
                            return 1f;
                    }

                    currentRawStartX = currentRawEndX + 1;
                }
            }

            return MathHelper.Clamp(coveredArea / (float)queryArea, 0f, 1f);
        }

        public int GetLiquidAmountAtTile(int x, int y)
        {
            x = WrapCellX(x);
            return IsCellInBounds(x, y) &&
                   cells.TryGetValue(CreateCellKey(x, y), out LiquidCell cell)
                ? cell.Amount
                : 0;
        }

        public LiquidType GetLiquidTypeAtTile(int x, int y)
        {
            x = WrapCellX(x);
            return IsCellInBounds(x, y) &&
                   cells.TryGetValue(CreateCellKey(x, y), out LiquidCell cell) &&
                   cell.Amount > 0
                ? cell.Type
                : LiquidType.None;
        }

        public bool TryGetLiquidCell(int x, int y, out LiquidCell cell)
        {
            x = WrapCellX(x);
            if (!IsCellInBounds(x, y))
            {
                cell = default;
                return false;
            }

            return cells.TryGetValue(CreateCellKey(x, y), out cell) && !cell.IsEmpty;
        }

        public bool SetLiquidAt(int pixelX, int pixelY, LiquidType liquidType, bool value)
        {
            if (!TryPixelToCell(pixelX, pixelY, out int cellX, out int cellY))
                return false;

            return SetLiquid(cellX, cellY, liquidType, value ? rules.MaxLiquidAmount : 0);
        }

        public bool SetLiquid(int x, int y, LiquidType liquidType, int amount)
            => SetLiquidCore(x, y, liquidType, amount, wakeSimulation: true, settleImmediately: false);

        public bool SetSettledLiquid(int x, int y, LiquidType liquidType, int amount)
            => SetLiquidCore(x, y, liquidType, amount, wakeSimulation: false, settleImmediately: true);

        private bool SetLiquidCore(int x, int y, LiquidType liquidType, int amount, bool wakeSimulation, bool settleImmediately)
        {
            if (amount > 0 && liquidType == LiquidType.None)
                return false;

            x = WrapCellX(x);
            int clampedAmount = Math.Clamp(amount, 0, rules.MaxCompressedAmount);
            if (clampedAmount > 0 && !CanContainLiquid(x, y))
                return false;

            bool changed = SetCellAmount(x, y, liquidType, clampedAmount);
            if (!changed)
                return false;

            revision++;
            if (settleImmediately)
                MarkCellSettled(x, y);

            if (!wakeSimulation)
                return true;

            WakeNeighbors(x, y);
            WakeSandAboveCell(x, y);
            WakeChunkForCell(x, y);
            return true;
        }

        public bool AddLiquid(int x, int y, LiquidType liquidType, int amount)
        {
            if (amount <= 0 || liquidType == LiquidType.None)
                return false;

            x = WrapCellX(x);
            if (!CanContainLiquid(x, y))
                return false;

            LiquidType existingType = GetLiquidTypeAtTile(x, y);
            if (existingType != LiquidType.None && existingType != liquidType)
                return false;

            int currentAmount = GetLiquidAmountAtTile(x, y);
            int nextAmount = Math.Clamp(currentAmount + amount, 0, rules.MaxCompressedAmount);
            return SetLiquid(x, y, liquidType, nextAmount);
        }

        public bool RemoveLiquid(int x, int y, int amount)
        {
            if (amount <= 0)
                return false;

            x = WrapCellX(x);
            int currentAmount = GetLiquidAmountAtTile(x, y);
            if (currentAmount <= 0)
                return false;

            LiquidType type = GetLiquidTypeAtTile(x, y);
            int nextAmount = Math.Max(0, currentAmount - amount);
            return SetLiquid(x, y, type, nextAmount);
        }

        public bool DisplaceLiquidForPlacedTile(int tileX, int tileY)
        {
            tileX = WrapCellX(tileX);
            if (!TryGetLiquidCell(tileX, tileY, out LiquidCell blockedCell))
                return false;

            int remaining = blockedCell.Amount;
            LiquidType type = blockedCell.Type;
            SetCellAmount(tileX, tileY, LiquidType.None, 0);

            TryPush(tileX, tileY + 1, rules.MaxCompressedAmount);

            int firstDx = ((tileX + tileY + simulationTick) & 1) == 0 ? -1 : 1;
            TryPush(tileX + firstDx, tileY, rules.MaxLiquidAmount);
            TryPush(tileX - firstDx, tileY, rules.MaxLiquidAmount);
            TryPush(tileX, tileY - 1, rules.MaxLiquidAmount);

            // MVP placement rule: any volume that cannot be safely moved is discarded.
            revision++;
            WakeAreaAroundTile(tileX, tileY);
            WakeSandAboveCell(tileX, tileY);
            return true;

            void TryPush(int targetX, int targetY, int targetMaxAmount)
            {
                if (remaining <= 0)
                    return;

                targetX = WrapCellX(targetX);
                if (!CanAcceptLiquid(targetX, targetY, type, targetMaxAmount))
                    return;

                int targetAmount = GetLiquidAmountAtTile(targetX, targetY);
                int flow = Math.Min(remaining, targetMaxAmount - targetAmount);
                if (flow <= 0)
                    return;

                if (!SetCellAmount(targetX, targetY, type, targetAmount + flow))
                    return;

                remaining -= flow;
                WakeNeighbors(targetX, targetY);
                WakeSandAboveCell(targetX, targetY);
                WakeChunkForCell(targetX, targetY);
            }
        }

        public void TickFast()
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            simulationTick++;
            transfersThisTick = 0;

            if (activeCells.Count != activeCellKeys.Count)
                CompactActiveCellList();

            int processedCells = 0;
            int cellIndex = activeCells.Count - 1;
            while (cellIndex >= 0 && processedCells < rules.MaxLiquidCellsPerTick)
            {
                long key = activeCells[cellIndex];
                activeCells.RemoveAt(cellIndex);
                activeCellKeys.Remove(key);

                DecodeCellKey(key, out int cellX, out int cellY);
                if (!cells.ContainsKey(key))
                {
                    cellIndex--;
                    continue;
                }

                if (!IsChunkSimulatable(cellX, cellY))
                {
                    cellIndex--;
                    continue;
                }

                processedCells++;
                bool changed = ProcessCell(cellX, cellY);
                if (!changed)
                    IncrementSettledTicks(cellX, cellY);

                if (ShouldStayActive(cellX, cellY))
                    AddActiveCell(cellX, cellY);

                cellIndex--;
            }

            AgeWokenChunks();
            LastStats = new LiquidSimulationStats(
                simulationTick,
                processedCells,
                transfersThisTick,
                activeCells.Count,
                cells.Count,
                totalLiquidAmount,
                wokenChunkTicks.Count,
                Stopwatch.GetElapsedTime(startTimestamp));
        }

        public byte[] ExportSnapshot()
        {
            if (cells.Count == 0)
                return Array.Empty<byte>();

            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);

            writer.Write(SnapshotVersion);
            writer.Write(TileSize);
            writer.Write(cells.Count);
            foreach (KeyValuePair<long, LiquidCell> entry in cells)
            {
                if (entry.Value.IsEmpty)
                    continue;

                DecodeCellKey(entry.Key, out int cellX, out int cellY);
                writer.Write(cellX);
                writer.Write(cellY);
                writer.Write((byte)entry.Value.Type);
                writer.Write(entry.Value.Amount);
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
                else if (version == LegacyTileAmountSnapshotVersion)
                    ImportLegacyTileAmountSnapshot(reader, savedCellSize);
                else if (version == SnapshotVersion)
                    ImportCurrentSnapshot(reader, savedCellSize);
            }
            catch
            {
                ClearInternal(markDirty: false);
            }

            SettleAllLiquids();
            MarkPersisted();
        }

        public void SettleAllLiquids()
        {
            if (cells.Count == 0)
            {
                activeCells.Clear();
                activeCellKeys.Clear();
                wokenChunkTicks.Clear();
                LastStats = new LiquidSimulationStats(simulationTick, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
                return;
            }

            List<long> keys = new(cells.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!cells.TryGetValue(keys[i], out LiquidCell cell) || cell.IsEmpty)
                    continue;

                cell.SettledTicks = (byte)Math.Min(byte.MaxValue, rules.SettlingThreshold);
                cell.Flags &= ~LiquidCellFlags.Active;
                cells[keys[i]] = cell;
            }

            activeCells.Clear();
            activeCellKeys.Clear();
            wokenChunkTicks.Clear();
            LastStats = new LiquidSimulationStats(simulationTick, 0, 0, 0, cells.Count, totalLiquidAmount, 0, TimeSpan.Zero);
        }

        public void WakeAllLiquids()
        {
            WakeAllLiquid();
        }

        public void MarkPersisted()
        {
            persistedRevision = revision;
        }

        public int Clear()
        {
            int count = cells.Count;
            ClearInternal(markDirty: count > 0);
            return count;
        }

        public void WakeAreaAroundTile(int tileX, int tileY)
        {
            for (int cellY = tileY - 1; cellY <= tileY + 1; cellY++)
            {
                for (int cellX = tileX - 1; cellX <= tileX + 1; cellX++)
                    AddActiveCell(cellX, cellY);
            }

            WakeChunkForCell(tileX, tileY);
        }

        public void WakeAreaAroundPixel(int pixelX, int pixelY)
        {
            if (!TryPixelToCell(pixelX, pixelY, out int cellX, out int cellY))
                return;

            WakeNeighbors(cellX, cellY);
            WakeChunkForCell(cellX, cellY);
        }

        public IEnumerable<Rectangle> GetVisibleSegments(int minPixelX, int maxPixelX, int minPixelY, int maxPixelY, bool bleedEdges = false)
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
                if (!occupiedRows.TryGetValue(cellY, out SortedSet<int> row) || row.Count == 0)
                    continue;

                foreach (int cellX in row.GetViewBetween(minCellX, maxCellX))
                {
                    Rectangle liquidBounds = CreateLiquidRectangle(cellX, cellY, bleedEdges);
                    if (liquidBounds.Height <= 0)
                        continue;

                    Rectangle clipped = Rectangle.Intersect(liquidBounds, visible);
                    if (!clipped.IsEmpty)
                        yield return clipped;
                }
            }
        }

        public IEnumerable<Rectangle> GetVisibleSurfaceSegments(int minPixelX, int maxPixelX, int minPixelY, int maxPixelY, bool bleedEdges = false)
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
                if (!occupiedRows.TryGetValue(cellY, out SortedSet<int> row) || row.Count == 0)
                    continue;

                foreach (int cellX in row.GetViewBetween(minCellX, maxCellX))
                {
                    if (GetLiquidAmountAtTile(cellX, cellY - 1) > 0)
                        continue;

                    Rectangle liquidBounds = CreateLiquidRectangle(cellX, cellY, bleedEdges);
                    if (liquidBounds.Height <= 0)
                        continue;

                    int surfaceHeight = Math.Min(rules.SurfaceLineHeight, liquidBounds.Height);
                    Rectangle surfaceBounds = new(liquidBounds.X, liquidBounds.Y, liquidBounds.Width, surfaceHeight);
                    Rectangle clipped = Rectangle.Intersect(surfaceBounds, visible);
                    if (!clipped.IsEmpty)
                        yield return clipped;
                }
            }
        }

        private bool ProcessCell(int cellX, int cellY)
        {
            cellX = WrapCellX(cellX);
            if (!TryGetLiquidCell(cellX, cellY, out LiquidCell cell))
                return false;

            if (!CanContainLiquid(cellX, cellY))
                return DisplaceLiquidForPlacedTile(cellX, cellY);

            LiquidType type = cell.Type;
            bool changed = false;

            int amount = GetLiquidAmountAtTile(cellX, cellY);
            changed |= TryTransferLiquid(
                cellX,
                cellY,
                cellX,
                cellY + 1,
                Math.Min(amount, rules.MaxFlowPerTick),
                rules.MaxCompressedAmount);

            amount = GetLiquidAmountAtTile(cellX, cellY);
            if (amount <= 0)
                return changed;

            if (!CanAcceptLiquid(cellX, cellY + 1, type, rules.MaxCompressedAmount))
            {
                int firstDx = ((cellX + cellY + simulationTick) & 1) == 0 ? -1 : 1;
                int secondDx = -firstDx;

                changed |= TryEqualizeSide(cellX, cellY, firstDx);
                changed |= TryEqualizeSide(cellX, cellY, secondDx);
            }

            amount = GetLiquidAmountAtTile(cellX, cellY);
            if (amount > rules.MaxLiquidAmount)
            {
                int excess = amount - rules.MaxLiquidAmount;
                changed |= TryTransferLiquid(
                    cellX,
                    cellY,
                    cellX,
                    cellY - 1,
                    Math.Min(excess, rules.MaxFlowPerTick / 2),
                    rules.MaxLiquidAmount);
            }

            if (changed)
                ResetSettledTicks(cellX, cellY);

            return changed;
        }

        private bool TryEqualizeSide(int cellX, int cellY, int direction)
        {
            LiquidType type = GetLiquidTypeAtTile(cellX, cellY);
            int amount = GetLiquidAmountAtTile(cellX, cellY);
            if (!TryFindLateralEqualizationTarget(cellX, cellY, type, amount, direction, out int targetX, out int targetAmount))
                return false;

            int desired = (amount - targetAmount) / 2;
            if (desired < rules.MinFlow)
                return false;

            int requestedFlow = Math.Min(desired, rules.MaxLateralFlowPerTick);
            return TryTransferLiquid(cellX, cellY, targetX, cellY, requestedFlow, rules.MaxLiquidAmount);
        }

        private bool TryTransferLiquid(
            int fromX,
            int fromY,
            int toX,
            int toY,
            int requestedAmount,
            int targetMaxAmount)
        {
            fromX = WrapCellX(fromX);
            toX = WrapCellX(toX);
            if (requestedAmount < rules.MinFlow ||
                !IsCellInBounds(fromX, fromY) ||
                !IsCellInBounds(toX, toY) ||
                !TryGetLiquidCell(fromX, fromY, out LiquidCell fromCell) ||
                !CanAcceptLiquid(toX, toY, fromCell.Type, targetMaxAmount))
            {
                return false;
            }

            int targetAmount = GetLiquidAmountAtTile(toX, toY);
            int capacity = targetMaxAmount - targetAmount;
            int flow = Math.Min(requestedAmount, Math.Min(fromCell.Amount, capacity));
            if (flow < rules.MinFlow)
                return false;

            if (!SetCellAmount(fromX, fromY, fromCell.Type, fromCell.Amount - flow))
                return false;

            if (!SetCellAmount(toX, toY, fromCell.Type, targetAmount + flow))
            {
                SetCellAmount(fromX, fromY, fromCell.Type, fromCell.Amount);
                return false;
            }

            revision++;
            transfersThisTick++;
            WakeNeighbors(fromX, fromY);
            WakeNeighbors(toX, toY);
            WakeSandAboveCell(fromX, fromY);
            WakeSandAboveCell(toX, toY);
            WakeChunkForCell(toX, toY);
            return true;
        }

        private bool ShouldStayActive(int cellX, int cellY)
        {
            if (!TryGetLiquidCell(cellX, cellY, out LiquidCell cell))
                return false;

            if (cell.SettledTicks >= rules.SettlingThreshold && !HasFlowOpportunity(cellX, cellY, cell))
                return false;

            return HasFlowOpportunity(cellX, cellY, cell);
        }

        private bool HasFlowOpportunity(int cellX, int cellY, LiquidCell cell)
        {
            return CanAcceptLiquid(cellX, cellY + 1, cell.Type, rules.MaxCompressedAmount) ||
                   CanEqualizeSide(cellX, cellY, cell.Type, -1) ||
                   CanEqualizeSide(cellX, cellY, cell.Type, 1) ||
                   (cell.Amount > rules.MaxLiquidAmount &&
                    CanAcceptLiquid(cellX, cellY - 1, cell.Type, rules.MaxLiquidAmount));
        }

        private bool CanEqualizeSide(int cellX, int cellY, LiquidType type, int direction)
        {
            int amount = GetLiquidAmountAtTile(cellX, cellY);
            return TryFindLateralEqualizationTarget(cellX, cellY, type, amount, direction, out _, out _);
        }

        private bool TryFindLateralEqualizationTarget(
            int cellX,
            int cellY,
            LiquidType type,
            int amount,
            int direction,
            out int targetX,
            out int targetAmount)
        {
            cellX = WrapCellX(cellX);
            targetX = cellX;
            targetAmount = amount;

            if (type == LiquidType.None || amount < rules.MinFlow * 2 || direction == 0)
                return false;

            int maxSearchTiles = Math.Min(Math.Max(1, rules.MaxLateralSearchTiles), Math.Max(0, CellWidth - 1));
            for (int step = 1; step <= maxSearchTiles; step++)
            {
                int scanX = WrapCellX(cellX + (direction * step));
                if (!CanContainLiquid(scanX, cellY))
                    break;

                LiquidType scanType = GetLiquidTypeAtTile(scanX, cellY);
                if (scanType != LiquidType.None && scanType != type)
                    break;

                int scanAmount = GetLiquidAmountAtTile(scanX, cellY);
                if (scanAmount < targetAmount && scanAmount < rules.MaxLiquidAmount)
                {
                    targetX = scanX;
                    targetAmount = scanAmount;
                    if (scanAmount == 0)
                        break;
                }
            }

            return targetX != cellX && amount - targetAmount >= rules.MinFlow * 2;
        }

        private bool CanAcceptLiquid(int cellX, int cellY, LiquidType type, int targetMaxAmount)
        {
            cellX = WrapCellX(cellX);
            if (type == LiquidType.None ||
                !IsCellInBounds(cellX, cellY) ||
                !CanContainLiquid(cellX, cellY))
            {
                return false;
            }

            LiquidType targetType = GetLiquidTypeAtTile(cellX, cellY);
            if (targetType != LiquidType.None && targetType != type)
                return false;

            return GetLiquidAmountAtTile(cellX, cellY) < targetMaxAmount;
        }

        private bool CanContainLiquid(int cellX, int cellY)
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

        private bool SetCellAmount(int cellX, int cellY, LiquidType type, int amount)
        {
            cellX = WrapCellX(cellX);
            if (!IsCellInBounds(cellX, cellY))
                return false;

            amount = Math.Clamp(amount, 0, rules.MaxCompressedAmount);
            if (amount > 0)
            {
                if (type == LiquidType.None || !CanContainLiquid(cellX, cellY))
                    return false;
            }
            else
            {
                type = LiquidType.None;
            }

            long key = CreateCellKey(cellX, cellY);
            cells.TryGetValue(key, out LiquidCell oldCell);
            int oldAmount = oldCell.Amount;
            LiquidType oldType = oldCell.Type;
            if (oldAmount == amount && oldType == type)
                return false;

            if (oldAmount > 0)
                totalLiquidAmount -= oldAmount;

            if (amount <= 0)
            {
                cells.Remove(key);
                RemoveOccupiedCell(cellX, cellY);
                activeCellKeys.Remove(key);
                return true;
            }

            LiquidCell next = oldCell.IsEmpty
                ? new LiquidCell(type, amount)
                : oldCell;
            next.Type = type;
            next.Amount = amount;
            next.SettledTicks = 0;
            next.Flags |= LiquidCellFlags.Dirty;
            cells[key] = next;
            totalLiquidAmount += amount;

            if (oldAmount <= 0)
                AddOccupiedCell(cellX, cellY);

            return true;
        }

        private void AddActiveCell(int cellX, int cellY)
        {
            cellX = WrapCellX(cellX);
            if (!IsCellInBounds(cellX, cellY))
                return;

            long key = CreateCellKey(cellX, cellY);
            if (!cells.TryGetValue(key, out LiquidCell cell) || cell.IsEmpty)
                return;

            cell.Flags |= LiquidCellFlags.Active;
            cells[key] = cell;
            if (activeCellKeys.Add(key))
                activeCells.Add(key);
        }

        private void WakeNeighbors(int cellX, int cellY)
        {
            AddActiveCell(cellX, cellY);
            AddActiveCell(cellX, cellY - 1);
            AddActiveCell(cellX, cellY + 1);
            AddActiveCell(cellX - 1, cellY);
            AddActiveCell(cellX + 1, cellY);
            AddActiveCell(cellX - 1, cellY + 1);
            AddActiveCell(cellX + 1, cellY + 1);
        }

        private void WakeAllLiquid()
        {
            activeCells.Clear();
            activeCellKeys.Clear();
            foreach (long key in cells.Keys)
            {
                activeCellKeys.Add(key);
                activeCells.Add(key);
            }
        }

        private void WakeSandAboveCell(int cellX, int cellY)
        {
            SandSystem?.WakeAreaAboveTile(WrapCellX(cellX), cellY);
        }

        private void WakeChunkForCell(int cellX, int cellY)
        {
            if (!IsCellInBounds(WrapCellX(cellX), cellY))
                return;

            WorldChunkCoord chunk = worldMap.GetChunkCoordForTile(cellX, cellY);
            if (activeSimulationChunks.Contains(chunk))
                return;

            wokenChunkTicks[chunk] = rules.ChunkWakeTicks;
        }

        private void WakeChunkCells(WorldChunkCoord chunk)
        {
            if (worldMap.ChunkCountX <= 0 || worldMap.ChunkCountY <= 0)
                return;

            Rectangle bounds = worldMap.GetChunkTileBounds(chunk);
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                if (!occupiedRows.TryGetValue(y, out SortedSet<int> row) || row.Count == 0)
                    continue;

                foreach (int x in row.GetViewBetween(bounds.Left, bounds.Right - 1))
                    AddActiveCell(x, y);
            }
        }

        private bool IsChunkSimulatable(int cellX, int cellY)
        {
            if (activeSimulationChunks.Count == 0)
                return true;

            WorldChunkCoord chunk = worldMap.GetChunkCoordForTile(cellX, cellY);
            return activeSimulationChunks.Contains(chunk) || wokenChunkTicks.ContainsKey(chunk);
        }

        private void AgeWokenChunks()
        {
            expiredWokenChunks.Clear();
            foreach (KeyValuePair<WorldChunkCoord, int> entry in wokenChunkTicks)
                expiredWokenChunks.Add(entry.Key);

            for (int i = 0; i < expiredWokenChunks.Count; i++)
            {
                WorldChunkCoord chunk = expiredWokenChunks[i];
                int ticks = wokenChunkTicks[chunk] - 1;
                if (ticks <= 0)
                    wokenChunkTicks.Remove(chunk);
                else
                    wokenChunkTicks[chunk] = ticks;
            }
        }

        private WorldChunkCoord NormalizeChunk(WorldChunkCoord chunk)
        {
            return new WorldChunkCoord(
                worldMap.WrapChunkX(chunk.X),
                Math.Clamp(chunk.Y, 0, worldMap.ChunkCountY - 1));
        }

        private void IncrementSettledTicks(int cellX, int cellY)
        {
            cellX = WrapCellX(cellX);
            long key = CreateCellKey(cellX, cellY);
            if (!cells.TryGetValue(key, out LiquidCell cell))
                return;

            if (cell.SettledTicks < byte.MaxValue)
                cell.SettledTicks++;

            cell.Flags &= ~LiquidCellFlags.Active;
            cells[key] = cell;
        }

        private void ResetSettledTicks(int cellX, int cellY)
        {
            cellX = WrapCellX(cellX);
            long key = CreateCellKey(cellX, cellY);
            if (!cells.TryGetValue(key, out LiquidCell cell))
                return;

            cell.SettledTicks = 0;
            cells[key] = cell;
        }

        private void MarkCellSettled(int cellX, int cellY)
        {
            cellX = WrapCellX(cellX);
            long key = CreateCellKey(cellX, cellY);
            if (!cells.TryGetValue(key, out LiquidCell cell) || cell.IsEmpty)
                return;

            cell.SettledTicks = (byte)Math.Min(byte.MaxValue, rules.SettlingThreshold);
            cell.Flags &= ~LiquidCellFlags.Active;
            cells[key] = cell;
            activeCellKeys.Remove(key);
        }

        private void ClearInternal(bool markDirty)
        {
            cells.Clear();
            occupiedRows.Clear();
            activeCells.Clear();
            activeCellKeys.Clear();
            wokenChunkTicks.Clear();
            totalLiquidAmount = 0;
            LastStats = new LiquidSimulationStats(simulationTick, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
            if (markDirty)
                revision++;
        }

        private void CompactActiveCellList()
        {
            activeCellKeys.Clear();
            for (int i = activeCells.Count - 1; i >= 0; i--)
            {
                long key = activeCells[i];
                if (!cells.TryGetValue(key, out LiquidCell cell) ||
                    cell.IsEmpty ||
                    !activeCellKeys.Add(key))
                {
                    activeCells.RemoveAt(i);
                }
            }
        }

        private void ImportCurrentSnapshot(BinaryReader reader, int savedCellSize)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int savedCellX = reader.ReadInt32();
                int savedCellY = reader.ReadInt32();
                LiquidType liquidType = (LiquidType)reader.ReadByte();
                int amount = reader.ReadInt32();
                MapSavedCell(savedCellX, savedCellY, savedCellSize, out int cellX, out int cellY);
                if (amount <= 0 ||
                    liquidType == LiquidType.None ||
                    !IsSupportedLiquidType(liquidType) ||
                    !IsCellInBounds(WrapCellX(cellX), cellY) ||
                    !CanContainLiquid(cellX, cellY))
                {
                    continue;
                }

                SetCellAmount(cellX, cellY, liquidType, amount);
            }
        }

        private void ImportLegacyTileAmountSnapshot(BinaryReader reader, int savedCellSize)
        {
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int savedCellX = reader.ReadInt32();
                int savedCellY = reader.ReadInt32();
                LiquidType liquidType = (LiquidType)reader.ReadByte();
                int legacyAmount = reader.ReadByte();
                MapSavedCell(savedCellX, savedCellY, savedCellSize, out int cellX, out int cellY);
                int amount = ScaleLegacyAmount(legacyAmount);
                if (amount <= 0 ||
                    liquidType != LiquidType.Water ||
                    !IsCellInBounds(WrapCellX(cellX), cellY) ||
                    !CanContainLiquid(cellX, cellY))
                {
                    continue;
                }

                SetCellAmount(cellX, cellY, LiquidType.Water, amount);
            }
        }

        private void ImportLegacyCellSnapshot(BinaryReader reader, int legacyCellSize)
        {
            if (legacyCellSize <= 0)
                return;

            int count = reader.ReadInt32();
            int cellsPerTileX = Math.Max(1, TileSize / legacyCellSize);
            int legacyCellsPerTile = Math.Max(1, cellsPerTileX * cellsPerTileX);
            int amountPerLegacyCell = Math.Max(1, (int)MathF.Ceiling(rules.MaxLiquidAmount / (float)legacyCellsPerTile));
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
                if (!IsCellInBounds(cellX, cellY) || !CanContainLiquid(cellX, cellY))
                    continue;

                long key = CreateCellKey(cellX, cellY);
                accumulatedAmounts.TryGetValue(key, out int amount);
                accumulatedAmounts[key] = Math.Min(rules.MaxLiquidAmount, amount + amountPerLegacyCell);
            }

            foreach (KeyValuePair<long, int> entry in accumulatedAmounts)
            {
                DecodeCellKey(entry.Key, out int cellX, out int cellY);
                SetCellAmount(cellX, cellY, LiquidType.Water, entry.Value);
            }
        }

        private void MapSavedCell(int savedCellX, int savedCellY, int savedCellSize, out int cellX, out int cellY)
        {
            if (savedCellSize <= 0 || savedCellSize == TileSize)
            {
                cellX = WrapCellX(savedCellX);
                cellY = savedCellY;
                return;
            }

            cellX = WrapCellX((savedCellX * savedCellSize) / TileSize);
            cellY = (savedCellY * savedCellSize) / TileSize;
        }

        private int ScaleLegacyAmount(int amount)
        {
            if (amount <= 0)
                return 0;

            if (amount >= LegacyMaxAmount)
                return rules.MaxLiquidAmount;

            return Math.Max(1, (int)MathF.Round((amount / (float)LegacyMaxAmount) * rules.MaxLiquidAmount));
        }

        private static bool IsSupportedLiquidType(LiquidType liquidType)
        {
            return liquidType == LiquidType.Water;
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
            return ((long)cellY << 32) | (uint)WrapCellX(cellX);
        }

        private void DecodeCellKey(long key, out int cellX, out int cellY)
        {
            cellX = (int)(key & 0xFFFFFFFF);
            cellY = (int)(key >> 32);
        }

        private void AddOccupiedCell(int cellX, int cellY)
        {
            if (!occupiedRows.TryGetValue(cellY, out SortedSet<int> row))
            {
                row = new SortedSet<int>();
                occupiedRows[cellY] = row;
            }

            row.Add(WrapCellX(cellX));
        }

        private void RemoveOccupiedCell(int cellX, int cellY)
        {
            if (!occupiedRows.TryGetValue(cellY, out SortedSet<int> row))
                return;

            row.Remove(WrapCellX(cellX));
            if (row.Count == 0)
                occupiedRows.Remove(cellY);
        }

        private Rectangle CreateLiquidRectangle(int cellX, int cellY, bool bleedEdges)
        {
            int wrappedX = WrapCellX(cellX);
            int liquidHeight = AmountToPixelHeight(GetLiquidAmountAtTile(cellX, cellY));

            int left = wrappedX * TileSize;
            int right = left + TileSize;
            int bottom = (cellY + 1) * TileSize;
            int top = bottom - liquidHeight;

            // Pressure equalization can leave a cell a pixel or two short even where it is
            // pressed against solid ground, and the stone's own tile art can have undrawn
            // pixels right at its edge. Bleed 1px into any adjacent solid tile on every side
            // so settling jitter or a seam in the tile art never exposes a gap, without ever
            // painting water into a cell that is genuinely empty. Only worth the extra solid
            // checks for the close-up camera view: the minimap and gameplay queries (coverage,
            // submersion) render/measure far below single-pixel scale.
            if (bleedEdges && liquidHeight > 0)
            {
                if (worldMap.IsSolidAt(wrappedX, cellY - 1))
                    top = cellY * TileSize;

                if (worldMap.IsSolidAt(wrappedX, cellY + 1))
                    bottom += 1;

                if (worldMap.IsSolidAt(WrapCellX(wrappedX - 1), cellY))
                    left -= 1;

                if (worldMap.IsSolidAt(WrapCellX(wrappedX + 1), cellY))
                    right += 1;
            }

            return new Rectangle(left, top, right - left, bottom - top);
        }

        private int AmountToPixelHeight(int amount)
        {
            if (amount < rules.MinRenderableAmount)
                return 0;

            if (amount >= rules.MaxLiquidAmount)
                return TileSize;

            int height = (int)(((long)amount * TileSize + rules.MaxLiquidAmount - 1) / rules.MaxLiquidAmount);
            return Math.Clamp(height, 1, TileSize);
        }
    }
}
