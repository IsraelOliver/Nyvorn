using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Nyvorn.Source.Gameplay.Combat.Weapons
{
    public class Weapon
    {
        protected readonly Texture2D texture;

        protected readonly int frameW;
        protected readonly int frameH;

        protected readonly Point pivot;

        public virtual void SetIdle() { }
        public virtual void SetWalk() { }

        public virtual void SetAttackFrame(int frameIndex) { }

        public virtual bool CanAttack => true;
        public virtual bool IsVisibleInHand => true;
        public virtual bool UsesAttackHandPose => false;
        public virtual bool IsActiveFrame(int frameIndex) { return false; }

        protected int frameX;
        protected int frameY;

        public Weapon(Texture2D texture, int frameW, int frameH, Point pivot)
        {
            this.texture = texture;
            this.frameW = frameW;
            this.frameH = frameH;
            this.pivot = pivot;

            frameX = 0;
            frameY = 0;
        }

        public void SetFrame(int x, int y)
        {
            frameX = x;
            frameY = y;
        }

        public virtual Rectangle GetAttackHitbox(Vector2 handWorld, bool facingRight)
        {
            return Rectangle.Empty;
        }

        public virtual void Draw(SpriteBatch spriteBatch, Vector2 handWorld, bool facingRight)
        {
            Rectangle src = new Rectangle(frameX * frameW, frameY * frameH, frameW, frameH);

            int pivotX = pivot.X;
            if (!facingRight)
                pivotX = (frameW - 1) - pivotX;

            Vector2 topLeft = handWorld - new Vector2(pivotX, pivot.Y);
            SpriteEffects fx = facingRight ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            spriteBatch.Draw(texture, topLeft, src, Color.White, 0f, Vector2.Zero, 1f, fx, 0f);
        }
    }
}
