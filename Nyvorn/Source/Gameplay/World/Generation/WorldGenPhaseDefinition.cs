namespace Nyvorn.Source.World.Generation
{
    public sealed class WorldGenPhaseDefinition
    {
        public WorldGenPhaseDefinition(string name, string label, float weight)
        {
            Name = name;
            Label = label;
            Weight = weight;
        }

        public string Name { get; }
        public string Label { get; }
        public float Weight { get; }
    }
}
