namespace Nyvorn.Source.World.Generation
{
    public readonly struct WorldLayerDefinition
    {
        public WorldLayerDefinition(WorldLayerType layerType, int startY, int endY)
        {
            LayerType = layerType;
            StartY = startY;
            EndY = endY;
        }

        public WorldLayerType LayerType { get; }
        public int StartY { get; }
        public int EndY { get; }
        public int Height => EndY - StartY + 1;

        public bool Contains(int y)
        {
            return y >= StartY && y <= EndY;
        }

        public float GetNormalizedDepth(int y)
        {
            if (Height <= 1)
                return 0f;

            int clampedY = System.Math.Clamp(y, StartY, EndY);
            return (clampedY - StartY) / (float)(Height - 1);
        }
    }
}
