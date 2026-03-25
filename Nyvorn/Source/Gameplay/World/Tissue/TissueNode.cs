using Microsoft.Xna.Framework;

namespace Nyvorn.Source.World.Tissue
{
    public sealed class TissueNode
    {
        public TissueNode(int id, Vector2 position, bool isPrimary, float strength)
        {
            Id = id;
            Position = position;
            IsPrimary = isPrimary;
            Strength = strength;
        }

        public int Id { get; }
        public Vector2 Position { get; }
        public bool IsPrimary { get; }
        public float Strength { get; }
    }
}
