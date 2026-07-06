namespace Nyvorn.Source.Engine.Physics.Liquids
{
    public sealed class LiquidRules
    {
        public static LiquidRules Default { get; } = CreateDefault();

        public static LiquidRules CreateDefault()
            => new();

        public int LiquidTicksPerSecond { get; set; } = 45;
        public int MaxLiquidCellsPerTick { get; set; } = 4096;
        public int MaxFlowPerTick { get; set; } = 512;
        public int MaxLateralFlowPerTick { get; set; } = 256;
        public int MaxLateralSearchTiles { get; set; } = 2;
        public int MinFlow { get; set; } = 1;
        public int MaxLiquidAmount { get; set; } = 4096;
        public int MaxCompress { get; set; } = 512;
        public int SettlingThreshold { get; set; } = 3;
        public int MinRenderableAmount { get; set; } = 32;
        public int SurfaceLineHeight { get; set; } = 2;
        public int ChunkWakeTicks { get; set; } = 90;

        public int MaxCompressedAmount => MaxLiquidAmount + MaxCompress;

        public void ApplyBalancedPreset()
        {
            LiquidTicksPerSecond = 45;
            MaxFlowPerTick = 512;
            MaxLateralFlowPerTick = 256;
            MaxLateralSearchTiles = 2;
            MaxLiquidCellsPerTick = 4096;
        }

        public void ApplySlowPreset()
        {
            LiquidTicksPerSecond = 30;
            MaxFlowPerTick = 384;
            MaxLateralFlowPerTick = 160;
            MaxLateralSearchTiles = 1;
            MaxLiquidCellsPerTick = 3072;
        }

        public void ApplyFastPreset()
        {
            LiquidTicksPerSecond = 60;
            MaxFlowPerTick = 1024;
            MaxLateralFlowPerTick = 512;
            MaxLateralSearchTiles = 4;
            MaxLiquidCellsPerTick = 6144;
        }
    }
}
