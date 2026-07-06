using System;

namespace Nyvorn.Source.Engine.Physics.Liquids
{
    public readonly struct LiquidSimulationStats
    {
        public LiquidSimulationStats(
            long simulationTick,
            int processedCells,
            int transfers,
            int activeCells,
            int liquidCells,
            long totalAmount,
            int wokenChunks,
            TimeSpan elapsed)
        {
            SimulationTick = simulationTick;
            ProcessedCells = processedCells;
            Transfers = transfers;
            ActiveCells = activeCells;
            LiquidCells = liquidCells;
            TotalAmount = totalAmount;
            WokenChunks = wokenChunks;
            Elapsed = elapsed;
        }

        public long SimulationTick { get; }
        public int ProcessedCells { get; }
        public int Transfers { get; }
        public int ActiveCells { get; }
        public int LiquidCells { get; }
        public long TotalAmount { get; }
        public int WokenChunks { get; }
        public TimeSpan Elapsed { get; }
    }
}
