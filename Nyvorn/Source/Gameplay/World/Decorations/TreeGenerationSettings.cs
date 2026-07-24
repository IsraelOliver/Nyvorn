namespace Nyvorn.Source.World.Decorations
{
    public sealed class TreeGenerationSettings
    {
        public float TreeSpawnChance { get; init; } = 0.035f;
        public int MinTreeHeight { get; init; } = 5;
        public int MaxTreeHeight { get; init; } = 30;
        public bool RequireFlatGroundForRoots { get; init; } = true;
        public int CanopyClearanceTiles { get; init; } = 3;

        // Spacing between tree bases, at the densest and sparsest points of a forest cluster.
        public int DenseClusterSpacingTiles { get; init; } = 1;
        public int SparseClusterSpacingTiles { get; init; } = 16;

        // Spawn-chance multiplier at the densest and sparsest points of a forest cluster.
        public float DenseClusterSpawnMultiplier { get; init; } = 3f;
        public float SparseClusterSpawnMultiplier { get; init; } = 0.15f;

        // Low frequencies keep clusters wide (roughly 1/frequency tiles across).
        public float ClusterDensityNoiseFrequency { get; init; } = 0.015f;
        public float ClusterHeightNoiseFrequency { get; init; } = 0.01f;
        public int ClusterHeightJitter { get; init; } = 1;

        // Group-based generation settings
        public int MinGroupRadius { get; init; } = 30;
        public int MaxGroupRadius { get; init; } = 100;
        public int MinTreesPerGroup { get; init; } = 3;
        public int MaxTreesPerGroup { get; init; } = 20;
        public int IntraGroupSpacing { get; init; } = 3;
        public int MaxBranchesPerTree { get; init; } = 3;
        public int MinHeightForBranches { get; init; } = 10;

        // Isolated trees (between groups)
        public float IsolatedTreeSpawnChance { get; init; } = 0.02f;

        public static TreeGenerationSettings Default { get; } = new();
    }
}
