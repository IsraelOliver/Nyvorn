namespace Nyvorn.Source.World.Decorations
{
    public sealed class TreeGenerationSettings
    {
        public float TreeSpawnChance { get; init; } = 0.035f;
        public int MinTreeSpacingTiles { get; init; } = 7;
        public int MinTreeHeight { get; init; } = 5;
        public int MaxTreeHeight { get; init; } = 10;
        public bool RequireFlatGroundForRoots { get; init; } = true;
        public bool EnableTreeDebug { get; init; } = false;
        public int CanopyClearanceTiles { get; init; } = 3;

        public static TreeGenerationSettings Default { get; } = new();
    }
}
