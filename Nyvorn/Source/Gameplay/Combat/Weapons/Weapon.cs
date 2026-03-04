using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Gameplay.Combat.Weapons
{
    public class Weapon
    {
        protected Texture2D texture;

        protected int frameW;
        protected int frameH;

        protected int frameIndex;

        protected Point pivot;

        public Weapon(Texture2D texture, int frameW, int frameH, Point pivot)
        {
            this.texture = texture;
            this.frameW = frameW;
            this.frameH = frameH;
            this.pivot = pivot;

            frameIndex = 0;
        }

        public virtual void Draw(SpriteBatch spriteBatch, Vector2 handWorld, bool facingRight)
        {
            Rectangle src = new Rectangle(frameIndex * frameW, 0, frameW, frameH);

            int pivotX = pivot.X;

            if (!facingRight)
                pivotX = (frameW - 1) - pivotX;

            Vector2 topLeft = handWorld - new Vector2(pivotX, pivot.Y);

            SpriteEffects fx = facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            spriteBatch.Draw(
                texture,
                topLeft,
                src,
                Color.White,
                0f,
                Vector2.Zero,
                1f,
                fx,
                0f
            );
        }

        public void SetFrame(int index)
        {
            frameIndex = index;
        }
    }
}