using Microsoft.Xna.Framework;

namespace Nyvorn.Source.Gameplay.Entities.Player
{
    public readonly struct AnimFrame
    {
        public AnimFrame(int row, int col, int offsetX = 0, int offsetY = 0, float duration = 0.1f)
        {
            Row = row;
            Col = col;
            OffsetX = offsetX;
            OffsetY = offsetY;
            Duration = duration;
        }

        public int Row { get; }
        public int Col { get; }
        public int OffsetX { get; }
        public int OffsetY { get; }
        public float Duration { get; }

        public Rectangle GetSourceRectangle(int frameWidth, int frameHeight)
        {
            return new Rectangle(Col * frameWidth, Row * frameHeight, frameWidth, frameHeight);
        }
    }
}
