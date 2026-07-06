namespace Nyvorn.Source.Engine.Physics.Liquids
{
    public struct LiquidCell
    {
        public LiquidCell(LiquidType type, int amount)
        {
            Type = type;
            Amount = amount;
            SettledTicks = 0;
            Flags = LiquidCellFlags.None;
        }

        public LiquidType Type { get; set; }
        public int Amount { get; set; }
        public byte SettledTicks { get; set; }
        public LiquidCellFlags Flags { get; set; }
        public bool IsEmpty => Type == LiquidType.None || Amount <= 0;
    }
}
