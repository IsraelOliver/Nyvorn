using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueNode
    {
        public TissueNode(int id, Vector2 position, bool isPrimary, float strength, int degree = 0, float nestInfluence = 0f)
        {
            Id = id;
            Position = position;
            IsPrimary = isPrimary;
            Strength = strength;
            Degree = degree;
            NestInfluence = nestInfluence;
        }

        public int Id { get; }
        public Vector2 Position { get; }
        public bool IsPrimary { get; }
        public float Strength { get; }
        public int Degree { get; }
        public float NestInfluence { get; }
    }
}
